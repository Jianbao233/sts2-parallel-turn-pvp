namespace ParallelTurnPvp.Core;

public sealed class PvpPlanningCompiler : IPvpPlanningCompiler
{
    public PvpRoundSubmission BuildSubmission(int roundIndex, PvpActionLog log, PvpPlayerIntentState? intentState)
    {
        var submission = new PvpRoundSubmission
        {
            RoundIndex = roundIndex,
            PlayerId = log.PlayerId,
            Locked = log.Locked,
            RoundStartEnergy = intentState?.RoundStartEnergy ?? 0,
            IsFirstFinisher = intentState?.IsFirstFinisher ?? false
        };

        foreach (PvpAction action in log.Actions.OrderBy(action => action.Sequence))
        {
            submission.Actions.Add(new PvpPlannedAction
            {
                Sequence = action.Sequence,
                RuntimeActionId = action.RuntimeActionId,
                ActionType = action.ActionType,
                ModelEntry = action.ModelEntry,
                Target = action.Target
            });
        }

        return submission;
    }
}
