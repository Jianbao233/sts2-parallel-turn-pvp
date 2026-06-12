using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
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
    private const int RunningRejoinDeferredRecoveryPasses = 10;
    private const int RunningRejoinDeferredRecoveryDelayMs = 250;
    private const int RunningRejoinReleaseAfterDeferredPass = 8;
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

            if (rejoinResponse.combatState != null)
            {
                TryApplyRejoinCombatState(runState, rejoinResponse.combatState, "directconnect_running_rejoin");
            }

            lobby.CleanUp(false);
            PvpNetBridge.EnsureRegistered();
            PvpMatchRuntime runtime = PvpRuntimeRegistry.GetOrCreate(runState);
            runtime.MarkDisconnectedPendingResume("directconnect_running_rejoin", remotePlayerId, "RunningRejoin");
            _ = TaskHelper.RunSafely(RunDeferredRejoinRecoveryLoopAsync(runState, rejoinResponse.combatState));
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

    private static async Task RunDeferredRejoinRecoveryLoopAsync(RunState runState, NetFullCombatState? combatState)
    {
        for (int attempt = 1; attempt <= RunningRejoinDeferredRecoveryPasses; attempt++)
        {
            await Task.Delay(RunningRejoinDeferredRecoveryDelayMs);

            if (!RunManager.Instance.IsInProgress ||
                !ReferenceEquals(RunManager.Instance.DebugOnlyGetState(), runState))
            {
                Log.Warn($"[ParallelTurnPvp] Deferred running-session rejoin recovery aborted. attempt={attempt} inProgress={RunManager.Instance.IsInProgress}");
                return;
            }

            if (RunManager.Instance.CombatStateSynchronizer is { IsDisabled: false } combatSync)
            {
                combatSync.IsDisabled = true;
                Log.Info($"[ParallelTurnPvp] Re-disabled CombatStateSynchronizer during running-session rejoin recovery. attempt={attempt}");
            }

            if (!PvpNetBridge.TryApplyCachedResumeLiveCombatState(runState, $"resume_live_combat_state_deferred_{attempt}") &&
                combatState is not null &&
                CombatManager.Instance.IsInProgress)
            {
                NetFullCombatState liveCombatState = combatState;
                TryApplyRejoinCombatState(runState, liveCombatState, $"directconnect_running_rejoin_deferred_{attempt}");
            }

            PvpNetBridge.PumpClientResumeStateRequest(runState);

            if (PvpRuntimeRegistry.TryGet(runState) is not { } runtime)
            {
                return;
            }

            if (!runtime.IsDisconnectedPendingResume)
            {
                Log.Info($"[ParallelTurnPvp] Deferred running-session rejoin recovery completed. attempts={attempt}");
                return;
            }

            bool isRunningRejoin = string.Equals(runtime.DisconnectReason, "RunningRejoin", StringComparison.OrdinalIgnoreCase);
            if (isRunningRejoin &&
                runtime.HasAppliedResumeStateWhilePending &&
                attempt >= RunningRejoinReleaseAfterDeferredPass)
            {
                runtime.ClearDisconnectedPendingResume($"running_rejoin_deferred_rehydrate_{attempt}");
                PvpNetBridge.TryResetClientResumeRequestRetryState(runState);
                Log.Info($"[ParallelTurnPvp] Deferred running-session rejoin recovery completed after authoritative metadata + live combat rehydrate. attempts={attempt}");
                return;
            }
        }

        if (PvpRuntimeRegistry.TryGet(runState) is { IsDisconnectedPendingResume: true } pendingRuntime)
        {
            Log.Warn($"[ParallelTurnPvp] Deferred running-session rejoin recovery exhausted without clearing pending resume. roomSession={pendingRuntime.RoomSession.SessionId} round={pendingRuntime.CurrentRound.RoundIndex} phase={pendingRuntime.CurrentRound.Phase}");
        }
    }

    public static PvpResumeLiveCombatState? CaptureAuthoritativeResumeLiveCombatState(RunState runState)
    {
        CombatState? combatState = runState.Players.FirstOrDefault()?.Creature.CombatState;
        if (combatState == null)
        {
            return null;
        }

        var state = new PvpResumeLiveCombatState
        {
            RoundNumber = Math.Max(1, combatState.RoundNumber),
            CurrentSide = (int)combatState.CurrentSide
        };

        foreach (Player player in runState.Players)
        {
            Creature? frontline = ResolveTrackedFrontlineForResume(combatState, player);
            state.Players.Add(new PvpResumeLivePlayerState
            {
                PlayerId = player.NetId,
                Energy = player.PlayerCombatState?.Energy ?? player.MaxEnergy,
                Stars = player.PlayerCombatState?.Stars ?? 0,
                Gold = player.Gold,
                Hero = CaptureCreatureState(player.Creature),
                Frontline = CaptureCreatureState(frontline),
                Piles = CaptureCombatPiles(player)
            });
        }

        return state;
    }

    public static bool TryApplyAuthoritativeResumeLiveCombatState(RunState runState, PvpResumeLiveCombatState? liveCombatState, string source)
    {
        if (liveCombatState == null)
        {
            return false;
        }

        try
        {
            var snapshot = new PvpCombatSnapshot
            {
                RoundIndex = Math.Max(1, liveCombatState.RoundNumber),
                SnapshotVersion = 0
            };

            foreach (PvpResumeLivePlayerState playerState in liveCombatState.Players)
            {
                snapshot.Heroes[playerState.PlayerId] = new PvpCreatureSnapshot
                {
                    Exists = playerState.Hero.Exists,
                    CurrentHp = playerState.Hero.CurrentHp,
                    MaxHp = playerState.Hero.MaxHp,
                    Block = playerState.Hero.Block
                };

                snapshot.Frontlines[playerState.PlayerId] = new PvpCreatureSnapshot
                {
                    Exists = playerState.Frontline.Exists,
                    CurrentHp = playerState.Frontline.CurrentHp,
                    MaxHp = playerState.Frontline.MaxHp,
                    Block = playerState.Frontline.Block
                };
            }

            PvpNetBridge.ApplyLiveSnapshot(runState, snapshot);

            foreach (PvpResumeLivePlayerState playerState in liveCombatState.Players)
            {
                Player? player = runState.Players.FirstOrDefault(entry => entry.NetId == playerState.PlayerId);
                if (player == null)
                {
                    continue;
                }

                if (player.PlayerCombatState is { } playerCombatState)
                {
                    playerCombatState.Energy = playerState.Energy;
                    playerCombatState.Stars = playerState.Stars;
                }

                player.Gold = playerState.Gold;
                SyncCombatPiles(runState, player, playerState.Piles);
                player.PlayerCombatState?.RecalculateCardValues();

                int handCount = CardPile.Get(PileType.Hand, player)?.Cards.Count ?? 0;
                int drawCount = CardPile.Get(PileType.Draw, player)?.Cards.Count ?? 0;
                int discardCount = CardPile.Get(PileType.Discard, player)?.Cards.Count ?? 0;
                int exhaustCount = CardPile.Get(PileType.Exhaust, player)?.Cards.Count ?? 0;
                int playCount = CardPile.Get(PileType.Play, player)?.Cards.Count ?? 0;
                Log.Info($"[ParallelTurnPvp] Resume live-combat player applied. source={source} player={player.NetId} energy={player.PlayerCombatState?.Energy ?? -1} stars={player.PlayerCombatState?.Stars ?? -1} hand={handCount} draw={drawCount} discard={discardCount} exhaust={exhaustCount} play={playCount}");
            }

            Log.Info($"[ParallelTurnPvp] Applied authoritative resume live combat state. source={source} round={liveCombatState.RoundNumber} side={(CombatSide)liveCombatState.CurrentSide} players={liveCombatState.Players.Count}");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error($"[ParallelTurnPvp] Failed to apply authoritative resume live combat state. source={source} error={ex}");
            return false;
        }
    }

    private static void TryApplyRejoinCombatState(RunState runState, NetFullCombatState combatState, string source)
    {
        try
        {
            foreach (NetFullCombatState.PlayerState playerState in combatState.Players)
            {
                Player? player = runState.Players.FirstOrDefault(entry => entry.NetId == playerState.playerId);
                if (player == null)
                {
                    continue;
                }

                ApplyPlayerCombatState(runState, player, playerState);
            }

            runState.Rng.LoadFromSerializable(combatState.Rng);
            Log.Info($"[ParallelTurnPvp] Applied rejoin NetFullCombatState snapshot. source={source} players={combatState.Players.Count} creatures={combatState.Creatures.Count}");
        }
        catch (Exception ex)
        {
            Log.Error($"[ParallelTurnPvp] Failed to apply rejoin NetFullCombatState snapshot. source={source} error={ex}");
        }
    }

    private static void ApplyPlayerCombatState(RunState runState, Player player, NetFullCombatState.PlayerState playerState)
    {
        if (player.PlayerCombatState is { } combatState)
        {
            combatState.Energy = playerState.energy;
            combatState.Stars = playerState.stars;
        }

        player.Gold = playerState.gold;
        player.PlayerRng.LoadFromSerializable(playerState.rngSet);
        player.RelicGrabBag.LoadFromSerializable(playerState.relicGrabBag);

        SyncCombatPiles(runState, player, playerState.piles);
        player.PlayerCombatState?.RecalculateCardValues();
    }

    private static PvpResumeCreatureState CaptureCreatureState(Creature? creature)
    {
        if (creature == null)
        {
            return new PvpResumeCreatureState();
        }

        return new PvpResumeCreatureState
        {
            Exists = true,
            CurrentHp = creature.CurrentHp,
            MaxHp = creature.MaxHp,
            Block = creature.Block
        };
    }

    private static List<PvpResumeLivePileState> CaptureCombatPiles(Player player)
    {
        var result = new List<PvpResumeLivePileState>();
        foreach (PileType pileType in new[] { PileType.Hand, PileType.Draw, PileType.Discard, PileType.Exhaust, PileType.Play })
        {
            CardPile? pile = CardPile.Get(pileType, player);
            if (pile == null)
            {
                continue;
            }

            var pileState = new PvpResumeLivePileState
            {
                PileType = (int)pileType
            };

            foreach (CardModel card in pile.Cards)
            {
                pileState.CardsJson.Add(JsonSerializer.Serialize(card.ToSerializable()));
            }

            result.Add(pileState);
        }

        return result;
    }

    private static void SyncCombatPiles(RunState runState, Player player, List<NetFullCombatState.CombatPileState> targetPiles)
    {
        List<CardPile> livePiles = new List<CardPile?>
        {
            CardPile.Get(PileType.Hand, player),
            CardPile.Get(PileType.Draw, player),
            CardPile.Get(PileType.Discard, player),
            CardPile.Get(PileType.Exhaust, player),
            CardPile.Get(PileType.Play, player)
        }
        .Where(pile => pile != null)
        .Cast<CardPile>()
        .ToList();

        List<CardModel> cardPool = livePiles
            .SelectMany(pile => pile.Cards.ToList())
            .ToList();

        foreach (CardPile pile in livePiles)
        {
            foreach (CardModel card in pile.Cards.ToList())
            {
                pile.RemoveInternal(card, true);
            }
        }

        foreach (NetFullCombatState.CombatPileState pileState in targetPiles)
        {
            CardPile? livePile = CardPile.Get(pileState.pileType, player);
            if (livePile == null)
            {
                continue;
            }

            foreach (NetFullCombatState.CardState cardState in pileState.cards)
            {
                CardModel? card = TakeMatchingCard(cardPool, cardState.card);
                if (card == null)
                {
                    card = CardModel.FromSerializable(cardState.card);
                    runState.AddCard(card, player);
                }

                livePile.AddInternal(card, -1, true);
            }
        }

        if (cardPool.Count > 0)
        {
            Log.Warn($"[ParallelTurnPvp] Rejoin combat-state apply left unmatched combat cards. player={player.NetId} count={cardPool.Count}");
        }
    }

    private static void SyncCombatPiles(RunState runState, Player player, List<PvpResumeLivePileState> targetPiles)
    {
        List<CardPile> livePiles = new List<CardPile?>
        {
            CardPile.Get(PileType.Hand, player),
            CardPile.Get(PileType.Draw, player),
            CardPile.Get(PileType.Discard, player),
            CardPile.Get(PileType.Exhaust, player),
            CardPile.Get(PileType.Play, player)
        }
        .Where(pile => pile != null)
        .Cast<CardPile>()
        .ToList();

        List<CardModel> cardPool = livePiles
            .SelectMany(pile => pile.Cards.ToList())
            .ToList();

        foreach (CardPile pile in livePiles)
        {
            foreach (CardModel card in pile.Cards.ToList())
            {
                pile.RemoveInternal(card, true);
            }
        }

        foreach (PvpResumeLivePileState pileState in targetPiles)
        {
            CardPile? livePile = CardPile.Get((PileType)pileState.PileType, player);
            if (livePile == null)
            {
                continue;
            }

            foreach (string cardJson in pileState.CardsJson)
            {
                CardModel? card = TakeMatchingCard(cardPool, cardJson);
                if (card == null)
                {
                    SerializableCard? serializedCard = JsonSerializer.Deserialize<SerializableCard>(cardJson);
                    if (serializedCard == null)
                    {
                        Log.Warn($"[ParallelTurnPvp] Resume live-combat apply skipped null serialized card. player={player.NetId} pile={(PileType)pileState.PileType}");
                        continue;
                    }

                    card = CardModel.FromSerializable(serializedCard);
                    runState.AddCard(card, player);
                }

                livePile.AddInternal(card, -1, true);
            }
        }

        if (cardPool.Count > 0)
        {
            Log.Warn($"[ParallelTurnPvp] Resume live-combat apply left unmatched combat cards. player={player.NetId} count={cardPool.Count}");
        }
    }

    private static CardModel? TakeMatchingCard(List<CardModel> cardPool, SerializableCard serializedCard)
    {
        for (int i = 0; i < cardPool.Count; i++)
        {
            CardModel card = cardPool[i];
            if (card.ToSerializable().Equals(serializedCard))
            {
                cardPool.RemoveAt(i);
                return card;
            }
        }

        return null;
    }

    private static CardModel? TakeMatchingCard(List<CardModel> cardPool, string serializedCardJson)
    {
        SerializableCard? serializedCard = JsonSerializer.Deserialize<SerializableCard>(serializedCardJson);
        if (serializedCard == null)
        {
            return null;
        }

        return TakeMatchingCard(cardPool, serializedCard);
    }

    private static Creature? ResolveTrackedFrontlineForResume(CombatState? combatState, Player player)
    {
        Creature? living = ParallelTurnFrontlineHelper.GetFrontline(player);
        if (living != null)
        {
            return living;
        }

        if (combatState == null)
        {
            return null;
        }

        return combatState.Creatures.FirstOrDefault(creature =>
            creature.PetOwner == player &&
            creature.Monster is Osty);
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
