namespace ParallelTurnPvp.Core;

public sealed class PvpExecutionPlanner : IPvpExecutionPlanner
{
    public PvpRoundExecutionPlan BuildPlan(int roundIndex, IReadOnlyList<PvpRoundSubmission> submissions)
    {
        ulong firstFinisherPlayerId = submissions
            .Where(submission => submission.IsFirstFinisher)
            .Select(submission => submission.PlayerId)
            .FirstOrDefault();
        var plan = new PvpRoundExecutionPlan
        {
            RoundIndex = roundIndex,
            FirstFinisherPlayerId = firstFinisherPlayerId
        };

        Dictionary<ulong, int> playerOrder = BuildPlayerOrder(submissions, firstFinisherPlayerId);
        foreach (PvpRoundSubmission submission in submissions.OrderBy(submission => GetPlayerOrder(playerOrder, submission.PlayerId)))
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

        plan.Steps.Sort((left, right) => CompareSteps(left, right, playerOrder));
        return plan;
    }

    private static Dictionary<ulong, int> BuildPlayerOrder(IReadOnlyList<PvpRoundSubmission> submissions, ulong firstFinisherPlayerId)
    {
        List<ulong> orderedPlayers = submissions
            .Select(submission => submission.PlayerId)
            .Distinct()
            .OrderBy(playerId => playerId == firstFinisherPlayerId ? 0 : 1)
            .ThenBy(playerId => playerId)
            .ToList();

        return orderedPlayers
            .Select((playerId, index) => new { playerId, index })
            .ToDictionary(entry => entry.playerId, entry => entry.index);
    }

    private static int GetPlayerOrder(IReadOnlyDictionary<ulong, int> playerOrder, ulong playerId)
    {
        return playerOrder.TryGetValue(playerId, out int order) ? order : int.MaxValue;
    }

    private static int CompareSteps(PvpExecutionStep left, PvpExecutionStep right, IReadOnlyDictionary<ulong, int> playerOrder)
    {
        int endRoundComparison = IsEndRound(left).CompareTo(IsEndRound(right));
        if (endRoundComparison != 0)
        {
            return endRoundComparison;
        }

        int sequenceComparison = left.Sequence.CompareTo(right.Sequence);
        if (sequenceComparison != 0)
        {
            return sequenceComparison;
        }

        int playerComparison = GetPlayerOrder(playerOrder, left.PlayerId).CompareTo(GetPlayerOrder(playerOrder, right.PlayerId));
        if (playerComparison != 0)
        {
            return playerComparison;
        }

        return (left.RuntimeActionId ?? uint.MaxValue).CompareTo(right.RuntimeActionId ?? uint.MaxValue);
    }

    private static int IsEndRound(PvpExecutionStep step)
    {
        return step.ActionType == PvpActionType.EndRound ? 1 : 0;
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
