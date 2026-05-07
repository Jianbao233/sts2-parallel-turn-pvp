using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using ParallelTurnPvp.Core;
using ParallelTurnPvp.Models;
using System.Collections.Generic;
using System.Linq;

namespace ParallelTurnPvp.Patches;

internal static class IndependentFrontlineBlockRuntime
{
    private static readonly System.Threading.AsyncLocal<Stack<Creature>?> ActiveTargets = new();
    private static readonly System.Threading.AsyncLocal<Creature?> BypassFrontline = new();

    public static bool IsIndependentFrontlineBlockActive(Creature target)
    {
        if (!ParallelTurnPatchContext.IsActiveDebugArena() || target.PetOwner == null)
        {
            return false;
        }

        return ParallelTurnFrontlineHelper.GetFrontline(target.PetOwner) == target;
    }

    public static void PushTarget(Creature target)
    {
        Stack<Creature> stack = ActiveTargets.Value ??= [];
        stack.Push(target);
    }

    public static Creature? PeekTarget()
    {
        Stack<Creature>? stack = ActiveTargets.Value;
        if (stack == null || stack.Count == 0)
        {
            return null;
        }

        return stack.Peek();
    }

    public static void PopTarget(Creature expectedTarget)
    {
        Stack<Creature>? stack = ActiveTargets.Value;
        if (stack == null || stack.Count == 0)
        {
            return;
        }

        if (stack.Peek() != expectedTarget)
        {
            return;
        }

        stack.Pop();
        if (stack.Count == 0)
        {
            ActiveTargets.Value = null;
        }
    }

    public static bool ShouldBypass(Creature creature)
    {
        return BypassFrontline.Value == creature;
    }

    public static void SetBypass(Creature? creature)
    {
        BypassFrontline.Value = creature;
    }
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetUpCombat))]
public static class ParallelTurnStripSharedFrontlinePowerPatch
{
    static void Postfix(CombatState state)
    {
        if (state.RunState is not RunState runState || !runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            return;
        }

        foreach (var player in state.Players)
        {
            if (ParallelTurnFrontlineHelper.GetFrontline(player)?.GetPower<DieForYouPower>() is not { } dieForYouPower)
            {
                continue;
            }

            dieForYouPower.RemoveInternal();
            Log.Info($"[ParallelTurnPvp] Removed DieForYouPower from frontline {dieForYouPower.Owner}.");
        }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeDamageReceived))]
public static class ParallelTurnPrepareIndependentFrontlineBlockPatch
{
    static void Prefix(Creature target, decimal amount)
    {
        if (!IndependentFrontlineBlockRuntime.IsIndependentFrontlineBlockActive(target))
        {
            return;
        }

        IndependentFrontlineBlockRuntime.PushTarget(target);
        Log.Info($"[ParallelTurnPvp] Registered frontline block target. frontline={target} hero={target.PetOwner!.Creature} amount={amount}");
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.DamageBlockInternal))]
public static class ParallelTurnConsumeIndependentFrontlineBlockPatch
{
    static bool Prefix(Creature __instance, decimal amount, ValueProp props, ref decimal __result)
    {
        if (IndependentFrontlineBlockRuntime.ShouldBypass(__instance))
        {
            return true;
        }

        Creature? frontline = IndependentFrontlineBlockRuntime.PeekTarget();
        if (frontline?.PetOwner?.Creature != __instance)
        {
            return true;
        }

        IndependentFrontlineBlockRuntime.SetBypass(frontline);
        try
        {
            __result = frontline.DamageBlockInternal(amount, props);
        }
        finally
        {
            IndependentFrontlineBlockRuntime.SetBypass(null);
            IndependentFrontlineBlockRuntime.PopTarget(frontline);
        }

        Log.Info($"[ParallelTurnPvp] Redirected block consumption from hero {__instance} to frontline {frontline}. blocked={__result} amount={amount}");
        return false;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.ModifyUnblockedDamageTarget))]
public static class ParallelTurnIndependentFrontlineBlockCleanupPatch
{
    static void Prefix(Creature originalTarget)
    {
        Creature? frontline = IndependentFrontlineBlockRuntime.PeekTarget();
        if (frontline != originalTarget)
        {
            return;
        }

        IndependentFrontlineBlockRuntime.PopTarget(originalTarget);
        Log.Warn($"[ParallelTurnPvp] Cleared stale frontline block interception for {originalTarget} before target redirection.");
    }
}

[HarmonyPatch(typeof(DieForYouPower), nameof(DieForYouPower.ModifyUnblockedDamageTarget))]
public static class ParallelTurnDisableDieForYouPatch
{
    static bool Prefix(DieForYouPower __instance, Creature target, ref Creature __result)
    {
        if (!ParallelTurnPatchContext.IsActiveDebugArena())
        {
            return true;
        }

        __result = target;
        Log.Info($"[ParallelTurnPvp] Disabled DieForYou redirect on {__instance.Owner} for target {target}.");
        return false;
    }
}

[HarmonyPatch(typeof(NCreature), nameof(NCreature.TrackBlockStatus))]
public static class ParallelTurnDisableSharedFrontlineBlockTrackingPatch
{
    static bool Prefix(NCreature __instance, Creature creature)
    {
        if (!ParallelTurnPatchContext.IsActiveDebugArena())
        {
            return true;
        }

        Creature entity = __instance.Entity;
        if (entity.PetOwner == null || creature != entity.PetOwner.Creature)
        {
            return true;
        }

        Log.Info($"[ParallelTurnPvp] Disabled shared block tracking for frontline {entity} from hero {creature}.");
        return false;
    }
}
