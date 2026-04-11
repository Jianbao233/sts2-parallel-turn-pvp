using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using ParallelTurnPvp.Models;

namespace ParallelTurnPvp.Core;

public static class PvpDelayedExecution
{
    private static readonly HashSet<string> DelayedLiveApplyModelEntries = new(StringComparer.Ordinal)
    {
        "FRONTLINE_BRACE",
        "FRONTLINE_SALVE"
    };

    public static bool ShouldDelayLiveApply(Player player, string modelEntry)
    {
        return player.RunState is RunState runState &&
               runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any() &&
               DelayedLiveApplyModelEntries.Contains(modelEntry);
    }

    public static bool ShouldDelayLiveApply(string modelEntry)
    {
        return DelayedLiveApplyModelEntries.Contains(modelEntry);
    }

    public static int ApplyDelayedLiveEffects(PvpMatchRuntime runtime, CombatState combatState)
    {
        if (runtime.CurrentRound.DelayedLiveEffectsApplied || runtime.CurrentRound.RoundIndex <= 0)
        {
            return 0;
        }

        PvpRoundExecutionPlan plan = new PvpExecutionPlanner().BuildPlan(runtime.CurrentRound.RoundIndex, runtime.GetPlanningSubmissions());
        PvpRoundDeltaPlan deltaPlan = new PvpDeltaPlanner().BuildDeltaPlan(runtime.CurrentRound.SnapshotAtRoundStart, plan);
        int appliedCount = 0;

        foreach (PvpDeltaOperation operation in deltaPlan.Operations.Where(operation => ShouldDelayLiveApply(operation.ModelEntry)))
        {
            Creature? target = ResolveTarget(combatState, operation);
            if (target == null || target.IsDead)
            {
                Log.Info($"[ParallelTurnPvp] Skipped delayed live apply for {operation.ModelEntry}. targetMissing=True player={operation.TargetPlayerId} kind={operation.TargetKind}");
                continue;
            }

            switch (operation.Kind)
            {
                case PvpDeltaOperationKind.GainBlock:
                    target.GainBlockInternal(operation.Amount);
                    appliedCount++;
                    Log.Info($"[ParallelTurnPvp] Applied delayed live block. model={operation.ModelEntry} player={operation.TargetPlayerId} kind={operation.TargetKind} amount={operation.Amount} currentBlock={target.Block}");
                    break;
                case PvpDeltaOperationKind.Heal:
                    target.HealInternal(operation.Amount);
                    appliedCount++;
                    Log.Info($"[ParallelTurnPvp] Applied delayed live heal. model={operation.ModelEntry} player={operation.TargetPlayerId} kind={operation.TargetKind} amount={operation.Amount} currentHp={target.CurrentHp}/{target.MaxHp}");
                    break;
            }
        }

        runtime.CurrentRound.DelayedLiveEffectsApplied = true;
        return appliedCount;
    }

    private static Creature? ResolveTarget(CombatState combatState, PvpDeltaOperation operation)
    {
        Player? player = combatState.Players.FirstOrDefault(candidate => candidate.NetId == operation.TargetPlayerId);
        if (player == null)
        {
            return null;
        }

        return operation.TargetKind switch
        {
            PvpTargetKind.SelfHero or PvpTargetKind.EnemyHero => player.Creature,
            PvpTargetKind.SelfFrontline or PvpTargetKind.EnemyFrontline => ParallelTurnFrontlineHelper.GetFrontline(player) ?? player.Creature,
            _ => player.Creature
        };
    }
}
