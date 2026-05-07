namespace ParallelTurnPvp.Core;

public sealed class PvpDelayedCommandPlanner : IPvpDelayedCommandPlanner
{
    public PvpRoundDelayedCommandPlan BuildCommandPlan(PvpCombatSnapshot initialSnapshot, PvpRoundDelayedPlan delayedPlan)
    {
        PvpRoundDelayedCommandPlan plan = new() { RoundIndex = initialSnapshot.RoundIndex };

        foreach (PvpDelayedCandidateOperation operation in delayedPlan.Operations)
        {
            if (!TryMapCommand(operation, out PvpDelayedCommandKind kind, out string executorHint))
            {
                continue;
            }

            plan.Commands.Add(new PvpDelayedCommand
            {
                Phase = operation.Phase,
                Kind = kind,
                SourcePlayerId = operation.SourcePlayerId,
                TargetPlayerId = operation.TargetPlayerId,
                TargetKind = operation.TargetKind,
                Amount = operation.Amount,
                Sequence = operation.Sequence,
                ModelEntry = operation.ModelEntry,
                RuntimeActionId = operation.RuntimeActionId,
                ExecutorHint = executorHint
            });
        }

        return plan;
    }

    private static bool TryMapCommand(PvpDelayedCandidateOperation operation, out PvpDelayedCommandKind kind, out string executorHint)
    {
        kind = operation.Kind switch
        {
            PvpDelayedCandidateKind.SafeSelfBlock => PvpDelayedCommandKind.GainBlock,
            PvpDelayedCandidateKind.SafeSelfHeal => PvpDelayedCommandKind.Heal,
            PvpDelayedCandidateKind.SafeSelfResource => PvpDelayedCommandKind.GainResource,
            PvpDelayedCandidateKind.SafeSelfMaxHp => PvpDelayedCommandKind.GainMaxHp,
            PvpDelayedCandidateKind.SafeSelfSummon => PvpDelayedCommandKind.SummonFrontline,
            PvpDelayedCandidateKind.CrossDamage => PvpDelayedCommandKind.Damage,
            PvpDelayedCandidateKind.EndRoundMarker => PvpDelayedCommandKind.EndRoundMarker,
            _ => default
        };

        executorHint = kind switch
        {
            PvpDelayedCommandKind.GainBlock => operation.TargetKind == PvpTargetKind.SelfFrontline ? "CreatureCmd.GainBlock(frontline)" : "CreatureCmd.GainBlock(hero)",
            PvpDelayedCommandKind.Heal => operation.TargetKind == PvpTargetKind.SelfFrontline ? "CreatureCmd.Heal(frontline)" : "CreatureCmd.Heal(hero)",
            PvpDelayedCommandKind.GainResource => "ConsoleCmdGameAction(\"energy\") or direct PlayerCombatState energy grant",
            PvpDelayedCommandKind.GainMaxHp => operation.TargetKind == PvpTargetKind.SelfFrontline ? "CreatureCmd.GainMaxHp(frontline)" : "CreatureCmd.GainMaxHp(hero)",
            PvpDelayedCommandKind.SummonFrontline => "OstyCmd.Summon(amount)",
            PvpDelayedCommandKind.Damage => operation.TargetKind == PvpTargetKind.EnemyFrontline ? "CreatureCmd.Damage(enemy_frontline)" : "CreatureCmd.Damage(enemy_hero)",
            PvpDelayedCommandKind.EndRoundMarker => "Round boundary marker",
            _ => string.Empty
        };

        return !string.IsNullOrEmpty(executorHint);
    }
}
