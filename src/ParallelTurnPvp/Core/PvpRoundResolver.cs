namespace ParallelTurnPvp.Core;

public sealed class PvpRoundResolver : IPvpRoundResolver
{
    private readonly IPvpExecutionPlanner _planner = new PvpExecutionPlanner();

    public PvpRoundResult Resolve(PvpCombatSnapshot initialSnapshot, IReadOnlyList<PvpRoundSubmission> submissions, PvpCombatSnapshot finalSnapshot)
    {
        var result = new PvpRoundResult
        {
            RoundIndex = initialSnapshot.RoundIndex,
            InitialSnapshot = initialSnapshot,
            FinalSnapshot = finalSnapshot
        };
        PvpRoundExecutionPlan plan = _planner.BuildPlan(initialSnapshot.RoundIndex, submissions);
        result.ExecutionPlan = plan;

        result.Events.Add(new PvpResolvedEvent
        {
            Kind = PvpResolvedEventKind.RoundResolved,
            Text = $"Resolved round {initialSnapshot.RoundIndex} with {submissions.Sum(submission => submission.Actions.Count)} planned actions."
        });
        result.Events.Add(new PvpResolvedEvent
        {
            Kind = PvpResolvedEventKind.ExecutionPlanBuilt,
            Text = $"Built execution plan for round {initialSnapshot.RoundIndex}: phases={plan.Steps.Select(step => step.Phase).Distinct().Count()}, steps={plan.Steps.Count}."
        });

        foreach (PvpRoundSubmission submission in submissions.OrderBy(submission => submission.PlayerId))
        {
            result.Events.Add(new PvpResolvedEvent
            {
                Kind = PvpResolvedEventKind.ActionLogged,
                Text = $"Player {submission.PlayerId} submitted {submission.Actions.Count} planned actions ({SummarizeActions(submission)}), energy={submission.RoundStartEnergy}, locked={submission.Locked}, first={submission.IsFirstFinisher}."
            });

            if (submission.Locked)
            {
                result.Events.Add(new PvpResolvedEvent
                {
                    Kind = PvpResolvedEventKind.PlayerLocked,
                    Text = $"Player {submission.PlayerId} locked round {submission.RoundIndex}{(submission.IsFirstFinisher ? " first" : string.Empty)}."
                });
            }
        }

        foreach (IGrouping<PvpResolutionPhase, PvpExecutionStep> phaseGroup in plan.Steps.GroupBy(step => step.Phase))
        {
            result.Events.Add(new PvpResolvedEvent
            {
                Kind = PvpResolvedEventKind.PhaseStarted,
                Text = $"Phase {phaseGroup.Key} scheduled {phaseGroup.Count()} step(s)."
            });

            foreach (PvpExecutionStep step in phaseGroup)
            {
                result.Events.Add(new PvpResolvedEvent
                {
                    Kind = PvpResolvedEventKind.ActionScheduled,
                    Text = $"Phase {step.Phase} player {step.PlayerId} #{step.Sequence + 1}: {step.ActionType} {step.ModelEntry} -> {step.Target.Kind} [actionId={step.RuntimeActionId?.ToString() ?? "-"}]"
                });
            }
        }

        AppendCreatureDeltaEvents(
            result,
            initialSnapshot.Heroes,
            finalSnapshot.Heroes,
            PvpResolvedEventKind.HeroStateChanged,
            "Hero");

        AppendCreatureDeltaEvents(
            result,
            initialSnapshot.Frontlines,
            finalSnapshot.Frontlines,
            PvpResolvedEventKind.FrontlineStateChanged,
            "Frontline");

        return result;
    }

    private static string SummarizeActions(PvpRoundSubmission submission)
    {
        int cards = submission.Actions.Count(action => action.ActionType == PvpActionType.PlayCard);
        int potions = submission.Actions.Count(action => action.ActionType == PvpActionType.UsePotion);
        int endTurn = submission.Actions.Count(action => action.ActionType == PvpActionType.EndRound);
        return $"cards={cards}, potions={potions}, endTurn={endTurn}";
    }

    private static void AppendCreatureDeltaEvents(
        PvpRoundResult result,
        IReadOnlyDictionary<ulong, PvpCreatureSnapshot> initialSnapshots,
        IReadOnlyDictionary<ulong, PvpCreatureSnapshot> finalSnapshots,
        PvpResolvedEventKind kind,
        string label)
    {
        foreach (ulong playerId in initialSnapshots.Keys.Union(finalSnapshots.Keys).OrderBy(id => id))
        {
            initialSnapshots.TryGetValue(playerId, out PvpCreatureSnapshot? before);
            finalSnapshots.TryGetValue(playerId, out PvpCreatureSnapshot? after);

            before ??= new PvpCreatureSnapshot();
            after ??= new PvpCreatureSnapshot();

            if (before.Exists != after.Exists)
            {
                result.Events.Add(new PvpResolvedEvent
                {
                    Kind = kind,
                    Text = $"{label} {playerId} exists {before.Exists} -> {after.Exists}"
                });
            }

            if (before.CurrentHp != after.CurrentHp || before.Block != after.Block || before.MaxHp != after.MaxHp)
            {
                result.Events.Add(new PvpResolvedEvent
                {
                    Kind = kind,
                    Text = $"{label} {playerId} hp {before.CurrentHp}/{before.MaxHp} -> {after.CurrentHp}/{after.MaxHp}, block {before.Block} -> {after.Block}"
                });
            }
        }
    }
}
