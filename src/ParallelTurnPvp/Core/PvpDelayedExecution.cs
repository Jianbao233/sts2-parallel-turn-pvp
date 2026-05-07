using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using Godot;
using HarmonyLib;
using ParallelTurnPvp.Models;
using ParallelTurnPvp.Ui;

namespace ParallelTurnPvp.Core;

public static class PvpDelayedExecution
{
    private const string LiveDelayedApplyEnvKey = "PTPVP_ENABLE_LIVE_DELAYED";
    private static readonly bool EnableLiveDelayedApply = ResolveLiveDelayedApplyFlag();
    private static readonly HashSet<string> DelayedLiveApplyModelEntries = new(StringComparer.Ordinal)
    {
        "EARLY_LOCK_REWARD",
        "AFTERLIFE",
        "DEFEND_NECROBINDER",
        "STRIKE_NECROBINDER",
        "POKE",
        "BREAK_FORMATION",
        "BLOCK_POTION",
        "BLOOD_POTION",
        "FRONTLINE_BRACE",
        "FRONTLINE_SALVE"
    };
    private static readonly IPvpExecutionPlanner ExecutionPlanner = new PvpExecutionPlanner();
    private static readonly IPvpDeltaPlanner DeltaPlanner = new PvpDeltaPlanner();
    private static readonly IPvpDelayedPlanner DelayedPlanner = new PvpDelayedPlanner();
    private static readonly IPvpDelayedCommandPlanner DelayedCommandPlanner = new PvpDelayedCommandPlanner();
    public static bool IsLiveDelayedApplyEnabled => EnableLiveDelayedApply;

    public static bool ShouldDelayLiveApply(Player player, string modelEntry)
    {
        if (!EnableLiveDelayedApply)
        {
            return false;
        }

        return player.RunState is RunState runState &&
               runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any() &&
               DelayedLiveApplyModelEntries.Contains(modelEntry);
    }

    public static bool ShouldDelayLiveApply(string modelEntry)
    {
        return EnableLiveDelayedApply && DelayedLiveApplyModelEntries.Contains(modelEntry);
    }

    public static int ApplyDelayedLiveEffects(PvpMatchRuntime runtime, CombatState combatState)
    {
        if (!EnableLiveDelayedApply)
        {
            return 0;
        }

        if (runtime.CurrentRound.DelayedLiveEffectsApplied || runtime.CurrentRound.RoundIndex <= 0)
        {
            return 0;
        }

        IReadOnlyList<PvpRoundSubmission> submissions = runtime.GetResolverSubmissions();
        PvpRoundExecutionPlan plan = ExecutionPlanner.BuildPlan(runtime.CurrentRound.RoundIndex, submissions);
        PvpRoundDeltaPlan deltaPlan = DeltaPlanner.BuildDeltaPlan(runtime.CurrentRound.SnapshotAtRoundStart, plan);
        PvpRoundDelayedPlan delayedPlan = DelayedPlanner.BuildDelayedPlan(runtime.CurrentRound.SnapshotAtRoundStart, deltaPlan);
        PvpRoundDelayedCommandPlan delayedCommandPlan = DelayedCommandPlanner.BuildCommandPlan(runtime.CurrentRound.SnapshotAtRoundStart, delayedPlan);
        int appliedCount = 0;

        Log.Info($"[ParallelTurnPvp] 延迟执行桥：round={runtime.CurrentRound.RoundIndex} submissions={submissions.Count} deltaOps={deltaPlan.Operations.Count} delayedCandidates={delayedPlan.Operations.Count} delayedCommands={delayedCommandPlan.Commands.Count} source=resolver_submissions");

        foreach (PvpDelayedCommand command in delayedCommandPlan.Commands.Where(command => ShouldDelayLiveApply(command.ModelEntry)))
        {
            if (command.Amount <= 0)
            {
                Log.Info($"[ParallelTurnPvp] 跳过延迟执行：amount<=0 model={command.ModelEntry} command={command.Kind} player={command.TargetPlayerId}");
                continue;
            }

            switch (command.Kind)
            {
                case PvpDelayedCommandKind.GainBlock:
                {
                    Creature? target = ResolveTarget(combatState, command);
                    if (target == null || target.IsDead)
                    {
                        Log.Info($"[ParallelTurnPvp] 跳过延迟执行：目标缺失 model={command.ModelEntry} command={command.Kind} player={command.TargetPlayerId} kind={command.TargetKind}");
                        continue;
                    }

                    target.GainBlockInternal(command.Amount);
                    ParallelTurnIntentOverlay.TryShowDelayedFloat(target, $"+{command.Amount} 格挡", new Color(0.46f, 0.82f, 1.00f, 1.00f));
                    appliedCount++;
                    Log.Info($"[ParallelTurnPvp] 已执行延迟格挡 model={command.ModelEntry} player={command.TargetPlayerId} kind={command.TargetKind} amount={command.Amount} currentBlock={target.Block} mode=stable_internal");
                    break;
                }
                case PvpDelayedCommandKind.Heal:
                {
                    Creature? target = ResolveTarget(combatState, command);
                    if (target == null || target.IsDead)
                    {
                        Log.Info($"[ParallelTurnPvp] 跳过延迟执行：目标缺失 model={command.ModelEntry} command={command.Kind} player={command.TargetPlayerId} kind={command.TargetKind}");
                        continue;
                    }

                    target.HealInternal(command.Amount);
                    TryPlayNativeHealVfx(target, command.Amount);
                    appliedCount++;
                    Log.Info($"[ParallelTurnPvp] 已执行延迟治疗 model={command.ModelEntry} player={command.TargetPlayerId} kind={command.TargetKind} amount={command.Amount} currentHp={target.CurrentHp}/{target.MaxHp} mode=stable_internal");
                    break;
                }
                case PvpDelayedCommandKind.GainMaxHp:
                {
                    Creature? target = ResolveTarget(combatState, command, fallbackFrontlineToHero: false);
                    if (target == null || target.IsDead)
                    {
                        Log.Info($"[ParallelTurnPvp] 跳过延迟执行：目标缺失 model={command.ModelEntry} command={command.Kind} player={command.TargetPlayerId} kind={command.TargetKind}");
                        continue;
                    }

                    int before = target.MaxHp;
                    target.SetMaxHpInternal(target.MaxHp + command.Amount);
                    appliedCount++;
                    Log.Info($"[ParallelTurnPvp] 已执行延迟生命上限 model={command.ModelEntry} player={command.TargetPlayerId} kind={command.TargetKind} amount={command.Amount} maxHp={before}->{target.MaxHp} mode=stable_internal");
                    break;
                }
                case PvpDelayedCommandKind.SummonFrontline:
                {
                    if (!TryApplySummonFrontline(combatState, command, out Creature? summoned))
                    {
                        Log.Info($"[ParallelTurnPvp] 跳过延迟执行：召唤失败 model={command.ModelEntry} command={command.Kind} player={command.TargetPlayerId} kind={command.TargetKind}");
                        continue;
                    }

                    appliedCount++;
                    Log.Info($"[ParallelTurnPvp] 已执行延迟召唤 model={command.ModelEntry} player={command.TargetPlayerId} kind={command.TargetKind} amount={command.Amount} frontline={summoned}");
                    break;
                }
                case PvpDelayedCommandKind.Damage:
                {
                    if (!TryApplyDelayedDamage(combatState, command))
                    {
                        Log.Info($"[ParallelTurnPvp] 跳过延迟执行：伤害落地失败 model={command.ModelEntry} command={command.Kind} player={command.TargetPlayerId} kind={command.TargetKind} amount={command.Amount}");
                        continue;
                    }

                    appliedCount++;
                    break;
                }
                default:
                    Log.Info($"[ParallelTurnPvp] 跳过延迟执行：暂不支持命令 kind={command.Kind} model={command.ModelEntry} executorHint={command.ExecutorHint}");
                    break;
            }
        }

        runtime.CurrentRound.DelayedLiveEffectsApplied = true;
        return appliedCount;
    }

    private static void TryPlayNativeHealVfx(Creature target, decimal amount)
    {
        if (amount <= 0m)
        {
            return;
        }

        try
        {
            string healFx = target.Monster is Osty ? "vfx/vfx_heal_osty" : "vfx/vfx_cross_heal";
            VfxCmd.PlayOnCreatureCenter(target, healFx);

            Node? vfxContainer = NCombatRoom.Instance?.CombatVfxContainer;
            NHealNumVfx? healNumVfx = NHealNumVfx.Create(target, amount);
            if (vfxContainer != null && healNumVfx != null)
            {
                vfxContainer.AddChild(healNumVfx);
                Log.Info($"[ParallelTurnPvp] 延迟治疗VFX：原版治疗弹字已播放 target={target} amount={amount}");
                return;
            }

            int fallbackAmount = Math.Max(1, (int)Math.Round(amount));
            ParallelTurnIntentOverlay.TryShowDelayedFloat(target, $"+{fallbackAmount} 治疗", new Color(0.22f, 0.95f, 0.35f, 1.00f));
            Log.Warn($"[ParallelTurnPvp] 延迟治疗VFX：原版弹字未创建，已回退绿色浮字 target={target} amount={amount} fallback={fallbackAmount}");
        }
        catch (Exception ex)
        {
            int fallbackAmount = Math.Max(1, (int)Math.Round(amount));
            ParallelTurnIntentOverlay.TryShowDelayedFloat(target, $"+{fallbackAmount} 治疗", new Color(0.22f, 0.95f, 0.35f, 1.00f));
            Log.Error($"[ParallelTurnPvp] 延迟治疗VFX：原版路径失败，已回退绿色浮字 target={target} amount={amount} fallback={fallbackAmount} error={ex.Message}");
        }
    }

    private static bool ResolveLiveDelayedApplyFlag()
    {
        string? raw = System.Environment.GetEnvironmentVariable(LiveDelayedApplyEnvKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            // Default to enabled on this test line; explicit env can still override.
            return true;
        }

        if (raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (raw == "0" || raw.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return false;
    }

    private static Creature? ResolveTarget(CombatState combatState, PvpDelayedCommand command, bool fallbackFrontlineToHero = true)
    {
        Player? player = ResolvePlayer(combatState, command.TargetPlayerId);
        if (player == null)
        {
            return null;
        }

        return command.TargetKind switch
        {
            PvpTargetKind.SelfHero or PvpTargetKind.EnemyHero => player.Creature,
            PvpTargetKind.SelfFrontline or PvpTargetKind.EnemyFrontline => fallbackFrontlineToHero
                ? ParallelTurnFrontlineHelper.GetFrontline(player) ?? player.Creature
                : ParallelTurnFrontlineHelper.GetFrontline(player),
            _ => player.Creature
        };
    }

    private static bool TryApplyDelayedDamage(CombatState combatState, PvpDelayedCommand command)
    {
        Creature? target = ResolveTarget(combatState, command, fallbackFrontlineToHero: false);
        if (target == null || target.IsDead || command.Amount <= 0)
        {
            return false;
        }

        int blocked = Math.Min(target.Block, command.Amount);
        if (blocked > 0)
        {
            target.LoseBlockInternal(blocked);
        }

        int unblocked = command.Amount - blocked;
        if (unblocked > 0)
        {
            int afterHp = Math.Max(target.CurrentHp - unblocked, 0);
            target.SetCurrentHpInternal(afterHp);
        }

        if (unblocked > 0)
        {
            ParallelTurnIntentOverlay.TryShowDelayedFloat(target, $"-{unblocked}", new Color(1.00f, 0.36f, 0.36f, 1.00f));
        }

        Log.Info($"[ParallelTurnPvp] 已执行延迟伤害 model={command.ModelEntry} targetPlayer={command.TargetPlayerId} kind={command.TargetKind} amount={command.Amount} blocked={blocked} hp={target.CurrentHp}/{target.MaxHp} block={target.Block}");
        return true;
    }

    private static Player? ResolvePlayer(CombatState combatState, ulong playerId)
    {
        return combatState.Players.FirstOrDefault(candidate => candidate.NetId == playerId);
    }

    private static bool TryApplySummonFrontline(CombatState combatState, PvpDelayedCommand command, out Creature? frontline)
    {
        frontline = null;
        Player? player = ResolvePlayer(combatState, command.TargetPlayerId);
        if (player == null || command.Amount <= 0)
        {
            return false;
        }

        frontline = ResolveTrackedFrontline(combatState, player);
        if (frontline == null)
        {
            Log.Warn($"[ParallelTurnPvp] 延迟召唤失败：找不到可复用前线实体 player={player.NetId} amount={command.Amount}");
            return false;
        }

        player.PlayerCombatState?.AddPetInternal(frontline);
        if (frontline.PetOwner != player)
        {
            frontline.PetOwner = player;
        }

        SetCreatureMaxHpExact(frontline, command.Amount);
        if (frontline.IsDead || frontline.CurrentHp <= 0)
        {
            frontline.HealInternal(command.Amount);
        }
        else
        {
            frontline.SetCurrentHpInternal(command.Amount);
        }

        if (frontline.Block > 0)
        {
            frontline.LoseBlockInternal(frontline.Block);
        }

        NCombatRoom.Instance?.GetCreatureNode(frontline)?.StartReviveAnim();
        return true;
    }

    private static Creature? ResolveTrackedFrontline(CombatState combatState, Player player)
    {
        Creature? living = ParallelTurnFrontlineHelper.GetFrontline(player);
        if (living != null)
        {
            return living;
        }

        if (player.Osty != null)
        {
            return player.Osty;
        }

        return combatState.Creatures.FirstOrDefault(creature =>
            creature.PetOwner == player &&
            creature.Monster is Osty);
    }

    private static void SetCreatureMaxHpExact(Creature creature, int targetMaxHp)
    {
        int safeTarget = Math.Max(0, targetMaxHp);
        creature.SetMaxHpInternal(safeTarget);
        if (creature.MaxHp == safeTarget)
        {
            return;
        }

        foreach (string fieldName in new[] { "<MaxHp>k__BackingField", "_maxHp", "maxHp", "_maxHP", "maxHP" })
        {
            try
            {
                var field = AccessTools.Field(creature.GetType(), fieldName);
                if (field == null)
                {
                    continue;
                }

                field.SetValue(creature, safeTarget);
                if (creature.MaxHp == safeTarget)
                {
                    return;
                }
            }
            catch
            {
                // Try next candidate.
            }
        }
    }
}


