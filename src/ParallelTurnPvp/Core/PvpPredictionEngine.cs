namespace ParallelTurnPvp.Core;

public sealed class PvpPredictionEngine : IPvpPredictionEngine
{
    public PvpCombatSnapshot Predict(PvpCombatSnapshot initialSnapshot, PvpRoundDeltaPlan deltaPlan)
    {
        PredictionState state = new(initialSnapshot);
        foreach (PvpDeltaOperation operation in deltaPlan.Operations)
        {
            state.Apply(operation);
        }

        return state.ToSnapshot(initialSnapshot.RoundIndex, initialSnapshot.SnapshotVersion);
    }

    private sealed class PredictionState
    {
        private readonly Dictionary<ulong, PredictionCreatureState> _heroes;
        private readonly Dictionary<ulong, PredictionCreatureState> _frontlines;

        public PredictionState(PvpCombatSnapshot snapshot)
        {
            _heroes = snapshot.Heroes.ToDictionary(
                entry => entry.Key,
                entry => PredictionCreatureState.FromSnapshot(entry.Value, isFrontline: false));
            _frontlines = snapshot.Frontlines.ToDictionary(
                entry => entry.Key,
                entry => PredictionCreatureState.FromSnapshot(entry.Value, isFrontline: true));
        }

        public void Apply(PvpDeltaOperation operation)
        {
            PredictionCreatureState? target = ResolveTarget(
                operation.TargetPlayerId,
                operation.TargetKind,
                fallbackToHero: operation.Kind is PvpDeltaOperationKind.Heal or PvpDeltaOperationKind.GainBlock);

            switch (operation.Kind)
            {
                case PvpDeltaOperationKind.SummonFrontline:
                    PredictionCreatureState frontline = GetOrCreateFrontline(operation.TargetPlayerId);
                    frontline.Exists = operation.Amount > 0;
                    frontline.MaxHp = Math.Max(operation.Amount, 0);
                    frontline.CurrentHp = Math.Max(operation.Amount, 0);
                    frontline.Block = 0;
                    break;
                case PvpDeltaOperationKind.GainMaxHp:
                    if (target is { Exists: true })
                    {
                        target.MaxHp += operation.Amount;
                    }
                    break;
                case PvpDeltaOperationKind.Heal:
                    if (target is { Exists: true })
                    {
                        target.CurrentHp = Math.Min(target.MaxHp, target.CurrentHp + operation.Amount);
                    }
                    break;
                case PvpDeltaOperationKind.GainBlock:
                    if (target is { Exists: true })
                    {
                        target.Block += operation.Amount;
                    }
                    break;
                case PvpDeltaOperationKind.Damage:
                    if (target is { Exists: true })
                    {
                        int blocked = Math.Min(target.Block, operation.Amount);
                        target.Block -= blocked;
                        int remaining = Math.Max(operation.Amount - blocked, 0);
                        if (remaining > 0)
                        {
                            target.CurrentHp = Math.Max(0, target.CurrentHp - remaining);
                            if (target.IsFrontline && target.CurrentHp <= 0)
                            {
                                target.Exists = false;
                                target.Block = 0;
                            }
                        }
                    }
                    break;
            }
        }

        public PvpCombatSnapshot ToSnapshot(int roundIndex, int snapshotVersion)
        {
            return new PvpCombatSnapshot
            {
                RoundIndex = roundIndex,
                SnapshotVersion = snapshotVersion,
                Heroes = _heroes.ToDictionary(entry => entry.Key, entry => entry.Value.ToSnapshot()),
                Frontlines = _frontlines.ToDictionary(entry => entry.Key, entry => entry.Value.ToSnapshot())
            };
        }

        private PredictionCreatureState GetHero(ulong playerId)
        {
            if (!_heroes.TryGetValue(playerId, out PredictionCreatureState? hero))
            {
                hero = new PredictionCreatureState { Exists = true };
                _heroes[playerId] = hero;
            }

            return hero;
        }

        private PredictionCreatureState GetOrCreateFrontline(ulong playerId)
        {
            if (!_frontlines.TryGetValue(playerId, out PredictionCreatureState? frontline))
            {
                frontline = new PredictionCreatureState { IsFrontline = true };
                _frontlines[playerId] = frontline;
            }

            return frontline;
        }

        private PredictionCreatureState? ResolveTarget(ulong playerId, PvpTargetKind kind, bool fallbackToHero)
        {
            return kind switch
            {
                PvpTargetKind.SelfHero or PvpTargetKind.EnemyHero => GetHero(playerId),
                PvpTargetKind.SelfFrontline or PvpTargetKind.EnemyFrontline => ResolveFrontlineOrFallback(playerId, fallbackToHero),
                _ => fallbackToHero ? GetHero(playerId) : null
            };
        }

        private PredictionCreatureState? ResolveFrontlineOrFallback(ulong playerId, bool fallbackToHero)
        {
            if (_frontlines.TryGetValue(playerId, out PredictionCreatureState? frontline) && frontline.Exists && frontline.CurrentHp > 0)
            {
                return frontline;
            }

            return fallbackToHero ? GetHero(playerId) : null;
        }
    }

    private sealed class PredictionCreatureState
    {
        public bool Exists { get; set; }
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }
        public int Block { get; set; }
        public bool IsFrontline { get; init; }

        public static PredictionCreatureState FromSnapshot(PvpCreatureSnapshot snapshot, bool isFrontline)
        {
            return new PredictionCreatureState
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
