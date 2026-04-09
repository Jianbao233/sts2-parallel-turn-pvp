using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Runs;
using ParallelTurnPvp.Core;

namespace ParallelTurnPvp.Patches;

[HarmonyPatch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestEnqueue))]
public static class ParallelTurnRejectUndoRequestPatch
{
    static bool Prefix(GameAction action)
    {
        if (action is UndoEndPlayerTurnAction && RunManager.Instance.DebugOnlyGetState() is RunState runState && runState.Modifiers.OfType<Models.ParallelTurnPvpDebugModifier>().Any())
        {
            Log.Info("[ParallelTurnPvp] Rejected UndoEndPlayerTurnAction before enqueue. End turn is final in debug arena.");
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(PlayCardAction), "ExecuteAction")]
public static class TrackPlayedCardsPatch
{
    static void Postfix(PlayCardAction __instance)
    {
        try
        {
            if (__instance.PlayerChoiceContext == null || __instance.Player.RunState is not RunState runState || !RunManager.Instance.IsInProgress)
            {
                return;
            }

            PvpNetBridge.EnsureRegistered();
            var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
            Creature? target = __instance.Target;
            runtime.AppendAction(new PvpAction
            {
                ActorPlayerId = __instance.Player.NetId,
                RoundIndex = runtime.CurrentRound.RoundIndex,
                Sequence = runtime.GetNextSequence(__instance.Player.NetId),
                ActionType = PvpActionType.PlayCard,
                ModelEntry = __instance.CardModelId.Entry,
                Target = ParallelTurnFrontlineHelper.CreateTargetRef(__instance.Player, target)
            });
        }
        catch (Exception ex)
        {
            Log.Error($"[ParallelTurnPvp] TrackPlayedCardsPatch failed: {ex}");
        }
    }
}

[HarmonyPatch(typeof(UsePotionAction), "ExecuteAction")]
public static class TrackPotionUsagePatch
{
    static void Postfix(UsePotionAction __instance)
    {
        try
        {
            if (__instance.PlayerChoiceContext == null || __instance.Player.RunState is not RunState runState || !RunManager.Instance.IsInProgress)
            {
                return;
            }

            PvpNetBridge.EnsureRegistered();
            CombatState? combatState = __instance.Player.Creature.CombatState;
            Creature? target = combatState?.GetCreature(__instance.TargetId) ?? __instance.Player.Creature;
            var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
            var potion = __instance.Player.GetPotionAtSlotIndex((int)__instance.PotionIndex);
            string modelEntry = potion?.Id.Entry ?? $"POTION_SLOT_{__instance.PotionIndex}";

            runtime.AppendAction(new PvpAction
            {
                ActorPlayerId = __instance.Player.NetId,
                RoundIndex = runtime.CurrentRound.RoundIndex,
                Sequence = runtime.GetNextSequence(__instance.Player.NetId),
                ActionType = PvpActionType.UsePotion,
                ModelEntry = modelEntry,
                Target = ParallelTurnFrontlineHelper.CreateTargetRef(__instance.Player, target)
            });
        }
        catch (Exception ex)
        {
            Log.Error($"[ParallelTurnPvp] TrackPotionUsagePatch failed: {ex}");
        }
    }
}

[HarmonyPatch(typeof(EndPlayerTurnAction), "ExecuteAction")]
public static class TrackEndTurnPatch
{
    static void Postfix(EndPlayerTurnAction __instance)
    {
        try
        {
            if (RunManager.Instance.DebugOnlyGetState() is RunState runState)
            {
                PvpNetBridge.EnsureRegistered();
                PvpRuntimeRegistry.GetOrCreate(runState).LockPlayer(__instance.OwnerId);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[ParallelTurnPvp] TrackEndTurnPatch failed: {ex}");
        }
    }
}

[HarmonyPatch(typeof(UndoEndPlayerTurnAction), "ExecuteAction")]
public static class TrackUndoEndTurnPatch
{
    static bool Prefix(UndoEndPlayerTurnAction __instance)
    {
        if (RunManager.Instance.DebugOnlyGetState() is RunState runState && runState.Modifiers.OfType<Models.ParallelTurnPvpDebugModifier>().Any())
        {
            Log.Info($"[ParallelTurnPvp] Ignored undo end turn request for {__instance.OwnerId}. End turn is final in debug arena.");
            return false;
        }

        return true;
    }

    static void Postfix(UndoEndPlayerTurnAction __instance)
    {
        try
        {
            if (RunManager.Instance.DebugOnlyGetState() is RunState runState && !runState.Modifiers.OfType<Models.ParallelTurnPvpDebugModifier>().Any())
            {
                PvpNetBridge.EnsureRegistered();
                PvpRuntimeRegistry.GetOrCreate(runState).UnlockPlayer(__instance.OwnerId);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[ParallelTurnPvp] TrackUndoEndTurnPatch failed: {ex}");
        }
    }
}
