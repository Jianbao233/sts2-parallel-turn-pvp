using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using HarmonyLib;
using System.Runtime.CompilerServices;

namespace ParallelTurnPvp.Core;

public sealed class PvpNetBridge : IPvpSyncBridge
{
    private const string ForceSwitchKickEnv = "PTPVP_ENABLE_FORCE_SWITCH_KICK";
    private const string RoundNumberMutationEnv = "PTPVP_ENABLE_ROUND_NUMBER_MUTATION";
    private const int MaxNetworkRoundEvents = 48;
    private const int MaxNetworkEventTextLength = 220;
    private const int ClientSubmissionRetryIntervalFastMs = 300;
    private const int ClientSubmissionRetryIntervalNormalMs = 500;
    private const int ClientSubmissionRetryIntervalSlowMs = 800;
    private const int DuplicateClientSubmissionSendWindowMs = 180;
    private const int MaxClientSubmissionRetriesPerRound = 12;
    private const int ResumeStateRequestIntervalMs = 1200;
    private const int ClientPlanningResyncGraceMs = 900;
    private const int ClientPlanningResyncIntervalMs = 1500;
    private const int HostResolveFallbackKickIntervalMs = 700;
    private const int RoundAlignKickIntervalMs = 700;
    private sealed class ClientSubmissionRetryState
    {
        public int RoundIndex { get; set; }
        public int SnapshotVersion { get; set; }
        public int Revision { get; set; }
        public ulong PlayerId { get; set; }
        public bool Locked { get; set; }
        public int ActionCount { get; set; }
        public DateTime LastSentUtc { get; set; }
        public int RetryCount { get; set; }
        public int AckRoundIndex { get; set; }
        public int AckSnapshotVersion { get; set; }
        public int AckRevision { get; set; }
        public DateTime LastAckUtc { get; set; }
        public bool ExhaustedLogged { get; set; }
    }

    private sealed class ClientResumeRequestState
    {
        public DateTime LastSentUtc { get; set; }
        public int AttemptCount { get; set; }
        public int LastRequestedRoundIndex { get; set; }
        public int LastRequestedSnapshotVersion { get; set; }
        public int LastRequestedPlanningRevision { get; set; }
    }

    private sealed class ClientPlanningResyncState
    {
        public int RoundIndex { get; set; }
        public int SnapshotVersion { get; set; }
        public int AckRevision { get; set; }
        public DateTime FirstObservedUtc { get; set; }
        public DateTime LastRequestUtc { get; set; }
        public int RequestCount { get; set; }
    }

    private sealed class HostResolveFallbackState
    {
        public int LastRoundIndex { get; set; }
        public DateTime LastKickUtc { get; set; }
    }

    private sealed class RoundAlignState
    {
        public int LastLiveRound { get; set; }
        public int LastTargetRound { get; set; }
        public DateTime LastKickUtc { get; set; }
    }

    private sealed class SubmissionLogThrottleState
    {
        public int RoundIndex { get; set; }
        public HashSet<string> LoggedKeys { get; } = new(StringComparer.Ordinal);
    }

    private sealed class PendingPlanningFrameState
    {
        public PvpPlanningFrame? Frame { get; set; }
    }

    private static readonly HashSet<PvpResolvedEventKind> NetworkSummaryEventKinds =
    [
        PvpResolvedEventKind.RoundResolved,
        PvpResolvedEventKind.ExecutionPlanBuilt,
        PvpResolvedEventKind.DeltaPlanBuilt,
        PvpResolvedEventKind.DelayedPlanBuilt,
        PvpResolvedEventKind.DelayedCommandPlanBuilt,
        PvpResolvedEventKind.DelayedCandidateScheduled,
        PvpResolvedEventKind.DelayedCommandScheduled,
        PvpResolvedEventKind.ActionLogged,
        PvpResolvedEventKind.PlayerLocked,
        PvpResolvedEventKind.PlaybackPlanBuilt,
        PvpResolvedEventKind.PlaybackEventScheduled,
        PvpResolvedEventKind.PredictionCompared,
        PvpResolvedEventKind.HeroStateChanged,
        PvpResolvedEventKind.FrontlineStateChanged,
        PvpResolvedEventKind.MatchEnded
    ];
    private static object? _registeredService;
    private static readonly IPvpExecutionPlanner FingerprintExecutionPlanner = new PvpExecutionPlanner();
    private static readonly IPvpDeltaPlanner FingerprintDeltaPlanner = new PvpDeltaPlanner();
    private static readonly IPvpDelayedPlanner FingerprintDelayedPlanner = new PvpDelayedPlanner();
    private static readonly IPvpDelayedCommandPlanner FingerprintDelayedCommandPlanner = new PvpDelayedCommandPlanner();
    private static readonly ConditionalWeakTable<RunState, ClientSubmissionRetryState> ClientSubmissionRetryStateTable = new();
    private static readonly ConditionalWeakTable<RunState, ClientResumeRequestState> ClientResumeRequestStateTable = new();
    private static readonly ConditionalWeakTable<RunState, ClientPlanningResyncState> ClientPlanningResyncStateTable = new();
    private static readonly ConditionalWeakTable<RunState, HostResolveFallbackState> HostResolveFallbackStateTable = new();
    private static readonly ConditionalWeakTable<RunState, RoundAlignState> RoundAlignStateTable = new();
    private static readonly ConditionalWeakTable<RunState, SubmissionLogThrottleState> SubmissionLogThrottleStateTable = new();
    private static readonly ConditionalWeakTable<RunState, PendingPlanningFrameState> PendingPlanningFrameStateTable = new();
    private static bool IsForceSwitchKickEnabled =>
        string.Equals(Environment.GetEnvironmentVariable(ForceSwitchKickEnv), "1", StringComparison.Ordinal);
    private static bool IsRoundNumberMutationEnabled =>
        !string.Equals(Environment.GetEnvironmentVariable(RoundNumberMutationEnv), "0", StringComparison.Ordinal);

    public static void EnsureRegistered()
    {
        var runManager = RunManager.Instance;
        if (runManager == null)
        {
            return;
        }

        var netService = runManager.NetService;
        if (netService == null)
        {
            return;
        }

        if (PvpShopFeatureFlags.IsShopDraftEnabled)
        {
            // Keep shop-sync handlers aligned with PvP core handler lifecycle on both host and client.
            PvpShopNetBridge.EnsureRegistered();
        }

        if (ReferenceEquals(_registeredService, netService))
        {
            return;
        }

        netService.RegisterMessageHandler<PvpRoundStateMessage>(HandleRoundStateMessage);
        netService.RegisterMessageHandler<PvpPlanningFrameMessage>(HandlePlanningFrameMessage);
        netService.RegisterMessageHandler<PvpRoundResultMessage>(HandleRoundResultMessage);
        netService.RegisterMessageHandler<PvpClientSubmissionMessage>(HandleClientSubmissionMessage);
        netService.RegisterMessageHandler<PvpClientSubmissionAckMessage>(HandleClientSubmissionAckMessage);
        netService.RegisterMessageHandler<PvpResumeStateRequestMessage>(HandleResumeStateRequestMessage);
        netService.RegisterMessageHandler<PvpResumeStateMessage>(HandleResumeStateMessage);
        _registeredService = netService;
        Log.Info($"[ParallelTurnPvp] Registered PvP message handlers. netType={netService.Type} inProgress={runManager.IsInProgress}");
    }

    public void BroadcastRoundState(PvpRoundState state)
    {
        if (!CanBroadcast())
        {
            return;
        }

        if (state.RoundIndex <= 0 || state.SnapshotAtRoundStart.SnapshotVersion <= 0)
        {
            return;
        }

        PvpMatchRuntime? runtime = null;
        if (RunManager.Instance.DebugOnlyGetState() is RunState runState)
        {
            runtime = PvpRuntimeRegistry.GetOrCreate(runState);
            if (!runtime.TryMarkRoundStateBroadcast(state.SnapshotAtRoundStart.SnapshotVersion))
            {
                Log.Info($"[ParallelTurnPvp] Skipped duplicate round state broadcast. round={state.RoundIndex} snapshotVersion={state.SnapshotAtRoundStart.SnapshotVersion}");
                return;
            }
        }

        RunManager.Instance.NetService.SendMessage(CreateRoundStateMessage(state, runtime));
    }

    public void BroadcastPlanningFrame(PvpPlanningFrame frame)
    {
        if (!CanBroadcast())
        {
            return;
        }

        if (frame.RoundIndex <= 0 || frame.SnapshotVersion <= 0 || frame.Revision <= 0)
        {
            return;
        }

        if (RunManager.Instance.DebugOnlyGetState() is RunState runState)
        {
            var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
            if (!runtime.TryMarkPlanningFrameBroadcast(frame))
            {
                Log.Info($"[ParallelTurnPvp] Skipped duplicate planning frame broadcast. round={frame.RoundIndex} snapshotVersion={frame.SnapshotVersion} revision={frame.Revision}");
                return;
            }
        }

        RunManager.Instance.NetService.SendMessage(new PvpPlanningFrameMessage
        {
            roomSessionId = frame.RoomSessionId ?? string.Empty,
            roomTopology = (int)frame.RoomTopology,
            roundIndex = frame.RoundIndex,
            snapshotVersion = frame.SnapshotVersion,
            phase = (int)frame.Phase,
            revision = frame.Revision,
            submissions = frame.Submissions.Select(CreateSubmissionPacket).ToList()
        });
    }

    public void SendClientSubmission(PvpPlanningFrame frame, ulong playerId)
    {
        SendClientSubmission(frame, playerId, isRetry: false);
    }

    private void SendClientSubmission(PvpPlanningFrame frame, ulong playerId, bool isRetry)
    {
        if (!RunManager.Instance.IsInProgress || RunManager.Instance.NetService.Type != NetGameType.Client)
        {
            return;
        }

        if (frame.RoundIndex <= 0 || frame.SnapshotVersion <= 0 || string.IsNullOrWhiteSpace(frame.RoomSessionId))
        {
            Log.Warn($"[ParallelTurnPvp] Client submission skipped: invalid planning frame. round={frame.RoundIndex} snapshotVersion={frame.SnapshotVersion} revision={frame.Revision} roomSession={frame.RoomSessionId ?? "<null>"}");
            return;
        }

        ulong resolvedPlayerId = playerId;
        if (RunManager.Instance.DebugOnlyGetState() is RunState runState)
        {
            var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
            if (runtime.RoomSession.LocalPlayerId != 0 && runtime.RoomSession.LocalPlayerId != playerId)
            {
                Log.Warn($"[ParallelTurnPvp] Client submission player override applied. requested={playerId} resolved={runtime.RoomSession.LocalPlayerId}");
                resolvedPlayerId = runtime.RoomSession.LocalPlayerId;
            }
        }

        PvpRoundSubmission? submission = frame.Submissions.FirstOrDefault(item => item.PlayerId == resolvedPlayerId);
        if (submission == null)
        {
            string players = frame.Submissions.Count == 0
                ? "-"
                : string.Join(",", frame.Submissions.Select(item => item.PlayerId));
            Log.Warn($"[ParallelTurnPvp] Client submission skipped: submission not found. requested={playerId} resolved={resolvedPlayerId} framePlayers=[{players}] round={frame.RoundIndex} snapshotVersion={frame.SnapshotVersion}");
            return;
        }

        if (RunManager.Instance.DebugOnlyGetState() is RunState senderRunState)
        {
            ClientSubmissionRetryState retryState = ClientSubmissionRetryStateTable.GetOrCreateValue(senderRunState);
            bool sameRound = retryState.RoundIndex == frame.RoundIndex && retryState.SnapshotVersion == frame.SnapshotVersion;
            bool isNewSubmission = !sameRound || frame.Revision > retryState.Revision;
            bool duplicateImmediateSend = !isRetry &&
                                          sameRound &&
                                          frame.Revision == retryState.Revision &&
                                          retryState.ActionCount == submission.Actions.Count &&
                                          retryState.Locked == submission.Locked &&
                                          (DateTime.UtcNow - retryState.LastSentUtc).TotalMilliseconds < DuplicateClientSubmissionSendWindowMs;
            if (duplicateImmediateSend)
            {
                Log.Info($"[ParallelTurnPvp] Skipped duplicate immediate client submission send. round={frame.RoundIndex} snapshotVersion={frame.SnapshotVersion} revision={frame.Revision} player={resolvedPlayerId} actions={submission.Actions.Count} locked={submission.Locked}");
                return;
            }

            RunManager.Instance.NetService.SendMessage(new PvpClientSubmissionMessage
            {
                roomSessionId = frame.RoomSessionId,
                roomTopology = (int)frame.RoomTopology,
                roundIndex = frame.RoundIndex,
                snapshotVersion = frame.SnapshotVersion,
                revision = frame.Revision,
                submission = CreateSubmissionPacket(submission)
            });

            retryState.RoundIndex = frame.RoundIndex;
            retryState.SnapshotVersion = frame.SnapshotVersion;
            retryState.Revision = frame.Revision;
            retryState.PlayerId = resolvedPlayerId;
            retryState.Locked = submission.Locked;
            retryState.ActionCount = submission.Actions.Count;
            retryState.LastSentUtc = DateTime.UtcNow;
            retryState.RetryCount = isRetry && sameRound ? retryState.RetryCount + 1 : 0;
            if (isNewSubmission)
            {
                retryState.ExhaustedLogged = false;
            }

            if (!sameRound || frame.Revision > retryState.AckRevision)
            {
                retryState.AckRoundIndex = 0;
                retryState.AckSnapshotVersion = 0;
                retryState.AckRevision = 0;
                retryState.LastAckUtc = default;
            }
        }
        else
        {
            RunManager.Instance.NetService.SendMessage(new PvpClientSubmissionMessage
            {
                roomSessionId = frame.RoomSessionId,
                roomTopology = (int)frame.RoomTopology,
                roundIndex = frame.RoundIndex,
                snapshotVersion = frame.SnapshotVersion,
                revision = frame.Revision,
                submission = CreateSubmissionPacket(submission)
            });
        }

        string sendTag = isRetry ? "Retried" : "Sent";
        Log.Info($"[ParallelTurnPvp] {sendTag} client submission. round={frame.RoundIndex} snapshotVersion={frame.SnapshotVersion} revision={frame.Revision} player={resolvedPlayerId} actions={submission.Actions.Count} locked={submission.Locked} retry={isRetry}");
    }

    public static void PumpClientSubmissionRetry(RunState runState)
    {
        if (!RunManager.Instance.IsInProgress || RunManager.Instance.NetService.Type != NetGameType.Client)
        {
            return;
        }

        if (!PvpResolveConfig.ShouldUseHostAuthoritativeSnapshotSync(runState))
        {
            return;
        }

        PvpMatchRuntime runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        if (runtime.IsDisconnectedPendingResume)
        {
            return;
        }

        if (runtime.CurrentRound.RoundIndex <= 0 || runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion <= 0)
        {
            return;
        }

        if (runtime.CurrentRound.HasResolved || runtime.CurrentRound.Phase == PvpMatchPhase.RoundEnd)
        {
            return;
        }

        ulong localPlayerId = runtime.RoomSession.LocalPlayerId;
        if (localPlayerId == 0 ||
            !runtime.CurrentRound.LogsByPlayer.TryGetValue(localPlayerId, out PvpActionLog? localLog) ||
            !localLog.Locked)
        {
            return;
        }

        ClientSubmissionRetryState retryState = ClientSubmissionRetryStateTable.GetOrCreateValue(runState);
        int currentRound = runtime.CurrentRound.RoundIndex;
        int currentSnapshot = runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion;
        if (retryState.AckRoundIndex == currentRound &&
            retryState.AckSnapshotVersion == currentSnapshot &&
            retryState.AckRevision >= retryState.Revision &&
            retryState.Revision > 0)
        {
            return;
        }

        if (runtime.LastAuthoritativePlanningFrame is { } authoritativeFrame &&
            authoritativeFrame.RoundIndex == currentRound &&
            authoritativeFrame.SnapshotVersion == currentSnapshot)
        {
            PvpRoundSubmission? authoritativeLocalSubmission = authoritativeFrame.Submissions.FirstOrDefault(item => item.PlayerId == localPlayerId);
            if (authoritativeLocalSubmission != null &&
                authoritativeLocalSubmission.Locked &&
                authoritativeFrame.Revision >= retryState.Revision &&
                authoritativeLocalSubmission.Actions.Count >= localLog.Actions.Count)
            {
                return;
            }
        }

        bool roundChanged = retryState.RoundIndex != currentRound || retryState.SnapshotVersion != currentSnapshot;
        TimeSpan elapsed = DateTime.UtcNow - retryState.LastSentUtc;

        if (!roundChanged)
        {
            if (retryState.RetryCount >= MaxClientSubmissionRetriesPerRound)
            {
                if (!retryState.ExhaustedLogged)
                {
                    retryState.ExhaustedLogged = true;
                    Log.Warn($"[ParallelTurnPvp] Client submission retry exhausted. round={currentRound} snapshotVersion={currentSnapshot} revision={retryState.Revision} retries={retryState.RetryCount} maxRetries={MaxClientSubmissionRetriesPerRound}");
                }

                return;
            }

            int retryIntervalMs = GetClientSubmissionRetryIntervalMs(retryState.RetryCount);
            if (elapsed.TotalMilliseconds < retryIntervalMs)
            {
                return;
            }
        }

        PvpPlanningFrame frame = runtime.BuildPlanningFrame();
        new PvpNetBridge().SendClientSubmission(frame, localPlayerId, isRetry: !roundChanged);
    }

    public static void PumpRoundAlignment(RunState runState)
    {
        if (!IsForceSwitchKickEnabled)
        {
            return;
        }

        if (!RunManager.Instance.IsInProgress ||
            !runState.Modifiers.OfType<Models.ParallelTurnPvpDebugModifier>().Any() ||
            !ParallelTurnFrontlineHelper.IsSplitRoomActive(runState))
        {
            return;
        }

        if (PvpRuntimeRegistry.TryGet(runState) is not { } runtime || runtime.IsDisconnectedPendingResume)
        {
            return;
        }

        CombatState? combatState = runState.Players.FirstOrDefault()?.Creature.CombatState;
        if (combatState == null || combatState.CurrentSide != CombatSide.Player)
        {
            return;
        }

        int liveRound = Math.Max(1, combatState.RoundNumber);
        int targetRound = runtime.CurrentRound.RoundIndex;
        if (targetRound <= 0 || liveRound >= targetRound)
        {
            return;
        }

        RoundAlignState state = RoundAlignStateTable.GetOrCreateValue(runState);
        double elapsedMs = (DateTime.UtcNow - state.LastKickUtc).TotalMilliseconds;
        if (state.LastLiveRound == liveRound &&
            state.LastTargetRound == targetRound &&
            elapsedMs < RoundAlignKickIntervalMs)
        {
            return;
        }

        state.LastLiveRound = liveRound;
        state.LastTargetRound = targetRound;
        state.LastKickUtc = DateTime.UtcNow;
        Log.Warn($"[ParallelTurnPvp] RoundAlign kick requested. liveRound={liveRound} targetRound={targetRound} phase={runtime.CurrentRound.Phase} reason=live_round_lag");
        TaskHelper.RunSafely(ForceSwitchSidesAsync("round_align"));
    }

    public static void PumpHostResolveFallback(RunState runState)
    {
        if (!IsForceSwitchKickEnabled)
        {
            return;
        }

        if (!RunManager.Instance.IsInProgress ||
            RunManager.Instance.NetService.Type != NetGameType.Host ||
            !runState.Modifiers.OfType<Models.ParallelTurnPvpDebugModifier>().Any() ||
            !ParallelTurnFrontlineHelper.IsSplitRoomActive(runState))
        {
            return;
        }

        if (PvpRuntimeRegistry.TryGet(runState) is not { } runtime || runtime.IsDisconnectedPendingResume)
        {
            return;
        }

        CombatState? combatState = runState.Players.FirstOrDefault()?.Creature.CombatState;
        if (combatState == null || combatState.CurrentSide != CombatSide.Player)
        {
            return;
        }

        if (!runtime.CanResolveRound(combatState.RoundNumber))
        {
            return;
        }

        HostResolveFallbackState state = HostResolveFallbackStateTable.GetOrCreateValue(runState);
        double elapsedMs = (DateTime.UtcNow - state.LastKickUtc).TotalMilliseconds;
        if (state.LastRoundIndex == runtime.CurrentRound.RoundIndex && elapsedMs < HostResolveFallbackKickIntervalMs)
        {
            return;
        }

        state.LastRoundIndex = runtime.CurrentRound.RoundIndex;
        state.LastKickUtc = DateTime.UtcNow;
        Log.Warn($"[ParallelTurnPvp] HostResolve kick requested. liveRound={combatState.RoundNumber} pvpRound={runtime.CurrentRound.RoundIndex} phase={runtime.CurrentRound.Phase}");
        TaskHelper.RunSafely(ForceSwitchSidesAsync("host_resolve_fallback"));
    }

    private static int GetClientSubmissionRetryIntervalMs(int retryCount)
    {
        if (retryCount <= 2)
        {
            return ClientSubmissionRetryIntervalFastMs;
        }

        if (retryCount <= 7)
        {
            return ClientSubmissionRetryIntervalNormalMs;
        }

        return ClientSubmissionRetryIntervalSlowMs;
    }

    public static void PumpClientResumeStateRequest(RunState runState)
    {
        if (!RunManager.Instance.IsInProgress || RunManager.Instance.NetService.Type != NetGameType.Client)
        {
            return;
        }

        if (!RunManager.Instance.NetService.IsConnected)
        {
            return;
        }

        if (!PvpResolveConfig.ShouldUseHostAuthoritativeSnapshotSync(runState))
        {
            return;
        }

        PvpMatchRuntime runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        if (!runtime.IsDisconnectedPendingResume)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(runtime.RoomSession.SessionId))
        {
            return;
        }

        ClientResumeRequestState retryState = ClientResumeRequestStateTable.GetOrCreateValue(runState);
        double elapsedMs = (DateTime.UtcNow - retryState.LastSentUtc).TotalMilliseconds;
        if (retryState.AttemptCount > 0 && elapsedMs < ResumeStateRequestIntervalMs)
        {
            return;
        }

        int roundIndex = runtime.CurrentRound.RoundIndex;
        int snapshotVersion = runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion;
        int planningRevision = runtime.CurrentRound.PlanningRevision;
        RunManager.Instance.NetService.SendMessage(new PvpResumeStateRequestMessage
        {
            roomSessionId = runtime.RoomSession.SessionId,
            roomTopology = (int)runtime.RoomSession.Topology,
            requesterRoundIndex = roundIndex,
            requesterSnapshotVersion = snapshotVersion,
            requesterPlanningRevision = planningRevision
        });

        retryState.LastSentUtc = DateTime.UtcNow;
        retryState.AttemptCount++;
        retryState.LastRequestedRoundIndex = roundIndex;
        retryState.LastRequestedSnapshotVersion = snapshotVersion;
        retryState.LastRequestedPlanningRevision = planningRevision;
        if (retryState.AttemptCount == 1 || retryState.AttemptCount % 3 == 0)
        {
            Log.Info($"[ParallelTurnPvp] Requested resume state from host. attempts={retryState.AttemptCount} round={roundIndex} snapshotVersion={snapshotVersion} planningRevision={planningRevision}");
        }
    }

    public static void PumpClientPlanningFrameResync(RunState runState)
    {
        if (!RunManager.Instance.IsInProgress || RunManager.Instance.NetService.Type != NetGameType.Client)
        {
            return;
        }

        if (!RunManager.Instance.NetService.IsConnected || !PvpResolveConfig.ShouldUseHostAuthoritativeSnapshotSync(runState))
        {
            return;
        }

        PvpMatchRuntime runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        if (runtime.IsDisconnectedPendingResume || string.IsNullOrWhiteSpace(runtime.RoomSession.SessionId))
        {
            return;
        }

        int roundIndex = runtime.CurrentRound.RoundIndex;
        int snapshotVersion = runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion;
        if (roundIndex <= 0 || snapshotVersion <= 0)
        {
            return;
        }

        if (!ClientSubmissionRetryStateTable.TryGetValue(runState, out ClientSubmissionRetryState? retryState) ||
            retryState == null ||
            retryState.AckRevision <= 0 ||
            retryState.AckRoundIndex != roundIndex ||
            retryState.AckSnapshotVersion != snapshotVersion)
        {
            return;
        }

        int ackRevision = retryState.AckRevision;
        PvpPlanningFrame? authoritativeFrame = runtime.LastAuthoritativePlanningFrame;
        bool hasAckedFrame = authoritativeFrame != null &&
                            authoritativeFrame.RoundIndex == roundIndex &&
                            authoritativeFrame.SnapshotVersion == snapshotVersion &&
                            authoritativeFrame.Revision >= ackRevision;
        if (hasAckedFrame)
        {
            ClientPlanningResyncStateTable.GetOrCreateValue(runState).RequestCount = 0;
            return;
        }

        ClientPlanningResyncState state = ClientPlanningResyncStateTable.GetOrCreateValue(runState);
        bool sameProbe = state.RoundIndex == roundIndex &&
                         state.SnapshotVersion == snapshotVersion &&
                         state.AckRevision == ackRevision;
        if (!sameProbe)
        {
            state.RoundIndex = roundIndex;
            state.SnapshotVersion = snapshotVersion;
            state.AckRevision = ackRevision;
            state.FirstObservedUtc = DateTime.UtcNow;
            state.LastRequestUtc = default;
            state.RequestCount = 0;
            return;
        }

        double waitedMs = (DateTime.UtcNow - state.FirstObservedUtc).TotalMilliseconds;
        if (waitedMs < ClientPlanningResyncGraceMs)
        {
            return;
        }

        double sinceLastRequestMs = (DateTime.UtcNow - state.LastRequestUtc).TotalMilliseconds;
        if (state.LastRequestUtc != default && sinceLastRequestMs < ClientPlanningResyncIntervalMs)
        {
            return;
        }

        int planningRevision = runtime.CurrentRound.PlanningRevision;
        RunManager.Instance.NetService.SendMessage(new PvpResumeStateRequestMessage
        {
            roomSessionId = runtime.RoomSession.SessionId,
            roomTopology = (int)runtime.RoomSession.Topology,
            requesterRoundIndex = roundIndex,
            requesterSnapshotVersion = snapshotVersion,
            requesterPlanningRevision = planningRevision
        });

        state.LastRequestUtc = DateTime.UtcNow;
        state.RequestCount++;
        if (state.RequestCount == 1 || state.RequestCount % 2 == 0)
        {
            Log.Warn($"[ParallelTurnPvp] Requested planning resync hint from host. round={roundIndex} snapshotVersion={snapshotVersion} ackRevision={ackRevision} localPlanningRevision={planningRevision} waitedMs={(int)waitedMs} requests={state.RequestCount}");
        }
    }

    public void BroadcastRoundResult(PvpRoundResult result)
    {
        if (!CanBroadcast())
        {
            return;
        }

        if (result.RoundIndex <= 0 || result.FinalSnapshot.SnapshotVersion <= 0)
        {
            return;
        }

        PvpMatchRuntime? runtime = null;
        if (RunManager.Instance.DebugOnlyGetState() is RunState runState)
        {
            runtime = PvpRuntimeRegistry.GetOrCreate(runState);
            if (!runtime.TryMarkRoundResultBroadcast(result.FinalSnapshot.SnapshotVersion))
            {
                Log.Info($"[ParallelTurnPvp] Skipped duplicate round result broadcast. round={result.RoundIndex} snapshotVersion={result.FinalSnapshot.SnapshotVersion}");
                return;
            }
        }

        PvpRoundResultMessage networkMessage = CreateRoundResultMessage(result, runtime);
        IReadOnlyList<PvpResolvedEvent> networkEvents = CreateResolvedEvents(networkMessage).ToList();
        RunManager.Instance.NetService.SendMessage(networkMessage);

        if (networkEvents.Count != result.Events.Count)
        {
            Log.Info($"[ParallelTurnPvp] Broadcast round result with compact network summary. round={result.RoundIndex} snapshotVersion={result.FinalSnapshot.SnapshotVersion} networkEvents={networkEvents.Count}/{result.Events.Count}");
        }
    }

    private static bool CanBroadcast()
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return false;
        }

        return RunManager.Instance.NetService.Type is NetGameType.Host or NetGameType.Singleplayer;
    }

    private static void HandleRoundStateMessage(PvpRoundStateMessage message, ulong _)
    {
        if (message.roundIndex <= 0 || message.snapshotVersion <= 0 || RunManager.Instance.DebugOnlyGetState() is not RunState runState)
        {
            return;
        }

        var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        if (!ValidateRoomContext(runtime, message.roomSessionId, message.roomTopology, nameof(PvpRoundStateMessage)))
        {
            return;
        }

        if (IsStaleIncomingRound(runtime, message.roundIndex, nameof(PvpRoundStateMessage), message.snapshotVersion))
        {
            return;
        }

        if (!runtime.TryMarkRoundStateReceived(message.snapshotVersion))
        {
            Log.Info($"[ParallelTurnPvp] Ignored duplicate/stale authoritative round state. round={message.roundIndex} snapshotVersion={message.snapshotVersion}");
            return;
        }

        PvpMatchPhase phase = (PvpMatchPhase)message.phase;
        PvpCombatSnapshot roundStartSnapshot = CreateSnapshotFromRoundStateMessage(runtime.CurrentRound.SnapshotAtRoundStart, message);
        if (RunManager.Instance.NetService.Type == NetGameType.Client)
        {
            PvpArenaTopology roomTopology = Enum.IsDefined(typeof(PvpArenaTopology), message.roomTopology)
                ? (PvpArenaTopology)message.roomTopology
                : runtime.RoomSession.Topology;
            ResetRoundFromAuthoritativeState(
                runtime,
                runState,
                roundStartSnapshot,
                phase,
                planningRevision: 1,
                message.roomSessionId,
                roomTopology);
            if (PvpResolveConfig.ShouldUseHostAuthoritativeSnapshotSync(runState))
            {
                ApplyLiveSnapshot(runState, roundStartSnapshot);
                TryAlignCombatRoundNumber(runState, message.roundIndex, "round_state_message");
                Log.Info($"[ParallelTurnPvp] Applied authoritative round-start snapshot on client. round={message.roundIndex} snapshotVersion={message.snapshotVersion}");
            }
            else
            {
                Log.Info($"[ParallelTurnPvp] Reset shared-combat client planning state from authoritative round state. round={message.roundIndex} snapshotVersion={message.snapshotVersion}");
            }
        }
        else
        {
            runtime.CurrentRound.RoundIndex = message.roundIndex;
            runtime.CurrentRound.Phase = phase;
            runtime.CurrentRound.SnapshotAtRoundStart = roundStartSnapshot;
        }

        TryApplyDeferredPlanningFrame(runState, runtime, "round_state_message");

        Log.Info($"[ParallelTurnPvp] Received authoritative round state. round={message.roundIndex} snapshotVersion={message.snapshotVersion} phase={(PvpMatchPhase)message.phase} roomSession={message.roomSessionId} topology={(PvpArenaTopology)message.roomTopology}");
    }

    private static void HandlePlanningFrameMessage(PvpPlanningFrameMessage message, ulong _)
    {
        if (message.roundIndex <= 0 || message.snapshotVersion <= 0 || message.revision <= 0 || RunManager.Instance.DebugOnlyGetState() is not RunState runState)
        {
            return;
        }

        var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        if (!ValidateRoomContext(runtime, message.roomSessionId, message.roomTopology, nameof(PvpPlanningFrameMessage)))
        {
            return;
        }

        if (IsStaleIncomingRound(runtime, message.roundIndex, nameof(PvpPlanningFrameMessage), message.snapshotVersion, message.revision))
        {
            return;
        }

        var frame = new PvpPlanningFrame
        {
            RoundIndex = message.roundIndex,
            SnapshotVersion = message.snapshotVersion,
            Phase = (PvpMatchPhase)message.phase,
            Revision = message.revision,
            RoomSessionId = message.roomSessionId ?? string.Empty,
            RoomTopology = (PvpArenaTopology)message.roomTopology
        };
        foreach (PvpRoundSubmissionPacket submissionPacket in message.submissions ?? new List<PvpRoundSubmissionPacket>())
        {
            frame.Submissions.Add(CreateSubmission(submissionPacket));
        }

        if (RunManager.Instance.NetService.Type == NetGameType.Client &&
            IsPlanningFrameStaleByAck(runState, frame, out int ackRevision))
        {
            Log.Info($"[ParallelTurnPvp] Ignored authoritative planning frame below ACK floor. round={frame.RoundIndex} snapshotVersion={frame.SnapshotVersion} revision={frame.Revision} ackRevision={ackRevision}");
            return;
        }

        if (RunManager.Instance.NetService.Type == NetGameType.Client &&
            PvpResolveConfig.ShouldUseHostAuthoritativeSnapshotSync(runState))
        {
            if (runtime.CurrentRound.RoundIndex <= 0 || runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion <= 0)
            {
                BufferDeferredPlanningFrame(runState, runtime, frame, "waiting_round_state");
                return;
            }

            if (runtime.CurrentRound.RoundIndex != frame.RoundIndex ||
                runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion != frame.SnapshotVersion)
            {
                BufferDeferredPlanningFrame(runState, runtime, frame, "snapshot_mismatch");
                return;
            }
        }

        if (!runtime.TryMarkPlanningFrameReceived(message.roundIndex, message.revision))
        {
            Log.Info($"[ParallelTurnPvp] Ignored duplicate/stale authoritative planning frame. round={message.roundIndex} snapshotVersion={message.snapshotVersion} revision={message.revision}");
            return;
        }

        bool rebuiltRound = false;
        runtime.ApplyAuthoritativePlanningFrame(frame);
        Log.Info($"[ParallelTurnPvp] Received authoritative planning frame. round={message.roundIndex} snapshotVersion={message.snapshotVersion} revision={message.revision} submissions={frame.Submissions.Count} rebuiltRound={rebuiltRound} roomSession={message.roomSessionId} topology={(PvpArenaTopology)message.roomTopology}");
    }

    private static void HandleClientSubmissionMessage(PvpClientSubmissionMessage message, ulong senderPlayerId)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host)
        {
            return;
        }

        if (message.roundIndex <= 0 || message.snapshotVersion <= 0 || RunManager.Instance.DebugOnlyGetState() is not RunState runState)
        {
            return;
        }

        var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        if (!ValidateRoomContext(runtime, message.roomSessionId, message.roomTopology, nameof(PvpClientSubmissionMessage)))
        {
            return;
        }

        if (!TryEnsureHostRoundContextForSubmission(runtime, runState, message, senderPlayerId))
        {
            int rejectedRevision = Math.Max(message.revision, 1);
            SendSubmissionAck(runtime, senderPlayerId, message.roundIndex, message.snapshotVersion, rejectedRevision, accepted: false, note: "round_not_ready");
            return;
        }

        PvpRoundSubmission submission = CreateSubmission(message.submission);
        if (submission.PlayerId == 0 || submission.PlayerId != senderPlayerId)
        {
            Log.Warn($"[ParallelTurnPvp] Rejected client submission pre-check: sender/player mismatch sender={senderPlayerId} submissionPlayer={submission.PlayerId}");
            int rejectedRevision = Math.Max(message.revision, 1);
            SendSubmissionAck(runtime, senderPlayerId, message.roundIndex, message.snapshotVersion, rejectedRevision, accepted: false, note: "sender_mismatch");
            return;
        }

        if (submission.RoundIndex != runtime.CurrentRound.RoundIndex)
        {
            Log.Warn($"[ParallelTurnPvp] Rejected client submission pre-check: round mismatch sender={senderPlayerId} incomingRound={submission.RoundIndex} localRound={runtime.CurrentRound.RoundIndex}");
            int rejectedRevision = Math.Max(message.revision, 1);
            SendSubmissionAck(runtime, senderPlayerId, message.roundIndex, message.snapshotVersion, rejectedRevision, accepted: false, note: "round_mismatch");
            SendRoundResyncHintToClient(runtime, senderPlayerId, "round_mismatch");
            return;
        }

        int localSnapshotVersion = runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion;
        if (message.snapshotVersion != localSnapshotVersion)
        {
            Log.Warn($"[ParallelTurnPvp] Rejected client submission pre-check: snapshot mismatch sender={senderPlayerId} incomingSnapshot={message.snapshotVersion} localSnapshot={localSnapshotVersion}");
            int rejectedRevision = Math.Max(message.revision, 1);
            SendSubmissionAck(runtime, senderPlayerId, message.roundIndex, message.snapshotVersion, rejectedRevision, accepted: false, note: "snapshot_mismatch");
            SendRoundResyncHintToClient(runtime, senderPlayerId, "snapshot_mismatch");
            return;
        }

        if (runtime.TryGetNetworkSubmissionRevision(senderPlayerId, out int knownRevision))
        {
            if (message.revision < knownRevision)
            {
                SendSubmissionAck(runtime, senderPlayerId, message.roundIndex, message.snapshotVersion, knownRevision, accepted: true, note: "already_applied");
                if (ShouldLogSubmissionNoise(runState, runtime.CurrentRound.RoundIndex, senderPlayerId, message.revision, "stale_before_record"))
                {
                    Log.Info($"[ParallelTurnPvp] Ignored stale client submission before record. sender={senderPlayerId} incomingRevision={message.revision} knownRevision={knownRevision}");
                }

                return;
            }

            if (message.revision == knownRevision)
            {
                SendSubmissionAck(runtime, senderPlayerId, message.roundIndex, message.snapshotVersion, knownRevision, accepted: true, note: "duplicate_revision");
                if (ShouldLogSubmissionNoise(runState, runtime.CurrentRound.RoundIndex, senderPlayerId, message.revision, "duplicate_revision"))
                {
                    Log.Info($"[ParallelTurnPvp] Ignored duplicate/conflicting client submission on same revision. sender={senderPlayerId} revision={message.revision}");
                }

                return;
            }
        }

        bool accepted = runtime.RecordNetworkSubmission(submission, message.snapshotVersion, senderPlayerId, message.revision);
        if (!accepted)
        {
            int latestRevision = runtime.TryGetNetworkSubmissionRevision(senderPlayerId, out int revision) ? revision : 0;
            bool alreadyCovered = latestRevision > 0 && latestRevision >= message.revision;
            if (alreadyCovered)
            {
                SendSubmissionAck(runtime, senderPlayerId, message.roundIndex, message.snapshotVersion, latestRevision, accepted: true, note: "already_applied");
            }
            else
            {
                int rejectedRevision = Math.Max(message.revision, Math.Max(latestRevision, 1));
                SendSubmissionAck(runtime, senderPlayerId, message.roundIndex, message.snapshotVersion, rejectedRevision, accepted: false, note: "rejected");
            }

            return;
        }

        SendSubmissionAck(runtime, senderPlayerId, message.roundIndex, message.snapshotVersion, message.revision, accepted: true, note: "accepted");
        new PvpNetBridge().BroadcastPlanningFrame(runtime.BuildPlanningFrame());
        Log.Info($"[ParallelTurnPvp] Received client submission. round={message.roundIndex} snapshotVersion={message.snapshotVersion} revision={message.revision} sender={senderPlayerId} actions={submission.Actions.Count} locked={submission.Locked}");
        PumpHostResolveFallback(runState);
    }

    private static bool TryEnsureHostRoundContextForSubmission(
        PvpMatchRuntime runtime,
        RunState runState,
        PvpClientSubmissionMessage message,
        ulong senderPlayerId)
    {
        if (runtime.CurrentRound.RoundIndex > 0 && runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion > 0)
        {
            return true;
        }

        CombatState? combatState = CombatManager.Instance?.DebugOnlyGetState();
        if (combatState == null || combatState.RunState != runState)
        {
            Log.Warn($"[ParallelTurnPvp] Host submission pre-check failed: combat context unavailable. sender={senderPlayerId} incomingRound={message.roundIndex} incomingSnapshot={message.snapshotVersion}");
            return false;
        }

        int liveRound = Math.Max(1, combatState.RoundNumber);
        runtime.StartRoundFromLiveState(combatState, liveRound);
        if (runtime.CurrentRound.RoundIndex <= 0 || runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion <= 0)
        {
            Log.Warn($"[ParallelTurnPvp] Host submission pre-check failed: round context still invalid after bootstrap. sender={senderPlayerId} incomingRound={message.roundIndex} incomingSnapshot={message.snapshotVersion} liveRound={liveRound}");
            return false;
        }

        if (runtime.CurrentRound.RoundIndex != message.roundIndex)
        {
            Log.Warn($"[ParallelTurnPvp] Host submission pre-check mismatch after bootstrap. sender={senderPlayerId} incomingRound={message.roundIndex} localRound={runtime.CurrentRound.RoundIndex} incomingSnapshot={message.snapshotVersion} localSnapshot={runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion}");
            return true;
        }

        Log.Info($"[ParallelTurnPvp] Bootstrapped host round context for first client submission. sender={senderPlayerId} round={runtime.CurrentRound.RoundIndex} snapshotVersion={runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion}");
        PvpNetBridge bridge = new();
        bridge.BroadcastRoundState(runtime.CurrentRound);
        bridge.BroadcastPlanningFrame(runtime.BuildPlanningFrame());
        return true;
    }

    private static void SendSubmissionAck(PvpMatchRuntime runtime, ulong playerId, int roundIndex, int snapshotVersion, int revision, bool accepted, string note)
    {
        if (!RunManager.Instance.IsInProgress || RunManager.Instance.NetService.Type != NetGameType.Host)
        {
            return;
        }

        if (playerId == 0 || roundIndex <= 0 || snapshotVersion <= 0 || revision <= 0)
        {
            return;
        }

        RunManager.Instance.NetService.SendMessage(new PvpClientSubmissionAckMessage
        {
            roomSessionId = runtime.RoomSession.SessionId,
            roomTopology = (int)runtime.RoomSession.Topology,
            roundIndex = roundIndex,
            snapshotVersion = snapshotVersion,
            playerId = playerId,
            revision = revision,
            accepted = accepted,
            note = note ?? string.Empty
        });
    }

    private static void SendRoundResyncHintToClient(PvpMatchRuntime runtime, ulong playerId, string source)
    {
        if (!RunManager.Instance.IsInProgress ||
            RunManager.Instance.NetService.Type != NetGameType.Host ||
            playerId == 0)
        {
            return;
        }

        PvpRoundState currentRound = runtime.CurrentRound;
        if (currentRound.RoundIndex <= 0 || currentRound.SnapshotAtRoundStart.SnapshotVersion <= 0)
        {
            return;
        }

        PvpRoundStateMessage roundState = CreateRoundStateMessage(currentRound, runtime);
        RunManager.Instance.NetService.SendMessage(roundState, playerId);

        PvpPlanningFrame frame = runtime.LastAuthoritativePlanningFrame ?? runtime.BuildPlanningFrame();
        if (frame.RoundIndex > 0 && frame.SnapshotVersion > 0 && frame.Revision > 0)
        {
            RunManager.Instance.NetService.SendMessage(CreatePlanningFrameMessage(frame), playerId);
        }

        Log.Info($"[ParallelTurnPvp] Sent round resync hint to client. source={source} target={playerId} round={currentRound.RoundIndex} snapshotVersion={currentRound.SnapshotAtRoundStart.SnapshotVersion} planningRevision={frame.Revision}");
    }

    private static void HandleClientSubmissionAckMessage(PvpClientSubmissionAckMessage message, ulong _)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Client)
        {
            return;
        }

        if (message.roundIndex <= 0 || message.snapshotVersion <= 0 || message.revision <= 0 || RunManager.Instance.DebugOnlyGetState() is not RunState runState)
        {
            return;
        }

        PvpMatchRuntime runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        if (!ValidateRoomContext(runtime, message.roomSessionId, message.roomTopology, nameof(PvpClientSubmissionAckMessage)))
        {
            return;
        }

        if (message.playerId != runtime.RoomSession.LocalPlayerId)
        {
            return;
        }

        ClientSubmissionRetryState retryState = ClientSubmissionRetryStateTable.GetOrCreateValue(runState);
        if (message.accepted)
        {
            if (message.revision >= retryState.AckRevision)
            {
                retryState.AckRoundIndex = message.roundIndex;
                retryState.AckSnapshotVersion = message.snapshotVersion;
                retryState.AckRevision = message.revision;
                retryState.LastAckUtc = DateTime.UtcNow;
            }

            Log.Info($"[ParallelTurnPvp] Received submission ACK. round={message.roundIndex} snapshotVersion={message.snapshotVersion} revision={message.revision} note={message.note}");
            return;
        }

        Log.Warn($"[ParallelTurnPvp] Received submission NACK. round={message.roundIndex} snapshotVersion={message.snapshotVersion} revision={message.revision} note={message.note}");
        if (!IsRetryableSubmissionNack(message.note))
        {
            retryState.LastSentUtc = DateTime.UtcNow;
            retryState.RetryCount = MaxClientSubmissionRetriesPerRound;
            retryState.ExhaustedLogged = true;
            Log.Info($"[ParallelTurnPvp] Submission NACK marked non-retryable. round={message.roundIndex} snapshotVersion={message.snapshotVersion} revision={message.revision} note={message.note}");
            return;
        }

        bool sameRound = runtime.CurrentRound.RoundIndex == message.roundIndex &&
                         runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion == message.snapshotVersion;
        if (!sameRound)
        {
            return;
        }

        PvpPlanningFrame localFrame = runtime.BuildPlanningFrame();
        if (localFrame.Revision < message.revision)
        {
            return;
        }

        ulong localPlayerId = runtime.RoomSession.LocalPlayerId;
        if (localPlayerId == 0)
        {
            return;
        }

        retryState.LastSentUtc = DateTime.UtcNow.AddMilliseconds(-ClientSubmissionRetryIntervalFastMs);
        retryState.ExhaustedLogged = false;
        new PvpNetBridge().SendClientSubmission(localFrame, localPlayerId, isRetry: true);
    }

    private static bool IsRetryableSubmissionNack(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return true;
        }

        return string.Equals(note, "rejected", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldLogSubmissionNoise(RunState runState, int roundIndex, ulong senderPlayerId, int revision, string tag)
    {
        SubmissionLogThrottleState state = SubmissionLogThrottleStateTable.GetOrCreateValue(runState);
        if (state.RoundIndex != roundIndex)
        {
            state.RoundIndex = roundIndex;
            state.LoggedKeys.Clear();
        }

        string key = $"{tag}:{senderPlayerId}:{revision}";
        return state.LoggedKeys.Add(key);
    }

    private static void HandleResumeStateRequestMessage(PvpResumeStateRequestMessage message, ulong senderPlayerId)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host ||
            RunManager.Instance.DebugOnlyGetState() is not RunState runState)
        {
            return;
        }

        PvpMatchRuntime runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        if (!ValidateRoomContext(runtime, message.roomSessionId, message.roomTopology, nameof(PvpResumeStateRequestMessage)))
        {
            return;
        }

        if (!runState.Players.Any(player => player.NetId == senderPlayerId))
        {
            Log.Warn($"[ParallelTurnPvp] Ignored resume-state request from unknown sender={senderPlayerId}.");
            return;
        }

        PvpResumeStateMessage response = BuildResumeStateMessage(runtime);
        RunManager.Instance.NetService.SendMessage(response, senderPlayerId);
        if (runtime.IsDisconnectedPendingResume && senderPlayerId == runtime.RoomSession.OpponentPlayerId)
        {
            runtime.ClearDisconnectedPendingResume("resume_request_received");
        }

        Log.Info($"[ParallelTurnPvp] Served resume state request. sender={senderPlayerId} requesterRound={message.requesterRoundIndex} requesterSnapshot={message.requesterSnapshotVersion} requesterRevision={message.requesterPlanningRevision} hasRoundState={response.hasRoundState} hasPlanningFrame={response.hasPlanningFrame} hasRoundResult={response.hasRoundResult}");
    }

    private static void HandleResumeStateMessage(PvpResumeStateMessage message, ulong _)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Client ||
            RunManager.Instance.DebugOnlyGetState() is not RunState runState)
        {
            return;
        }

        PvpMatchRuntime runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        if (!ValidateRoomContext(runtime, message.roomSessionId, message.roomTopology, nameof(PvpResumeStateMessage)))
        {
            return;
        }

        if (!runtime.IsDisconnectedPendingResume)
        {
            Log.Info("[ParallelTurnPvp] Ignored resume state message: runtime is not in disconnected-pending-resume.");
            return;
        }

        bool appliedAny = false;
        PvpPlanningFrame? incomingPlanningFrame = null;
        if (message.hasPlanningFrame &&
            message.planningFrame.roundIndex > 0 &&
            message.planningFrame.snapshotVersion > 0 &&
            message.planningFrame.revision > 0)
        {
            incomingPlanningFrame = CreatePlanningFrame(message.planningFrame);
        }

        if (message.hasRoundState &&
            message.roundState.roundIndex > 0 &&
            message.roundState.snapshotVersion > 0)
        {
            PvpArenaTopology roomTopology = Enum.IsDefined(typeof(PvpArenaTopology), message.roundState.roomTopology)
                ? (PvpArenaTopology)message.roundState.roomTopology
                : runtime.RoomSession.Topology;
            PvpCombatSnapshot roundStartSnapshot = CreateSnapshotFromRoundStateMessage(runtime.CurrentRound.SnapshotAtRoundStart, message.roundState);
            PvpMatchPhase authoritativePhase = (PvpMatchPhase)message.roundState.phase;
            int authoritativeRevision = 1;

            if (incomingPlanningFrame != null &&
                incomingPlanningFrame.RoundIndex == message.roundState.roundIndex &&
                incomingPlanningFrame.SnapshotVersion == message.roundState.snapshotVersion)
            {
                authoritativePhase = incomingPlanningFrame.Phase;
                authoritativeRevision = incomingPlanningFrame.Revision;
            }

            ResetRoundFromAuthoritativeState(
                runtime,
                runState,
                roundStartSnapshot,
                authoritativePhase,
                authoritativeRevision,
                message.roundState.roomSessionId,
                roomTopology);

            if (incomingPlanningFrame != null &&
                incomingPlanningFrame.RoundIndex == roundStartSnapshot.RoundIndex &&
                incomingPlanningFrame.SnapshotVersion == roundStartSnapshot.SnapshotVersion)
            {
                ApplyAuthoritativePlanningSubmissions(runtime, runState, incomingPlanningFrame);
            }

            runtime.TryMarkRoundStateReceived(message.roundState.snapshotVersion);
            if (PvpResolveConfig.ShouldUseHostAuthoritativeSnapshotSync(runState))
            {
                ApplyLiveSnapshot(runState, runtime.CurrentRound.SnapshotAtRoundStart);
                TryAlignCombatRoundNumber(runState, message.roundState.roundIndex, "resume_round_state");
                Log.Info($"[ParallelTurnPvp] Resume applied authoritative round-start snapshot. round={message.roundState.roundIndex} snapshotVersion={message.roundState.snapshotVersion}");
            }

            appliedAny = true;
        }

        if (incomingPlanningFrame != null)
        {
            bool freshPlanningFrame = runtime.TryMarkPlanningFrameReceived(incomingPlanningFrame.RoundIndex, incomingPlanningFrame.Revision) ||
                                      runtime.LastAuthoritativePlanningFrame == null;
            if (freshPlanningFrame)
            {
                runtime.ApplyAuthoritativePlanningFrame(incomingPlanningFrame);
                appliedAny = true;
            }
        }

        if (message.hasRoundResult &&
            message.roundResult.roundIndex > 0 &&
            message.roundResult.snapshotVersion > 0)
        {
            if (message.hasRoundState &&
                message.roundState.roundIndex > 0 &&
                message.roundResult.roundIndex < message.roundState.roundIndex)
            {
                Log.Info($"[ParallelTurnPvp] Ignored stale resume round result. resultRound={message.roundResult.roundIndex} stateRound={message.roundState.roundIndex} snapshotVersion={message.roundResult.snapshotVersion}");
            }
            else
            {
                bool freshRoundResult = runtime.TryMarkRoundResultReceived(message.roundResult.snapshotVersion) ||
                                        runtime.LastAuthoritativeResult == null ||
                                        runtime.LastAuthoritativeResult.FinalSnapshot.SnapshotVersion < message.roundResult.snapshotVersion;
                if (freshRoundResult)
                {
                    PvpCombatSnapshot initialSnapshot = runtime.CurrentRound.SnapshotAtRoundStart;
                    PvpCombatSnapshot finalSnapshot = CreateSnapshotFromMessage(initialSnapshot, message.roundResult);
                    var authoritativeResult = new PvpRoundResult
                    {
                        RoundIndex = message.roundResult.roundIndex,
                        InitialSnapshot = initialSnapshot,
                        FinalSnapshot = finalSnapshot
                    };
                    foreach (PvpResolvedEvent resolvedEvent in CreateResolvedEvents(message.roundResult))
                    {
                        authoritativeResult.Events.Add(resolvedEvent);
                    }

                    runtime.ApplyAuthoritativeResult(authoritativeResult);
                    if (PvpResolveConfig.ShouldUseHostAuthoritativeSnapshotSync(runState))
                    {
                        ApplyLiveSnapshot(runState, finalSnapshot);
                        TryAlignCombatRoundNumber(runState, message.roundResult.roundIndex, "resume_round_result");
                        Log.Info($"[ParallelTurnPvp] Resume applied authoritative final snapshot. round={message.roundResult.roundIndex} snapshotVersion={message.roundResult.snapshotVersion}");
                    }
                    else
                    {
                        runtime.QueueAuthoritativeSnapshot(finalSnapshot);
                    }
                    runtime.ClearClientAwaitAuthoritativeResult();
                    TryApplyClientAuthoritativeMatchEnd(runState, runtime, authoritativeResult, "resume_state");
                    appliedAny = true;
                }
            }
        }

        if (appliedAny)
        {
            runtime.ClearDisconnectedPendingResume("resume_state_applied");
            if (ClientResumeRequestStateTable.TryGetValue(runState, out ClientResumeRequestState? requestState))
            {
                requestState.AttemptCount = 0;
                requestState.LastSentUtc = default;
            }
        }

        Log.Info($"[ParallelTurnPvp] Received resume state response. applied={appliedAny} hasRoundState={message.hasRoundState} hasPlanningFrame={message.hasPlanningFrame} hasRoundResult={message.hasRoundResult}");
    }

    private static void HandleRoundResultMessage(PvpRoundResultMessage message, ulong _)
    {
        if (message.roundIndex <= 0 || message.snapshotVersion <= 0 || RunManager.Instance.DebugOnlyGetState() is not RunState runState)
        {
            return;
        }

        var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        if (!ValidateRoomContext(runtime, message.roomSessionId, message.roomTopology, nameof(PvpRoundResultMessage)))
        {
            return;
        }

        if (RunManager.Instance.NetService.Type != NetGameType.Client &&
            IsStaleIncomingRound(runtime, message.roundIndex, nameof(PvpRoundResultMessage), message.snapshotVersion))
        {
            return;
        }

        if (RunManager.Instance.NetService.Type == NetGameType.Client &&
            runtime.CurrentRound.RoundIndex > message.roundIndex)
        {
            Log.Warn($"[ParallelTurnPvp] Processing lagged authoritative round result on client. incomingRound={message.roundIndex} localRound={runtime.CurrentRound.RoundIndex} snapshotVersion={message.snapshotVersion}");
        }

        if (!runtime.TryMarkRoundResultReceived(message.snapshotVersion))
        {
            Log.Info($"[ParallelTurnPvp] Ignored duplicate/stale authoritative round result. round={message.roundIndex} snapshotVersion={message.snapshotVersion}");
            return;
        }

        var initialSnapshot = runtime.CurrentRound.SnapshotAtRoundStart;
        var finalSnapshot = CreateSnapshotFromMessage(initialSnapshot, message);
        var authoritativeResult = new PvpRoundResult
        {
            RoundIndex = message.roundIndex,
            InitialSnapshot = initialSnapshot,
            FinalSnapshot = finalSnapshot
        };
        foreach (PvpResolvedEvent resolvedEvent in CreateResolvedEvents(message))
        {
            authoritativeResult.Events.Add(resolvedEvent);
        }

        runtime.ApplyAuthoritativeResult(authoritativeResult);
        string liveApplyMode = "host";
        if (RunManager.Instance.NetService.Type == NetGameType.Client)
        {
            runtime.ClearClientAwaitAuthoritativeResult();
            runtime.QueueAuthoritativeSnapshot(finalSnapshot);
            TryApplyClientAuthoritativeMatchEnd(runState, runtime, authoritativeResult, "round_result");
            liveApplyMode = "queued";
            Log.Info($"[ParallelTurnPvp] Queued authoritative live snapshot on client. round={message.roundIndex} snapshotVersion={message.snapshotVersion}");
        }

        VerifyDelayedFingerprint(runtime, message);

        Log.Info($"[ParallelTurnPvp] Received authoritative round result. round={message.roundIndex} snapshotVersion={message.snapshotVersion} hero1Hp={message.hero1Hp} hero2Hp={message.hero2Hp} events={message.eventTexts?.Count ?? 0} delayedFingerprint={message.delayedCommandFingerprint} delayedCount={message.delayedCommandCount} roomSession={message.roomSessionId} topology={(PvpArenaTopology)message.roomTopology} liveApply={liveApplyMode}");
    }

    private static void TryApplyClientAuthoritativeMatchEnd(RunState runState, PvpMatchRuntime runtime, PvpRoundResult authoritativeResult, string source)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Client)
        {
            return;
        }

        if (runState.Modifiers.OfType<Models.ParallelTurnPvpDebugModifier>().FirstOrDefault() is not { } modifier || modifier.MatchEnded)
        {
            return;
        }

        IReadOnlyDictionary<ulong, PvpCreatureSnapshot> heroes = authoritativeResult.FinalSnapshot.Heroes;
        if (heroes.Count < 2)
        {
            return;
        }

        List<ulong> aliveHeroes = [];
        foreach ((ulong netId, PvpCreatureSnapshot heroSnapshot) in heroes)
        {
            if (heroSnapshot.Exists && heroSnapshot.CurrentHp > 0)
            {
                aliveHeroes.Add(netId);
            }
        }

        if (aliveHeroes.Count > 1)
        {
            return;
        }

        ulong winnerNetId = aliveHeroes.Count == 1 ? aliveHeroes[0] : 0UL;
        Log.Warn($"[ParallelTurnPvp] Client authoritative match-end fallback triggered. source={source} round={authoritativeResult.RoundIndex} winner={winnerNetId} snapshotVersion={authoritativeResult.FinalSnapshot.SnapshotVersion}");
        TaskHelper.RunSafely(global::ParallelTurnPvp.Patches.ParallelTurnMatchEndFlow.EndMatchAsync(runState, modifier, winnerNetId, authoritativeResult.RoundIndex));
    }

    private static void VerifyDelayedFingerprint(PvpMatchRuntime runtime, PvpRoundResultMessage message)
    {
        if (runtime.CurrentRound.RoundIndex <= 0)
        {
            return;
        }

        IReadOnlyList<PvpRoundSubmission> authoritativeSubmissions =
            runtime.LastAuthoritativePlanningFrame is { } frame && frame.RoundIndex == message.roundIndex
                ? frame.Submissions
                : runtime.GetPlanningSubmissions();
        (uint authoritativeFingerprint, int authoritativeCount) = BuildDelayedFingerprint(runtime.CurrentRound.SnapshotAtRoundStart, runtime.CurrentRound.RoundIndex, authoritativeSubmissions);

        IReadOnlyList<PvpRoundSubmission> localSubmissions = runtime.GetPlanningSubmissions();
        (uint localFingerprint, int localCount) = BuildDelayedFingerprint(runtime.CurrentRound.SnapshotAtRoundStart, runtime.CurrentRound.RoundIndex, localSubmissions);

        if (authoritativeFingerprint == message.delayedCommandFingerprint && authoritativeCount == message.delayedCommandCount)
        {
            if (localFingerprint != authoritativeFingerprint || localCount != authoritativeCount)
            {
                Log.Info($"[ParallelTurnPvp] Delayed fingerprint authoritative match with local drift. round={message.roundIndex} hostFingerprint={message.delayedCommandFingerprint} hostCount={message.delayedCommandCount} localFingerprint={localFingerprint} localCount={localCount}");
            }
            else
            {
                Log.Info($"[ParallelTurnPvp] Delayed fingerprint matched. round={message.roundIndex} fingerprint={authoritativeFingerprint} count={authoritativeCount}");
            }

            return;
        }

        Log.Warn($"[ParallelTurnPvp] Delayed fingerprint mismatch. round={message.roundIndex} hostFingerprint={message.delayedCommandFingerprint} hostCount={message.delayedCommandCount} authoritativeFingerprint={authoritativeFingerprint} authoritativeCount={authoritativeCount} localFingerprint={localFingerprint} localCount={localCount}");
    }

    private static (uint Fingerprint, int CommandCount) BuildDelayedFingerprint(PvpCombatSnapshot snapshotAtRoundStart, int roundIndex, IReadOnlyList<PvpRoundSubmission> submissions)
    {
        PvpRoundExecutionPlan executionPlan = FingerprintExecutionPlanner.BuildPlan(roundIndex, submissions);
        PvpRoundDeltaPlan deltaPlan = FingerprintDeltaPlanner.BuildDeltaPlan(snapshotAtRoundStart, executionPlan);
        PvpRoundDelayedPlan delayedPlan = FingerprintDelayedPlanner.BuildDelayedPlan(snapshotAtRoundStart, deltaPlan);
        PvpRoundDelayedCommandPlan delayedCommandPlan = FingerprintDelayedCommandPlanner.BuildCommandPlan(snapshotAtRoundStart, delayedPlan);
        return PvpDelayedPlanFingerprint.Compute(delayedCommandPlan);
    }

    private static PvpRoundStateMessage CreateRoundStateMessage(PvpRoundState state, PvpMatchRuntime? runtime)
    {
        var orderedHeroes = state.SnapshotAtRoundStart.Heroes.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToList();
        var orderedFrontlines = state.SnapshotAtRoundStart.Frontlines.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToList();
        return new PvpRoundStateMessage
        {
            roomSessionId = runtime?.RoomSession.SessionId ?? string.Empty,
            roomTopology = (int)(runtime?.RoomSession.Topology ?? PvpArenaTopology.SharedCombat),
            roundIndex = state.RoundIndex,
            snapshotVersion = state.SnapshotAtRoundStart.SnapshotVersion,
            phase = (int)state.Phase,
            hero1Hp = orderedHeroes.ElementAtOrDefault(0)?.CurrentHp ?? 0,
            hero2Hp = orderedHeroes.ElementAtOrDefault(1)?.CurrentHp ?? 0,
            hero1MaxHp = orderedHeroes.ElementAtOrDefault(0)?.MaxHp ?? 0,
            hero2MaxHp = orderedHeroes.ElementAtOrDefault(1)?.MaxHp ?? 0,
            hero1Block = orderedHeroes.ElementAtOrDefault(0)?.Block ?? 0,
            hero2Block = orderedHeroes.ElementAtOrDefault(1)?.Block ?? 0,
            frontline1Exists = orderedFrontlines.ElementAtOrDefault(0)?.Exists ?? false,
            frontline2Exists = orderedFrontlines.ElementAtOrDefault(1)?.Exists ?? false,
            frontline1Hp = orderedFrontlines.ElementAtOrDefault(0)?.CurrentHp ?? 0,
            frontline2Hp = orderedFrontlines.ElementAtOrDefault(1)?.CurrentHp ?? 0,
            frontline1MaxHp = orderedFrontlines.ElementAtOrDefault(0)?.MaxHp ?? 0,
            frontline2MaxHp = orderedFrontlines.ElementAtOrDefault(1)?.MaxHp ?? 0,
            frontline1Block = orderedFrontlines.ElementAtOrDefault(0)?.Block ?? 0,
            frontline2Block = orderedFrontlines.ElementAtOrDefault(1)?.Block ?? 0
        };
    }

    private static PvpRoundResultMessage CreateRoundResultMessage(PvpRoundResult result, PvpMatchRuntime? runtime)
    {
        var orderedHeroes = result.FinalSnapshot.Heroes.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToList();
        var orderedFrontlines = result.FinalSnapshot.Frontlines.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToList();
        IReadOnlyList<PvpResolvedEvent> networkEvents = CreateNetworkResolvedEvents(result);
        (uint delayedFingerprint, int delayedCount) = PvpDelayedPlanFingerprint.Compute(result.DelayedCommandPlan);

        return new PvpRoundResultMessage
        {
            roomSessionId = runtime?.RoomSession.SessionId ?? string.Empty,
            roomTopology = (int)(runtime?.RoomSession.Topology ?? PvpArenaTopology.SharedCombat),
            roundIndex = result.RoundIndex,
            snapshotVersion = result.FinalSnapshot.SnapshotVersion,
            delayedCommandFingerprint = delayedFingerprint,
            delayedCommandCount = delayedCount,
            hero1Hp = orderedHeroes.ElementAtOrDefault(0)?.CurrentHp ?? 0,
            hero2Hp = orderedHeroes.ElementAtOrDefault(1)?.CurrentHp ?? 0,
            hero1MaxHp = orderedHeroes.ElementAtOrDefault(0)?.MaxHp ?? 0,
            hero2MaxHp = orderedHeroes.ElementAtOrDefault(1)?.MaxHp ?? 0,
            hero1Block = orderedHeroes.ElementAtOrDefault(0)?.Block ?? 0,
            hero2Block = orderedHeroes.ElementAtOrDefault(1)?.Block ?? 0,
            frontline1Exists = orderedFrontlines.ElementAtOrDefault(0)?.Exists ?? false,
            frontline2Exists = orderedFrontlines.ElementAtOrDefault(1)?.Exists ?? false,
            frontline1Hp = orderedFrontlines.ElementAtOrDefault(0)?.CurrentHp ?? 0,
            frontline2Hp = orderedFrontlines.ElementAtOrDefault(1)?.CurrentHp ?? 0,
            frontline1MaxHp = orderedFrontlines.ElementAtOrDefault(0)?.MaxHp ?? 0,
            frontline2MaxHp = orderedFrontlines.ElementAtOrDefault(1)?.MaxHp ?? 0,
            frontline1Block = orderedFrontlines.ElementAtOrDefault(0)?.Block ?? 0,
            frontline2Block = orderedFrontlines.ElementAtOrDefault(1)?.Block ?? 0,
            eventKinds = networkEvents.Select(resolvedEvent => (int)resolvedEvent.Kind).ToList(),
            eventTexts = networkEvents.Select(resolvedEvent => resolvedEvent.Text).ToList()
        };
    }

    private static PvpCombatSnapshot CreateSnapshotFromRoundStateMessage(PvpCombatSnapshot initialSnapshot, PvpRoundStateMessage message)
    {
        return CreateSnapshotFromMessage(initialSnapshot, new PvpRoundResultMessage
        {
            roomSessionId = message.roomSessionId,
            roomTopology = message.roomTopology,
            roundIndex = message.roundIndex,
            snapshotVersion = message.snapshotVersion,
            hero1Hp = message.hero1Hp,
            hero2Hp = message.hero2Hp,
            hero1MaxHp = message.hero1MaxHp,
            hero2MaxHp = message.hero2MaxHp,
            hero1Block = message.hero1Block,
            hero2Block = message.hero2Block,
            frontline1Exists = message.frontline1Exists,
            frontline2Exists = message.frontline2Exists,
            frontline1Hp = message.frontline1Hp,
            frontline2Hp = message.frontline2Hp,
            frontline1MaxHp = message.frontline1MaxHp,
            frontline2MaxHp = message.frontline2MaxHp,
            frontline1Block = message.frontline1Block,
            frontline2Block = message.frontline2Block
        });
    }

    private static PvpCombatSnapshot CreateSnapshotFromMessage(PvpCombatSnapshot initialSnapshot, PvpRoundResultMessage message)
    {
        var snapshot = new PvpCombatSnapshot
        {
            RoundIndex = message.roundIndex,
            SnapshotVersion = message.snapshotVersion
        };

        var orderedHeroes = initialSnapshot.Heroes.OrderBy(entry => entry.Key).ToList();
        for (int i = 0; i < orderedHeroes.Count; i++)
        {
            ulong playerId = orderedHeroes[i].Key;
            PvpCreatureSnapshot initialHero = orderedHeroes[i].Value;
            int hp = i == 0 ? message.hero1Hp : message.hero2Hp;
            int block = i == 0 ? message.hero1Block : message.hero2Block;
            snapshot.Heroes[playerId] = new PvpCreatureSnapshot
            {
                Exists = initialHero.Exists,
                CurrentHp = hp,
                MaxHp = i == 0 ? message.hero1MaxHp : message.hero2MaxHp,
                Block = block
            };
        }

        foreach (var frontline in initialSnapshot.Frontlines)
        {
            int orderedIndex = orderedHeroes.FindIndex(entry => entry.Key == frontline.Key);
            bool exists = orderedIndex == 0 ? message.frontline1Exists : orderedIndex == 1 ? message.frontline2Exists : frontline.Value.Exists;
            int hp = orderedIndex == 0 ? message.frontline1Hp : orderedIndex == 1 ? message.frontline2Hp : frontline.Value.CurrentHp;
            int block = orderedIndex == 0 ? message.frontline1Block : orderedIndex == 1 ? message.frontline2Block : frontline.Value.Block;
            snapshot.Frontlines[frontline.Key] = new PvpCreatureSnapshot
            {
                Exists = exists,
                CurrentHp = hp,
                MaxHp = orderedIndex == 0 ? message.frontline1MaxHp : orderedIndex == 1 ? message.frontline2MaxHp : frontline.Value.MaxHp,
                Block = block
            };
        }

        return snapshot;
    }

    private static IEnumerable<PvpResolvedEvent> CreateResolvedEvents(PvpRoundResultMessage message)
    {
        List<int> eventKinds = message.eventKinds ?? new List<int>();
        List<string> eventTexts = message.eventTexts ?? new List<string>();
        int eventCount = Math.Min(eventKinds.Count, eventTexts.Count);
        for (int i = 0; i < eventCount; i++)
        {
            yield return new PvpResolvedEvent
            {
                Kind = (PvpResolvedEventKind)eventKinds[i],
                Text = eventTexts[i] ?? string.Empty
            };
        }
    }

    private static IReadOnlyList<PvpResolvedEvent> CreateNetworkResolvedEvents(PvpRoundResult result)
    {
        List<PvpResolvedEvent> filteredEvents = result.Events
            .Where(resolvedEvent => NetworkSummaryEventKinds.Contains(resolvedEvent.Kind))
            .Select(TrimResolvedEventForNetwork)
            .ToList();

        if (filteredEvents.Count == 0)
        {
            filteredEvents.Add(new PvpResolvedEvent
            {
                Kind = PvpResolvedEventKind.RoundResolved,
                Text = $"Resolved round {result.RoundIndex}."
            });
        }

        int availableSlots = MaxNetworkRoundEvents;
        bool needsTrimNote = filteredEvents.Count != result.Events.Count || filteredEvents.Count > MaxNetworkRoundEvents;
        if (needsTrimNote)
        {
            availableSlots = Math.Max(MaxNetworkRoundEvents - 1, 1);
        }

        if (filteredEvents.Count > availableSlots)
        {
            filteredEvents = filteredEvents.Take(availableSlots).ToList();
        }

        if (needsTrimNote)
        {
            filteredEvents.Add(new PvpResolvedEvent
            {
                Kind = PvpResolvedEventKind.RoundResolved,
                Text = $"Round summary trimmed for network: showing {filteredEvents.Count} of {result.Events.Count} events."
            });
        }

        return filteredEvents;
    }

    private static PvpResolvedEvent TrimResolvedEventForNetwork(PvpResolvedEvent resolvedEvent)
    {
        if (resolvedEvent.Text.Length <= MaxNetworkEventTextLength)
        {
            return resolvedEvent;
        }

        return new PvpResolvedEvent
        {
            Kind = resolvedEvent.Kind,
            Text = $"{resolvedEvent.Text[..(MaxNetworkEventTextLength - 3)]}..."
        };
    }

    private static PvpRoundSubmissionPacket CreateSubmissionPacket(PvpRoundSubmission submission)
    {
        return new PvpRoundSubmissionPacket
        {
            roundIndex = submission.RoundIndex,
            playerId = submission.PlayerId,
            roundStartEnergy = submission.RoundStartEnergy,
            locked = submission.Locked,
            isFirstFinisher = submission.IsFirstFinisher,
            actions = submission.Actions.Select(action => new PvpPlannedActionPacket
            {
                sequence = action.Sequence,
                hasRuntimeActionId = action.RuntimeActionId != null,
                runtimeActionId = action.RuntimeActionId ?? 0U,
                actionType = (int)action.ActionType,
                modelEntry = action.ModelEntry,
                targetOwnerPlayerId = action.Target.OwnerPlayerId,
                targetKind = (int)action.Target.Kind
            }).ToList()
        };
    }

    private static PvpRoundSubmission CreateSubmission(PvpRoundSubmissionPacket packet)
    {
        var submission = new PvpRoundSubmission
        {
            RoundIndex = packet.roundIndex,
            PlayerId = packet.playerId,
            RoundStartEnergy = packet.roundStartEnergy,
            Locked = packet.locked,
            IsFirstFinisher = packet.isFirstFinisher
        };

        foreach (PvpPlannedActionPacket actionPacket in packet.actions ?? new List<PvpPlannedActionPacket>())
        {
            submission.Actions.Add(new PvpPlannedAction
            {
                Sequence = actionPacket.sequence,
                RuntimeActionId = actionPacket.hasRuntimeActionId ? actionPacket.runtimeActionId : null,
                ActionType = (PvpActionType)actionPacket.actionType,
                ModelEntry = actionPacket.modelEntry ?? string.Empty,
                Target = new PvpTargetRef
                {
                    OwnerPlayerId = actionPacket.targetOwnerPlayerId,
                    Kind = (PvpTargetKind)actionPacket.targetKind
                }
            });
        }

        return submission;
    }

    private static PvpPlanningFrameMessage CreatePlanningFrameMessage(PvpPlanningFrame frame)
    {
        return new PvpPlanningFrameMessage
        {
            roomSessionId = frame.RoomSessionId ?? string.Empty,
            roomTopology = (int)frame.RoomTopology,
            roundIndex = frame.RoundIndex,
            snapshotVersion = frame.SnapshotVersion,
            phase = (int)frame.Phase,
            revision = frame.Revision,
            submissions = frame.Submissions.Select(CreateSubmissionPacket).ToList()
        };
    }

    private static PvpPlanningFrame CreatePlanningFrame(PvpPlanningFrameMessage message)
    {
        var frame = new PvpPlanningFrame
        {
            RoundIndex = message.roundIndex,
            SnapshotVersion = message.snapshotVersion,
            Phase = (PvpMatchPhase)message.phase,
            Revision = message.revision,
            RoomSessionId = message.roomSessionId ?? string.Empty,
            RoomTopology = Enum.IsDefined(typeof(PvpArenaTopology), message.roomTopology)
                ? (PvpArenaTopology)message.roomTopology
                : PvpArenaTopology.SharedCombat
        };
        foreach (PvpRoundSubmissionPacket submissionPacket in message.submissions ?? new List<PvpRoundSubmissionPacket>())
        {
            frame.Submissions.Add(CreateSubmission(submissionPacket));
        }

        return frame;
    }

    private static PvpResumeStateMessage BuildResumeStateMessage(PvpMatchRuntime runtime)
    {
        PvpRoundState currentRound = runtime.CurrentRound;
        var response = new PvpResumeStateMessage
        {
            roomSessionId = runtime.RoomSession.SessionId,
            roomTopology = (int)runtime.RoomSession.Topology
        };

        if (currentRound.RoundIndex > 0 && currentRound.SnapshotAtRoundStart.SnapshotVersion > 0)
        {
            response.hasRoundState = true;
            response.roundState = CreateRoundStateMessage(currentRound, runtime);
        }

        PvpPlanningFrame frame = runtime.LastAuthoritativePlanningFrame ?? runtime.BuildPlanningFrame();
        if (frame.RoundIndex > 0 && frame.SnapshotVersion > 0 && frame.Revision > 0)
        {
            response.hasPlanningFrame = true;
            response.planningFrame = CreatePlanningFrameMessage(frame);
        }

        PvpRoundResult? latestResult = runtime.LastAuthoritativeResult ?? currentRound.LastResult;
        if (latestResult != null &&
            latestResult.RoundIndex > 0 &&
            latestResult.FinalSnapshot.SnapshotVersion > 0 &&
            (currentRound.RoundIndex <= 0 || latestResult.RoundIndex >= currentRound.RoundIndex))
        {
            response.hasRoundResult = true;
            response.roundResult = CreateRoundResultMessage(latestResult, runtime);
        }

        return response;
    }

    private static void ResetRoundFromAuthoritativeState(
        PvpMatchRuntime runtime,
        RunState runState,
        PvpCombatSnapshot snapshot,
        PvpMatchPhase phase,
        int planningRevision,
        string? roomSessionId,
        PvpArenaTopology roomTopology)
    {
        runtime.CurrentRound.RoundIndex = snapshot.RoundIndex;
        runtime.CurrentRound.Phase = phase;
        runtime.CurrentRound.PlanningRevision = Math.Max(planningRevision, 1);
        runtime.CurrentRound.RoomSessionId = string.IsNullOrWhiteSpace(roomSessionId)
            ? runtime.RoomSession.SessionId
            : roomSessionId;
        runtime.CurrentRound.RoomTopology = roomTopology;
        runtime.CurrentRound.SnapshotAtRoundStart = snapshot;
        runtime.CurrentRound.LastResult = null;
        runtime.CurrentRound.HasResolved = phase is PvpMatchPhase.RoundEnd or PvpMatchPhase.MatchEnd;
        runtime.CurrentRound.DelayedLiveEffectsApplied = false;
        runtime.CurrentRound.FirstLockedPlayerId = 0;
        runtime.CurrentRound.FirstLockRewardGranted = false;
        runtime.CurrentRound.PendingAuthoritativeSnapshot = null;
        runtime.CurrentRound.ResolveInputSourceTag = string.Empty;

        runtime.CurrentRound.LogsByPlayer.Clear();
        runtime.CurrentRound.PublicIntentByPlayer.Clear();
        runtime.CurrentRound.RecordedActionKeys.Clear();
        runtime.CurrentRound.NetworkSubmissionsByPlayer.Clear();
        runtime.CurrentRound.NetworkSubmissionRevisionByPlayer.Clear();
        runtime.CurrentRound.ResolverFallbackPlayers.Clear();
        runtime.CurrentRound.ResolverForcedLockedPlayers.Clear();

        foreach (Player player in runState.Players)
        {
            runtime.CurrentRound.LogsByPlayer[player.NetId] = new PvpActionLog
            {
                PlayerId = player.NetId
            };
            runtime.CurrentRound.PublicIntentByPlayer[player.NetId] = new PvpPlayerIntentState
            {
                PlayerId = player.NetId,
                RoundStartEnergy = player.PlayerCombatState?.Energy ?? player.MaxEnergy
            };
        }
    }

    private static void ApplyAuthoritativePlanningSubmissions(PvpMatchRuntime runtime, RunState runState, PvpPlanningFrame frame)
    {
        if (frame.RoundIndex != runtime.CurrentRound.RoundIndex ||
            frame.SnapshotVersion != runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion)
        {
            Log.Warn($"[ParallelTurnPvp] Skipped authoritative submission rehydrate: frame/snapshot mismatch. frameRound={frame.RoundIndex} frameSnapshot={frame.SnapshotVersion} localRound={runtime.CurrentRound.RoundIndex} localSnapshot={runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion}");
            return;
        }

        HashSet<ulong> playerIds = runState.Players.Select(player => player.NetId).ToHashSet();
        foreach (PvpRoundSubmission submission in frame.Submissions.OrderBy(item => item.PlayerId))
        {
            if (!playerIds.Contains(submission.PlayerId))
            {
                continue;
            }

            if (submission.RoundIndex != frame.RoundIndex)
            {
                continue;
            }

            ApplySubmissionToRoundState(runtime.CurrentRound, submission);
            runtime.CurrentRound.NetworkSubmissionsByPlayer[submission.PlayerId] = CloneSubmission(submission);
            runtime.CurrentRound.NetworkSubmissionRevisionByPlayer[submission.PlayerId] = Math.Max(frame.Revision, 1);
            if (submission.IsFirstFinisher && runtime.CurrentRound.FirstLockedPlayerId == 0)
            {
                runtime.CurrentRound.FirstLockedPlayerId = submission.PlayerId;
            }
        }

        runtime.CurrentRound.Phase = frame.Phase;
        runtime.CurrentRound.PlanningRevision = Math.Max(frame.Revision, 1);
        runtime.CurrentRound.HasResolved = frame.Phase is PvpMatchPhase.RoundEnd or PvpMatchPhase.MatchEnd;
    }

    private static void ApplySubmissionToRoundState(PvpRoundState roundState, PvpRoundSubmission submission)
    {
        var actionLog = new PvpActionLog
        {
            PlayerId = submission.PlayerId,
            Locked = submission.Locked
        };

        foreach (PvpPlannedAction plannedAction in submission.Actions.OrderBy(item => item.Sequence))
        {
            var runtimeAction = new PvpAction
            {
                ActorPlayerId = submission.PlayerId,
                RoundIndex = submission.RoundIndex,
                Sequence = plannedAction.Sequence,
                RuntimeActionId = plannedAction.RuntimeActionId,
                ActionType = plannedAction.ActionType,
                ModelEntry = plannedAction.ModelEntry,
                Target = plannedAction.Target
            };

            actionLog.Actions.Add(runtimeAction);
            roundState.RecordedActionKeys.Add(GetActionDedupeKey(runtimeAction));
        }

        roundState.LogsByPlayer[submission.PlayerId] = actionLog;
        if (!roundState.PublicIntentByPlayer.TryGetValue(submission.PlayerId, out PvpPlayerIntentState? intentState))
        {
            intentState = new PvpPlayerIntentState
            {
                PlayerId = submission.PlayerId
            };
            roundState.PublicIntentByPlayer[submission.PlayerId] = intentState;
        }

        intentState.RoundStartEnergy = submission.RoundStartEnergy;
        intentState.Locked = submission.Locked;
        intentState.IsFirstFinisher = submission.IsFirstFinisher;
        intentState.Slots.Clear();

        foreach (PvpPlannedAction plannedAction in submission.Actions.Where(item => ShouldCreateIntentSlot(item.ActionType)))
        {
            var runtimeAction = new PvpAction
            {
                ActorPlayerId = submission.PlayerId,
                RoundIndex = submission.RoundIndex,
                Sequence = plannedAction.Sequence,
                RuntimeActionId = plannedAction.RuntimeActionId,
                ActionType = plannedAction.ActionType,
                ModelEntry = plannedAction.ModelEntry,
                Target = plannedAction.Target
            };
            intentState.Slots.Add(new PvpPublicIntentSlot
            {
                Category = PvpIntentClassifier.GetCategory(runtimeAction),
                TargetSide = PvpIntentClassifier.GetTargetSide(runtimeAction)
            });
        }
    }

    private static bool ShouldCreateIntentSlot(PvpActionType actionType)
    {
        return actionType is PvpActionType.PlayCard or PvpActionType.UsePotion;
    }

    private static string GetActionDedupeKey(PvpAction action)
    {
        if (action.RuntimeActionId != null)
        {
            return $"{action.ActorPlayerId}:{action.RuntimeActionId.Value}";
        }

        return $"{action.ActorPlayerId}:{action.RoundIndex}:{action.ActionType}:{action.ModelEntry}:{action.Target.Kind}:{action.Target.OwnerPlayerId}";
    }

    private static PvpRoundSubmission CloneSubmission(PvpRoundSubmission source)
    {
        var cloned = new PvpRoundSubmission
        {
            RoundIndex = source.RoundIndex,
            PlayerId = source.PlayerId,
            RoundStartEnergy = source.RoundStartEnergy,
            Locked = source.Locked,
            IsFirstFinisher = source.IsFirstFinisher
        };

        foreach (PvpPlannedAction action in source.Actions)
        {
            cloned.Actions.Add(new PvpPlannedAction
            {
                Sequence = action.Sequence,
                RuntimeActionId = action.RuntimeActionId,
                ActionType = action.ActionType,
                ModelEntry = action.ModelEntry,
                Target = new PvpTargetRef
                {
                    OwnerPlayerId = action.Target.OwnerPlayerId,
                    Kind = action.Target.Kind
                }
            });
        }

        return cloned;
    }

    private static bool ShouldRefreshClientRound(PvpMatchRuntime runtime, int incomingRoundIndex)
    {
        if (incomingRoundIndex > runtime.CurrentRound.RoundIndex)
        {
            return true;
        }

        if (incomingRoundIndex == runtime.CurrentRound.RoundIndex &&
            (runtime.CurrentRound.HasResolved || runtime.CurrentRound.PublicIntentByPlayer.Values.Any(state => state.Slots.Count > 0 || state.Locked)))
        {
            return true;
        }

        return runtime.CurrentRound.RoundIndex == 0;
    }

    private static bool ShouldRefreshClientRoundForPlanningFrame(PvpMatchRuntime runtime, int incomingRoundIndex)
    {
        if (incomingRoundIndex > runtime.CurrentRound.RoundIndex)
        {
            return true;
        }

        return runtime.CurrentRound.RoundIndex == 0;
    }

    private static bool ValidateRoomContext(PvpMatchRuntime runtime, string? incomingRoomSessionId, int incomingRoomTopology, string messageName)
    {
        PvpArenaTopology incomingTopology = Enum.IsDefined(typeof(PvpArenaTopology), incomingRoomTopology)
            ? (PvpArenaTopology)incomingRoomTopology
            : PvpArenaTopology.SharedCombat;
        string roomSessionId = incomingRoomSessionId ?? string.Empty;

        if (string.IsNullOrWhiteSpace(roomSessionId))
        {
            Log.Warn($"[ParallelTurnPvp] Ignored {messageName}: missing room session id.");
            return false;
        }

        if (!string.Equals(runtime.RoomSession.SessionId, roomSessionId, StringComparison.Ordinal) ||
            runtime.RoomSession.Topology != incomingTopology)
        {
            Log.Warn($"[ParallelTurnPvp] Ignored {messageName}: room context mismatch. incoming={roomSessionId}/{incomingTopology} local={runtime.RoomSession.SessionId}/{runtime.RoomSession.Topology}");
            return false;
        }

        return true;
    }

    private static void BufferDeferredPlanningFrame(RunState runState, PvpMatchRuntime runtime, PvpPlanningFrame frame, string reason)
    {
        if (RunManager.Instance.NetService.Type == NetGameType.Client &&
            IsPlanningFrameStaleByAck(runState, frame, out int ackRevision))
        {
            Log.Info($"[ParallelTurnPvp] Ignored deferred planning frame below ACK floor. reason={reason} frameRound={frame.RoundIndex} frameSnapshot={frame.SnapshotVersion} frameRevision={frame.Revision} ackRevision={ackRevision}");
            return;
        }

        PendingPlanningFrameState pendingState = PendingPlanningFrameStateTable.GetOrCreateValue(runState);
        if (pendingState.Frame != null)
        {
            if (frame.RoundIndex < pendingState.Frame.RoundIndex ||
                (frame.RoundIndex == pendingState.Frame.RoundIndex && frame.Revision <= pendingState.Frame.Revision))
            {
                Log.Info($"[ParallelTurnPvp] Deferred authoritative planning frame ignored (older/equal pending). reason={reason} frameRound={frame.RoundIndex} frameSnapshot={frame.SnapshotVersion} frameRevision={frame.Revision} pendingRound={pendingState.Frame.RoundIndex} pendingSnapshot={pendingState.Frame.SnapshotVersion} pendingRevision={pendingState.Frame.Revision}");
                return;
            }
        }

        pendingState.Frame = ClonePlanningFrame(frame);
        Log.Info($"[ParallelTurnPvp] Deferred authoritative planning frame cached. reason={reason} frameRound={frame.RoundIndex} frameSnapshot={frame.SnapshotVersion} frameRevision={frame.Revision} localRound={runtime.CurrentRound.RoundIndex} localSnapshot={runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion} localRevision={runtime.CurrentRound.PlanningRevision}");
    }

    private static void TryApplyDeferredPlanningFrame(RunState runState, PvpMatchRuntime runtime, string source)
    {
        if (!PendingPlanningFrameStateTable.TryGetValue(runState, out PendingPlanningFrameState? pendingState) ||
            pendingState.Frame == null)
        {
            return;
        }

        PvpPlanningFrame frame = pendingState.Frame;
        int localRound = runtime.CurrentRound.RoundIndex;
        int localSnapshot = runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion;
        if (frame.RoundIndex < localRound)
        {
            Log.Info($"[ParallelTurnPvp] Dropped deferred planning frame: stale by round. source={source} frameRound={frame.RoundIndex} frameSnapshot={frame.SnapshotVersion} frameRevision={frame.Revision} localRound={localRound} localSnapshot={localSnapshot}");
            pendingState.Frame = null;
            return;
        }

        if (frame.RoundIndex != localRound || frame.SnapshotVersion != localSnapshot)
        {
            return;
        }

        if (RunManager.Instance.NetService.Type == NetGameType.Client &&
            IsPlanningFrameStaleByAck(runState, frame, out int ackRevision))
        {
            Log.Info($"[ParallelTurnPvp] Dropped deferred planning frame below ACK floor. source={source} round={frame.RoundIndex} snapshotVersion={frame.SnapshotVersion} revision={frame.Revision} ackRevision={ackRevision}");
            pendingState.Frame = null;
            return;
        }

        pendingState.Frame = null;
        if (!runtime.TryMarkPlanningFrameReceived(frame.RoundIndex, frame.Revision))
        {
            Log.Info($"[ParallelTurnPvp] Deferred planning frame became duplicate/stale on apply. source={source} round={frame.RoundIndex} snapshotVersion={frame.SnapshotVersion} revision={frame.Revision}");
            return;
        }

        runtime.ApplyAuthoritativePlanningFrame(frame);
        Log.Info($"[ParallelTurnPvp] Applied deferred authoritative planning frame. source={source} round={frame.RoundIndex} snapshotVersion={frame.SnapshotVersion} revision={frame.Revision}");
    }

    private static PvpPlanningFrame ClonePlanningFrame(PvpPlanningFrame frame)
    {
        var clone = new PvpPlanningFrame
        {
            RoundIndex = frame.RoundIndex,
            SnapshotVersion = frame.SnapshotVersion,
            Phase = frame.Phase,
            Revision = frame.Revision,
            RoomSessionId = frame.RoomSessionId,
            RoomTopology = frame.RoomTopology
        };

        foreach (PvpRoundSubmission submission in frame.Submissions)
        {
            clone.Submissions.Add(CloneSubmission(submission));
        }

        return clone;
    }

    private static bool IsPlanningFrameStaleByAck(RunState runState, PvpPlanningFrame frame, out int ackRevision)
    {
        ackRevision = 0;
        if (!ClientSubmissionRetryStateTable.TryGetValue(runState, out ClientSubmissionRetryState? retryState))
        {
            return false;
        }

        if (retryState.AckRevision <= 0)
        {
            return false;
        }

        if (retryState.AckRoundIndex != frame.RoundIndex || retryState.AckSnapshotVersion != frame.SnapshotVersion)
        {
            return false;
        }

        ackRevision = retryState.AckRevision;
        return frame.Revision < ackRevision;
    }

    private static bool IsStaleIncomingRound(
        PvpMatchRuntime runtime,
        int incomingRoundIndex,
        string messageName,
        int snapshotVersion = 0,
        int revision = 0)
    {
        int localRoundIndex = runtime.CurrentRound.RoundIndex;
        if (incomingRoundIndex <= 0 || localRoundIndex <= 0)
        {
            return false;
        }

        if (incomingRoundIndex >= localRoundIndex)
        {
            return false;
        }

        Log.Warn(
            $"[ParallelTurnPvp] Ignored {messageName}: stale round payload. " +
            $"incomingRound={incomingRoundIndex} localRound={localRoundIndex} " +
            $"incomingSnapshot={snapshotVersion} localSnapshot={runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion} " +
            $"incomingRevision={revision} localRevision={runtime.CurrentRound.PlanningRevision} " +
            $"phase={runtime.CurrentRound.Phase}");
        return true;
    }

    private static void TryAlignCombatRoundNumber(RunState runState, int targetRoundIndex, string source)
    {
        if (!IsRoundNumberMutationEnabled ||
            RunManager.Instance.NetService.Type != NetGameType.Client ||
            targetRoundIndex <= 0 ||
            !PvpResolveConfig.ShouldUseHostAuthoritativeSnapshotSync(runState))
        {
            return;
        }

        CombatState? combatState = runState.Players.FirstOrDefault()?.Creature.CombatState;
        if (combatState == null)
        {
            return;
        }

        int liveRound = Math.Max(1, combatState.RoundNumber);
        if (liveRound >= targetRoundIndex)
        {
            return;
        }

        bool aligned = false;
        try
        {
            var roundSetter = AccessTools.PropertySetter(typeof(CombatState), nameof(CombatState.RoundNumber));
            if (roundSetter != null)
            {
                roundSetter.Invoke(combatState, new object[] { targetRoundIndex });
                aligned = true;
            }
        }
        catch
        {
            // Ignore and try field fallback below.
        }

        if (!aligned)
        {
            foreach (string fieldName in new[] { "<RoundNumber>k__BackingField", "_roundNumber", "roundNumber" })
            {
                try
                {
                    var roundField = AccessTools.Field(typeof(CombatState), fieldName);
                    if (roundField == null)
                    {
                        continue;
                    }

                    roundField.SetValue(combatState, targetRoundIndex);
                    aligned = true;
                    break;
                }
                catch
                {
                    // Try next field candidate.
                }
            }
        }

        if (aligned)
        {
            Log.Warn($"[ParallelTurnPvp] Aligned client live round number. source={source} from={liveRound} to={targetRoundIndex}");
        }
        else
        {
            Log.Warn($"[ParallelTurnPvp] Failed to align client live round number. source={source} live={liveRound} target={targetRoundIndex}");
        }
    }

    private static async Task ForceSwitchSidesAsync(string source)
    {
        await Task.Yield();

        if (!RunManager.Instance.IsInProgress || RunManager.Instance.DebugOnlyGetState() is not RunState runState)
        {
            return;
        }

        if (!runState.Modifiers.OfType<Models.ParallelTurnPvpDebugModifier>().Any())
        {
            return;
        }

        CombatManager? manager = CombatManager.Instance;
        CombatState? combatState = runState.Players.FirstOrDefault()?.Creature.CombatState;
        if (manager == null || combatState == null || combatState.CurrentSide != CombatSide.Player)
        {
            return;
        }

        if (PvpRuntimeRegistry.TryGet(runState) is not { } runtime)
        {
            return;
        }

        bool shouldKick = source switch
        {
            "host_resolve_fallback" => RunManager.Instance.NetService.Type == NetGameType.Host && runtime.CanResolveRound(combatState.RoundNumber),
            "round_align" => runtime.CurrentRound.RoundIndex > Math.Max(1, combatState.RoundNumber),
            _ => false
        };
        if (!shouldKick)
        {
            return;
        }

        var switchSidesMethod = AccessTools.Method(typeof(CombatManager), "SwitchSides");
        if (switchSidesMethod == null)
        {
            Log.Warn("[ParallelTurnPvp] ForceSwitchSides skipped: CombatManager.SwitchSides method not found.");
            return;
        }

        try
        {
            Log.Warn($"[ParallelTurnPvp] ForceSwitchSides executing. source={source} liveRound={combatState.RoundNumber} pvpRound={runtime.CurrentRound.RoundIndex} phase={runtime.CurrentRound.Phase}");
            object? result = switchSidesMethod.Invoke(manager, null);
            if (result is Task task)
            {
                await task;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[ParallelTurnPvp] ForceSwitchSides failed. source={source} error={ex}");
        }
    }

    public static void ApplyLiveSnapshot(RunState runState, PvpCombatSnapshot snapshot)
    {
        CombatState? combatState = runState.Players.FirstOrDefault()?.Creature.CombatState;
        foreach (Player player in runState.Players)
        {
            if (snapshot.Heroes.TryGetValue(player.NetId, out PvpCreatureSnapshot? heroSnapshot))
            {
                ApplyCreatureSnapshot(player.Creature, heroSnapshot);
            }

            if (snapshot.Frontlines.TryGetValue(player.NetId, out PvpCreatureSnapshot? frontlineSnapshot))
            {
                ApplyFrontlineSnapshot(combatState, player, frontlineSnapshot);
            }
        }
    }

    private static void ApplyCreatureSnapshot(Creature creature, PvpCreatureSnapshot snapshot)
    {
        if (creature.MaxHp != snapshot.MaxHp)
        {
            SetCreatureMaxHpExact(creature, snapshot.MaxHp);
        }

        creature.SetCurrentHpInternal(snapshot.CurrentHp);
        if (creature.Block > snapshot.Block)
        {
            creature.LoseBlockInternal(creature.Block - snapshot.Block);
        }
        else if (creature.Block < snapshot.Block)
        {
            creature.GainBlockInternal(snapshot.Block - creature.Block);
        }
    }

    private static void ApplyFrontlineSnapshot(CombatState? combatState, Player player, PvpCreatureSnapshot snapshot)
    {
        Creature? frontline = ResolveTrackedFrontline(combatState, player);
        if (!snapshot.Exists)
        {
            if (frontline == null)
            {
                return;
            }

            if (frontline.CurrentHp > 0)
            {
                frontline.SetCurrentHpInternal(0);
            }

            if (frontline.Block > 0)
            {
                frontline.LoseBlockInternal(frontline.Block);
            }

            if (player.PlayerCombatState?.Pets.Any(pet => ReferenceEquals(pet, frontline)) == true)
            {
                frontline.InvokeDiedEvent();
            }

            return;
        }

        if (frontline == null)
        {
            Log.Warn($"[ParallelTurnPvp] Frontline snapshot apply skipped: tracked creature missing for player={player.NetId}");
            return;
        }

        player.PlayerCombatState?.AddPetInternal(frontline);
        if (frontline.PetOwner != player)
        {
            frontline.PetOwner = player;
        }

        bool wasDead = frontline.IsDead;
        SetCreatureMaxHpExact(frontline, snapshot.MaxHp);
        if (snapshot.CurrentHp > 0)
        {
            if (frontline.IsDead)
            {
                frontline.HealInternal(snapshot.CurrentHp);
            }
            else
            {
                frontline.SetCurrentHpInternal(snapshot.CurrentHp);
            }
        }
        else
        {
            frontline.SetCurrentHpInternal(0);
        }

        if (frontline.Block > snapshot.Block)
        {
            frontline.LoseBlockInternal(frontline.Block - snapshot.Block);
        }
        else if (frontline.Block < snapshot.Block)
        {
            frontline.GainBlockInternal(snapshot.Block - frontline.Block);
        }

        if (wasDead && snapshot.CurrentHp > 0)
        {
            NCombatRoom.Instance?.GetCreatureNode(frontline)?.StartReviveAnim();
        }
    }

    private static Creature? ResolveTrackedFrontline(CombatState? combatState, Player player)
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

    private static void SetCreatureMaxHpExact(Creature creature, int targetMaxHp)
    {
        int safeTarget = Math.Max(0, targetMaxHp);
        creature.SetMaxHpInternal(safeTarget);
        if (creature.MaxHp == safeTarget)
        {
            return;
        }

        foreach (string fieldName in new[] { "<MaxHp>k__BackingField", "_maxHp", "maxHp", "_maxHP", "maxHP" })
        {
            try
            {
                var maxHpField = AccessTools.Field(creature.GetType(), fieldName);
                if (maxHpField == null)
                {
                    continue;
                }

                maxHpField.SetValue(creature, safeTarget);
                if (creature.MaxHp == safeTarget)
                {
                    return;
                }
            }
            catch
            {
                // Try next field candidate.
            }
        }
    }
}
