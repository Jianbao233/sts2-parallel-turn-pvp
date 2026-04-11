namespace ParallelTurnPvp.Core;

public sealed class PvpDeltaPlanner : IPvpDeltaPlanner
{
    public PvpRoundDeltaPlan BuildDeltaPlan(PvpCombatSnapshot initialSnapshot, PvpRoundExecutionPlan plan)
    {
        DeltaPlanningState state = new(initialSnapshot);
        PvpRoundDeltaPlan deltaPlan = new() { RoundIndex = plan.RoundIndex };

        foreach (PvpExecutionStep step in plan.Steps)
        {
            foreach (PvpDeltaOperation operation in BuildOperationsForStep(state, step))
            {
                deltaPlan.Operations.Add(operation);
                state.Apply(operation);
            }
        }

        return deltaPlan;
    }

    private static IEnumerable<PvpDeltaOperation> BuildOperationsForStep(DeltaPlanningState state, PvpExecutionStep step)
    {
        switch (step.ModelEntry)
        {
            case "STRIKE_NECROBINDER":
                return BuildAttackOperations(state, step, 6, requiresFrontlineAttacker: false);
            case "DEFEND_NECROBINDER":
                return [CreateOperation(step, PvpDeltaOperationKind.GainBlock, step.PlayerId, PvpTargetKind.SelfHero, 5)];
            case "AFTERLIFE":
                return BuildAfterlifeOperations(state, step, 6);
            case "POKE":
                return BuildAttackOperations(state, step, 6, requiresFrontlineAttacker: true);
            case "FRONTLINE_BRACE":
                return [CreateOperation(step, PvpDeltaOperationKind.GainBlock, step.PlayerId, state.HasFrontline(step.PlayerId) ? PvpTargetKind.SelfFrontline : PvpTargetKind.SelfHero, 5)];
            case "BREAK_FORMATION":
                return BuildAttackOperations(state, step, 8, requiresFrontlineAttacker: false);
            case "BLOCK_POTION":
                return [CreateOperation(step, PvpDeltaOperationKind.GainBlock, step.Target.OwnerPlayerId != 0 ? step.Target.OwnerPlayerId : step.PlayerId, step.Target.Kind is PvpTargetKind.None ? PvpTargetKind.SelfHero : NormalizeFriendlyTarget(step.Target.Kind), 12)];
            case "ENERGY_POTION":
                return [CreateOperation(step, PvpDeltaOperationKind.GainResource, step.PlayerId, PvpTargetKind.SelfHero, 2)];
            case "BLOOD_POTION":
                return BuildBloodPotionOperations(state, step);
            case "FRONTLINE_SALVE":
                return [CreateOperation(step, PvpDeltaOperationKind.Heal, step.PlayerId, state.HasFrontline(step.PlayerId) ? PvpTargetKind.SelfFrontline : PvpTargetKind.SelfHero, 8)];
            case "END_TURN":
                return [CreateOperation(step, PvpDeltaOperationKind.EndRoundMarker, step.PlayerId, PvpTargetKind.None, 0)];
            default:
                return Array.Empty<PvpDeltaOperation>();
        }
    }

    private static IEnumerable<PvpDeltaOperation> BuildAfterlifeOperations(DeltaPlanningState state, PvpExecutionStep step, int amount)
    {
        if (state.HasFrontline(step.PlayerId))
        {
            return
            [
                CreateOperation(step, PvpDeltaOperationKind.GainMaxHp, step.PlayerId, PvpTargetKind.SelfFrontline, amount),
                CreateOperation(step, PvpDeltaOperationKind.Heal, step.PlayerId, PvpTargetKind.SelfFrontline, amount)
            ];
        }

        return [CreateOperation(step, PvpDeltaOperationKind.SummonFrontline, step.PlayerId, PvpTargetKind.SelfFrontline, amount)];
    }

    private static IEnumerable<PvpDeltaOperation> BuildBloodPotionOperations(DeltaPlanningState state, PvpExecutionStep step)
    {
        ulong targetPlayerId = step.Target.OwnerPlayerId != 0 ? step.Target.OwnerPlayerId : step.PlayerId;
        PvpTargetKind targetKind = NormalizeFriendlyTarget(step.Target.Kind is PvpTargetKind.None ? PvpTargetKind.SelfHero : step.Target.Kind);
        PlanningTargetInfo? targetInfo = state.ResolveTarget(targetPlayerId, targetKind, fallbackToHero: true);
        if (targetInfo == null || !targetInfo.Exists)
        {
            return Array.Empty<PvpDeltaOperation>();
        }

        int amount = (int)Math.Floor(targetInfo.MaxHp * 0.20m);
        if (amount <= 0 && targetInfo.MaxHp > 0)
        {
            amount = 1;
        }

        return [CreateOperation(step, PvpDeltaOperationKind.Heal, targetPlayerId, targetInfo.Kind, amount)];
    }

    private static IEnumerable<PvpDeltaOperation> BuildAttackOperations(DeltaPlanningState state, PvpExecutionStep step, int amount, bool requiresFrontlineAttacker)
    {
        if (requiresFrontlineAttacker && !state.HasFrontline(step.PlayerId))
        {
            return Array.Empty<PvpDeltaOperation>();
        }

        AttackResolution? resolution = state.ResolveAttack(step.Target);
        if (resolution == null)
        {
            return Array.Empty<PvpDeltaOperation>();
        }

        List<PvpDeltaOperation> operations =
        [
            CreateOperation(step, PvpDeltaOperationKind.Damage, resolution.PrimaryPlayerId, resolution.PrimaryKind, amount)
        ];
        if (resolution.IsIntercepted)
        {
            int overflow = state.PredictOverflowDamage(resolution.PrimaryPlayerId, resolution.PrimaryKind, amount);
            if (overflow > 0)
            {
                operations.Add(CreateOperation(step, PvpDeltaOperationKind.Damage, resolution.HeroPlayerId, PvpTargetKind.EnemyHero, overflow));
            }
        }

        return operations;
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

    private static PvpDeltaOperation CreateOperation(PvpExecutionStep step, PvpDeltaOperationKind kind, ulong targetPlayerId, PvpTargetKind targetKind, int amount)
    {
        return new PvpDeltaOperation
        {
            Phase = step.Phase,
            Kind = kind,
            SourcePlayerId = step.PlayerId,
            TargetPlayerId = targetPlayerId,
            TargetKind = targetKind,
            Amount = amount,
            Sequence = step.Sequence,
            ModelEntry = step.ModelEntry,
            RuntimeActionId = step.RuntimeActionId
        };
    }

    private sealed class DeltaPlanningState
    {
        private readonly Dictionary<ulong, PlanningTargetInfo> _heroes;
        private readonly Dictionary<ulong, PlanningTargetInfo> _frontlines;

        public DeltaPlanningState(PvpCombatSnapshot snapshot)
        {
            _heroes = snapshot.Heroes.ToDictionary(entry => entry.Key, entry => PlanningTargetInfo.FromSnapshot(entry.Key, PvpTargetKind.SelfHero, entry.Value));
            _frontlines = snapshot.Frontlines.ToDictionary(entry => entry.Key, entry => PlanningTargetInfo.FromSnapshot(entry.Key, PvpTargetKind.SelfFrontline, entry.Value));
        }

        public bool HasFrontline(ulong playerId)
        {
            return _frontlines.TryGetValue(playerId, out PlanningTargetInfo? frontline) && frontline.Exists && frontline.CurrentHp > 0;
        }

        public PlanningTargetInfo? ResolveTarget(ulong playerId, PvpTargetKind kind, bool fallbackToHero)
        {
            return kind switch
            {
                PvpTargetKind.SelfHero or PvpTargetKind.EnemyHero => GetHero(playerId),
                PvpTargetKind.SelfFrontline or PvpTargetKind.EnemyFrontline => ResolveFrontline(playerId, fallbackToHero),
                _ => fallbackToHero ? GetHero(playerId) : null
            };
        }

        public AttackResolution? ResolveAttack(PvpTargetRef target)
        {
            if (target.OwnerPlayerId == 0)
            {
                return null;
            }

            if (target.Kind == PvpTargetKind.EnemyHero)
            {
                if (HasFrontline(target.OwnerPlayerId))
                {
                    return new AttackResolution { PrimaryPlayerId = target.OwnerPlayerId, PrimaryKind = PvpTargetKind.EnemyFrontline, HeroPlayerId = target.OwnerPlayerId, IsIntercepted = true };
                }

                return new AttackResolution { PrimaryPlayerId = target.OwnerPlayerId, PrimaryKind = PvpTargetKind.EnemyHero, HeroPlayerId = target.OwnerPlayerId, IsIntercepted = false };
            }

            if (target.Kind == PvpTargetKind.EnemyFrontline)
            {
                return new AttackResolution { PrimaryPlayerId = target.OwnerPlayerId, PrimaryKind = HasFrontline(target.OwnerPlayerId) ? PvpTargetKind.EnemyFrontline : PvpTargetKind.EnemyHero, HeroPlayerId = target.OwnerPlayerId, IsIntercepted = false };
            }

            return new AttackResolution { PrimaryPlayerId = target.OwnerPlayerId, PrimaryKind = target.Kind, HeroPlayerId = target.OwnerPlayerId, IsIntercepted = false };
        }

        public int PredictOverflowDamage(ulong playerId, PvpTargetKind kind, int amount)
        {
            PlanningTargetInfo? target = ResolveTarget(playerId, kind, fallbackToHero: false);
            if (target == null || !target.Exists)
            {
                return amount;
            }

            int blocked = Math.Min(target.Block, amount);
            int remaining = Math.Max(amount - blocked, 0);
            return Math.Max(remaining - target.CurrentHp, 0);
        }

        public void Apply(PvpDeltaOperation operation)
        {
            PlanningTargetInfo? target = ResolveTarget(operation.TargetPlayerId, operation.TargetKind, fallbackToHero: operation.Kind is PvpDeltaOperationKind.Heal or PvpDeltaOperationKind.GainBlock);
            switch (operation.Kind)
            {
                case PvpDeltaOperationKind.SummonFrontline:
                    PlanningTargetInfo frontline = GetOrCreateFrontline(operation.TargetPlayerId);
                    frontline.Exists = operation.Amount > 0;
                    frontline.MaxHp = Math.Max(operation.Amount, 0);
                    frontline.CurrentHp = Math.Max(operation.Amount, 0);
                    frontline.Block = 0;
                    break;
                case PvpDeltaOperationKind.GainMaxHp:
                    if (target is { Exists: true }) target.MaxHp += operation.Amount;
                    break;
                case PvpDeltaOperationKind.Heal:
                    if (target is { Exists: true }) target.CurrentHp = Math.Min(target.MaxHp, target.CurrentHp + operation.Amount);
                    break;
                case PvpDeltaOperationKind.GainBlock:
                    if (target is { Exists: true }) target.Block += operation.Amount;
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

        private PlanningTargetInfo GetHero(ulong playerId)
        {
            if (!_heroes.TryGetValue(playerId, out PlanningTargetInfo? hero))
            {
                hero = new PlanningTargetInfo { PlayerId = playerId, Kind = PvpTargetKind.SelfHero, Exists = true };
                _heroes[playerId] = hero;
            }

            return hero;
        }

        private PlanningTargetInfo GetOrCreateFrontline(ulong playerId)
        {
            if (!_frontlines.TryGetValue(playerId, out PlanningTargetInfo? frontline))
            {
                frontline = new PlanningTargetInfo { PlayerId = playerId, Kind = PvpTargetKind.SelfFrontline, IsFrontline = true };
                _frontlines[playerId] = frontline;
            }

            return frontline;
        }

        private PlanningTargetInfo? ResolveFrontline(ulong playerId, bool fallbackToHero)
        {
            if (HasFrontline(playerId))
            {
                return GetOrCreateFrontline(playerId);
            }

            return fallbackToHero ? GetHero(playerId) : null;
        }
    }

    private sealed class PlanningTargetInfo
    {
        public ulong PlayerId { get; init; }
        public PvpTargetKind Kind { get; set; }
        public bool Exists { get; set; }
        public int CurrentHp { get; set; }
        public int MaxHp { get; set; }
        public int Block { get; set; }
        public bool IsFrontline { get; init; }

        public static PlanningTargetInfo FromSnapshot(ulong playerId, PvpTargetKind kind, PvpCreatureSnapshot snapshot)
        {
            return new PlanningTargetInfo
            {
                PlayerId = playerId,
                Kind = kind,
                Exists = snapshot.Exists,
                CurrentHp = snapshot.CurrentHp,
                MaxHp = snapshot.MaxHp,
                Block = snapshot.Block,
                IsFrontline = kind == PvpTargetKind.SelfFrontline
            };
        }
    }

    private sealed class AttackResolution
    {
        public ulong PrimaryPlayerId { get; init; }
        public PvpTargetKind PrimaryKind { get; init; }
        public ulong HeroPlayerId { get; init; }
        public bool IsIntercepted { get; init; }
    }
}
