namespace ParallelTurnPvp.Core;

public interface IFrontlineRouter
{
    MegaCrit.Sts2.Core.Entities.Creatures.Creature ResolveProtectedTarget(MegaCrit.Sts2.Core.Entities.Creatures.Creature originalTarget);
}

public interface IPvpRoundResolver
{
    PvpRoundResult Resolve(PvpCombatSnapshot initialSnapshot, IReadOnlyList<PvpActionLog> logs, PvpCombatSnapshot finalSnapshot);
}

public interface IPvpSyncBridge
{
    void BroadcastRoundState(PvpRoundState state);
    void BroadcastRoundResult(PvpRoundResult result);
}
