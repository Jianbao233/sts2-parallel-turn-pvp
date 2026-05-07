namespace ParallelTurnPvp.Core;

public sealed class PvpRoundResolver : IPvpRoundResolver
{
    private readonly IPvpExecutionPlanner _planner = new PvpExecutionPlanner();
    private readonly IPvpDeltaPlanner _deltaPlanner = new PvpDeltaPlanner();
    private readonly IPvpDelayedPlanner _delayedPlanner = new PvpDelayedPlanner();
    private readonly IPvpDelayedCommandPlanner _delayedCommandPlanner = new PvpDelayedCommandPlanner();
    private readonly IPvpPredictionEngine _predictionEngine = new PvpPredictionEngine();
    private readonly IPvpPlaybackPlanner _playbackPlanner = new PvpPlaybackPlanner();

    public PvpRoundResult Resolve(PvpCombatSnapshot initialSnapshot, IReadOnlyList<PvpRoundSubmission> submissions, PvpCombatSnapshot finalSnapshot)
    {
        var result = new PvpRoundResult
        {
            RoundIndex = initialSnapshot.RoundIndex,
            InitialSnapshot = initialSnapshot,
            FinalSnapshot = finalSnapshot
        };
        PvpRoundExecutionPlan plan = _planner.BuildPlan(initialSnapshot.RoundIndex, submissions);
        PvpRoundDeltaPlan deltaPlan = _deltaPlanner.BuildDeltaPlan(initialSnapshot, plan);
        PvpRoundDelayedPlan delayedPlan = _delayedPlanner.BuildDelayedPlan(initialSnapshot, deltaPlan);
        PvpRoundDelayedCommandPlan delayedCommandPlan = _delayedCommandPlanner.BuildCommandPlan(initialSnapshot, delayedPlan);
        PvpCombatSnapshot predictedSnapshot = _predictionEngine.Predict(initialSnapshot, deltaPlan);
        PvpRoundPlaybackPlan playbackPlan = _playbackPlanner.BuildPlaybackPlan(initialSnapshot, plan, deltaPlan, finalSnapshot);
        result.ExecutionPlan = plan;
        result.DeltaPlan = deltaPlan;
        result.DelayedPlan = delayedPlan;
        result.DelayedCommandPlan = delayedCommandPlan;
        result.PredictedFinalSnapshot = predictedSnapshot;
        result.PlaybackPlan = playbackPlan;

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
        result.Events.Add(new PvpResolvedEvent
        {
            Kind = PvpResolvedEventKind.DeltaPlanBuilt,
            Text = $"Built delta plan for round {initialSnapshot.RoundIndex}: operations={deltaPlan.Operations.Count}."
        });
        result.Events.Add(new PvpResolvedEvent
        {
            Kind = PvpResolvedEventKind.DelayedPlanBuilt,
            Text = $"Built delayed plan for round {initialSnapshot.RoundIndex}: candidates={delayedPlan.Operations.Count}."
        });
        result.Events.Add(new PvpResolvedEvent
        {
            Kind = PvpResolvedEventKind.DelayedCommandPlanBuilt,
            Text = $"Built delayed command plan for round {initialSnapshot.RoundIndex}: commands={delayedCommandPlan.Commands.Count}."
        });
        result.Events.Add(new PvpResolvedEvent
        {
            Kind = PvpResolvedEventKind.PredictionBuilt,
            Text = $"Predicted round {initialSnapshot.RoundIndex} snapshot from delta plan."
        });
        result.Events.Add(new PvpResolvedEvent
        {
            Kind = PvpResolvedEventKind.PlaybackPlanBuilt,
            Text = $"Built playback plan for round {initialSnapshot.RoundIndex}: events={playbackPlan.Events.Count}, frames={playbackPlan.Frames.Count}."
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

        foreach (IGrouping<PvpResolutionPhase, PvpDeltaOperation> phaseGroup in deltaPlan.Operations.GroupBy(operation => operation.Phase))
        {
            foreach (PvpDeltaOperation operation in phaseGroup)
            {
                result.Events.Add(new PvpResolvedEvent
                {
                    Kind = PvpResolvedEventKind.DeltaOperationScheduled,
                    Text = $"Delta {operation.Phase} player {operation.SourcePlayerId} #{operation.Sequence + 1}: {operation.Kind} {operation.Amount} -> {operation.TargetKind} via {operation.ModelEntry} [actionId={operation.RuntimeActionId?.ToString() ?? "-"}]"
                });
            }
        }

        foreach (PvpDelayedCandidateOperation operation in delayedPlan.Operations)
        {
            result.Events.Add(new PvpResolvedEvent
            {
                Kind = PvpResolvedEventKind.DelayedCandidateScheduled,
                Text = $"Delayed {operation.Phase} player {operation.SourcePlayerId} #{operation.Sequence + 1}: {operation.Kind} {operation.Amount} -> {operation.TargetKind} via {operation.ModelEntry} [actionId={operation.RuntimeActionId?.ToString() ?? "-"}]"
            });
        }

        foreach (PvpDelayedCommand command in delayedCommandPlan.Commands)
        {
            result.Events.Add(new PvpResolvedEvent
            {
                Kind = PvpResolvedEventKind.DelayedCommandScheduled,
                Text = $"DelayedCommand {command.Phase} player {command.SourcePlayerId} #{command.Sequence + 1}: {command.Kind} {command.Amount} -> {command.TargetKind} via {command.ModelEntry} [actionId={command.RuntimeActionId?.ToString() ?? "-"}] executor={command.ExecutorHint}"
            });
        }

        foreach (PvpPlaybackEvent playbackEvent in playbackPlan.Events)
        {
            result.Events.Add(new PvpResolvedEvent
            {
                Kind = PvpResolvedEventKind.PlaybackEventScheduled,
                Text = $"Playback {playbackEvent.Phase} #{playbackEvent.Sequence + 1}: {playbackEvent.Kind} {playbackEvent.Amount} -> {playbackEvent.TargetKind} via {playbackEvent.ModelEntry} [actionId={playbackEvent.RuntimeActionId?.ToString() ?? "-"}]"
            });
        }

        AppendCreatureDeltaEvents(
            result,
            initialSnapshot.Heroes,
            predictedSnapshot.Heroes,
            PvpResolvedEventKind.PredictionBuilt,
            "Predicted Hero");

        AppendCreatureDeltaEvents(
            result,
            initialSnapshot.Frontlines,
            predictedSnapshot.Frontlines,
            PvpResolvedEventKind.PredictionBuilt,
            "Predicted Frontline");

        AppendPredictionComparisonEvents(
            result,
            predictedSnapshot.Heroes,
            finalSnapshot.Heroes,
            "Hero",
            deltaPlan.Operations);

        AppendPredictionComparisonEvents(
            result,
            predictedSnapshot.Frontlines,
            finalSnapshot.Frontlines,
            "Frontline",
            deltaPlan.Operations);

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

    private static void AppendPredictionComparisonEvents(
        PvpRoundResult result,
        IReadOnlyDictionary<ulong, PvpCreatureSnapshot> predictedSnapshots,
        IReadOnlyDictionary<ulong, PvpCreatureSnapshot> finalSnapshots,
        string label,
        IReadOnlyList<PvpDeltaOperation> operations)
    {
        foreach (ulong playerId in predictedSnapshots.Keys.Union(finalSnapshots.Keys).OrderBy(id => id))
        {
            predictedSnapshots.TryGetValue(playerId, out PvpCreatureSnapshot? predicted);
            finalSnapshots.TryGetValue(playerId, out PvpCreatureSnapshot? actual);

            predicted ??= new PvpCreatureSnapshot();
            actual ??= new PvpCreatureSnapshot();

            if (predicted.Exists == actual.Exists &&
                predicted.CurrentHp == actual.CurrentHp &&
                predicted.MaxHp == actual.MaxHp &&
                predicted.Block == actual.Block)
            {
                result.Events.Add(new PvpResolvedEvent
                {
                    Kind = PvpResolvedEventKind.PredictionCompared,
                    Text = $"{label} {playerId} prediction matched actual."
                });
                continue;
            }

            result.Events.Add(new PvpResolvedEvent
            {
                Kind = PvpResolvedEventKind.PredictionCompared,
                Text = $"{label} {playerId} prediction drift: predicted exists={predicted.Exists} hp {predicted.CurrentHp}/{predicted.MaxHp} block {predicted.Block}, actual exists={actual.Exists} hp {actual.CurrentHp}/{actual.MaxHp} block {actual.Block}"
            });

            string trace = BuildPredictionTrace(label, playerId, operations);
            if (!string.IsNullOrEmpty(trace))
            {
                result.Events.Add(new PvpResolvedEvent
                {
                    Kind = PvpResolvedEventKind.PredictionCompared,
                    Text = trace
                });
            }
        }
    }

    private static string BuildPredictionTrace(string label, ulong playerId, IReadOnlyList<PvpDeltaOperation> operations)
    {
        PvpTargetKind primaryTarget = label == "Frontline" ? PvpTargetKind.SelfFrontline : PvpTargetKind.SelfHero;
        PvpTargetKind relatedSecondaryTarget = label == "Frontline" ? PvpTargetKind.SelfHero : PvpTargetKind.SelfFrontline;

        List<string> relevant = operations
            .Where(operation => operation.TargetPlayerId == playerId &&
                (NormalizeFriendlyTarget(operation.TargetKind) == primaryTarget ||
                 NormalizeFriendlyTarget(operation.TargetKind) == relatedSecondaryTarget))
            .Select(FormatTraceOperation)
            .TakeLast(8)
            .ToList();

        if (relevant.Count == 0)
        {
            return string.Empty;
        }

        return $"{label} {playerId} prediction trace: {string.Join(" | ", relevant)}";
    }

    private static string FormatTraceOperation(PvpDeltaOperation operation)
    {
        string actionId = operation.RuntimeActionId?.ToString() ?? "-";
        return $"id={actionId} {operation.Kind} {operation.Amount} -> {operation.TargetKind} via {operation.ModelEntry}";
    }

    private static PvpTargetKind NormalizeFriendlyTarget(PvpTargetKind kind)
    {
        return kind switch
        {
            PvpTargetKind.EnemyHero => PvpTargetKind.SelfHero,
            PvpTargetKind.EnemyFrontline => PvpTargetKind.SelfFrontline,
            _ => kind
        };
    }
}
