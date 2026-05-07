using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using ParallelTurnPvp.Core;
using System.Text.RegularExpressions;
using System.Reflection;

namespace ParallelTurnPvp.Patches;

internal static class RoundTrackingTargetResolver
{
    public static PvpTargetRef ResolveCardTarget(PlayCardAction action)
    {
        Creature? explicitTarget = action.Target;
        if (explicitTarget != null)
        {
            return ParallelTurnFrontlineHelper.CreateTargetRef(action.Player, explicitTarget);
        }

        return action.CardModelId.Entry switch
        {
            "FRONTLINE_BRACE" => ResolveSelfFrontlinePreferredTarget(action.Player),
            _ => ParallelTurnFrontlineHelper.CreateTargetRef(action.Player, null)
        };
    }

    public static PvpTargetRef ResolvePotionTarget(Player player, string modelEntry, Creature? explicitTarget)
    {
        if (explicitTarget != null)
        {
            return ParallelTurnFrontlineHelper.CreateTargetRef(player, explicitTarget);
        }

        return modelEntry switch
        {
            "FRONTLINE_SALVE" => ResolveSelfFrontlinePreferredTarget(player),
            _ => ParallelTurnFrontlineHelper.CreateTargetRef(player, null)
        };
    }

    private static PvpTargetRef ResolveSelfFrontlinePreferredTarget(Player player)
    {
        Creature? frontline = ParallelTurnFrontlineHelper.GetFrontline(player);
        return new PvpTargetRef
        {
            OwnerPlayerId = player.NetId,
            Kind = frontline != null ? PvpTargetKind.SelfFrontline : PvpTargetKind.SelfHero
        };
    }
}

internal static class RoundTrackingMatchGuard
{
    public static bool IsEnded(RunState runState)
    {
        return ParallelTurnMatchStateRegistry.IsEnded(runState);
    }
}

internal static class RoundTrackingSubmissionModeGuard
{
    public static bool ShouldTrackLiveAction(PvpMatchRuntime runtime, ulong actorPlayerId, string actionTag)
    {
        if (RunManager.Instance.NetService.Type == NetGameType.Host &&
            ParallelTurnFrontlineHelper.IsSplitRoomActive(runtime.RunState) &&
            actorPlayerId != runtime.RoomSession.LocalPlayerId)
        {
            Log.Info($"[ParallelTurnPvp] SplitRoom host ignored live {actionTag} from remote actor {actorPlayerId}; relying on submission channel.");
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestEnqueue))]
public static class ParallelTurnRejectUndoRequestPatch
{
    static bool Prefix(GameAction action)
    {
        if (RunManager.Instance.DebugOnlyGetState() is RunState runState &&
            runState.Modifiers.OfType<Models.ParallelTurnPvpDebugModifier>().Any())
        {
            if (PvpRuntimeRegistry.TryGet(runState) is { } runtime && runtime.IsDisconnectedPendingResume)
            {
                ulong localPlayerId = runtime.RoomSession.LocalPlayerId;
                bool reject = action switch
                {
                    PlayCardAction playCardAction => playCardAction.Player?.NetId == localPlayerId,
                    UsePotionAction usePotionAction => usePotionAction.OwnerId == localPlayerId,
                    EndPlayerTurnAction endPlayerTurnAction => endPlayerTurnAction.OwnerId == localPlayerId,
                    UndoEndPlayerTurnAction undoEndPlayerTurnAction => undoEndPlayerTurnAction.OwnerId == localPlayerId,
                    _ => false
                };

                if (reject)
                {
                    Log.Warn($"[ParallelTurnPvp] Rejected local action enqueue while disconnected pending resume. action={action.GetType().Name} local={localPlayerId} reason={runtime.DisconnectReason}");
                    return false;
                }
            }

            if (action is UndoEndPlayerTurnAction)
            {
                Log.Info("[ParallelTurnPvp] Rejected UndoEndPlayerTurnAction before enqueue. End turn is final in debug arena.");
                return false;
            }

            if (action is EndPlayerTurnAction endTurnAction)
            {
                TryAlignRequestEndTurnRound(runState, endTurnAction);
            }
        }

        return true;
    }

    private static void TryAlignRequestEndTurnRound(RunState runState, EndPlayerTurnAction action)
    {
        if (!ParallelTurnFrontlineHelper.IsSplitRoomActive(runState))
        {
            return;
        }

        CombatState? combatState = runState.Players.FirstOrDefault(player => player.NetId == action.OwnerId)?.Creature.CombatState;
        if (combatState == null)
        {
            return;
        }

        int liveRound = Math.Max(1, combatState.RoundNumber);
        int currentRound = ReadEndTurnRound(action);
        int targetRound = ResolvePreferredEndTurnRound(runState, liveRound);
        if (currentRound == targetRound)
        {
            return;
        }

        if (!WriteEndTurnRound(action, targetRound))
        {
            return;
        }

        Log.Warn($"[ParallelTurnPvp] Aligned EndTurnAction round before request enqueue. owner={action.OwnerId} from={currentRound} to={targetRound} liveRound={liveRound}");
    }

    private static int ResolvePreferredEndTurnRound(RunState runState, int liveRound)
    {
        if (RunManager.Instance.NetService.Type == NetGameType.Client &&
            PvpRuntimeRegistry.TryGet(runState) is { } runtime &&
            runtime.CurrentRound.RoundIndex > 0)
        {
            int authoritativeRound = runtime.CurrentRound.RoundIndex;
            // EndTurnAction is gated by vanilla live combat round number.
            // Prefer the larger one to avoid "Ignoring end turn action" hard drops.
            return Math.Max(authoritativeRound, liveRound);
        }

        return liveRound;
    }

    private static int ReadEndTurnRound(EndPlayerTurnAction action)
    {
        try
        {
            var getter = AccessTools.PropertyGetter(action.GetType(), "RoundNumber");
            if (getter?.Invoke(action, null) is int fromProperty)
            {
                return fromProperty;
            }
        }
        catch
        {
        }

        foreach (string fieldName in new[] { "<RoundNumber>k__BackingField", "_roundNumber", "roundNumber" })
        {
            try
            {
                var field = AccessTools.Field(action.GetType(), fieldName);
                if (field?.GetValue(action) is int fromField)
                {
                    return fromField;
                }
            }
            catch
            {
            }
        }

        return 0;
    }

    private static bool WriteEndTurnRound(EndPlayerTurnAction action, int round)
    {
        bool assigned = false;
        try
        {
            var setter = AccessTools.PropertySetter(action.GetType(), "RoundNumber");
            if (setter != null)
            {
                setter.Invoke(action, new object[] { round });
                assigned = true;
            }
        }
        catch
        {
        }

        foreach (string fieldName in new[] { "<RoundNumber>k__BackingField", "_roundNumber", "roundNumber" })
        {
            try
            {
                var field = AccessTools.Field(action.GetType(), fieldName);
                if (field == null)
                {
                    continue;
                }

                field.SetValue(action, round);
                assigned = true;
            }
            catch
            {
            }
        }

        return assigned;
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

            if (RoundTrackingMatchGuard.IsEnded(runState))
            {
                return;
            }

            PvpNetBridge.EnsureRegistered();
            var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
            if (!RoundTrackingSubmissionModeGuard.ShouldTrackLiveAction(runtime, __instance.Player.NetId, "PlayCard"))
            {
                return;
            }

            runtime.AppendAction(new PvpAction
            {
                ActorPlayerId = __instance.Player.NetId,
                RoundIndex = runtime.CurrentRound.RoundIndex,
                Sequence = runtime.GetNextSequence(__instance.Player.NetId),
                RuntimeActionId = __instance.Id,
                ActionType = PvpActionType.PlayCard,
                ModelEntry = __instance.CardModelId.Entry,
                Target = RoundTrackingTargetResolver.ResolveCardTarget(__instance)
            });
            PvpPlanningFrame frame = runtime.BuildPlanningFrame();
            PvpNetBridge bridge = new();
            bridge.BroadcastPlanningFrame(frame);
            if (RunManager.Instance.NetService.Type == NetGameType.Client &&
                __instance.Player.NetId == runtime.RoomSession.LocalPlayerId)
            {
                bridge.SendClientSubmission(frame, runtime.RoomSession.LocalPlayerId);
            }
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
    private static readonly Regex PotionModelRegex = new(@"POTION\.([A-Z0-9_]+)", RegexOptions.Compiled);

    static void Prefix(UsePotionAction __instance, out string? __state)
    {
        __state = null;

        try
        {
            __state = __instance.Player.GetPotionAtSlotIndex((int)__instance.PotionIndex)?.Id.Entry;
            if (!string.IsNullOrWhiteSpace(__state))
            {
                return;
            }

            __state = TryExtractPotionModelEntry(__instance.ToString());
        }
        catch (Exception ex)
        {
            Log.Warn($"[ParallelTurnPvp] 药水追踪前置读取失败：owner={__instance.OwnerId} slot={__instance.PotionIndex} error={ex.Message}");
        }
    }

    static void Postfix(UsePotionAction __instance, string? __state)
    {
        try
        {
            if (__instance.PlayerChoiceContext == null || __instance.Player.RunState is not RunState runState || !RunManager.Instance.IsInProgress)
            {
                return;
            }

            if (RoundTrackingMatchGuard.IsEnded(runState))
            {
                return;
            }

            PvpNetBridge.EnsureRegistered();
            CombatState? combatState = __instance.Player.Creature.CombatState;
            Creature? explicitTarget = __instance.TargetId.HasValue
                ? combatState?.GetCreature(__instance.TargetId.Value)
                : null;
            var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
            if (!RoundTrackingSubmissionModeGuard.ShouldTrackLiveAction(runtime, __instance.Player.NetId, "UsePotion"))
            {
                return;
            }

            string modelEntry = ResolvePotionModelEntry(__instance, __state);
            PvpTargetRef resolvedTarget = RoundTrackingTargetResolver.ResolvePotionTarget(__instance.Player, modelEntry, explicitTarget);
            Log.Info($"[ParallelTurnPvp] 药水动作追踪：owner={__instance.Player.NetId} actionId={__instance.Id} model={modelEntry} slot={__instance.PotionIndex} targetKind={resolvedTarget.Kind} targetOwner={resolvedTarget.OwnerPlayerId}");

            runtime.AppendAction(new PvpAction
            {
                ActorPlayerId = __instance.Player.NetId,
                RoundIndex = runtime.CurrentRound.RoundIndex,
                Sequence = runtime.GetNextSequence(__instance.Player.NetId),
                RuntimeActionId = __instance.Id,
                ActionType = PvpActionType.UsePotion,
                ModelEntry = modelEntry,
                Target = resolvedTarget
            });
            PvpPlanningFrame frame = runtime.BuildPlanningFrame();
            PvpNetBridge bridge = new();
            bridge.BroadcastPlanningFrame(frame);
            if (RunManager.Instance.NetService.Type == NetGameType.Client &&
                __instance.Player.NetId == runtime.RoomSession.LocalPlayerId)
            {
                bridge.SendClientSubmission(frame, runtime.RoomSession.LocalPlayerId);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[ParallelTurnPvp] TrackPotionUsagePatch failed: {ex}");
        }
    }

    private static string ResolvePotionModelEntry(UsePotionAction action, string? preExecuteModelEntry)
    {
        if (!string.IsNullOrWhiteSpace(preExecuteModelEntry))
        {
            return preExecuteModelEntry;
        }

        string? fromText = TryExtractPotionModelEntry(action.ToString());
        if (!string.IsNullOrWhiteSpace(fromText))
        {
            Log.Warn($"[ParallelTurnPvp] 药水追踪回退：前置模型缺失，使用动作文本模型。owner={action.OwnerId} slot={action.PotionIndex} model={fromText}");
            return fromText;
        }

        string? liveModelEntry = action.Player.GetPotionAtSlotIndex((int)action.PotionIndex)?.Id.Entry;
        if (!string.IsNullOrWhiteSpace(liveModelEntry))
        {
            Log.Warn($"[ParallelTurnPvp] 药水追踪回退：前置模型缺失，使用后置槽位模型。owner={action.OwnerId} slot={action.PotionIndex} model={liveModelEntry}");
            return liveModelEntry;
        }

        string fallback = $"POTION_SLOT_{action.PotionIndex}";
        Log.Warn($"[ParallelTurnPvp] 药水追踪异常：无法解析药水模型，使用槽位占位符。owner={action.OwnerId} slot={action.PotionIndex} fallback={fallback}");
        return fallback;
    }

    private static string? TryExtractPotionModelEntry(string actionText)
    {
        if (string.IsNullOrWhiteSpace(actionText))
        {
            return null;
        }

        Match match = PotionModelRegex.Match(actionText);
        if (!match.Success || match.Groups.Count < 2)
        {
            return null;
        }

        string modelEntry = match.Groups[1].Value;
        return string.IsNullOrWhiteSpace(modelEntry) ? null : modelEntry;
    }
}

[HarmonyPatch(typeof(EndPlayerTurnAction), "ExecuteAction")]
public static class TrackEndTurnPatch
{
    private static bool _loggedEndTurnRoundMemberMap;
    private static bool _loggedEndTurnRoundAlignFailure;

    static void Prefix(EndPlayerTurnAction __instance)
    {
        try
        {
            if (RunManager.Instance.DebugOnlyGetState() is not RunState runState ||
                !runState.Modifiers.OfType<Models.ParallelTurnPvpDebugModifier>().Any() ||
                !ParallelTurnFrontlineHelper.IsSplitRoomActive(runState))
            {
                return;
            }

            CombatState? combatState = runState.Players.FirstOrDefault(player => player.NetId == __instance.OwnerId)?.Creature.CombatState;
            if (combatState == null)
            {
                return;
            }

            int liveRound = Math.Max(1, combatState.RoundNumber);
            int targetRound = ResolvePreferredExecuteEndTurnRound(runState, liveRound);
            bool hasActionRound = TryGetActionRoundNumber(__instance, out int actionRound);
            if (hasActionRound && actionRound == targetRound)
            {
                return;
            }

            if (TrySetActionRoundNumber(__instance, targetRound))
            {
                int fromRound = hasActionRound ? actionRound : -1;
                Log.Warn($"[ParallelTurnPvp] Aligned EndTurnAction round number before execute. owner={__instance.OwnerId} from={fromRound} to={targetRound} liveRound={liveRound}");
            }
            else if (!_loggedEndTurnRoundAlignFailure)
            {
                _loggedEndTurnRoundAlignFailure = true;
                LogRoundMembersOnce(__instance.GetType());
                int fromRound = hasActionRound ? actionRound : -1;
                Log.Warn($"[ParallelTurnPvp] Failed to align EndTurnAction round number. owner={__instance.OwnerId} from={fromRound} target={targetRound} liveRound={liveRound}");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[ParallelTurnPvp] EndTurnAction round align prefix failed: {ex.Message}");
        }
    }

    static void Postfix(EndPlayerTurnAction __instance)
    {
        try
        {
            if (RunManager.Instance.DebugOnlyGetState() is RunState runState)
            {
                if (RoundTrackingMatchGuard.IsEnded(runState))
                {
                    return;
                }

                PvpNetBridge.EnsureRegistered();
                var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
                if (!RoundTrackingSubmissionModeGuard.ShouldTrackLiveAction(runtime, __instance.OwnerId, "EndTurn"))
                {
                    return;
                }

                runtime.AppendAction(new PvpAction
                {
                    ActorPlayerId = __instance.OwnerId,
                    RoundIndex = runtime.CurrentRound.RoundIndex,
                    Sequence = runtime.GetNextSequence(__instance.OwnerId),
                    RuntimeActionId = __instance.Id,
                    ActionType = PvpActionType.EndRound,
                    ModelEntry = "END_TURN",
                    Target = new PvpTargetRef
                    {
                        OwnerPlayerId = __instance.OwnerId,
                        Kind = PvpTargetKind.None
                    }
                });
                bool lockApplied = runtime.LockPlayer(__instance.OwnerId);
                if (!lockApplied)
                {
                    return;
                }

                PvpPlanningFrame frame = runtime.BuildPlanningFrame();
                PvpNetBridge bridge = new();
                bridge.BroadcastPlanningFrame(frame);
                if (RunManager.Instance.NetService.Type == NetGameType.Client &&
                    __instance.OwnerId == runtime.RoomSession.LocalPlayerId)
                {
                    bridge.SendClientSubmission(frame, runtime.RoomSession.LocalPlayerId);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[ParallelTurnPvp] TrackEndTurnPatch failed: {ex}");
        }
    }

    private static bool TryGetActionRoundNumber(EndPlayerTurnAction action, out int roundNumber)
    {
        roundNumber = 0;
        try
        {
            var getter = AccessTools.PropertyGetter(action.GetType(), "RoundNumber");
            if (getter != null && getter.Invoke(action, null) is int fromProperty)
            {
                roundNumber = fromProperty;
                return true;
            }
        }
        catch
        {
        }

        foreach (string fieldName in new[] { "<RoundNumber>k__BackingField", "_roundNumber", "roundNumber" })
        {
            try
            {
                var field = AccessTools.Field(action.GetType(), fieldName);
                if (field != null && field.GetValue(action) is int fromField)
                {
                    roundNumber = fromField;
                    return true;
                }
            }
            catch
            {
            }
        }

        foreach (PropertyInfo property in EnumerateRoundIntProperties(action.GetType()))
        {
            try
            {
                if (property.GetValue(action) is int fromProperty && fromProperty > 0)
                {
                    roundNumber = fromProperty;
                    return true;
                }
            }
            catch
            {
            }
        }

        foreach (FieldInfo field in EnumerateRoundIntFields(action.GetType()))
        {
            try
            {
                if (field.GetValue(action) is int fromField && fromField > 0)
                {
                    roundNumber = fromField;
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TrySetActionRoundNumber(EndPlayerTurnAction action, int roundNumber)
    {
        bool assigned = false;
        try
        {
            var setter = AccessTools.PropertySetter(action.GetType(), "RoundNumber");
            if (setter != null)
            {
                setter.Invoke(action, new object[] { roundNumber });
                assigned = true;
            }
        }
        catch
        {
        }

        foreach (string fieldName in new[] { "<RoundNumber>k__BackingField", "_roundNumber", "roundNumber" })
        {
            try
            {
                var field = AccessTools.Field(action.GetType(), fieldName);
                if (field == null)
                {
                    continue;
                }

                field.SetValue(action, roundNumber);
                assigned = true;
            }
            catch
            {
            }
        }

        foreach (PropertyInfo property in EnumerateRoundIntProperties(action.GetType()))
        {
            try
            {
                property.SetValue(action, roundNumber);
                assigned = true;
            }
            catch
            {
            }
        }

        foreach (FieldInfo field in EnumerateRoundIntFields(action.GetType()))
        {
            try
            {
                field.SetValue(action, roundNumber);
                assigned = true;
            }
            catch
            {
            }
        }

        return assigned;
    }

    private static int ResolvePreferredExecuteEndTurnRound(RunState runState, int liveRound)
    {
        if (RunManager.Instance.NetService.Type == NetGameType.Client &&
            PvpRuntimeRegistry.TryGet(runState) is { } runtime &&
            runtime.CurrentRound.RoundIndex > 0)
        {
            int authoritativeRound = runtime.CurrentRound.RoundIndex;
            // EndTurnAction is gated by vanilla live combat round number.
            // Prefer the larger one to avoid "Ignoring end turn action" hard drops.
            return Math.Max(authoritativeRound, liveRound);
        }

        return liveRound;
    }

    private static IEnumerable<PropertyInfo> EnumerateRoundIntProperties(Type type)
    {
        for (Type? cursor = type; cursor != null; cursor = cursor.BaseType)
        {
            foreach (PropertyInfo property in cursor.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (property.PropertyType != typeof(int) ||
                    !property.CanRead ||
                    !property.CanWrite ||
                    property.Name.IndexOf("round", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                yield return property;
            }
        }
    }

    private static IEnumerable<FieldInfo> EnumerateRoundIntFields(Type type)
    {
        for (Type? cursor = type; cursor != null; cursor = cursor.BaseType)
        {
            foreach (FieldInfo field in cursor.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType != typeof(int) ||
                    field.Name.IndexOf("round", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                yield return field;
            }
        }
    }

    private static void LogRoundMembersOnce(Type actionType)
    {
        if (_loggedEndTurnRoundMemberMap)
        {
            return;
        }

        _loggedEndTurnRoundMemberMap = true;
        string properties = string.Join(", ", EnumerateRoundIntProperties(actionType).Select(property => property.Name));
        string fields = string.Join(", ", EnumerateRoundIntFields(actionType).Select(field => field.Name));
        Log.Warn($"[ParallelTurnPvp] EndTurnAction round-member map. type={actionType.FullName} properties=[{properties}] fields=[{fields}]");
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
                var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
                runtime.UnlockPlayer(__instance.OwnerId);
                new PvpNetBridge().BroadcastPlanningFrame(runtime.BuildPlanningFrame());
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[ParallelTurnPvp] TrackUndoEndTurnPatch failed: {ex}");
        }
    }
}
