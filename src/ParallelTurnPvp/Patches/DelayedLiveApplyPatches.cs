using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Potions;
using ParallelTurnPvp.Core;

namespace ParallelTurnPvp.Patches;

[HarmonyPatch(typeof(DefendNecrobinder), "OnPlay")]
public static class ParallelTurnDelayDefendOnPlayPatch
{
    static bool Prefix(DefendNecrobinder __instance, ref Task __result, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        _ = choiceContext;
        _ = cardPlay;
        if (!ParallelTurnPatchContext.IsActiveDebugArena() || !PvpDelayedExecution.ShouldDelayLiveApply(__instance.Owner, __instance.Id.Entry))
        {
            return true;
        }

        Log.Info($"[ParallelTurnPvp] 延迟拦截：{__instance.Id.Entry} 即时效果已跳过，等待回合结算统一生效。");
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(Afterlife), "OnPlay")]
public static class ParallelTurnDelayAfterlifeOnPlayPatch
{
    static bool Prefix(Afterlife __instance, ref Task __result, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        _ = choiceContext;
        _ = cardPlay;
        if (!ParallelTurnPatchContext.IsActiveDebugArena() || !PvpDelayedExecution.ShouldDelayLiveApply(__instance.Owner, __instance.Id.Entry))
        {
            return true;
        }

        Log.Info($"[ParallelTurnPvp] 延迟拦截：{__instance.Id.Entry} 即时效果已跳过，等待回合结算统一生效。");
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(StrikeNecrobinder), "OnPlay")]
public static class ParallelTurnDelayStrikeOnPlayPatch
{
    static bool Prefix(StrikeNecrobinder __instance, ref Task __result, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        _ = choiceContext;
        _ = cardPlay;
        if (!ParallelTurnPatchContext.IsActiveDebugArena() || !PvpDelayedExecution.ShouldDelayLiveApply(__instance.Owner, __instance.Id.Entry))
        {
            return true;
        }

        Log.Info($"[ParallelTurnPvp] 延迟拦截：{__instance.Id.Entry} 即时效果已跳过，等待回合结算统一生效。");
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(Poke), "OnPlay")]
public static class ParallelTurnDelayPokeOnPlayPatch
{
    static bool Prefix(Poke __instance, ref Task __result, PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        _ = choiceContext;
        _ = cardPlay;
        if (!ParallelTurnPatchContext.IsActiveDebugArena() || !PvpDelayedExecution.ShouldDelayLiveApply(__instance.Owner, __instance.Id.Entry))
        {
            return true;
        }

        Log.Info($"[ParallelTurnPvp] 延迟拦截：{__instance.Id.Entry} 即时效果已跳过，等待回合结算统一生效。");
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(BlockPotion), "OnUse")]
public static class ParallelTurnDelayBlockPotionOnUsePatch
{
    static bool Prefix(BlockPotion __instance, ref Task __result, PlayerChoiceContext choiceContext, Creature? target)
    {
        _ = choiceContext;
        _ = target;
        if (!ParallelTurnPatchContext.IsActiveDebugArena() || !PvpDelayedExecution.ShouldDelayLiveApply(__instance.Owner, __instance.Id.Entry))
        {
            return true;
        }

        Log.Info($"[ParallelTurnPvp] 延迟拦截：{__instance.Id.Entry} 即时效果已跳过，等待回合结算统一生效。");
        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(BloodPotion), "OnUse")]
public static class ParallelTurnDelayBloodPotionOnUsePatch
{
    static bool Prefix(BloodPotion __instance, ref Task __result, PlayerChoiceContext choiceContext, Creature? target)
    {
        _ = choiceContext;
        _ = target;
        if (!ParallelTurnPatchContext.IsActiveDebugArena() || !PvpDelayedExecution.ShouldDelayLiveApply(__instance.Owner, __instance.Id.Entry))
        {
            return true;
        }

        Log.Info($"[ParallelTurnPvp] 延迟拦截：{__instance.Id.Entry} 即时效果已跳过，等待回合结算统一生效。");
        __result = Task.CompletedTask;
        return false;
    }
}
