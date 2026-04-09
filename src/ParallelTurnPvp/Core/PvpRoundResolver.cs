namespace ParallelTurnPvp.Core;

public sealed class PvpRoundResolver : IPvpRoundResolver
{
    public PvpRoundResult Resolve(PvpCombatSnapshot initialSnapshot, IReadOnlyList<PvpActionLog> logs, PvpCombatSnapshot finalSnapshot)
    {
        var result = new PvpRoundResult
        {
            RoundIndex = initialSnapshot.RoundIndex,
            InitialSnapshot = initialSnapshot,
            FinalSnapshot = finalSnapshot
        };

        result.Events.Add(new PvpResolvedEvent
        {
            Kind = PvpResolvedEventKind.RoundResolved,
            Text = $"Resolved round {initialSnapshot.RoundIndex} with {logs.Sum(log => log.Actions.Count)} logged actions."
        });

        foreach (PvpActionLog log in logs.OrderBy(log => log.PlayerId))
        {
            result.Events.Add(new PvpResolvedEvent
            {
                Kind = PvpResolvedEventKind.ActionLogged,
                Text = $"Player {log.PlayerId} submitted {log.Actions.Count} actions ({SummarizeActions(log)})."
            });

            foreach (PvpAction action in log.Actions.OrderBy(action => action.Sequence))
            {
                result.Events.Add(new PvpResolvedEvent
                {
                    Kind = PvpResolvedEventKind.ActionLogged,
                    Text = $"Player {log.PlayerId} #{action.Sequence + 1}: {action.ActionType} {action.ModelEntry} -> {action.Target.Kind}"
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

    private static string SummarizeActions(PvpActionLog log)
    {
        int cards = log.Actions.Count(action => action.ActionType == PvpActionType.PlayCard);
        int potions = log.Actions.Count(action => action.ActionType == PvpActionType.UsePotion);
        int endTurn = log.Actions.Count(action => action.ActionType == PvpActionType.EndRound);
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
