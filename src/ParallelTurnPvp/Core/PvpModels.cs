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
    public PvpRoundExecutionPlan? ExecutionPlan { get; set; }
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
    public List<PvpExecutionStep> Steps { get; } = new();
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
    public List<PvpRoundSubmission> Submissions { get; } = new();
}

public sealed class PvpRoundState
{
    public int RoundIndex { get; set; }
    public PvpMatchPhase Phase { get; set; }
    public int PlanningRevision { get; set; }
    public PvpCombatSnapshot SnapshotAtRoundStart { get; set; } = new();
    public Dictionary<ulong, PvpActionLog> LogsByPlayer { get; } = new();
    public Dictionary<ulong, PvpPlayerIntentState> PublicIntentByPlayer { get; } = new();
    public PvpRoundResult? LastResult { get; set; }
    public bool HasResolved { get; set; }
    public ulong FirstLockedPlayerId { get; set; }
    public bool FirstLockRewardGranted { get; set; }
    public PvpCombatSnapshot? PendingAuthoritativeSnapshot { get; set; }
    public HashSet<string> RecordedActionKeys { get; } = new();
}

public sealed class PvpMatchRuntime
{
    public const int EarlyLockHealAmount = 3;
    private readonly Dictionary<ulong, Player> _playersById;
    private readonly IPvpRoundResolver _resolver;
    private readonly IPvpPlanningCompiler _planningCompiler;
    private readonly HashSet<ulong> _missingLogWarnings = new();

    public PvpMatchRuntime(RunState runState, IEnumerable<Player> players)
    {
        RunState = runState;
        _playersById = players.ToDictionary(player => player.NetId);
        _resolver = new PvpRoundResolver();
        _planningCompiler = new PvpPlanningCompiler();
    }

    public RunState RunState { get; }
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

    public void BeginCombat(CombatState combatState)
    {
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
        SnapshotVersion++;
        var snapshot = SnapshotFactory.Create(combatState, roundIndex, SnapshotVersion);
        CurrentRound = new PvpRoundState
        {
            RoundIndex = roundIndex,
            Phase = PvpMatchPhase.Planning,
            PlanningRevision = 1,
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

        Log.Info($"[ParallelTurnPvp] StartRoundFromLiveState round={roundIndex} logs=[{string.Join(", ", CurrentRound.LogsByPlayer.Keys.OrderBy(id => id))}] intents=[{string.Join(", ", CurrentRound.PublicIntentByPlayer.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}:energy={entry.Value.RoundStartEnergy}"))}]");
        _missingLogWarnings.Clear();
        RefreshPlanningFrameCache();
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

        return !CurrentRound.HasResolved && CurrentRound.Phase == PvpMatchPhase.Resolving;
    }

    public bool ShouldStartRound(int liveRoundIndex)
    {
        if (liveRoundIndex <= 0)
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
        EnsureRoundInitialized(action.ActorPlayerId);
        var log = GetOrCreateLog(action.ActorPlayerId);
        if (log.Locked)
        {
            return;
        }

        string dedupeKey = GetActionDedupeKey(action);
        if (!CurrentRound.RecordedActionKeys.Add(dedupeKey))
        {
            Log.Info($"[ParallelTurnPvp] Skipped duplicate tracked action. round={CurrentRound.RoundIndex} player={action.ActorPlayerId} type={action.ActionType} runtimeActionId={action.RuntimeActionId?.ToString() ?? "-"} model={action.ModelEntry} target={action.Target.Kind}");
            return;
        }

        log.Actions.Add(action);
        UpdateIntentState(action);
        BumpPlanningRevision();
        RefreshPlanningFrameCache();
        LogIntentVisibility(action.ActorPlayerId);
        LogPlanningSubmission(action.ActorPlayerId);
    }

    public void LockPlayer(ulong playerId)
    {
        EnsureRoundInitialized(playerId);
        var log = GetOrCreateLog(playerId);
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

        CurrentRound.FirstLockRewardGranted = true;
        Log.Warn($"[ParallelTurnPvp] Early lock reward is disabled in live combat. player={player.NetId} round={CurrentRound.RoundIndex} heal={EarlyLockHealAmount} reason=ConsoleCmdGameAction caused replay/checksum divergence");
        return null;
    }

    public PvpRoundResult ResolveLiveRound(CombatState combatState)
    {
        int resolvedRoundIndex = CurrentRound.RoundIndex > 0 ? CurrentRound.RoundIndex : combatState.RoundNumber;
        var finalSnapshot = SnapshotFactory.Create(combatState, resolvedRoundIndex, SnapshotVersion + 1);
        IReadOnlyList<PvpRoundSubmission> submissions = GetPlanningSubmissions();
        var result = _resolver.Resolve(CurrentRound.SnapshotAtRoundStart, submissions, finalSnapshot);
        CurrentRound.LastResult = result;
        CurrentRound.HasResolved = true;
        CurrentRound.Phase = PvpMatchPhase.RoundEnd;
        LastAuthoritativeResult = result;
        SnapshotVersion++;
        Log.Info($"[ParallelTurnPvp] ResolveLiveRound used planning submissions. round={resolvedRoundIndex} submissions={submissions.Count} actions={submissions.Sum(submission => submission.Actions.Count)}");
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

        LastAuthoritativeResult = result;
        CurrentRound.LastResult = result;
        CurrentRound.HasResolved = true;
        CurrentRound.Phase = PvpMatchPhase.RoundEnd;
    }

    public void ApplyAuthoritativePlanningFrame(PvpPlanningFrame frame)
    {
        LastAuthoritativePlanningFrame = frame;
        if (frame.RoundIndex > CurrentRound.RoundIndex)
        {
            CurrentRound.RoundIndex = frame.RoundIndex;
        }

        if (frame.RoundIndex == CurrentRound.RoundIndex)
        {
            CurrentRound.Phase = frame.Phase;
        }
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
        int targetActionCount = GetRevealActionCount(targetId);
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

    public PvpPlanningFrame BuildPlanningFrame()
    {
        var frame = new PvpPlanningFrame
        {
            RoundIndex = CurrentRound.RoundIndex,
            SnapshotVersion = CurrentRound.SnapshotAtRoundStart.SnapshotVersion,
            Phase = CurrentRound.Phase,
            Revision = CurrentRound.PlanningRevision
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
        if (action.ActionType is not (PvpActionType.PlayCard or PvpActionType.UsePotion))
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
                int targetActionCount = GetRevealActionCount(targetId);
                int visibleCount = Math.Min(viewerActionCount, state.Slots.Count);
                string visible = visibleCount == 0
                    ? "-"
                    : string.Join(", ", state.Slots.Take(visibleCount).Select(slot => $"{slot.Category}/{slot.TargetSide}"));
                int hiddenCount = Math.Max(state.Slots.Count - visibleCount, 0);
                Log.Info($"[ParallelTurnPvp] IntentView viewer={viewerId} target={targetId} startEnergy={state.RoundStartEnergy} locked={state.Locked} firstFinisher={state.IsFirstFinisher} viewerActions={viewerActionCount} targetActions={targetActionCount} reveal={visibleCount}/{state.Slots.Count} visible=[{visible}] hidden={hiddenCount} changed={changedPlayerId}");
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
            ? log.Actions.Count(action => action.ActionType is PvpActionType.PlayCard or PvpActionType.UsePotion)
            : 0;
    }

    private IReadOnlyList<PvpRoundSubmission> GetResolverSubmissionsForCurrentRound()
    {
        if (LastAuthoritativePlanningFrame != null && LastAuthoritativePlanningFrame.RoundIndex == CurrentRound.RoundIndex)
        {
            return LastAuthoritativePlanningFrame.Submissions;
        }

        return GetPlanningSubmissions();
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
            .Where(action => action.ActionType is PvpActionType.PlayCard or PvpActionType.UsePotion)
            .Select(CreateIntentSlot)
            .ToList();
        int viewerActionCount = viewerSubmission.Actions.Count(action => action.ActionType is PvpActionType.PlayCard or PvpActionType.UsePotion);
        int targetActionCount = targetSubmission.Actions.Count(action => action.ActionType is PvpActionType.PlayCard or PvpActionType.UsePotion);
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
