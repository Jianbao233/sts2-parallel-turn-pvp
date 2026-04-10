namespace ParallelTurnPvp.Core;

public sealed class PvpExecutionPlanner : IPvpExecutionPlanner
{
    public PvpRoundExecutionPlan BuildPlan(int roundIndex, IReadOnlyList<PvpRoundSubmission> submissions)
    {
        var plan = new PvpRoundExecutionPlan
        {
            RoundIndex = roundIndex
        };

        foreach (PvpRoundSubmission submission in submissions.OrderBy(submission => submission.PlayerId))
        {
            foreach (PvpPlannedAction action in submission.Actions.OrderBy(action => action.Sequence))
            {
                plan.Steps.Add(new PvpExecutionStep
                {
                    Phase = ClassifyPhase(action),
                    PlayerId = submission.PlayerId,
                    Sequence = action.Sequence,
                    ActionType = action.ActionType,
                    ModelEntry = action.ModelEntry,
                    Target = action.Target,
                    RuntimeActionId = action.RuntimeActionId
                });
            }
        }

        plan.Steps.Sort(CompareSteps);
        return plan;
    }

    private static int CompareSteps(PvpExecutionStep left, PvpExecutionStep right)
    {
        int phaseComparison = GetPhasePriority(left.Phase).CompareTo(GetPhasePriority(right.Phase));
        if (phaseComparison != 0)
        {
            return phaseComparison;
        }

        int sequenceComparison = left.Sequence.CompareTo(right.Sequence);
        if (sequenceComparison != 0)
        {
            return sequenceComparison;
        }

        return left.PlayerId.CompareTo(right.PlayerId);
    }

    private static int GetPhasePriority(PvpResolutionPhase phase)
    {
        return phase switch
        {
            PvpResolutionPhase.Summon => 0,
            PvpResolutionPhase.Buff => 1,
            PvpResolutionPhase.Debuff => 2,
            PvpResolutionPhase.Recover => 3,
            PvpResolutionPhase.Resource => 4,
            PvpResolutionPhase.Attack => 5,
            PvpResolutionPhase.EndRound => 6,
            _ => int.MaxValue
        };
    }

    private static PvpResolutionPhase ClassifyPhase(PvpPlannedAction action)
    {
        if (action.ActionType == PvpActionType.EndRound)
        {
            return PvpResolutionPhase.EndRound;
        }

        PvpAction classifierAction = new()
        {
            ActionType = action.ActionType,
            ModelEntry = action.ModelEntry,
            Target = action.Target
        };

        return PvpIntentClassifier.GetCategory(classifierAction) switch
        {
            PvpIntentCategory.Summon => PvpResolutionPhase.Summon,
            PvpIntentCategory.Guard or PvpIntentCategory.Buff => PvpResolutionPhase.Buff,
            PvpIntentCategory.Debuff => PvpResolutionPhase.Debuff,
            PvpIntentCategory.Recover => PvpResolutionPhase.Recover,
            PvpIntentCategory.Resource => PvpResolutionPhase.Resource,
            PvpIntentCategory.Attack => PvpResolutionPhase.Attack,
            _ => PvpResolutionPhase.Resource
        };
    }
}
