using System.Text.Json;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace ParallelTurnPvp.Core;

internal static class PvpRoundSummarySink
{
    private static readonly object WriteLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void TryWriteHostRoundSummary(RunState runState, PvpMatchRuntime runtime, PvpRoundResult result, IReadOnlyList<PvpRoundSubmission> submissions)
    {
        if (RunManager.Instance.NetService.Type != NetGameType.Host)
        {
            return;
        }

        try
        {
            string logsDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SlayTheSpire2",
                "logs");
            Directory.CreateDirectory(logsDir);
            string filePath = Path.Combine(logsDir, "ptpvp_round_summary.ndjson");
            (uint delayedFingerprint, int delayedCount) = PvpDelayedPlanFingerprint.Compute(result.DelayedCommandPlan);

            var payload = new
            {
                utc = DateTime.UtcNow.ToString("O"),
                sessionId = runtime.RoomSession.SessionId,
                topology = runtime.RoomSession.Topology.ToString(),
                round = result.RoundIndex,
                snapshotStart = result.InitialSnapshot.SnapshotVersion,
                snapshotFinal = result.FinalSnapshot.SnapshotVersion,
                submissions = submissions
                    .OrderBy(item => item.PlayerId)
                    .Select(item => new
                    {
                        playerId = item.PlayerId,
                        locked = item.Locked,
                        first = item.IsFirstFinisher,
                        energy = item.RoundStartEnergy,
                        actionCount = item.Actions.Count,
                        actions = item.Actions.Select(action => new
                        {
                            seq = action.Sequence,
                            type = action.ActionType.ToString(),
                            model = action.ModelEntry,
                            targetKind = action.Target.Kind.ToString(),
                            targetOwner = action.Target.OwnerPlayerId
                        }).ToList()
                    }).ToList(),
                finalHeroes = result.FinalSnapshot.Heroes
                    .OrderBy(entry => entry.Key)
                    .ToDictionary(
                        entry => entry.Key.ToString(),
                        entry => new { entry.Value.Exists, entry.Value.CurrentHp, entry.Value.MaxHp, entry.Value.Block }),
                finalFrontlines = result.FinalSnapshot.Frontlines
                    .OrderBy(entry => entry.Key)
                    .ToDictionary(
                        entry => entry.Key.ToString(),
                        entry => new { entry.Value.Exists, entry.Value.CurrentHp, entry.Value.MaxHp, entry.Value.Block }),
                eventCount = result.Events.Count,
                delayedFingerprint,
                delayedCount,
                resolveInputSource = runtime.CurrentRound.ResolveInputSourceTag,
                resolverFallbackCount = runtime.CurrentRound.ResolverFallbackPlayers.Count,
                resolverFallbackPlayers = runtime.CurrentRound.ResolverFallbackPlayers.OrderBy(id => id).ToList(),
                resolverForcedLockedCount = runtime.CurrentRound.ResolverForcedLockedPlayers.Count,
                resolverForcedLockedPlayers = runtime.CurrentRound.ResolverForcedLockedPlayers.OrderBy(id => id).ToList(),
                resolverDegraded = runtime.CurrentRound.ResolverFallbackPlayers.Count > 0 || runtime.CurrentRound.ResolverForcedLockedPlayers.Count > 0
            };

            string line = JsonSerializer.Serialize(payload, JsonOptions);
            lock (WriteLock)
            {
                File.AppendAllText(filePath, line + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[ParallelTurnPvp] 回合摘要落盘失败: {ex.Message}");
        }
    }
}
