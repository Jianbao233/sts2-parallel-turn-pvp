namespace ParallelTurnPvp.Core;

public interface IFrontlineRouter
{
    MegaCrit.Sts2.Core.Entities.Creatures.Creature ResolveProtectedTarget(MegaCrit.Sts2.Core.Entities.Creatures.Creature originalTarget);
}

public interface IPvpRoundResolver
{
    PvpRoundResult Resolve(PvpCombatSnapshot initialSnapshot, IReadOnlyList<PvpActionLog> logs, PvpCombatSnapshot finalSnapshot);
}

public interface IPvpPlanningCompiler
{
    PvpRoundSubmission BuildSubmission(int roundIndex, PvpActionLog log, PvpPlayerIntentState? intentState);
}

public interface IPvpSyncBridge
{
    void BroadcastRoundState(PvpRoundState state);
    void BroadcastPlanningFrame(PvpPlanningFrame frame);
    void BroadcastRoundResult(PvpRoundResult result);
}
