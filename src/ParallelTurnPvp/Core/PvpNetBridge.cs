using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace ParallelTurnPvp.Core;

public sealed class PvpNetBridge : IPvpSyncBridge
{
    private static object? _registeredService;

    public static void EnsureRegistered()
    {
        var runManager = RunManager.Instance;
        if (runManager == null)
        {
            return;
        }

        var netService = runManager.NetService;
        if (netService == null || ReferenceEquals(_registeredService, netService))
        {
            return;
        }

        netService.RegisterMessageHandler<PvpRoundStateMessage>(HandleRoundStateMessage);
        netService.RegisterMessageHandler<PvpRoundResultMessage>(HandleRoundResultMessage);
        _registeredService = netService;
        Log.Info($"[ParallelTurnPvp] Registered PvP message handlers. netType={netService.Type} inProgress={runManager.IsInProgress}");
    }

    public void BroadcastRoundState(PvpRoundState state)
    {
        if (!CanBroadcast())
        {
            return;
        }

        if (state.RoundIndex <= 0 || state.SnapshotAtRoundStart.SnapshotVersion <= 0)
        {
            return;
        }

        if (RunManager.Instance.DebugOnlyGetState() is RunState runState)
        {
            var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
            if (!runtime.TryMarkRoundStateBroadcast(state.SnapshotAtRoundStart.SnapshotVersion))
            {
                Log.Info($"[ParallelTurnPvp] Skipped duplicate round state broadcast. round={state.RoundIndex} snapshotVersion={state.SnapshotAtRoundStart.SnapshotVersion}");
                return;
            }
        }

        RunManager.Instance.NetService.SendMessage(new PvpRoundStateMessage
        {
            roundIndex = state.RoundIndex,
            snapshotVersion = state.SnapshotAtRoundStart.SnapshotVersion,
            phase = (int)state.Phase
        });
    }

    public void BroadcastRoundResult(PvpRoundResult result)
    {
        if (!CanBroadcast())
        {
            return;
        }

        if (result.RoundIndex <= 0 || result.FinalSnapshot.SnapshotVersion <= 0)
        {
            return;
        }

        if (RunManager.Instance.DebugOnlyGetState() is RunState runState)
        {
            var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
            if (!runtime.TryMarkRoundResultBroadcast(result.FinalSnapshot.SnapshotVersion))
            {
                Log.Info($"[ParallelTurnPvp] Skipped duplicate round result broadcast. round={result.RoundIndex} snapshotVersion={result.FinalSnapshot.SnapshotVersion}");
                return;
            }
        }

        var orderedHeroes = result.FinalSnapshot.Heroes.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToList();
        var orderedFrontlines = result.FinalSnapshot.Frontlines.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToList();
        RunManager.Instance.NetService.SendMessage(new PvpRoundResultMessage
        {
            roundIndex = result.RoundIndex,
            snapshotVersion = result.FinalSnapshot.SnapshotVersion,
            hero1Hp = orderedHeroes.ElementAtOrDefault(0)?.CurrentHp ?? 0,
            hero2Hp = orderedHeroes.ElementAtOrDefault(1)?.CurrentHp ?? 0,
            hero1Block = orderedHeroes.ElementAtOrDefault(0)?.Block ?? 0,
            hero2Block = orderedHeroes.ElementAtOrDefault(1)?.Block ?? 0,
            frontline1Exists = orderedFrontlines.ElementAtOrDefault(0)?.Exists ?? false,
            frontline2Exists = orderedFrontlines.ElementAtOrDefault(1)?.Exists ?? false,
            frontline1Hp = orderedFrontlines.ElementAtOrDefault(0)?.CurrentHp ?? 0,
            frontline2Hp = orderedFrontlines.ElementAtOrDefault(1)?.CurrentHp ?? 0,
            frontline1Block = orderedFrontlines.ElementAtOrDefault(0)?.Block ?? 0,
            frontline2Block = orderedFrontlines.ElementAtOrDefault(1)?.Block ?? 0,
            eventKinds = result.Events.Select(resolvedEvent => (int)resolvedEvent.Kind).ToList(),
            eventTexts = result.Events.Select(resolvedEvent => resolvedEvent.Text).ToList()
        });
    }

    private static bool CanBroadcast()
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return false;
        }

        return RunManager.Instance.NetService.Type is NetGameType.Host or NetGameType.Singleplayer;
    }

    private static void HandleRoundStateMessage(PvpRoundStateMessage message, ulong _)
    {
        if (message.roundIndex <= 0 || message.snapshotVersion <= 0 || RunManager.Instance.DebugOnlyGetState() is not RunState runState)
        {
            return;
        }

        var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        if (!runtime.TryMarkRoundStateReceived(message.snapshotVersion))
        {
            Log.Info($"[ParallelTurnPvp] Ignored duplicate/stale authoritative round state. round={message.roundIndex} snapshotVersion={message.snapshotVersion}");
            return;
        }

        runtime.CurrentRound.RoundIndex = message.roundIndex;
        runtime.CurrentRound.Phase = (PvpMatchPhase)message.phase;
        Log.Info($"[ParallelTurnPvp] Received authoritative round state. round={message.roundIndex} snapshotVersion={message.snapshotVersion} phase={(PvpMatchPhase)message.phase}");
    }

    private static void HandleRoundResultMessage(PvpRoundResultMessage message, ulong _)
    {
        if (message.roundIndex <= 0 || message.snapshotVersion <= 0 || RunManager.Instance.DebugOnlyGetState() is not RunState runState)
        {
            return;
        }

        var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        if (!runtime.TryMarkRoundResultReceived(message.snapshotVersion))
        {
            Log.Info($"[ParallelTurnPvp] Ignored duplicate/stale authoritative round result. round={message.roundIndex} snapshotVersion={message.snapshotVersion}");
            return;
        }

        var initialSnapshot = runtime.CurrentRound.SnapshotAtRoundStart;
        var finalSnapshot = CreateSnapshotFromMessage(initialSnapshot, message);
        var authoritativeResult = new PvpRoundResult
        {
            RoundIndex = message.roundIndex,
            InitialSnapshot = initialSnapshot,
            FinalSnapshot = finalSnapshot
        };
        foreach (PvpResolvedEvent resolvedEvent in CreateResolvedEvents(message))
        {
            authoritativeResult.Events.Add(resolvedEvent);
        }

        runtime.ApplyAuthoritativeResult(authoritativeResult);

        Log.Info($"[ParallelTurnPvp] Received authoritative round result. round={message.roundIndex} snapshotVersion={message.snapshotVersion} hero1Hp={message.hero1Hp} hero2Hp={message.hero2Hp} events={message.eventTexts?.Count ?? 0} liveApply=disabled");
    }

    private static PvpCombatSnapshot CreateSnapshotFromMessage(PvpCombatSnapshot initialSnapshot, PvpRoundResultMessage message)
    {
        var snapshot = new PvpCombatSnapshot
        {
            RoundIndex = message.roundIndex,
            SnapshotVersion = message.snapshotVersion
        };

        var orderedHeroes = initialSnapshot.Heroes.OrderBy(entry => entry.Key).ToList();
        for (int i = 0; i < orderedHeroes.Count; i++)
        {
            ulong playerId = orderedHeroes[i].Key;
            PvpCreatureSnapshot initialHero = orderedHeroes[i].Value;
            int hp = i == 0 ? message.hero1Hp : message.hero2Hp;
            int block = i == 0 ? message.hero1Block : message.hero2Block;
            snapshot.Heroes[playerId] = new PvpCreatureSnapshot
            {
                Exists = initialHero.Exists,
                CurrentHp = hp,
                MaxHp = initialHero.MaxHp,
                Block = block
            };
        }

        foreach (var frontline in initialSnapshot.Frontlines)
        {
            int orderedIndex = orderedHeroes.FindIndex(entry => entry.Key == frontline.Key);
            bool exists = orderedIndex == 0 ? message.frontline1Exists : orderedIndex == 1 ? message.frontline2Exists : frontline.Value.Exists;
            int hp = orderedIndex == 0 ? message.frontline1Hp : orderedIndex == 1 ? message.frontline2Hp : frontline.Value.CurrentHp;
            int block = orderedIndex == 0 ? message.frontline1Block : orderedIndex == 1 ? message.frontline2Block : frontline.Value.Block;
            snapshot.Frontlines[frontline.Key] = new PvpCreatureSnapshot
            {
                Exists = exists,
                CurrentHp = hp,
                MaxHp = frontline.Value.MaxHp,
                Block = block
            };
        }

        return snapshot;
    }

    private static IEnumerable<PvpResolvedEvent> CreateResolvedEvents(PvpRoundResultMessage message)
    {
        int eventCount = Math.Min(message.eventKinds.Count, message.eventTexts.Count);
        for (int i = 0; i < eventCount; i++)
        {
            yield return new PvpResolvedEvent
            {
                Kind = (PvpResolvedEventKind)message.eventKinds[i],
                Text = message.eventTexts[i] ?? string.Empty
            };
        }
    }

    public static void ApplyLiveSnapshot(RunState runState, PvpCombatSnapshot snapshot)
    {
        foreach (Player player in runState.Players)
        {
            if (snapshot.Heroes.TryGetValue(player.NetId, out PvpCreatureSnapshot? heroSnapshot))
            {
                ApplyCreatureSnapshot(player.Creature, heroSnapshot);
            }
        }

        if (snapshot.Frontlines.Count > 0)
        {
            Log.Info($"[ParallelTurnPvp] Skipped live frontline snapshot apply to avoid checksum drift. trackedFrontlines={snapshot.Frontlines.Count}");
        }
    }

    private static void ApplyCreatureSnapshot(MegaCrit.Sts2.Core.Entities.Creatures.Creature creature, PvpCreatureSnapshot snapshot)
    {
        creature.SetCurrentHpInternal(snapshot.CurrentHp);
        if (creature.Block > snapshot.Block)
        {
            creature.LoseBlockInternal(creature.Block - snapshot.Block);
        }
        else if (creature.Block < snapshot.Block)
        {
            creature.GainBlockInternal(snapshot.Block - creature.Block);
        }
    }
}
