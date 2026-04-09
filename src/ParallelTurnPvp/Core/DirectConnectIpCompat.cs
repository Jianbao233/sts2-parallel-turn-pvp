using System.Reflection;
using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;

namespace ParallelTurnPvp.Core;

public static class DirectConnectIpCompat
{
    private static readonly Version RecommendedVersion = new(1, 2, 7);
    private const string AssemblyName = "DirectConnectIP";
    private const string HostModeSettingsTypeName = "DirectConnectIP.HostModeSettings";
    private const string HostModeTypeName = "DirectConnectIP.HostMode";
    private const string CurrentModePropertyName = "CurrentMode";
    private const string EnetModeName = "ENet";
    private const string ModFolderName = "DirectConnectIP";
    private const string ManifestName = "DirectConnectIP.json";

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
}
