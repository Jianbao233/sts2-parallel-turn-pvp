namespace ParallelTurnPvp.Core;

public sealed class PvpPredictionEngine : IPvpPredictionEngine
{
    public PvpCombatSnapshot Predict(PvpCombatSnapshot initialSnapshot, PvpRoundExecutionPlan plan)
    {
        PredictionState state = new(initialSnapshot);
        foreach (PvpExecutionStep step in plan.Steps)
        {
            ApplyStep(state, step);
        }

        return state.ToSnapshot(initialSnapshot.RoundIndex, initialSnapshot.SnapshotVersion);
    }

    private static void ApplyStep(PredictionState state, PvpExecutionStep step)
    {
        switch (step.ModelEntry)
        {
            case "STRIKE_NECROBINDER":
                ApplyAttack(state, step.PlayerId, step.Target, 6, requiresFrontlineAttacker: false);
                break;
            case "DEFEND_NECROBINDER":
                state.GainBlock(step.PlayerId, PvpTargetKind.SelfHero, 5);
                break;
            case "AFTERLIFE":
                ApplyAfterlife(state, step.PlayerId, 6);
                break;
            case "POKE":
                ApplyAttack(state, step.PlayerId, step.Target, 6, requiresFrontlineAttacker: true);
                break;
            case "FRONTLINE_BRACE":
                if (state.HasFrontline(step.PlayerId))
                {
                    state.GainBlock(step.PlayerId, PvpTargetKind.SelfFrontline, 5);
                }
                else
                {
                    state.GainBlock(step.PlayerId, PvpTargetKind.SelfHero, 5);
                }
                break;
            case "BREAK_FORMATION":
                ApplyAttack(state, step.PlayerId, step.Target, 8, requiresFrontlineAttacker: false);
                break;
            case "BLOCK_POTION":
                state.GainBlock(step.Target.OwnerPlayerId != 0 ? step.Target.OwnerPlayerId : step.PlayerId, step.Target.Kind, 12, fallbackToHero: true);
                break;
            case "ENERGY_POTION":
                break;
            case "BLOOD_POTION":
                ApplyBloodPotion(state, step.PlayerId, step.Target);
                break;
            case "FRONTLINE_SALVE":
                ApplyFrontlineSalve(state, step.PlayerId, 8);
                break;
        }
    }

    private static void ApplyAfterlife(PredictionState state, ulong playerId, int amount)
    {
        if (state.HasFrontline(playerId))
        {
            state.GainMaxHp(playerId, PvpTargetKind.SelfFrontline, amount);
            state.Heal(playerId, PvpTargetKind.SelfFrontline, amount);
            return;
        }

        state.SummonFrontline(playerId, amount);
    }

    private static void ApplyFrontlineSalve(PredictionState state, ulong playerId, int amount)
    {
        if (state.HasFrontline(playerId))
        {
            state.Heal(playerId, PvpTargetKind.SelfFrontline, amount);
            return;
        }

        state.Heal(playerId, PvpTargetKind.SelfHero, amount);
    }

    private static void ApplyBloodPotion(PredictionState state, ulong playerId, PvpTargetRef target)
    {
        PvpTargetKind targetKind = target.Kind is PvpTargetKind.EnemyHero or PvpTargetKind.EnemyFrontline or PvpTargetKind.None
            ? PvpTargetKind.SelfHero
            : target.Kind;
        ulong targetOwner = target.OwnerPlayerId != 0 ? target.OwnerPlayerId : playerId;
        PredictionCreatureState? creature = state.ResolveTarget(targetOwner, targetKind, fallbackToHero: true);
        if (creature == null || !creature.Exists)
        {
            return;
        }

        int amount = (int)Math.Floor(creature.MaxHp * 0.20m);
        if (amount <= 0 && creature.MaxHp > 0)
        {
            amount = 1;
        }

        creature.CurrentHp = Math.Min(creature.MaxHp, creature.CurrentHp + amount);
    }

    private static void ApplyAttack(PredictionState state, ulong playerId, PvpTargetRef target, int amount, bool requiresFrontlineAttacker)
    {
        if (requiresFrontlineAttacker && !state.HasFrontline(playerId))
        {
            return;
        }

        bool intercepted = false;
        PredictionCreatureState? attackTarget = state.ResolveAttackTarget(target, out ulong ownerId, out intercepted);
        if (attackTarget == null || !attackTarget.Exists)
        {
            return;
        }

        int overflow = ApplyDamage(attackTarget, amount);
        if (overflow <= 0 || !intercepted)
        {
            return;
        }

        PredictionCreatureState? hero = state.ResolveTarget(ownerId, PvpTargetKind.EnemyHero, fallbackToHero: true)
            ?? state.ResolveTarget(ownerId, PvpTargetKind.SelfHero, fallbackToHero: true);
        if (hero == null || !hero.Exists)
        {
            return;
        }

        ApplyDamage(hero, overflow);
    }

    private static int ApplyDamage(PredictionCreatureState target, int amount)
    {
        int blocked = Math.Min(target.Block, amount);
        target.Block -= blocked;
        int remaining = Math.Max(amount - blocked, 0);
        if (remaining <= 0)
        {
            return 0;
        }

        target.CurrentHp -= remaining;
        if (target.CurrentHp > 0)
        {
            return 0;
        }

        int overflow = -target.CurrentHp;
        target.CurrentHp = 0;
        if (target.IsFrontline)
        {
            target.Exists = false;
            target.Block = 0;
        }

        return overflow;
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

        public bool HasFrontline(ulong playerId)
        {
            return _frontlines.TryGetValue(playerId, out PredictionCreatureState? frontline) && frontline.Exists && frontline.CurrentHp > 0;
        }

        public void SummonFrontline(ulong playerId, int amount)
        {
            PredictionCreatureState frontline = GetOrCreateFrontline(playerId);
            frontline.Exists = amount > 0;
            frontline.MaxHp = Math.Max(amount, 0);
            frontline.CurrentHp = Math.Max(amount, 0);
            frontline.Block = 0;
        }

        public void GainMaxHp(ulong playerId, PvpTargetKind kind, int amount)
        {
            PredictionCreatureState? target = ResolveTarget(playerId, kind, fallbackToHero: true);
            if (target == null || !target.Exists || amount <= 0)
            {
                return;
            }

            target.MaxHp += amount;
        }

        public void Heal(ulong playerId, PvpTargetKind kind, int amount, bool fallbackToHero = true)
        {
            PredictionCreatureState? target = ResolveTarget(playerId, kind, fallbackToHero);
            if (target == null || !target.Exists || amount <= 0)
            {
                return;
            }

            target.CurrentHp = Math.Min(target.MaxHp, target.CurrentHp + amount);
        }

        public void GainBlock(ulong playerId, PvpTargetKind kind, int amount, bool fallbackToHero = false)
        {
            PredictionCreatureState? target = ResolveTarget(playerId, kind, fallbackToHero);
            if (target == null || !target.Exists || amount <= 0)
            {
                return;
            }

            target.Block += amount;
        }

        public PredictionCreatureState? ResolveTarget(ulong playerId, PvpTargetKind kind, bool fallbackToHero)
        {
            return kind switch
            {
                PvpTargetKind.SelfHero or PvpTargetKind.EnemyHero => GetHero(playerId),
                PvpTargetKind.SelfFrontline or PvpTargetKind.EnemyFrontline => ResolveFrontlineOrFallback(playerId, fallbackToHero),
                _ => fallbackToHero ? GetHero(playerId) : null
            };
        }

        public PredictionCreatureState? ResolveAttackTarget(PvpTargetRef target, out ulong ownerId, out bool intercepted)
        {
            ownerId = target.OwnerPlayerId;
            intercepted = false;
            switch (target.Kind)
            {
                case PvpTargetKind.EnemyHero:
                    if (HasFrontline(ownerId))
                    {
                        intercepted = true;
                        return GetOrCreateFrontline(ownerId);
                    }

                    return GetHero(ownerId);
                case PvpTargetKind.EnemyFrontline:
                    if (HasFrontline(ownerId))
                    {
                        return GetOrCreateFrontline(ownerId);
                    }

                    return GetHero(ownerId);
                case PvpTargetKind.SelfHero:
                    ownerId = target.OwnerPlayerId;
                    return GetHero(ownerId);
                case PvpTargetKind.SelfFrontline:
                    ownerId = target.OwnerPlayerId;
                    return HasFrontline(ownerId) ? GetOrCreateFrontline(ownerId) : GetHero(ownerId);
                default:
                    return ownerId != 0 ? GetHero(ownerId) : null;
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

        private PredictionCreatureState? ResolveFrontlineOrFallback(ulong playerId, bool fallbackToHero)
        {
            if (HasFrontline(playerId))
            {
                return GetOrCreateFrontline(playerId);
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
