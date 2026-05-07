namespace ParallelTurnPvp.Core;

public sealed class PvpDelayedPlanner : IPvpDelayedPlanner
{
    public PvpRoundDelayedPlan BuildDelayedPlan(PvpCombatSnapshot initialSnapshot, PvpRoundDeltaPlan deltaPlan)
    {
        PvpRoundDelayedPlan plan = new() { RoundIndex = initialSnapshot.RoundIndex };

        foreach (PvpDeltaOperation operation in deltaPlan.Operations)
        {
            if (!TryMapCandidate(operation, out PvpDelayedCandidateKind candidateKind))
            {
                continue;
            }

            plan.Operations.Add(new PvpDelayedCandidateOperation
            {
                Phase = operation.Phase,
                Kind = candidateKind,
                SourcePlayerId = operation.SourcePlayerId,
                TargetPlayerId = operation.TargetPlayerId,
                TargetKind = operation.TargetKind,
                Amount = operation.Amount,
                Sequence = operation.Sequence,
                ModelEntry = operation.ModelEntry,
                RuntimeActionId = operation.RuntimeActionId
            });
        }

        return plan;
    }

    private static bool TryMapCandidate(PvpDeltaOperation operation, out PvpDelayedCandidateKind candidateKind)
    {
        candidateKind = default;
        bool selfTarget = operation.SourcePlayerId == operation.TargetPlayerId &&
            operation.TargetKind is PvpTargetKind.SelfHero or PvpTargetKind.SelfFrontline;
        bool crossDamageTarget = operation.Kind == PvpDeltaOperationKind.Damage &&
            operation.TargetKind is PvpTargetKind.EnemyHero or PvpTargetKind.EnemyFrontline;

        if (!selfTarget && !crossDamageTarget && operation.Kind != PvpDeltaOperationKind.EndRoundMarker)
        {
            return false;
        }

        candidateKind = operation.Kind switch
        {
            PvpDeltaOperationKind.GainBlock => PvpDelayedCandidateKind.SafeSelfBlock,
            PvpDeltaOperationKind.Heal => PvpDelayedCandidateKind.SafeSelfHeal,
            PvpDeltaOperationKind.GainResource => PvpDelayedCandidateKind.SafeSelfResource,
            PvpDeltaOperationKind.GainMaxHp => PvpDelayedCandidateKind.SafeSelfMaxHp,
            PvpDeltaOperationKind.SummonFrontline => PvpDelayedCandidateKind.SafeSelfSummon,
            PvpDeltaOperationKind.Damage => PvpDelayedCandidateKind.CrossDamage,
            PvpDeltaOperationKind.EndRoundMarker => PvpDelayedCandidateKind.EndRoundMarker,
            _ => default
        };

        return operation.Kind is
            PvpDeltaOperationKind.GainBlock or
            PvpDeltaOperationKind.Heal or
            PvpDeltaOperationKind.GainResource or
            PvpDeltaOperationKind.GainMaxHp or
            PvpDeltaOperationKind.Damage or
            PvpDeltaOperationKind.SummonFrontline or
            PvpDeltaOperationKind.EndRoundMarker;
    }
}
