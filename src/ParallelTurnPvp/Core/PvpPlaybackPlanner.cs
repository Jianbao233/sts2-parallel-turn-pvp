namespace ParallelTurnPvp.Core;

public sealed class PvpPlaybackPlanner : IPvpPlaybackPlanner
{
    public PvpRoundPlaybackPlan BuildPlaybackPlan(PvpCombatSnapshot initialSnapshot, PvpRoundExecutionPlan executionPlan, PvpRoundDeltaPlan deltaPlan, PvpCombatSnapshot finalSnapshot)
    {
        PvpRoundPlaybackPlan plan = new()
        {
            RoundIndex = executionPlan.RoundIndex
        };
        PlaybackState state = new(initialSnapshot, finalSnapshot.RoundIndex, finalSnapshot.SnapshotVersion);

        int sequence = 0;
        foreach (IGrouping<PvpResolutionPhase, PvpDeltaOperation> phaseGroup in deltaPlan.Operations.GroupBy(operation => operation.Phase))
        {
            PvpPlaybackEvent phaseStart = new()
            {
                Sequence = sequence++,
                Phase = phaseGroup.Key,
                Kind = PvpPlaybackEventKind.PhaseStarted,
                Amount = phaseGroup.Count(),
                ModelEntry = "PHASE"
            };
            plan.Events.Add(phaseStart);
            plan.Frames.Add(state.Capture(phaseStart));

            foreach (PvpDeltaOperation operation in phaseGroup.OrderBy(operation => operation.Sequence).ThenBy(operation => operation.RuntimeActionId ?? uint.MaxValue))
            {
                PvpPlaybackEvent playbackEvent = new()
                {
                    Sequence = sequence++,
                    Phase = operation.Phase,
                    Kind = MapKind(operation.Kind),
                    DeltaKind = operation.Kind,
                    SourcePlayerId = operation.SourcePlayerId,
                    TargetPlayerId = operation.TargetPlayerId,
                    TargetKind = operation.TargetKind,
                    Amount = operation.Amount,
                    ModelEntry = operation.ModelEntry,
                    RuntimeActionId = operation.RuntimeActionId
                };
                plan.Events.Add(playbackEvent);
                state.Apply(playbackEvent);
                plan.Frames.Add(state.Capture(playbackEvent));
            }
        }

        foreach ((ulong playerId, PvpCreatureSnapshot hero) in finalSnapshot.Heroes.OrderBy(entry => entry.Key))
        {
            PvpPlaybackEvent playbackEvent = new()
            {
                Sequence = sequence++,
                Phase = PvpResolutionPhase.EndRound,
                Kind = PvpPlaybackEventKind.StateSync,
                SourcePlayerId = playerId,
                TargetPlayerId = playerId,
                TargetKind = PvpTargetKind.SelfHero,
                Amount = hero.CurrentHp,
                ModelEntry = $"STATE_SYNC_HERO_BLOCK_{hero.Block}"
            };
            plan.Events.Add(playbackEvent);
            state.Apply(playbackEvent);
            plan.Frames.Add(state.Capture(playbackEvent));
        }

        foreach ((ulong playerId, PvpCreatureSnapshot frontline) in finalSnapshot.Frontlines.OrderBy(entry => entry.Key))
        {
            if (!frontline.Exists)
            {
                continue;
            }

            PvpPlaybackEvent playbackEvent = new()
            {
                Sequence = sequence++,
                Phase = PvpResolutionPhase.EndRound,
                Kind = PvpPlaybackEventKind.StateSync,
                SourcePlayerId = playerId,
                TargetPlayerId = playerId,
                TargetKind = PvpTargetKind.SelfFrontline,
                Amount = frontline.CurrentHp,
                ModelEntry = $"STATE_SYNC_FRONTLINE_BLOCK_{frontline.Block}"
            };
            plan.Events.Add(playbackEvent);
            state.Apply(playbackEvent);
            plan.Frames.Add(state.Capture(playbackEvent));
        }

        return plan;
    }

    private static PvpPlaybackEventKind MapKind(PvpDeltaOperationKind kind)
    {
        return kind switch
        {
            PvpDeltaOperationKind.SummonFrontline => PvpPlaybackEventKind.SummonApplied,
            PvpDeltaOperationKind.GainMaxHp or PvpDeltaOperationKind.GainBlock => PvpPlaybackEventKind.BuffApplied,
            PvpDeltaOperationKind.Heal => PvpPlaybackEventKind.RecoverApplied,
            PvpDeltaOperationKind.GainResource => PvpPlaybackEventKind.ResourceApplied,
            PvpDeltaOperationKind.Damage => PvpPlaybackEventKind.DamageApplied,
            PvpDeltaOperationKind.EndRoundMarker => PvpPlaybackEventKind.EndRoundApplied,
            _ => PvpPlaybackEventKind.StateSync
        };
    }

    private sealed class PlaybackState
    {
        private readonly int _liveRoundIndex;
        private readonly int _snapshotVersion;
        private readonly Dictionary<ulong, MutableCreatureState> _heroes;
        private readonly Dictionary<ulong, MutableCreatureState> _frontlines;

        public PlaybackState(PvpCombatSnapshot initialSnapshot, int liveRoundIndex, int snapshotVersion)
        {
            _liveRoundIndex = liveRoundIndex;
            _snapshotVersion = snapshotVersion;
            _heroes = initialSnapshot.Heroes.ToDictionary(entry => entry.Key, entry => MutableCreatureState.FromSnapshot(entry.Value, false));
            _frontlines = initialSnapshot.Frontlines.ToDictionary(entry => entry.Key, entry => MutableCreatureState.FromSnapshot(entry.Value, true));
        }

        public void Apply(PvpPlaybackEvent playbackEvent)
        {
            if (playbackEvent.Kind == PvpPlaybackEventKind.PhaseStarted)
            {
                return;
            }

            if (playbackEvent.Kind == PvpPlaybackEventKind.StateSync)
            {
                ApplyStateSync(playbackEvent);
                return;
            }

            if (playbackEvent.DeltaKind == null)
            {
                return;
            }

            MutableCreatureState? target = ResolveTarget(
                playbackEvent.TargetPlayerId,
                playbackEvent.TargetKind,
                fallbackToHero: playbackEvent.DeltaKind is PvpDeltaOperationKind.Heal or PvpDeltaOperationKind.GainBlock);

            switch (playbackEvent.DeltaKind)
            {
                case PvpDeltaOperationKind.SummonFrontline:
                    MutableCreatureState frontline = GetOrCreateFrontline(playbackEvent.TargetPlayerId);
                    frontline.Exists = playbackEvent.Amount > 0;
                    frontline.MaxHp = Math.Max(playbackEvent.Amount, 0);
                    frontline.CurrentHp = Math.Max(playbackEvent.Amount, 0);
                    frontline.Block = 0;
                    break;
                case PvpDeltaOperationKind.GainMaxHp:
                    if (target is { Exists: true })
                    {
                        target.MaxHp += playbackEvent.Amount;
                    }
                    break;
                case PvpDeltaOperationKind.Heal:
                    if (target is { Exists: true })
                    {
                        target.CurrentHp = Math.Min(target.MaxHp, target.CurrentHp + playbackEvent.Amount);
                    }
                    break;
                case PvpDeltaOperationKind.GainBlock:
                    if (target is { Exists: true })
                    {
                        target.Block += playbackEvent.Amount;
                    }
                    break;
                case PvpDeltaOperationKind.Damage:
                    if (target is { Exists: true })
                    {
                        MutableCreatureState blockSource = target;
                        int blocked = Math.Min(blockSource.Block, playbackEvent.Amount);
                        blockSource.Block -= blocked;
                        int remaining = Math.Max(playbackEvent.Amount - blocked, 0);
                        if (remaining > 0)
                        {
                            target.CurrentHp = Math.Max(0, target.CurrentHp - remaining);
                            if (target.IsFrontline && target.CurrentHp <= 0)
                            {
                                target.Exists = false;
                                target.Block = 0;
                                target.MaxHp = 0;
                            }
                        }
                    }
                    break;
            }
        }

        public PvpPlaybackFrame Capture(PvpPlaybackEvent playbackEvent)
        {
            return new PvpPlaybackFrame
            {
                Sequence = playbackEvent.Sequence,
                Phase = playbackEvent.Phase,
                Kind = playbackEvent.Kind,
                Snapshot = new PvpCombatSnapshot
                {
                    RoundIndex = _liveRoundIndex,
                    SnapshotVersion = _snapshotVersion,
                    Heroes = _heroes.ToDictionary(entry => entry.Key, entry => entry.Value.ToSnapshot()),
                    Frontlines = _frontlines.ToDictionary(entry => entry.Key, entry => entry.Value.ToSnapshot())
                }
            };
        }

        private void ApplyStateSync(PvpPlaybackEvent playbackEvent)
        {
            MutableCreatureState target = playbackEvent.TargetKind switch
            {
                PvpTargetKind.SelfFrontline or PvpTargetKind.EnemyFrontline => GetOrCreateFrontline(playbackEvent.TargetPlayerId),
                _ => GetHero(playbackEvent.TargetPlayerId)
            };

            int block = 0;
            string suffix = playbackEvent.TargetKind is PvpTargetKind.SelfFrontline or PvpTargetKind.EnemyFrontline
                ? "STATE_SYNC_FRONTLINE_BLOCK_"
                : "STATE_SYNC_HERO_BLOCK_";
            if (playbackEvent.ModelEntry.StartsWith(suffix) && int.TryParse(playbackEvent.ModelEntry[suffix.Length..], out int parsedBlock))
            {
                block = parsedBlock;
            }

            target.Exists = true;
            target.CurrentHp = Math.Max(playbackEvent.Amount, 0);
            target.MaxHp = Math.Max(target.MaxHp, target.CurrentHp);
            target.Block = Math.Max(block, 0);
        }

        private MutableCreatureState GetHero(ulong playerId)
        {
            if (!_heroes.TryGetValue(playerId, out MutableCreatureState? hero))
            {
                hero = new MutableCreatureState { Exists = true };
                _heroes[playerId] = hero;
            }

            return hero;
        }

        private MutableCreatureState GetOrCreateFrontline(ulong playerId)
        {
            if (!_frontlines.TryGetValue(playerId, out MutableCreatureState? frontline))
            {
                frontline = new MutableCreatureState { IsFrontline = true };
                _frontlines[playerId] = frontline;
            }

            return frontline;
        }

        private MutableCreatureState? ResolveTarget(ulong playerId, PvpTargetKind kind, bool fallbackToHero)
        {
            return kind switch
            {
                PvpTargetKind.SelfHero or PvpTargetKind.EnemyHero => GetHero(playerId),
                PvpTargetKind.SelfFrontline or PvpTargetKind.EnemyFrontline => ResolveFrontlineOrFallback(playerId, fallbackToHero),
                _ => fallbackToHero ? GetHero(playerId) : null
            };
        }

        private MutableCreatureState? ResolveFrontlineOrFallback(ulong playerId, bool fallbackToHero)
        {
            if (_frontlines.TryGetValue(playerId, out MutableCreatureState? frontline) && frontline.Exists && frontline.CurrentHp > 0)
            {
                return frontline;
            }

            return fallbackToHero ? GetHero(playerId) : null;
        }
    }

    private sealed class MutableCreatureState
    {
        public bool Exists { get; set; }
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }
        public int Block { get; set; }
        public bool IsFrontline { get; init; }

        public static MutableCreatureState FromSnapshot(PvpCreatureSnapshot snapshot, bool isFrontline)
        {
            return new MutableCreatureState
            {
                Exists = snapshot.Exists,
                CurrentHp = snapshot.CurrentHp,
                MaxHp = snapshot.MaxHp,
                Block = snapshot.Block,
                IsFrontline = isFrontline
            };
        }

        public PvpCreatureSnapshot ToSnapshot()
        {
            return new PvpCreatureSnapshot
            {
                Exists = Exists,
                CurrentHp = CurrentHp,
                MaxHp = MaxHp,
                Block = Block
            };
        }
    }
}
