using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;

namespace ParallelTurnPvp.Core;

public static class PvpRuntimeRegistry
{
    private static readonly ConditionalWeakTable<RunState, PvpMatchRuntime> Table = new();

    public static PvpMatchRuntime GetOrCreate(RunState runState)
    {
        return Table.GetValue(runState, state => new PvpMatchRuntime(state, state.Players));
    }

    public static PvpMatchRuntime? TryGet(CombatState? combatState)
    {
        if (combatState?.RunState is not RunState runState)
        {
            return null;
        }

        return Table.TryGetValue(runState, out var runtime) ? runtime : null;
    }

    public static PvpMatchRuntime? TryGet(RunState? runState)
    {
        if (runState == null)
        {
            return null;
        }

        return Table.TryGetValue(runState, out var runtime) ? runtime : null;
    }
}
