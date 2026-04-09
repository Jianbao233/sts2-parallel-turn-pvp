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

        foreach (var log in logs.OrderBy(log => log.PlayerId))
        {
            result.Events.Add(new PvpResolvedEvent
            {
                Kind = PvpResolvedEventKind.ActionLogged,
                Text = $"Player {log.PlayerId} submitted {log.Actions.Count} actions."
            });
        }

        return result;
    }
}
