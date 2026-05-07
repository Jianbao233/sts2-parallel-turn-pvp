using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;

namespace ParallelTurnPvp.Core;

public sealed class PvpTargetRef
{
    public ulong OwnerPlayerId { get; init; }
    public PvpTargetKind Kind { get; init; }
}

public sealed class PvpAction
{
    public ulong ActorPlayerId { get; init; }
    public int RoundIndex { get; init; }
    public int Sequence { get; init; }
    public uint? RuntimeActionId { get; init; }
    public PvpActionType ActionType { get; init; }
    public string ModelEntry { get; init; } = string.Empty;
    public PvpTargetRef Target { get; init; } = new();
}

public sealed class PvpActionLog
{
    public ulong PlayerId { get; init; }
    public List<PvpAction> Actions { get; } = new();
    public bool Locked { get; set; }
}

public sealed class PvpCreatureSnapshot
{
    public bool Exists { get; init; }
    public int CurrentHp { get; init; }
    public int MaxHp { get; init; }
    public int Block { get; init; }
}

public sealed class PvpCombatSnapshot
{
    public int RoundIndex { get; init; }
    public int SnapshotVersion { get; init; }
    public Dictionary<ulong, PvpCreatureSnapshot> Heroes { get; init; } = new();
    public Dictionary<ulong, PvpCreatureSnapshot> Frontlines { get; init; } = new();
}

public sealed class PvpResolvedEvent
{
    public PvpResolvedEventKind Kind { get; init; }
    public string Text { get; init; } = string.Empty;
}

public sealed class PvpPublicIntentSlot
{
    public PvpIntentCategory Category { get; init; }
    public PvpIntentTargetSide TargetSide { get; init; }
}

public sealed class PvpPlayerIntentState
{
    public ulong PlayerId { get; init; }
    public int RoundStartEnergy { get; set; }
    public bool Locked { get; set; }
    public bool IsFirstFinisher { get; set; }
    public List<PvpPublicIntentSlot> Slots { get; } = new();
}

public sealed class PvpIntentView
{
    public int RoundIndex { get; init; }
    public ulong ViewerId { get; init; }
    public ulong TargetId { get; init; }
    public int RoundStartEnergy { get; init; }
    public bool Locked { get; init; }
    public bool IsFirstFinisher { get; init; }
    public int RevealBudget { get; init; }
    public int ViewerActionCount { get; init; }
    public int TargetActionCount { get; init; }
    public int VisibleCount { get; init; }
    public int HiddenCount { get; init; }
    public IReadOnlyList<PvpPublicIntentSlot> VisibleSlots { get; init; } = Array.Empty<PvpPublicIntentSlot>();
}

public sealed class PvpRoundResult
{
    public int RoundIndex { get; init; }
    public PvpCombatSnapshot InitialSnapshot { get; init; } = new();
    public PvpCombatSnapshot FinalSnapshot { get; init; } = new();
    public PvpCombatSnapshot? PredictedFinalSnapshot { get; set; }
    public PvpRoundExecutionPlan? ExecutionPlan { get; set; }
    public PvpRoundDeltaPlan? DeltaPlan { get; set; }
    public PvpRoundDelayedPlan? DelayedPlan { get; set; }
    public PvpRoundDelayedCommandPlan? DelayedCommandPlan { get; set; }
    public PvpRoundPlaybackPlan? PlaybackPlan { get; set; }
    public List<PvpResolvedEvent> Events { get; } = new();
}

public sealed class PvpExecutionStep
{
    public PvpResolutionPhase Phase { get; init; }
    public ulong PlayerId { get; init; }
    public int Sequence { get; init; }
    public PvpActionType ActionType { get; init; }
    public string ModelEntry { get; init; } = string.Empty;
    public PvpTargetRef Target { get; init; } = new();
    public uint? RuntimeActionId { get; init; }
}

public sealed class PvpRoundExecutionPlan
{
    public int RoundIndex { get; init; }
    public ulong FirstFinisherPlayerId { get; init; }
    public List<PvpExecutionStep> Steps { get; } = new();
}

public sealed class PvpDeltaOperation
{
    public PvpResolutionPhase Phase { get; init; }
    public PvpDeltaOperationKind Kind { get; init; }
    public ulong SourcePlayerId { get; init; }
    public ulong TargetPlayerId { get; init; }
    public PvpTargetKind TargetKind { get; init; }
    public int Amount { get; init; }
    public int Sequence { get; init; }
    public string ModelEntry { get; init; } = string.Empty;
    public uint? RuntimeActionId { get; init; }
}

public sealed class PvpRoundDeltaPlan
{
    public int RoundIndex { get; init; }
    public List<PvpDeltaOperation> Operations { get; } = new();
}

public sealed class PvpDelayedCandidateOperation
{
    public PvpResolutionPhase Phase { get; init; }
    public PvpDelayedCandidateKind Kind { get; init; }
    public ulong SourcePlayerId { get; init; }
    public ulong TargetPlayerId { get; init; }
    public PvpTargetKind TargetKind { get; init; }
    public int Amount { get; init; }
    public int Sequence { get; init; }
    public string ModelEntry { get; init; } = string.Empty;
    public uint? RuntimeActionId { get; init; }
}

public sealed class PvpRoundDelayedPlan
{
    public int RoundIndex { get; init; }
    public List<PvpDelayedCandidateOperation> Operations { get; } = new();
}

public sealed class PvpDelayedCommand
{
    public PvpResolutionPhase Phase { get; init; }
    public PvpDelayedCommandKind Kind { get; init; }
    public ulong SourcePlayerId { get; init; }
    public ulong TargetPlayerId { get; init; }
    public PvpTargetKind TargetKind { get; init; }
    public int Amount { get; init; }
    public int Sequence { get; init; }
    public string ModelEntry { get; init; } = string.Empty;
    public uint? RuntimeActionId { get; init; }
    public string ExecutorHint { get; init; } = string.Empty;
}

public sealed class PvpRoundDelayedCommandPlan
{
    public int RoundIndex { get; init; }
    public List<PvpDelayedCommand> Commands { get; } = new();
}

public sealed class PvpPlaybackEvent
{
    public int Sequence { get; init; }
    public PvpResolutionPhase Phase { get; init; }
    public PvpPlaybackEventKind Kind { get; init; }
    public PvpDeltaOperationKind? DeltaKind { get; init; }
    public ulong SourcePlayerId { get; init; }
    public ulong TargetPlayerId { get; init; }
    public PvpTargetKind TargetKind { get; init; }
    public int Amount { get; init; }
    public string ModelEntry { get; init; } = string.Empty;
    public uint? RuntimeActionId { get; init; }
}

public sealed class PvpPlaybackFrame
{
    public int Sequence { get; init; }
    public PvpResolutionPhase Phase { get; init; }
    public PvpPlaybackEventKind Kind { get; init; }
    public PvpCombatSnapshot Snapshot { get; init; } = new();
}

public sealed class PvpRoundPlaybackPlan
{
    public int RoundIndex { get; init; }
    public List<PvpPlaybackEvent> Events { get; } = new();
    public List<PvpPlaybackFrame> Frames { get; } = new();
}

public sealed class PvpPlannedAction
{
    public int Sequence { get; init; }
    public uint? RuntimeActionId { get; init; }
    public PvpActionType ActionType { get; init; }
    public string ModelEntry { get; init; } = string.Empty;
    public PvpTargetRef Target { get; init; } = new();
}

public sealed class PvpRoundSubmission
{
    public int RoundIndex { get; init; }
    public ulong PlayerId { get; init; }
    public int RoundStartEnergy { get; init; }
    public bool Locked { get; init; }
    public bool IsFirstFinisher { get; init; }
    public List<PvpPlannedAction> Actions { get; } = new();
}

public sealed class PvpPlanningFrame
{
    public int RoundIndex { get; init; }
    public int SnapshotVersion { get; init; }
    public PvpMatchPhase Phase { get; init; }
    public int Revision { get; init; }
    public string RoomSessionId { get; init; } = string.Empty;
    public PvpArenaTopology RoomTopology { get; init; } = PvpArenaTopology.SharedCombat;
    public List<PvpRoundSubmission> Submissions { get; } = new();
}

public sealed class PvpRoundState
{
    public const int DuplicateTrackedActionLogBudget = 8;
    public int RoundIndex { get; set; }
    public PvpMatchPhase Phase { get; set; }
    public int PlanningRevision { get; set; }
    public string RoomSessionId { get; set; } = string.Empty;
    public PvpArenaTopology RoomTopology { get; set; } = PvpArenaTopology.SharedCombat;
    public PvpCombatSnapshot SnapshotAtRoundStart { get; set; } = new();
    public Dictionary<ulong, PvpActionLog> LogsByPlayer { get; } = new();
    public Dictionary<ulong, PvpPlayerIntentState> PublicIntentByPlayer { get; } = new();
    public PvpRoundResult? LastResult { get; set; }
    public bool HasResolved { get; set; }
    public bool DelayedLiveEffectsApplied { get; set; }
    public ulong FirstLockedPlayerId { get; set; }
    public bool FirstLockRewardGranted { get; set; }
    public PvpCombatSnapshot? PendingAuthoritativeSnapshot { get; set; }
    public HashSet<string> RecordedActionKeys { get; } = new();
    public Dictionary<ulong, PvpRoundSubmission> NetworkSubmissionsByPlayer { get; } = new();
    public Dictionary<ulong, int> NetworkSubmissionRevisionByPlayer { get; } = new();
    public HashSet<ulong> ResolverFallbackPlayers { get; } = new();
    public HashSet<ulong> ResolverForcedLockedPlayers { get; } = new();
    public string ResolveInputSourceTag { get; set; } = string.Empty;
    public int DuplicateTrackedActionLogCount { get; set; }
    public bool DuplicateTrackedActionLogSuppressed { get; set; }
}

public sealed class PvpMatchRuntime
{
    public const int EarlyLockHealAmount = 3;
    private const int HostResolveWaitTimeoutMs = 1800;
    private const int HostResolveWaitLockedPeerGraceMs = 1200;
    private const int HostResolveWaitRecentSubmissionWindowMs = 1200;
    private const int HostResolveWaitRecentSubmissionGraceMs = 900;
    private const int ClientAuthoritativeResultWarnMs = 4500;
    private readonly Dictionary<ulong, Player> _playersById;
    private readonly IPvpRoundResolver _resolver;
    private readonly IPvpPlanningCompiler _planningCompiler;
    private readonly HashSet<ulong> _missingLogWarnings = new();
    private int _hostResolveWaitRoundIndex;
    private DateTime _hostResolveWaitSinceUtc;
    private int _hostResolveWaitLoggedSecond = -1;
    private int _hostResolveTimeoutWarnedRoundIndex;
    private DateTime _hostLastNetworkSubmissionUtc;
    private int _clientAuthoritativeWaitRoundIndex;
    private int _clientAuthoritativeWaitSnapshotVersion;
    private DateTime _clientAuthoritativeWaitSinceUtc;
    private int _clientAuthoritativeWaitLoggedSecond = -1;
    private int _clientAuthoritativeWaitWarnedRoundIndex;
    private DateTime _disconnectedSinceUtc;

    public PvpMatchRuntime(RunState runState, IEnumerable<Player> players)
    {
        RunState = runState;
        _playersById = players.ToDictionary(player => player.NetId);
        RoomSession = PvpRoomSessionFactory.Create(runState, _playersById.Keys.ToList());
        _resolver = new PvpRoundResolver();
        _planningCompiler = new PvpPlanningCompiler();
        Log.Info($"[ParallelTurnPvp] RoomSession initialized. session={RoomSession.SessionId} topology={RoomSession.Topology} local={RoomSession.LocalPlayerId} opponent={RoomSession.OpponentPlayerId}");
    }

    public RunState RunState { get; }
    public PvpRoomSession RoomSession { get; }
    public PvpRoundState CurrentRound { get; private set; } = new();
    public PvpRoundResult? LastAuthoritativeResult { get; private set; }
    public PvpPlanningFrame? LastAuthoritativePlanningFrame { get; private set; }
    public int SnapshotVersion { get; private set; }
    public int LastReceivedRoundStateVersion { get; private set; }
    public int LastReceivedRoundResultVersion { get; private set; }
    public int LastBroadcastRoundStateVersion { get; private set; }
    public int LastBroadcastRoundResultVersion { get; private set; }
    public int LastReceivedPlanningRoundIndex { get; private set; }
    public int LastReceivedPlanningRevision { get; private set; }
    public int LastBroadcastPlanningRoundIndex { get; private set; }
    public int LastBroadcastPlanningRevision { get; private set; }
    public bool IsDisconnectedPendingResume { get; private set; }
    public string DisconnectReason { get; private set; } = string.Empty;

    public void BeginCombat(CombatState combatState)
    {
        ClearDisconnectedPendingResume("begin_combat");
        SnapshotVersion = 0;
        LastAuthoritativeResult = null;
        LastAuthoritativePlanningFrame = null;
        LastReceivedRoundStateVersion = 0;
        LastReceivedRoundResultVersion = 0;
        LastBroadcastRoundStateVersion = 0;
        LastBroadcastRoundResultVersion = 0;
        LastReceivedPlanningRoundIndex = 0;
        LastReceivedPlanningRevision = 0;
        LastBroadcastPlanningRoundIndex = 0;
        LastBroadcastPlanningRevision = 0;
        StartRoundFromLiveState(combatState, 1);
    }

    public void StartRoundFromLiveState(CombatState combatState, int roundIndex)
    {
        if (IsDisconnectedPendingResume)
        {
            Log.Warn($"[ParallelTurnPvp] StartRoundFromLiveState ignored: runtime disconnected pending resume. round={CurrentRound.RoundIndex} requested={roundIndex} reason={DisconnectReason}");
            return;
        }

        SnapshotVersion++;
        PvpCombatSnapshot liveSnapshot = SnapshotFactory.Create(combatState, roundIndex, SnapshotVersion);
        var snapshot = SnapshotFactory.NormalizeForPlanning(liveSnapshot);
        CurrentRound = new PvpRoundState
        {
            RoundIndex = roundIndex,
            Phase = PvpMatchPhase.Planning,
            PlanningRevision = 1,
            RoomSessionId = RoomSession.SessionId,
            RoomTopology = RoomSession.Topology,
            SnapshotAtRoundStart = snapshot
        };

        foreach (var player in _playersById.Values)
        {
            CurrentRound.LogsByPlayer[player.NetId] = new PvpActionLog { PlayerId = player.NetId };
            CurrentRound.PublicIntentByPlayer[player.NetId] = new PvpPlayerIntentState
            {
                PlayerId = player.NetId,
                RoundStartEnergy = player.PlayerCombatState?.Energy ?? player.MaxEnergy
            };
        }

        Log.Info($"[ParallelTurnPvp] StartRoundFromLiveState round={roundIndex} roomSession={RoomSession.SessionId} topology={RoomSession.Topology} logs=[{string.Join(", ", CurrentRound.LogsByPlayer.Keys.OrderBy(id => id))}] intents=[{string.Join(", ", CurrentRound.PublicIntentByPlayer.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}:energy={entry.Value.RoundStartEnergy}"))}] heroes=[{string.Join(", ", snapshot.Heroes.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}:exists={entry.Value.Exists},hp={entry.Value.CurrentHp}/{entry.Value.MaxHp},block={entry.Value.Block}"))}] frontlines=[{string.Join(", ", snapshot.Frontlines.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}:exists={entry.Value.Exists},hp={entry.Value.CurrentHp}/{entry.Value.MaxHp},block={entry.Value.Block}"))}]");
        _missingLogWarnings.Clear();
        ResetResolveWaitState();
        _hostLastNetworkSubmissionUtc = default;
        ResetClientAuthoritativeWaitState();
        RefreshPlanningFrameCache();
    }

    public void MarkClientAwaitAuthoritativeResult(int liveRoundIndex, int snapshotVersion)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Client || liveRoundIndex <= 0 || snapshotVersion <= 0)
        {
            return;
        }

        if (_clientAuthoritativeWaitRoundIndex != liveRoundIndex || _clientAuthoritativeWaitSnapshotVersion != snapshotVersion)
        {
            _clientAuthoritativeWaitRoundIndex = liveRoundIndex;
            _clientAuthoritativeWaitSnapshotVersion = snapshotVersion;
            _clientAuthoritativeWaitSinceUtc = DateTime.UtcNow;
            _clientAuthoritativeWaitLoggedSecond = -1;
            _clientAuthoritativeWaitWarnedRoundIndex = 0;
            Log.Info($"[ParallelTurnPvp] Client waiting authoritative round result. round={liveRoundIndex} snapshotVersion={snapshotVersion}");
            return;
        }

        double waitedMs = (DateTime.UtcNow - _clientAuthoritativeWaitSinceUtc).TotalMilliseconds;
        int waitedSecond = (int)(waitedMs / 1000d);
        if (waitedSecond != _clientAuthoritativeWaitLoggedSecond)
        {
            _clientAuthoritativeWaitLoggedSecond = waitedSecond;
            Log.Info($"[ParallelTurnPvp] Client still waiting authoritative round result. round={liveRoundIndex} snapshotVersion={snapshotVersion} waitedMs={(int)waitedMs}");
        }

        if (waitedMs >= ClientAuthoritativeResultWarnMs && _clientAuthoritativeWaitWarnedRoundIndex != liveRoundIndex)
        {
            _clientAuthoritativeWaitWarnedRoundIndex = liveRoundIndex;
            Log.Warn($"[ParallelTurnPvp] Client authoritative round result wait exceeded threshold. round={liveRoundIndex} snapshotVersion={snapshotVersion} waitedMs={(int)waitedMs}");
        }
    }

    public void ClearClientAwaitAuthoritativeResult()
    {
        if (_clientAuthoritativeWaitRoundIndex <= 0 || _clientAuthoritativeWaitSnapshotVersion <= 0)
        {
            return;
        }

        int roundIndex = _clientAuthoritativeWaitRoundIndex;
        int snapshotVersion = _clientAuthoritativeWaitSnapshotVersion;
        int waitedMs = (int)(DateTime.UtcNow - _clientAuthoritativeWaitSinceUtc).TotalMilliseconds;
        Log.Info($"[ParallelTurnPvp] Client authoritative round result received. round={roundIndex} snapshotVersion={snapshotVersion} waitedMs={Math.Max(waitedMs, 0)}");
        ResetClientAuthoritativeWaitState();
    }

    public bool CanResolveRound(int liveRoundIndex)
    {
        if (CurrentRound.RoundIndex <= 0 || liveRoundIndex <= 0)
        {
            return false;
        }

        if (CurrentRound.RoundIndex != liveRoundIndex)
        {
            return false;
        }

        if (CurrentRound.HasResolved || CurrentRound.Phase != PvpMatchPhase.Resolving)
        {
            return false;
        }

        if (!ShouldAllowResolveWithSubmissionGrace())
        {
            return false;
        }

        return true;
    }

    public bool ShouldStartRound(int liveRoundIndex)
    {
        if (liveRoundIndex <= 0)
        {
            return false;
        }

        if (CurrentRound.Phase == PvpMatchPhase.MatchEnd)
        {
            return false;
        }

        if (CurrentRound.RoundIndex < liveRoundIndex)
        {
            return true;
        }

        return CurrentRound.RoundIndex == 0;
    }

    public int GetNextSequence(ulong playerId)
    {
        EnsureRoundInitialized(playerId);
        return GetOrCreateLog(playerId).Actions.Count;
    }

    public void AppendAction(PvpAction action)
    {
        if (IsDisconnectedPendingResume && action.ActorPlayerId == RoomSession.LocalPlayerId)
        {
            Log.Warn($"[ParallelTurnPvp] Ignored local action append while disconnected pending resume. player={action.ActorPlayerId} type={action.ActionType} model={action.ModelEntry} reason={DisconnectReason}");
            return;
        }

        EnsureRoundInitialized(action.ActorPlayerId);
        string dedupeKey = GetActionDedupeKey(action);
        if (!CurrentRound.RecordedActionKeys.Add(dedupeKey))
        {
            if (CurrentRound.DuplicateTrackedActionLogCount < PvpRoundState.DuplicateTrackedActionLogBudget)
            {
                CurrentRound.DuplicateTrackedActionLogCount++;
                Log.Info($"[ParallelTurnPvp] Skipped duplicate tracked action. round={CurrentRound.RoundIndex} player={action.ActorPlayerId} type={action.ActionType} runtimeActionId={action.RuntimeActionId?.ToString() ?? "-"} model={action.ModelEntry} target={action.Target.Kind} sample={CurrentRound.DuplicateTrackedActionLogCount}/{PvpRoundState.DuplicateTrackedActionLogBudget}");
            }
            else if (!CurrentRound.DuplicateTrackedActionLogSuppressed)
            {
                CurrentRound.DuplicateTrackedActionLogSuppressed = true;
                Log.Info($"[ParallelTurnPvp] Suppressing additional duplicate tracked action logs for round {CurrentRound.RoundIndex}. budget={PvpRoundState.DuplicateTrackedActionLogBudget}");
            }

            return;
        }

        var log = GetOrCreateLog(action.ActorPlayerId);
        if (log.Locked)
        {
            Log.Warn($"[ParallelTurnPvp] Ignored action append because player log is locked. round={CurrentRound.RoundIndex} player={action.ActorPlayerId} type={action.ActionType} model={action.ModelEntry} runtimeActionId={action.RuntimeActionId?.ToString() ?? "-"} existingActions={log.Actions.Count}");
            CurrentRound.RecordedActionKeys.Remove(dedupeKey);
            return;
        }

        log.Actions.Add(action);
        UpdateIntentState(action);
        BumpPlanningRevision();
        RefreshPlanningFrameCache();
        LogIntentVisibility(action.ActorPlayerId);
        LogPlanningSubmission(action.ActorPlayerId);
    }

    public bool LockPlayer(ulong playerId)
    {
        if (IsDisconnectedPendingResume && playerId == RoomSession.LocalPlayerId)
        {
            Log.Warn($"[ParallelTurnPvp] Ignored local lock request while disconnected pending resume. player={playerId} round={CurrentRound.RoundIndex} reason={DisconnectReason}");
            return false;
        }

        EnsureRoundInitialized(playerId);
        var log = GetOrCreateLog(playerId);
        if (log.Locked)
        {
            Log.Info($"[ParallelTurnPvp] Ignored duplicate lock request. round={CurrentRound.RoundIndex} player={playerId}");
            return false;
        }

        log.Locked = true;
        var intentState = GetOrCreateIntentState(playerId);
        intentState.Locked = true;
        if (CurrentRound.FirstLockedPlayerId == 0)
        {
            CurrentRound.FirstLockedPlayerId = playerId;
            intentState.IsFirstFinisher = true;
            Log.Info($"[ParallelTurnPvp] Player {playerId} locked first for round {CurrentRound.RoundIndex}. pendingHeal={EarlyLockHealAmount}");
        }

        CurrentRound.Phase = CurrentRound.LogsByPlayer.Values.All(l => l.Locked)
            ? PvpMatchPhase.Resolving
            : PvpMatchPhase.LockedWaitingPeer;
        BumpPlanningRevision();
        RefreshPlanningFrameCache();
        LogIntentVisibility(playerId);
        LogPlanningSubmission(playerId);
        return true;
    }

    public void UnlockPlayer(ulong playerId)
    {
        var log = GetOrCreateLog(playerId);
        log.Locked = false;
        GetOrCreateIntentState(playerId).Locked = false;
        CurrentRound.Phase = PvpMatchPhase.Planning;
        BumpPlanningRevision();
        RefreshPlanningFrameCache();
    }

    public Player? ApplyFirstLockRewardIfPending()
    {
        if (CurrentRound.FirstLockRewardGranted || CurrentRound.FirstLockedPlayerId == 0)
        {
            return null;
        }

        if (!_playersById.TryGetValue(CurrentRound.FirstLockedPlayerId, out Player? player) || !player.Creature.IsAlive)
        {
            CurrentRound.FirstLockRewardGranted = true;
            return null;
        }

        if (PvpDelayedExecution.IsLiveDelayedApplyEnabled)
        {
            CurrentRound.FirstLockRewardGranted = true;
            Log.Info($"[ParallelTurnPvp] Early lock reward routed to delayed command plan. player={player.NetId} round={CurrentRound.RoundIndex} heal={EarlyLockHealAmount}");
            return null;
        }

        CurrentRound.FirstLockRewardGranted = true;
        Log.Warn($"[ParallelTurnPvp] Early lock reward is disabled in live combat. player={player.NetId} round={CurrentRound.RoundIndex} heal={EarlyLockHealAmount} reason=ConsoleCmdGameAction caused replay/checksum divergence");
        return null;
    }

    public PvpRoundResult ResolveLiveRound(CombatState combatState)
    {
        int resolvedRoundIndex = CurrentRound.RoundIndex > 0 ? CurrentRound.RoundIndex : combatState.RoundNumber;
        var finalSnapshot = SnapshotFactory.Create(combatState, resolvedRoundIndex, SnapshotVersion + 1);
        (IReadOnlyList<PvpRoundSubmission> compiledSubmissions, string sourceTag) = GetResolveSourceSubmissions();
        LogNetworkSubmissionParity(compiledSubmissions);
        IReadOnlyList<PvpRoundSubmission> submissions = BuildResolverSubmissions(compiledSubmissions);
        var result = _resolver.Resolve(CurrentRound.SnapshotAtRoundStart, submissions, finalSnapshot);
        if (CurrentRound.ResolverForcedLockedPlayers.Count > 0)
        {
            result.Events.Add(new PvpResolvedEvent
            {
                Kind = PvpResolvedEventKind.RoundResolved,
                Text = $"Resolver strict mode forced locked submissions for players [{string.Join(",", CurrentRound.ResolverForcedLockedPlayers.OrderBy(id => id))}]."
            });
        }

        if (CurrentRound.ResolverFallbackPlayers.Count > 0)
        {
            result.Events.Add(new PvpResolvedEvent
            {
                Kind = PvpResolvedEventKind.RoundResolved,
                Text = $"Resolver strict mode synthesized submissions for players [{string.Join(",", CurrentRound.ResolverFallbackPlayers.OrderBy(id => id))}]."
            });
        }

        PvpRoundSummarySink.TryWriteHostRoundSummary(RunState, this, result, submissions);
        CurrentRound.LastResult = result;
        CurrentRound.HasResolved = true;
        CurrentRound.Phase = PvpMatchPhase.RoundEnd;
        CurrentRound.ResolveInputSourceTag = sourceTag;
        LastAuthoritativeResult = result;
        SnapshotVersion++;
        Log.Info($"[ParallelTurnPvp] ResolveLiveRound used resolver submissions. round={resolvedRoundIndex} submissions={submissions.Count} actions={submissions.Sum(submission => submission.Actions.Count)} source={sourceTag} forcedLocked={CurrentRound.ResolverForcedLockedPlayers.Count} synthesized={CurrentRound.ResolverFallbackPlayers.Count}");
        foreach (PvpResolvedEvent resolvedEvent in result.Events)
        {
            Log.Info($"[ParallelTurnPvp] RoundEvent kind={resolvedEvent.Kind} text={resolvedEvent.Text}");
        }
        return result;
    }

    public void ApplyAuthoritativeResult(PvpRoundResult result)
    {
        if (result.Events.Count == 0)
        {
            IReadOnlyList<PvpRoundSubmission> submissions = GetResolverSubmissionsForCurrentRound();
            result = _resolver.Resolve(CurrentRound.SnapshotAtRoundStart, submissions, result.FinalSnapshot);
            Log.Info($"[ParallelTurnPvp] Rebuilt authoritative round summary from planning submissions. round={result.RoundIndex} submissions={submissions.Count} events={result.Events.Count}");
        }

        if (result.FinalSnapshot.SnapshotVersion > SnapshotVersion)
        {
            SnapshotVersion = result.FinalSnapshot.SnapshotVersion;
            Log.Info($"[ParallelTurnPvp] SnapshotVersion aligned to authoritative result. round={result.RoundIndex} snapshotVersion={SnapshotVersion}");
        }

        LastAuthoritativeResult = result;
        CurrentRound.LastResult = result;
        CurrentRound.HasResolved = true;
        CurrentRound.Phase = PvpMatchPhase.RoundEnd;
    }

    public void ApplyAuthoritativePlanningFrame(PvpPlanningFrame frame)
    {
        LastAuthoritativePlanningFrame = frame;
        CurrentRound.RoundIndex = frame.RoundIndex;
        int expectedRoundIndex = frame.RoundIndex;
        int expectedSnapshotVersion = frame.SnapshotVersion > 0
            ? frame.SnapshotVersion
            : CurrentRound.SnapshotAtRoundStart.SnapshotVersion;

        if (CurrentRound.SnapshotAtRoundStart.RoundIndex != expectedRoundIndex ||
            CurrentRound.SnapshotAtRoundStart.SnapshotVersion != expectedSnapshotVersion)
        {
            CurrentRound.SnapshotAtRoundStart = CloneSnapshotWithMeta(
                CurrentRound.SnapshotAtRoundStart,
                expectedRoundIndex,
                expectedSnapshotVersion);
        }

        CurrentRound.Phase = frame.Phase;
        CurrentRound.HasResolved = frame.Phase is PvpMatchPhase.RoundEnd or PvpMatchPhase.MatchEnd;
        CurrentRound.PlanningRevision = Math.Max(frame.Revision, 1);
        if (!string.IsNullOrWhiteSpace(frame.RoomSessionId))
        {
            CurrentRound.RoomSessionId = frame.RoomSessionId;
        }

        CurrentRound.RoomTopology = frame.RoomTopology;
    }

    private static PvpCombatSnapshot CloneSnapshotWithMeta(PvpCombatSnapshot source, int roundIndex, int snapshotVersion)
    {
        var snapshot = new PvpCombatSnapshot
        {
            RoundIndex = roundIndex,
            SnapshotVersion = snapshotVersion
        };

        foreach ((ulong playerId, PvpCreatureSnapshot creature) in source.Heroes)
        {
            snapshot.Heroes[playerId] = creature;
        }

        foreach ((ulong playerId, PvpCreatureSnapshot creature) in source.Frontlines)
        {
            snapshot.Frontlines[playerId] = creature;
        }

        return snapshot;
    }

    public void QueueAuthoritativeSnapshot(PvpCombatSnapshot snapshot)
    {
        CurrentRound.PendingAuthoritativeSnapshot = snapshot;
    }

    public PvpCombatSnapshot? ConsumePendingAuthoritativeSnapshot()
    {
        PvpCombatSnapshot? snapshot = CurrentRound.PendingAuthoritativeSnapshot;
        CurrentRound.PendingAuthoritativeSnapshot = null;
        return snapshot;
    }

    public bool TryMarkRoundStateBroadcast(int snapshotVersion)
    {
        if (snapshotVersion <= LastBroadcastRoundStateVersion)
        {
            return false;
        }

        LastBroadcastRoundStateVersion = snapshotVersion;
        return true;
    }

    public bool TryMarkRoundResultBroadcast(int snapshotVersion)
    {
        if (snapshotVersion <= LastBroadcastRoundResultVersion)
        {
            return false;
        }

        LastBroadcastRoundResultVersion = snapshotVersion;
        return true;
    }

    public bool TryMarkRoundStateReceived(int snapshotVersion)
    {
        if (snapshotVersion <= LastReceivedRoundStateVersion)
        {
            return false;
        }

        LastReceivedRoundStateVersion = snapshotVersion;
        return true;
    }

    public bool TryMarkRoundResultReceived(int snapshotVersion)
    {
        if (snapshotVersion <= LastReceivedRoundResultVersion)
        {
            return false;
        }

        LastReceivedRoundResultVersion = snapshotVersion;
        return true;
    }

    public bool TryMarkPlanningFrameBroadcast(PvpPlanningFrame frame)
    {
        if (frame.RoundIndex < LastBroadcastPlanningRoundIndex)
        {
            return false;
        }

        if (frame.RoundIndex == LastBroadcastPlanningRoundIndex && frame.Revision <= LastBroadcastPlanningRevision)
        {
            return false;
        }

        LastBroadcastPlanningRoundIndex = frame.RoundIndex;
        LastBroadcastPlanningRevision = frame.Revision;
        return true;
    }

    public bool TryMarkPlanningFrameReceived(int roundIndex, int revision)
    {
        if (roundIndex < LastReceivedPlanningRoundIndex)
        {
            return false;
        }

        if (roundIndex == LastReceivedPlanningRoundIndex && revision <= LastReceivedPlanningRevision)
        {
            return false;
        }

        LastReceivedPlanningRoundIndex = roundIndex;
        LastReceivedPlanningRevision = revision;
        return true;
    }

    public PvpIntentView? GetIntentView(ulong viewerId, ulong targetId)
    {
        if (!_playersById.ContainsKey(viewerId))
        {
            return null;
        }

        if (TryBuildAuthoritativeIntentView(viewerId, targetId, out PvpIntentView? authoritativeView))
        {
            return authoritativeView;
        }

        if (!CurrentRound.PublicIntentByPlayer.TryGetValue(targetId, out PvpPlayerIntentState? state))
        {
            return null;
        }

        int viewerActionCount = GetRevealActionCount(viewerId);
        int targetActionCount = GetIntentSlotCount(targetId);
        int visibleCount = Math.Min(viewerActionCount, state.Slots.Count);
        int hiddenCount = Math.Max(state.Slots.Count - visibleCount, 0);
        return new PvpIntentView
        {
            RoundIndex = CurrentRound.RoundIndex,
            ViewerId = viewerId,
            TargetId = targetId,
            RoundStartEnergy = state.RoundStartEnergy,
            Locked = state.Locked,
            IsFirstFinisher = state.IsFirstFinisher,
            RevealBudget = viewerActionCount,
            ViewerActionCount = viewerActionCount,
            TargetActionCount = targetActionCount,
            VisibleCount = visibleCount,
            HiddenCount = hiddenCount,
            VisibleSlots = state.Slots.Take(visibleCount).ToList()
        };
    }

    public PvpRoundSubmission? GetPlanningSubmission(ulong playerId)
    {
        if (!CurrentRound.LogsByPlayer.TryGetValue(playerId, out PvpActionLog? log))
        {
            return null;
        }

        CurrentRound.PublicIntentByPlayer.TryGetValue(playerId, out PvpPlayerIntentState? intentState);
        return _planningCompiler.BuildSubmission(CurrentRound.RoundIndex, log, intentState);
    }

    public IReadOnlyList<PvpRoundSubmission> GetPlanningSubmissions()
    {
        return CurrentRound.LogsByPlayer.Keys
            .OrderBy(id => id)
            .Select(GetPlanningSubmission)
            .Where(submission => submission != null)
            .Cast<PvpRoundSubmission>()
            .ToList();
    }

    public IReadOnlyList<PvpRoundSubmission> GetResolverSubmissions()
    {
        return GetResolverSubmissionsForCurrentRound();
    }

    public bool RecordNetworkSubmission(PvpRoundSubmission submission, int snapshotVersion, ulong senderPlayerId, int submissionRevision)
    {
        if (submissionRevision <= 0)
        {
            Log.Warn($"[ParallelTurnPvp] Rejected network submission: invalid revision sender={senderPlayerId} revision={submissionRevision}");
            return false;
        }

        if (submission.PlayerId == 0 || submission.PlayerId != senderPlayerId)
        {
            Log.Warn($"[ParallelTurnPvp] Rejected network submission: sender/player mismatch sender={senderPlayerId} submissionPlayer={submission.PlayerId}");
            return false;
        }

        if (submission.RoundIndex != CurrentRound.RoundIndex)
        {
            Log.Warn($"[ParallelTurnPvp] Rejected network submission: round mismatch sender={senderPlayerId} incomingRound={submission.RoundIndex} localRound={CurrentRound.RoundIndex}");
            return false;
        }

        if (snapshotVersion != CurrentRound.SnapshotAtRoundStart.SnapshotVersion)
        {
            Log.Warn($"[ParallelTurnPvp] Rejected network submission: snapshot mismatch sender={senderPlayerId} incomingSnapshot={snapshotVersion} localSnapshot={CurrentRound.SnapshotAtRoundStart.SnapshotVersion}");
            return false;
        }

        if (!ValidateNetworkSubmissionPayload(submission, senderPlayerId, out string invalidReason))
        {
            Log.Warn($"[ParallelTurnPvp] Rejected network submission: invalid payload sender={senderPlayerId} player={submission.PlayerId} reason={invalidReason}");
            return false;
        }

        bool hasExistingSubmission = CurrentRound.NetworkSubmissionsByPlayer.TryGetValue(submission.PlayerId, out PvpRoundSubmission? existing);
        bool hasExistingRevision = CurrentRound.NetworkSubmissionRevisionByPlayer.TryGetValue(submission.PlayerId, out int existingRevision);
        if (hasExistingRevision)
        {
            if (submissionRevision < existingRevision)
            {
                Log.Warn($"[ParallelTurnPvp] Rejected network submission: stale revision sender={senderPlayerId} player={submission.PlayerId} incomingRevision={submissionRevision} localRevision={existingRevision}");
                return false;
            }

            if (submissionRevision == existingRevision)
            {
                if (hasExistingSubmission && existing != null && IsSameSubmission(existing, submission))
                {
                    Log.Info($"[ParallelTurnPvp] Ignored duplicate network submission. round={submission.RoundIndex} player={submission.PlayerId} revision={submissionRevision}");
                    return false;
                }

                Log.Warn($"[ParallelTurnPvp] Rejected network submission: conflicting payload on same revision sender={senderPlayerId} player={submission.PlayerId} revision={submissionRevision}");
                return false;
            }
        }
        else if (hasExistingSubmission && existing != null && IsSameSubmission(existing, submission))
        {
            Log.Info($"[ParallelTurnPvp] Ignored duplicate network submission. round={submission.RoundIndex} player={submission.PlayerId} revision={submissionRevision}");
            return false;
        }

        CurrentRound.NetworkSubmissionsByPlayer[submission.PlayerId] = CloneSubmission(submission);
        CurrentRound.NetworkSubmissionRevisionByPlayer[submission.PlayerId] = submissionRevision;
        _hostLastNetworkSubmissionUtc = DateTime.UtcNow;
        ApplySubmissionToActionLog(submission);

        if (CurrentRound.PublicIntentByPlayer.TryGetValue(submission.PlayerId, out PvpPlayerIntentState? intentState))
        {
            intentState.Locked = submission.Locked;
            intentState.IsFirstFinisher = submission.IsFirstFinisher;
            if (intentState.RoundStartEnergy <= 0)
            {
                intentState.RoundStartEnergy = submission.RoundStartEnergy;
            }
        }

        if (submission.IsFirstFinisher && CurrentRound.FirstLockedPlayerId == 0)
        {
            CurrentRound.FirstLockedPlayerId = submission.PlayerId;
        }

        if (!CurrentRound.HasResolved)
        {
            bool allLocked = CurrentRound.LogsByPlayer.Values.All(item => item.Locked);
            CurrentRound.Phase = allLocked ? PvpMatchPhase.Resolving : PvpMatchPhase.LockedWaitingPeer;
        }

        BumpPlanningRevision();
        RefreshPlanningFrameCache();
        Log.Info($"[ParallelTurnPvp] Accepted network submission. round={submission.RoundIndex} player={submission.PlayerId} revision={submissionRevision} actions={submission.Actions.Count} locked={submission.Locked} first={submission.IsFirstFinisher}");
        return true;
    }

    public void MarkDisconnectedPendingResume(string source, ulong remotePlayerId, string reason)
    {
        string normalizedReason = string.IsNullOrWhiteSpace(reason) ? "Unknown" : reason;
        if (!IsDisconnectedPendingResume)
        {
            IsDisconnectedPendingResume = true;
            _disconnectedSinceUtc = DateTime.UtcNow;
        }

        DisconnectReason = normalizedReason;
        if (CurrentRound.RoundIndex > 0 &&
            CurrentRound.Phase != PvpMatchPhase.MatchEnd &&
            CurrentRound.Phase != PvpMatchPhase.RoundEnd)
        {
            CurrentRound.Phase = PvpMatchPhase.LockedWaitingPeer;
        }

        Log.Warn($"[ParallelTurnPvp] Entered disconnected-pending-resume. source={source} remote={remotePlayerId} round={CurrentRound.RoundIndex} phase={CurrentRound.Phase} reason={DisconnectReason}");
    }

    public void ClearDisconnectedPendingResume(string source)
    {
        if (!IsDisconnectedPendingResume)
        {
            return;
        }

        int elapsedMs = (int)Math.Max((DateTime.UtcNow - _disconnectedSinceUtc).TotalMilliseconds, 0d);
        IsDisconnectedPendingResume = false;
        DisconnectReason = string.Empty;
        _disconnectedSinceUtc = default;
        Log.Info($"[ParallelTurnPvp] Cleared disconnected-pending-resume. source={source} elapsedMs={elapsedMs}");
    }

    public bool TryGetNetworkSubmissionRevision(ulong playerId, out int revision)
    {
        return CurrentRound.NetworkSubmissionRevisionByPlayer.TryGetValue(playerId, out revision);
    }

    public PvpPlanningFrame BuildPlanningFrame()
    {
        var frame = new PvpPlanningFrame
        {
            RoundIndex = CurrentRound.RoundIndex,
            SnapshotVersion = CurrentRound.SnapshotAtRoundStart.SnapshotVersion,
            Phase = CurrentRound.Phase,
            Revision = CurrentRound.PlanningRevision,
            RoomSessionId = RoomSession.SessionId,
            RoomTopology = RoomSession.Topology
        };

        foreach (ulong playerId in CurrentRound.LogsByPlayer.Keys.OrderBy(id => id))
        {
            PvpRoundSubmission? submission = GetPlanningSubmission(playerId);
            if (submission != null)
            {
                frame.Submissions.Add(submission);
            }
        }

        return frame;
    }

    private PvpActionLog GetOrCreateLog(ulong playerId)
    {
        EnsureCurrentRoundLogs();

        if (CurrentRound.LogsByPlayer.TryGetValue(playerId, out var log))
        {
            return log;
        }

        log = new PvpActionLog { PlayerId = playerId };
        CurrentRound.LogsByPlayer[playerId] = log;
        if (_missingLogWarnings.Add(playerId))
        {
            Log.Warn($"[ParallelTurnPvp] Missing player action log for {playerId}. Creating fallback log entry.");
        }

        return log;
    }

    private void EnsureCurrentRoundLogs()
    {
        if (CurrentRound.LogsByPlayer.Count >= _playersById.Count)
        {
            return;
        }

        foreach ((ulong playerId, _) in _playersById)
        {
            CurrentRound.LogsByPlayer.TryAdd(playerId, new PvpActionLog { PlayerId = playerId });
            CurrentRound.PublicIntentByPlayer.TryAdd(playerId, new PvpPlayerIntentState
            {
                PlayerId = playerId,
                RoundStartEnergy = _playersById[playerId].PlayerCombatState?.Energy ?? _playersById[playerId].MaxEnergy
            });
        }
    }

    private void EnsureRoundInitialized(ulong playerId)
    {
        if (CurrentRound.RoundIndex > 0)
        {
            return;
        }

        if (!_playersById.TryGetValue(playerId, out Player? player))
        {
            return;
        }

        CombatState? combatState = player.Creature.CombatState;
        if (combatState == null)
        {
            return;
        }

        int liveRoundIndex = Math.Max(1, combatState.RoundNumber);
        StartRoundFromLiveState(combatState, liveRoundIndex);
        Log.Info($"[ParallelTurnPvp] Lazily initialized PvP round state from live combat. player={playerId} round={liveRoundIndex}");
    }

    private static string GetActionDedupeKey(PvpAction action)
    {
        if (action.RuntimeActionId != null)
        {
            return $"{action.ActorPlayerId}:{action.RuntimeActionId.Value}";
        }

        return $"{action.ActorPlayerId}:{action.RoundIndex}:{action.ActionType}:{action.ModelEntry}:{action.Target.Kind}:{action.Target.OwnerPlayerId}";
    }

    private PvpPlayerIntentState GetOrCreateIntentState(ulong playerId)
    {
        EnsureCurrentRoundLogs();
        if (CurrentRound.PublicIntentByPlayer.TryGetValue(playerId, out PvpPlayerIntentState? state))
        {
            return state;
        }

        state = new PvpPlayerIntentState
        {
            PlayerId = playerId,
            RoundStartEnergy = _playersById.TryGetValue(playerId, out Player? player) ? player.PlayerCombatState?.Energy ?? player.MaxEnergy : 0
        };
        CurrentRound.PublicIntentByPlayer[playerId] = state;
        return state;
    }

    private void UpdateIntentState(PvpAction action)
    {
        if (!CreatesIntentSlot(action.ActionType))
        {
            return;
        }

        PvpPlayerIntentState state = GetOrCreateIntentState(action.ActorPlayerId);
        state.Slots.Add(new PvpPublicIntentSlot
        {
            Category = PvpIntentClassifier.GetCategory(action),
            TargetSide = PvpIntentClassifier.GetTargetSide(action)
        });
    }

    private void LogIntentVisibility(ulong changedPlayerId)
    {
        foreach ((ulong viewerId, _) in _playersById.OrderBy(entry => entry.Key))
        {
            foreach ((ulong targetId, PvpPlayerIntentState state) in CurrentRound.PublicIntentByPlayer.OrderBy(entry => entry.Key))
            {
                if (viewerId == targetId)
                {
                    continue;
                }

                int viewerActionCount = GetRevealActionCount(viewerId);
                int targetActionCount = GetIntentSlotCount(targetId);
                int visibleCount = Math.Min(viewerActionCount, state.Slots.Count);
                string visible = visibleCount == 0
                    ? "-"
                    : string.Join(", ", state.Slots.Take(visibleCount).Select(slot => $"{slot.Category}/{slot.TargetSide}"));
                int hiddenCount = Math.Max(state.Slots.Count - visibleCount, 0);
                Log.Info($"[ParallelTurnPvp] IntentView viewer={viewerId} target={targetId} startEnergy={state.RoundStartEnergy} locked={state.Locked} firstFinisher={state.IsFirstFinisher} viewerRevealBudget={viewerActionCount} targetSlots={targetActionCount} reveal={visibleCount}/{state.Slots.Count} visible=[{visible}] hidden={hiddenCount} changed={changedPlayerId}");
            }
        }
    }

    private void LogPlanningSubmission(ulong changedPlayerId)
    {
        PvpRoundSubmission? submission = GetPlanningSubmission(changedPlayerId);
        if (submission == null)
        {
            return;
        }

        string actions = submission.Actions.Count == 0
            ? "-"
            : string.Join(", ", submission.Actions.Select(action => $"{action.Sequence + 1}:{action.ActionType}/{action.ModelEntry}->{action.Target.Kind}[id={action.RuntimeActionId?.ToString() ?? "-"}]"));
        Log.Info($"[ParallelTurnPvp] PlanningSubmission round={submission.RoundIndex} player={submission.PlayerId} energy={submission.RoundStartEnergy} locked={submission.Locked} first={submission.IsFirstFinisher} actions={submission.Actions.Count} [{actions}]");
    }

    private void BumpPlanningRevision()
    {
        CurrentRound.PlanningRevision = Math.Max(CurrentRound.PlanningRevision + 1, 1);
    }

    private void RefreshPlanningFrameCache()
    {
        if (CurrentRound.RoundIndex <= 0 || CurrentRound.SnapshotAtRoundStart.SnapshotVersion <= 0)
        {
            return;
        }

        LastAuthoritativePlanningFrame = BuildPlanningFrame();
    }

    private int GetRevealActionCount(ulong playerId)
    {
        return CurrentRound.LogsByPlayer.TryGetValue(playerId, out PvpActionLog? log)
            ? log.Actions.Count(action => action.ActionType == PvpActionType.PlayCard)
            : 0;
    }

    private int GetIntentSlotCount(ulong playerId)
    {
        return CurrentRound.LogsByPlayer.TryGetValue(playerId, out PvpActionLog? log)
            ? log.Actions.Count(action => CreatesIntentSlot(action.ActionType))
            : 0;
    }

    private IReadOnlyList<PvpRoundSubmission> GetResolverSubmissionsForCurrentRound()
    {
        (IReadOnlyList<PvpRoundSubmission> compiled, _) = GetResolveSourceSubmissions();
        return BuildResolverSubmissions(compiled);
    }

    private (IReadOnlyList<PvpRoundSubmission> submissions, string sourceTag) GetResolveSourceSubmissions()
    {
        if (LastAuthoritativePlanningFrame != null && LastAuthoritativePlanningFrame.RoundIndex == CurrentRound.RoundIndex)
        {
            return (LastAuthoritativePlanningFrame.Submissions, "authoritative_planning_frame");
        }

        return (GetPlanningSubmissions(), "local_compiled_logs");
    }

    private IReadOnlyList<PvpRoundSubmission> BuildResolverSubmissions(IReadOnlyList<PvpRoundSubmission> compiledSubmissions)
    {
        CurrentRound.ResolverFallbackPlayers.Clear();
        CurrentRound.ResolverForcedLockedPlayers.Clear();
        if (RunManager.Instance.NetService.Type != NetGameType.Host || !ParallelTurnFrontlineHelper.IsSplitRoomActive(RunState))
        {
            return compiledSubmissions;
        }

        Dictionary<ulong, PvpRoundSubmission> compiledByPlayer = compiledSubmissions
            .OrderBy(submission => submission.PlayerId)
            .ToDictionary(submission => submission.PlayerId, CloneSubmission);
        var merged = new Dictionary<ulong, PvpRoundSubmission>();

        foreach (ulong playerId in _playersById.Keys.OrderBy(id => id))
        {
            if (playerId == RoomSession.LocalPlayerId)
            {
                if (compiledByPlayer.TryGetValue(playerId, out PvpRoundSubmission? localCompiled))
                {
                    merged[playerId] = CloneSubmission(localCompiled);
                }

                continue;
            }

            if (CurrentRound.NetworkSubmissionsByPlayer.TryGetValue(playerId, out PvpRoundSubmission? networkSubmission))
            {
                PvpRoundSubmission resolvedRemoteSubmission = CloneSubmission(networkSubmission);
                if (!resolvedRemoteSubmission.Locked)
                {
                    resolvedRemoteSubmission = ForceLockedSubmission(resolvedRemoteSubmission);
                    CurrentRound.ResolverForcedLockedPlayers.Add(playerId);
                    Log.Warn($"[ParallelTurnPvp] Resolver strict mode: remote submission not locked at resolve boundary; forced locked. round={CurrentRound.RoundIndex} player={playerId}");
                }

                merged[playerId] = resolvedRemoteSubmission;
                continue;
            }

            PvpRoundSubmission synthesized = CreateSyntheticLockedSubmission(playerId);
            merged[playerId] = synthesized;
            CurrentRound.ResolverFallbackPlayers.Add(playerId);
            Log.Warn($"[ParallelTurnPvp] Resolver strict mode: missing remote submission; using synthesized locked submission. round={CurrentRound.RoundIndex} player={playerId} actions={synthesized.Actions.Count}");
        }

        if (CurrentRound.ResolverFallbackPlayers.Count > 0)
        {
            Log.Warn($"[ParallelTurnPvp] Resolver strict fallback summary. round={CurrentRound.RoundIndex} synthesizedPlayers=[{string.Join(",", CurrentRound.ResolverFallbackPlayers.OrderBy(id => id))}]");
        }

        return merged
            .OrderBy(entry => entry.Key)
            .Select(entry => entry.Value)
            .ToList();
    }

    private bool TryBuildAuthoritativeIntentView(ulong viewerId, ulong targetId, out PvpIntentView? view)
    {
        view = null;
        PvpPlanningFrame? frame = GetDisplayPlanningFrame();
        if (frame == null)
        {
            return false;
        }

        PvpRoundSubmission? viewerSubmission = frame.Submissions.FirstOrDefault(submission => submission.PlayerId == viewerId);
        PvpRoundSubmission? targetSubmission = frame.Submissions.FirstOrDefault(submission => submission.PlayerId == targetId);
        if (viewerSubmission == null || targetSubmission == null)
        {
            return false;
        }

        List<PvpPublicIntentSlot> targetSlots = targetSubmission.Actions
            .Where(action => CreatesIntentSlot(action.ActionType))
            .Select(CreateIntentSlot)
            .ToList();
        int viewerActionCount = viewerSubmission.Actions.Count(action => action.ActionType == PvpActionType.PlayCard);
        int targetActionCount = targetSubmission.Actions.Count(action => CreatesIntentSlot(action.ActionType));
        int visibleCount = Math.Min(viewerActionCount, targetSlots.Count);
        view = new PvpIntentView
        {
            RoundIndex = frame.RoundIndex,
            ViewerId = viewerId,
            TargetId = targetId,
            RoundStartEnergy = targetSubmission.RoundStartEnergy,
            Locked = targetSubmission.Locked,
            IsFirstFinisher = targetSubmission.IsFirstFinisher,
            RevealBudget = viewerActionCount,
            ViewerActionCount = viewerActionCount,
            TargetActionCount = targetActionCount,
            VisibleCount = visibleCount,
            HiddenCount = Math.Max(targetSlots.Count - visibleCount, 0),
            VisibleSlots = targetSlots.Take(visibleCount).ToList()
        };
        return true;
    }

    private PvpPlanningFrame? GetDisplayPlanningFrame()
    {
        if (LastAuthoritativePlanningFrame == null)
        {
            return null;
        }

        if (CurrentRound.RoundIndex > 0 && LastAuthoritativePlanningFrame.RoundIndex != CurrentRound.RoundIndex)
        {
            return null;
        }

        return LastAuthoritativePlanningFrame;
    }

    private static PvpPublicIntentSlot CreateIntentSlot(PvpPlannedAction action)
    {
        PvpAction classifierAction = new()
        {
            ActionType = action.ActionType,
            ModelEntry = action.ModelEntry,
            Target = action.Target
        };
        return new PvpPublicIntentSlot
        {
            Category = PvpIntentClassifier.GetCategory(classifierAction),
            TargetSide = PvpIntentClassifier.GetTargetSide(classifierAction)
        };
    }

    private void LogNetworkSubmissionParity(IReadOnlyList<PvpRoundSubmission> compiledSubmissions)
    {
        if (!ParallelTurnFrontlineHelper.IsSplitRoomActive(RunState) || CurrentRound.NetworkSubmissionsByPlayer.Count == 0)
        {
            return;
        }

        foreach (PvpRoundSubmission compiled in compiledSubmissions)
        {
            if (!CurrentRound.NetworkSubmissionsByPlayer.TryGetValue(compiled.PlayerId, out PvpRoundSubmission? network))
            {
                continue;
            }

            string compiledSig = BuildSubmissionSignature(compiled);
            string networkSig = BuildSubmissionSignature(network);
            bool same = string.Equals(compiledSig, networkSig, StringComparison.Ordinal);
            Log.Info($"[ParallelTurnPvp] SubmissionParity round={compiled.RoundIndex} player={compiled.PlayerId} match={same} compiled={compiledSig} network={networkSig}");
        }
    }

    private static string BuildSubmissionSignature(PvpRoundSubmission submission)
    {
        if (submission.Actions.Count == 0)
        {
            return "empty";
        }

        return string.Join("|", submission.Actions.Select(action =>
            $"{action.Sequence}:{action.ActionType}:{action.ModelEntry}:{action.Target.Kind}:{action.Target.OwnerPlayerId}"));
    }

    private bool ShouldAllowResolveWithSubmissionGrace()
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host || !ParallelTurnFrontlineHelper.IsSplitRoomActive(RunState))
        {
            return true;
        }

        IReadOnlyList<ulong> missingPlayers = GetMissingNetworkSubmissionPlayers();
        if (missingPlayers.Count == 0)
        {
            ResetResolveWaitState();
            return true;
        }

        int timeoutMs = ComputeHostResolveWaitTimeoutMs(missingPlayers, out string timeoutReason);

        if (_hostResolveWaitRoundIndex != CurrentRound.RoundIndex)
        {
            _hostResolveWaitRoundIndex = CurrentRound.RoundIndex;
            _hostResolveWaitSinceUtc = DateTime.UtcNow;
            _hostResolveWaitLoggedSecond = -1;
            _hostResolveTimeoutWarnedRoundIndex = 0;
            Log.Warn($"[ParallelTurnPvp] Resolve waiting for network submission. round={CurrentRound.RoundIndex} missing=[{string.Join(",", missingPlayers)}] timeoutMs={timeoutMs} reason={timeoutReason}");
            return false;
        }

        double waitedMs = (DateTime.UtcNow - _hostResolveWaitSinceUtc).TotalMilliseconds;
        int waitedSecond = (int)(waitedMs / 1000d);
        if (waitedMs < timeoutMs)
        {
            if (waitedSecond != _hostResolveWaitLoggedSecond)
            {
                _hostResolveWaitLoggedSecond = waitedSecond;
                Log.Info($"[ParallelTurnPvp] Resolve still waiting. round={CurrentRound.RoundIndex} waitedMs={(int)waitedMs} timeoutMs={timeoutMs} reason={timeoutReason} missing=[{string.Join(",", missingPlayers)}]");
            }

            return false;
        }

        if (_hostResolveTimeoutWarnedRoundIndex != CurrentRound.RoundIndex)
        {
            _hostResolveTimeoutWarnedRoundIndex = CurrentRound.RoundIndex;
            Log.Warn($"[ParallelTurnPvp] Resolve wait timeout reached. round={CurrentRound.RoundIndex} waitedMs={(int)waitedMs} timeoutMs={timeoutMs} reason={timeoutReason} missing=[{string.Join(",", missingPlayers)}] fallback=network_strict");
        }

        return true;
    }

    private int ComputeHostResolveWaitTimeoutMs(IReadOnlyList<ulong> missingPlayers, out string reason)
    {
        int timeoutMs = HostResolveWaitTimeoutMs;
        List<string> reasons = new();

        if (_hostLastNetworkSubmissionUtc != default &&
            (DateTime.UtcNow - _hostLastNetworkSubmissionUtc).TotalMilliseconds <= HostResolveWaitRecentSubmissionWindowMs)
        {
            timeoutMs += HostResolveWaitRecentSubmissionGraceMs;
            reasons.Add("recent_network_activity");
        }

        if (AreAllMissingPlayersLockedLocally(missingPlayers))
        {
            timeoutMs += HostResolveWaitLockedPeerGraceMs;
            reasons.Add("missing_peer_locked_local_log");
        }

        reason = reasons.Count == 0 ? "base" : string.Join("+", reasons);
        return timeoutMs;
    }

    private bool AreAllMissingPlayersLockedLocally(IReadOnlyList<ulong> missingPlayers)
    {
        if (missingPlayers.Count == 0)
        {
            return false;
        }

        foreach (ulong playerId in missingPlayers)
        {
            if (!CurrentRound.LogsByPlayer.TryGetValue(playerId, out PvpActionLog? log) || !log.Locked)
            {
                return false;
            }
        }

        return true;
    }

    private IReadOnlyList<ulong> GetMissingNetworkSubmissionPlayers()
    {
        var missingPlayers = new List<ulong>();
        foreach (ulong playerId in _playersById.Keys.OrderBy(id => id))
        {
            if (playerId == RoomSession.LocalPlayerId)
            {
                continue;
            }

            if (!CurrentRound.LogsByPlayer.TryGetValue(playerId, out PvpActionLog? log) || !log.Locked)
            {
                missingPlayers.Add(playerId);
                continue;
            }

            if (!CurrentRound.NetworkSubmissionsByPlayer.TryGetValue(playerId, out PvpRoundSubmission? submission) ||
                submission.RoundIndex != CurrentRound.RoundIndex ||
                !submission.Locked)
            {
                missingPlayers.Add(playerId);
            }
        }

        return missingPlayers;
    }

    private void ResetResolveWaitState()
    {
        _hostResolveWaitRoundIndex = 0;
        _hostResolveWaitSinceUtc = default;
        _hostResolveWaitLoggedSecond = -1;
        _hostResolveTimeoutWarnedRoundIndex = 0;
    }

    private void ResetClientAuthoritativeWaitState()
    {
        _clientAuthoritativeWaitRoundIndex = 0;
        _clientAuthoritativeWaitSnapshotVersion = 0;
        _clientAuthoritativeWaitSinceUtc = default;
        _clientAuthoritativeWaitLoggedSecond = -1;
        _clientAuthoritativeWaitWarnedRoundIndex = 0;
    }

    private static bool IsSameSubmission(PvpRoundSubmission left, PvpRoundSubmission right)
    {
        bool sameLock = left.Locked == right.Locked && left.IsFirstFinisher == right.IsFirstFinisher;
        bool sameEnergy = left.RoundStartEnergy == right.RoundStartEnergy;
        bool sameSignature = string.Equals(BuildSubmissionSignature(left), BuildSubmissionSignature(right), StringComparison.Ordinal);
        return sameLock && sameEnergy && sameSignature;
    }

    private static PvpRoundSubmission CloneSubmission(PvpRoundSubmission source)
    {
        var clone = new PvpRoundSubmission
        {
            RoundIndex = source.RoundIndex,
            PlayerId = source.PlayerId,
            RoundStartEnergy = source.RoundStartEnergy,
            Locked = source.Locked,
            IsFirstFinisher = source.IsFirstFinisher
        };

        foreach (PvpPlannedAction action in source.Actions.OrderBy(item => item.Sequence))
        {
            clone.Actions.Add(new PvpPlannedAction
            {
                Sequence = action.Sequence,
                RuntimeActionId = action.RuntimeActionId,
                ActionType = action.ActionType,
                ModelEntry = action.ModelEntry,
                Target = action.Target
            });
        }

        return clone;
    }

    private PvpRoundSubmission ForceLockedSubmission(PvpRoundSubmission source)
    {
        var locked = new PvpRoundSubmission
        {
            RoundIndex = CurrentRound.RoundIndex,
            PlayerId = source.PlayerId,
            RoundStartEnergy = source.RoundStartEnergy,
            Locked = true,
            IsFirstFinisher = source.IsFirstFinisher
        };

        foreach (PvpPlannedAction action in source.Actions.OrderBy(item => item.Sequence))
        {
            locked.Actions.Add(new PvpPlannedAction
            {
                Sequence = action.Sequence,
                RuntimeActionId = action.RuntimeActionId,
                ActionType = action.ActionType,
                ModelEntry = action.ModelEntry,
                Target = action.Target
            });
        }

        return locked;
    }

    private PvpRoundSubmission CreateSyntheticLockedSubmission(ulong playerId)
    {
        int roundStartEnergy = CurrentRound.PublicIntentByPlayer.TryGetValue(playerId, out PvpPlayerIntentState? state)
            ? state.RoundStartEnergy
            : 0;

        return new PvpRoundSubmission
        {
            RoundIndex = CurrentRound.RoundIndex,
            PlayerId = playerId,
            RoundStartEnergy = Math.Max(roundStartEnergy, 0),
            Locked = true,
            IsFirstFinisher = CurrentRound.FirstLockedPlayerId == playerId
        };
    }

    private static bool CreatesIntentSlot(PvpActionType actionType)
    {
        return actionType is PvpActionType.PlayCard or PvpActionType.UsePotion;
    }

    private bool ValidateNetworkSubmissionPayload(PvpRoundSubmission submission, ulong senderPlayerId, out string reason)
    {
        const int MaxActionsPerSubmission = 64;
        reason = string.Empty;

        if (submission.Actions.Count > MaxActionsPerSubmission)
        {
            reason = $"too_many_actions count={submission.Actions.Count} limit={MaxActionsPerSubmission}";
            return false;
        }

        int expectedSequence = 0;
        bool hasEndRound = false;
        int endRoundIndex = -1;
        var runtimeActionIds = new HashSet<uint>();

        for (int i = 0; i < submission.Actions.Count; i++)
        {
            PvpPlannedAction action = submission.Actions[i];
            if (action.Sequence != expectedSequence)
            {
                reason = $"invalid_sequence idx={i} expected={expectedSequence} actual={action.Sequence}";
                return false;
            }

            expectedSequence++;

            if (action.RuntimeActionId is uint runtimeActionId && !runtimeActionIds.Add(runtimeActionId))
            {
                reason = $"duplicate_runtime_action_id id={runtimeActionId}";
                return false;
            }

            if (action.ActionType is not (PvpActionType.PlayCard or PvpActionType.UsePotion or PvpActionType.EndRound))
            {
                reason = $"invalid_action_type type={action.ActionType}";
                return false;
            }

            if (string.IsNullOrWhiteSpace(action.ModelEntry))
            {
                reason = $"empty_model_entry idx={i}";
                return false;
            }

            if (action.ActionType == PvpActionType.EndRound)
            {
                if (hasEndRound)
                {
                    reason = "duplicate_end_round_action";
                    return false;
                }

                hasEndRound = true;
                endRoundIndex = i;
            }

            if (!_playersById.ContainsKey(action.Target.OwnerPlayerId))
            {
                reason = $"invalid_target_owner owner={action.Target.OwnerPlayerId}";
                return false;
            }

            if (action.ActionType == PvpActionType.EndRound && action.Target.Kind != PvpTargetKind.None)
            {
                reason = $"end_round_requires_none_target target={action.Target.Kind}";
                return false;
            }
        }

        if (hasEndRound && endRoundIndex != submission.Actions.Count - 1)
        {
            reason = $"end_round_not_last index={endRoundIndex} count={submission.Actions.Count}";
            return false;
        }

        if (submission.Locked && !hasEndRound && submission.PlayerId == senderPlayerId)
        {
            reason = "locked_submission_missing_end_round";
            return false;
        }

        if (!submission.Locked && hasEndRound)
        {
            reason = "unlocked_submission_contains_end_round";
            return false;
        }

        return true;
    }

    private void ApplySubmissionToActionLog(PvpRoundSubmission submission)
    {
        var log = new PvpActionLog
        {
            PlayerId = submission.PlayerId,
            Locked = submission.Locked
        };

        foreach (PvpPlannedAction action in submission.Actions.OrderBy(item => item.Sequence))
        {
            var runtimeAction = new PvpAction
            {
                ActorPlayerId = submission.PlayerId,
                RoundIndex = submission.RoundIndex,
                Sequence = action.Sequence,
                RuntimeActionId = action.RuntimeActionId,
                ActionType = action.ActionType,
                ModelEntry = action.ModelEntry,
                Target = action.Target
            };
            log.Actions.Add(runtimeAction);
            CurrentRound.RecordedActionKeys.Add(GetActionDedupeKey(runtimeAction));
        }

        CurrentRound.LogsByPlayer[submission.PlayerId] = log;

        PvpPlayerIntentState state = GetOrCreateIntentState(submission.PlayerId);
        state.RoundStartEnergy = submission.RoundStartEnergy;
        state.Locked = submission.Locked;
        state.IsFirstFinisher = submission.IsFirstFinisher;
        state.Slots.Clear();
        foreach (PvpPlannedAction action in submission.Actions.Where(item => CreatesIntentSlot(item.ActionType)))
        {
            state.Slots.Add(CreateIntentSlot(action));
        }
    }

}

internal static class PvpIntentClassifier
{
    public static PvpIntentCategory GetCategory(PvpAction action)
    {
        return action.ModelEntry switch
        {
            "STRIKE_NECROBINDER" => PvpIntentCategory.Attack,
            "DEFEND_NECROBINDER" => PvpIntentCategory.Guard,
            "AFTERLIFE" => PvpIntentCategory.Summon,
            "POKE" => PvpIntentCategory.Attack,
            "FRONTLINE_BRACE" => PvpIntentCategory.Buff,
            "BREAK_FORMATION" => PvpIntentCategory.Attack,
            "BLOCK_POTION" => PvpIntentCategory.Guard,
            "ENERGY_POTION" => PvpIntentCategory.Resource,
            "BLOOD_POTION" => PvpIntentCategory.Recover,
            "FRONTLINE_SALVE" => PvpIntentCategory.Recover,
            _ => action.ActionType == PvpActionType.UsePotion ? PvpIntentCategory.Resource : PvpIntentCategory.Unknown
        };
    }

    public static PvpIntentTargetSide GetTargetSide(PvpAction action)
    {
        return action.Target.Kind switch
        {
            PvpTargetKind.SelfHero or PvpTargetKind.SelfFrontline => PvpIntentTargetSide.Self,
            PvpTargetKind.EnemyHero or PvpTargetKind.EnemyFrontline => PvpIntentTargetSide.Enemy,
            _ => PvpIntentTargetSide.None
        };
    }
}

internal static class SnapshotFactory
{
    public static PvpCombatSnapshot Create(CombatState combatState, int roundIndex, int snapshotVersion)
    {
        var snapshot = new PvpCombatSnapshot
        {
            RoundIndex = roundIndex,
            SnapshotVersion = snapshotVersion
        };

        foreach (var player in combatState.Players)
        {
            snapshot.Heroes[player.NetId] = CreateCreatureSnapshot(player.Creature);
            var frontline = ParallelTurnFrontlineHelper.GetFrontline(player);
            snapshot.Frontlines[player.NetId] = frontline == null
                ? new PvpCreatureSnapshot { Exists = false }
                : CreateCreatureSnapshot(frontline);
        }

        return snapshot;
    }

    // Live combat can retain stale block values at round-boundary snapshot timing.
    // For planning/prediction, normalize to round-start semantics where block has decayed.
    public static PvpCombatSnapshot NormalizeForPlanning(PvpCombatSnapshot snapshot)
    {
        return new PvpCombatSnapshot
        {
            RoundIndex = snapshot.RoundIndex,
            SnapshotVersion = snapshot.SnapshotVersion,
            Heroes = snapshot.Heroes.ToDictionary(
                entry => entry.Key,
                entry => NormalizeCreature(entry.Value)),
            Frontlines = snapshot.Frontlines.ToDictionary(
                entry => entry.Key,
                entry => NormalizeCreature(entry.Value))
        };
    }

    private static PvpCreatureSnapshot NormalizeCreature(PvpCreatureSnapshot snapshot)
    {
        return new PvpCreatureSnapshot
        {
            Exists = snapshot.Exists,
            CurrentHp = snapshot.CurrentHp,
            MaxHp = snapshot.MaxHp,
            Block = 0
        };
    }

    private static PvpCreatureSnapshot CreateCreatureSnapshot(Creature creature)
    {
        return new PvpCreatureSnapshot
        {
            Exists = creature.IsAlive || creature.MaxHp > 0,
            CurrentHp = creature.CurrentHp,
            MaxHp = creature.MaxHp,
            Block = creature.Block
        };
    }
}
