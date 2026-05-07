using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Runs;
using ParallelTurnPvp.Models;

namespace ParallelTurnPvp.Core;

public enum PvpArenaTopology
{
    SharedCombat = 0,
    SplitRoom = 1
}

public sealed class PvpRoomSession
{
    public string SessionId { get; init; } = string.Empty;
    public PvpArenaTopology Topology { get; init; } = PvpArenaTopology.SharedCombat;
    public ulong LocalPlayerId { get; init; }
    public ulong OpponentPlayerId { get; init; }
}

public static class PvpSplitRoomConfig
{
    private const string SplitRoomEnvKey = "PTPVP_ENABLE_SPLIT_ROOM";
    private static readonly bool EnableSplitRoom = ResolveSplitRoomFlag();

    public static bool IsSplitRoomEnabled => EnableSplitRoom;

    private static bool ResolveSplitRoomFlag()
    {
        string? raw = Environment.GetEnvironmentVariable(SplitRoomEnvKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            // Current mainline defaults to shared-combat for stability.
            return false;
        }

        if (raw == "1" ||
            raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("on", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("yes", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (raw == "0" ||
            raw.Equals("false", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            raw.Equals("no", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return false;
    }
}

public static class PvpResolveConfig
{
    private const string ClientReadOnlyResolveEnvKey = "PTPVP_ENABLE_CLIENT_READONLY_RESOLVE";
    private static readonly bool EnableClientReadOnlyResolve = ResolveClientReadOnlyResolveFlag();

    public static bool IsClientReadOnlyResolveEnabled => EnableClientReadOnlyResolve;

    public static bool IsClientReadOnlyResolveEnabledFor(RunState? runState)
    {
        if (runState?.Modifiers.OfType<ParallelTurnPvpDebugModifier>().FirstOrDefault() is { } modifier)
        {
            return modifier.ClientReadOnlyResolveEnabledField;
        }

        return EnableClientReadOnlyResolve;
    }

    private static bool ResolveClientReadOnlyResolveFlag()
    {
        string? raw = Environment.GetEnvironmentVariable(ClientReadOnlyResolveEnvKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            // Keep default OFF on mainline until read-only resolve path is fully stable.
            return false;
        }

        if (raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (raw == "0" || raw.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return false;
    }

    public static bool ShouldUseHostAuthoritativeSnapshotSync(RunState? runState)
    {
        return ParallelTurnFrontlineHelper.IsSplitRoomActive(runState) ||
               IsClientReadOnlyResolveEnabledFor(runState);
    }
}

public static class PvpRoomSessionFactory
{
    public static PvpRoomSession Create(RunState runState, IReadOnlyCollection<ulong> playerIds)
    {
        List<ulong> orderedIds = playerIds.OrderBy(id => id).ToList();
        ulong localPlayerId = ResolveLocalPlayerId(runState, orderedIds);
        ulong opponentPlayerId = orderedIds.FirstOrDefault(id => id != localPlayerId);
        PvpArenaTopology topology = PvpSplitRoomConfig.IsSplitRoomEnabled
            ? PvpArenaTopology.SplitRoom
            : PvpArenaTopology.SharedCombat;
        string modeTag = topology == PvpArenaTopology.SplitRoom ? "split" : "shared";
        string sessionId = $"ptpvp-{modeTag}-{string.Join("-", orderedIds)}";

        return new PvpRoomSession
        {
            SessionId = sessionId,
            Topology = topology,
            LocalPlayerId = localPlayerId,
            OpponentPlayerId = opponentPlayerId
        };
    }

    private static ulong ResolveLocalPlayerId(RunState runState, IReadOnlyCollection<ulong> orderedIds)
    {
        ulong netServicePlayerId = RunManager.Instance.NetService?.NetId ?? 0;
        if (netServicePlayerId != 0 && orderedIds.Contains(netServicePlayerId))
        {
            return netServicePlayerId;
        }

        ulong localContextPlayerId = LocalContext.GetMe(runState)?.NetId ?? 0;
        if (localContextPlayerId != 0 && orderedIds.Contains(localContextPlayerId))
        {
            return localContextPlayerId;
        }

        return orderedIds.FirstOrDefault();
    }
}
