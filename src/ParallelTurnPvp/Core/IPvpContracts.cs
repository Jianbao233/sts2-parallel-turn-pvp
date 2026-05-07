namespace ParallelTurnPvp.Core;

public interface IFrontlineRouter
{
    MegaCrit.Sts2.Core.Entities.Creatures.Creature ResolveProtectedTarget(MegaCrit.Sts2.Core.Entities.Creatures.Creature originalTarget);
}

public interface IPvpRoundResolver
{
    PvpRoundResult Resolve(PvpCombatSnapshot initialSnapshot, IReadOnlyList<PvpRoundSubmission> submissions, PvpCombatSnapshot finalSnapshot);
}

public interface IPvpPlanningCompiler
{
    PvpRoundSubmission BuildSubmission(int roundIndex, PvpActionLog log, PvpPlayerIntentState? intentState);
}

public interface IPvpExecutionPlanner
{
    PvpRoundExecutionPlan BuildPlan(int roundIndex, IReadOnlyList<PvpRoundSubmission> submissions);
}

public interface IPvpDeltaPlanner
{
    PvpRoundDeltaPlan BuildDeltaPlan(PvpCombatSnapshot initialSnapshot, PvpRoundExecutionPlan plan);
}

public interface IPvpPredictionEngine
{
    PvpCombatSnapshot Predict(PvpCombatSnapshot initialSnapshot, PvpRoundDeltaPlan deltaPlan);
}

public interface IPvpPlaybackPlanner
{
    PvpRoundPlaybackPlan BuildPlaybackPlan(PvpCombatSnapshot initialSnapshot, PvpRoundExecutionPlan executionPlan, PvpRoundDeltaPlan deltaPlan, PvpCombatSnapshot finalSnapshot);
}

public interface IPvpDelayedPlanner
{
    PvpRoundDelayedPlan BuildDelayedPlan(PvpCombatSnapshot initialSnapshot, PvpRoundDeltaPlan deltaPlan);
}

public interface IPvpDelayedCommandPlanner
{
    PvpRoundDelayedCommandPlan BuildCommandPlan(PvpCombatSnapshot initialSnapshot, PvpRoundDelayedPlan delayedPlan);
}

public interface IPvpSyncBridge
{
    void BroadcastRoundState(PvpRoundState state);
    void BroadcastPlanningFrame(PvpPlanningFrame frame);
    void BroadcastRoundResult(PvpRoundResult result);
    void SendClientSubmission(PvpPlanningFrame frame, ulong playerId);
}
