using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using ParallelTurnPvp.Bootstrap;
using ParallelTurnPvp.Models;

namespace ParallelTurnPvp.Core;

public static class DirectConnectIpCompat
{
    private static readonly Version RecommendedVersion = new(1, 2, 7);
    private const string AssemblyName = "DirectConnectIP";
    private const string ConnectionServiceTypeName = "DirectConnectIP.Network.ConnectionService";
    private const string RouteSessionStateMethodName = "RouteSessionState";
    private const string HostModeSettingsTypeName = "DirectConnectIP.HostModeSettings";
    private const string HostModeTypeName = "DirectConnectIP.HostMode";
    private const string CurrentModePropertyName = "CurrentMode";
    private const string EnetModeName = "ENet";
    private const string ModFolderName = "DirectConnectIP";
    private const string ManifestName = "DirectConnectIP.json";
    private static bool _routePatchInstalled;
    private static int _runningRejoinBootstrapGuard;
    private static int _routeSessionTraceCounter;

    public static bool IsLoaded()
    {
        return FindAssembly() != null;
    }

    public static string? TryGetLoadedAssemblyVersion()
    {
        return FindAssembly()?.GetName().Version?.ToString();
    }

    public static string? TryGetInstalledManifestVersion()
    {
        string? manifestPath = TryGetInstalledManifestPath();
        if (manifestPath == null || !File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (document.RootElement.TryGetProperty("version", out JsonElement versionElement))
            {
                return versionElement.GetString();
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[ParallelTurnPvp] Failed to read DirectConnectIP manifest version: {ex.Message}");
        }

        return null;
    }

    public static bool TryEnableEnetHostMode()
    {
        Assembly? assembly = FindAssembly();
        if (assembly == null)
        {
            Log.Warn("[ParallelTurnPvp] DirectConnectIP is not loaded. PvP host will use the game's default networking path.");
            return false;
        }

        try
        {
            Type? settingsType = assembly.GetType(HostModeSettingsTypeName);
            Type? modeType = assembly.GetType(HostModeTypeName);
            if (settingsType == null || modeType == null)
            {
                Log.Warn("[ParallelTurnPvp] DirectConnectIP is loaded, but host mode types were not found.");
                return false;
            }

            PropertyInfo? currentModeProperty = settingsType.GetProperty(CurrentModePropertyName, BindingFlags.Public | BindingFlags.Static);
            if (currentModeProperty == null || !currentModeProperty.CanWrite)
            {
                Log.Warn("[ParallelTurnPvp] DirectConnectIP is loaded, but HostModeSettings.CurrentMode is not writable.");
                return false;
            }

            object enetMode = Enum.Parse(modeType, EnetModeName);
            currentModeProperty.SetValue(null, enetMode);
            string manifestVersion = TryGetInstalledManifestVersion() ?? "unknown";
            Log.Info($"[ParallelTurnPvp] DirectConnectIP ENet mode enabled. loadedAssemblyVersion={TryGetLoadedAssemblyVersion() ?? "unknown"}, manifestVersion={manifestVersion}");
            LogVersionWarningIfNeeded(manifestVersion);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"[ParallelTurnPvp] Failed to switch DirectConnectIP to ENet mode: {ex}");
            return false;
        }
    }

    public static bool TryPatchRunningRejoinPath(Harmony harmony)
    {
        if (_routePatchInstalled)
        {
            return true;
        }

        Assembly? assembly = FindAssembly();
        if (assembly == null)
        {
            return false;
        }

        Type? connectionServiceType = assembly.GetType(ConnectionServiceTypeName);
        MethodInfo? routeMethod = connectionServiceType?.GetMethod(RouteSessionStateMethodName, BindingFlags.NonPublic | BindingFlags.Static);
        MethodInfo? prefixMethod = typeof(DirectConnectIpCompat).GetMethod(nameof(RouteSessionStatePrefix), BindingFlags.NonPublic | BindingFlags.Static);
        if (routeMethod == null || prefixMethod == null)
        {
            Log.Warn("[ParallelTurnPvp] Failed to patch DirectConnectIP Running rejoin path: RouteSessionState method not found.");
            return false;
        }

        harmony.Patch(routeMethod, prefix: new HarmonyMethod(prefixMethod));
        _routePatchInstalled = true;
        Log.Info("[ParallelTurnPvp] Patched DirectConnectIP RouteSessionState for PvP running-session rejoin.");
        return true;
    }

    private static bool RouteSessionStatePrefix(JoinResult result, INetGameService netService)
    {
        if (result.sessionState != RunSessionState.Running)
        {
            return true;
        }

        if (Interlocked.Increment(ref _routeSessionTraceCounter) <= 6)
        {
            Log.Info($"[ParallelTurnPvp] RouteSessionState intercept probe. state={result.sessionState} hasRejoin={result.rejoinResponse.HasValue} netType={netService.Type} connected={netService.IsConnected}");
        }

        if (!result.rejoinResponse.HasValue)
        {
            Log.Warn("[ParallelTurnPvp] RouteSessionState running intercept skipped: rejoinResponse is missing.");
            return true;
        }

        ClientRejoinResponseMessage rejoinResponse = result.rejoinResponse.Value;
        if (!IsParallelTurnRun(rejoinResponse))
        {
            return true;
        }

        if (Interlocked.Exchange(ref _runningRejoinBootstrapGuard, 1) == 1)
        {
            Log.Warn("[ParallelTurnPvp] Ignored duplicate Running rejoin bootstrap request.");
            return false;
        }

        TaskHelper.RunSafely(BootstrapParallelTurnRunningRejoinAsync(netService, rejoinResponse));
        return false;
    }

    private static async Task BootstrapParallelTurnRunningRejoinAsync(INetGameService netService, ClientRejoinResponseMessage rejoinResponse)
    {
        try
        {
            if (netService.Type != NetGameType.Client || !netService.IsConnected)
            {
                Log.Warn($"[ParallelTurnPvp] Rejoin bootstrap aborted: net service unavailable. type={netService.Type} connected={netService.IsConnected}");
                return;
            }

            RunState runState = RunState.FromSerializable(rejoinResponse.serializableRun);
            if (!runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
            {
                Log.Warn("[ParallelTurnPvp] Rejoin bootstrap skipped: running session is not ParallelTurnPvP.");
                return;
            }

            if (RunManager.Instance.IsInProgress)
            {
                Log.Warn("[ParallelTurnPvp] Rejoin bootstrap skipped: RunManager is already in progress.");
                return;
            }

            ulong remotePlayerId = runState.Players.Select(player => player.NetId).FirstOrDefault(id => id != netService.NetId);
            var lobby = new LoadRunLobby(netService, NullLoadRunLobbyListener.Instance, rejoinResponse.serializableRun);
            RunManager.Instance.SetUpSavedMultiPlayer(runState, lobby);
            if (RunManager.Instance.CombatStateSynchronizer is { } combatSync)
            {
                if (!combatSync.IsDisabled)
                {
                    combatSync.IsDisabled = true;
                    Log.Info("[ParallelTurnPvp] Disabled CombatStateSynchronizer for running-session rejoin bootstrap.");
                }
            }

            if (NGame.Instance is not { } game)
            {
                Log.Warn("[ParallelTurnPvp] Rejoin bootstrap aborted: NGame.Instance is null.");
                return;
            }

            Log.Info($"[ParallelTurnPvp] Bootstrapping running-session rejoin. local={netService.NetId} remote={remotePlayerId} hasCombatState={(rejoinResponse.combatState != null)}");
            await game.LoadRun(runState, rejoinResponse.serializableRun.PreFinishedRoom);
            Log.Info($"[ParallelTurnPvp] Running-session rejoin load completed. room={runState.CurrentRoom?.GetType().Name ?? "null"}");

            if (runState.CurrentRoom is not MegaCrit.Sts2.Core.Rooms.CombatRoom)
            {
                await ParallelTurnPvpArenaBootstrap.TryEnterCombatFromCurrentNeowAsync(runState, "directconnect_running_rejoin");
            }

            lobby.CleanUp(false);
            PvpNetBridge.EnsureRegistered();
            PvpMatchRuntime runtime = PvpRuntimeRegistry.GetOrCreate(runState);
            runtime.MarkDisconnectedPendingResume("directconnect_running_rejoin", remotePlayerId, "RunningRejoin");
            PvpNetBridge.PumpClientResumeStateRequest(runState);
            Log.Info("[ParallelTurnPvp] Running-session rejoin bootstrap completed.");
        }
        catch (Exception ex)
        {
            Log.Error($"[ParallelTurnPvp] Running-session rejoin bootstrap failed: {ex}");
        }
        finally
        {
            Interlocked.Exchange(ref _runningRejoinBootstrapGuard, 0);
        }
    }

    private static bool IsParallelTurnRun(ClientRejoinResponseMessage rejoinResponse)
    {
        if (rejoinResponse.serializableRun?.Modifiers == null || rejoinResponse.serializableRun.Modifiers.Count == 0)
        {
            return false;
        }

        string expectedEntry = string.Empty;
        string expectedNormalized = string.Empty;
        try
        {
            expectedEntry = ModelDb.GetId<ParallelTurnPvpDebugModifier>().Entry ?? string.Empty;
            expectedNormalized = NormalizeId(expectedEntry);
        }
        catch
        {
            // Ignore model db resolution failures during early menu lifetime.
        }

        List<string> entries = new();
        foreach (var modifier in rejoinResponse.serializableRun.Modifiers)
        {
            if (modifier == null)
            {
                continue;
            }

            var modelId = modifier.Id;
            string entry = modelId?.Entry ?? string.Empty;
            entries.Add(entry);
            string normalized = NormalizeId(entry);
            bool matchByExact = !string.IsNullOrWhiteSpace(expectedEntry) &&
                                string.Equals(entry, expectedEntry, StringComparison.OrdinalIgnoreCase);
            bool matchByNormalized = !string.IsNullOrWhiteSpace(expectedNormalized) &&
                                     normalized == expectedNormalized;
            bool matchByHeuristic =
                (normalized.Contains("parallelturn") && normalized.Contains("pvp")) ||
                normalized.Contains("parallelturnpvpdebug") ||
                normalized.Contains("parallelturndebug");
            if (matchByExact || matchByNormalized || matchByHeuristic)
            {
                return true;
            }
        }

        if (Interlocked.CompareExchange(ref _routeSessionTraceCounter, 0, 0) <= 8)
        {
            string joined = entries.Count == 0 ? "-" : string.Join(", ", entries);
            Log.Warn($"[ParallelTurnPvp] RouteSessionState running intercept: no ParallelTurn marker in modifiers. expected={expectedEntry} entries=[{joined}]");
        }

        return false;
    }

    private static string NormalizeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        return new string(chars);
    }

    private static Assembly? FindAssembly()
    {
        return AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, AssemblyName, StringComparison.Ordinal));
    }

    private static string? TryGetInstalledManifestPath()
    {
        string gameDataDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string candidate = Path.GetFullPath(Path.Combine(gameDataDir, "..", "mods", ModFolderName, ManifestName));
        return File.Exists(candidate) ? candidate : null;
    }

    private static void LogVersionWarningIfNeeded(string manifestVersion)
    {
        if (!Version.TryParse(manifestVersion, out Version? installedVersion))
        {
            return;
        }

        if (installedVersion < RecommendedVersion)
        {
            Log.Warn($"[ParallelTurnPvp] DirectConnectIP {installedVersion} is older than the recommended {RecommendedVersion}. If direct-IP testing is unstable, update DirectConnectIP first.");
        }
    }

    private sealed class NullLoadRunLobbyListener : ILoadRunLobbyListener
    {
        public static readonly NullLoadRunLobbyListener Instance = new();

        public void PlayerConnected(ulong playerId)
        {
        }

        public void RemotePlayerDisconnected(ulong playerId)
        {
        }

        public Task<bool> ShouldAllowRunToBegin()
        {
            return Task.FromResult(true);
        }

        public void BeginRun()
        {
        }

        public void PlayerReadyChanged(ulong playerId)
        {
        }

        public void LocalPlayerDisconnected(NetErrorInfo info)
        {
        }
    }
}
