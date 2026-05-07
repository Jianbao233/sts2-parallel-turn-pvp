using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ParallelTurnPvp.Core;
using ParallelTurnPvp.Models;
using ParallelTurnPvp.Ui;
using System.Runtime.CompilerServices;

namespace ParallelTurnPvp.Patches;

internal static class ParallelTurnPatchContext
{
    public static bool IsActiveDebugArena()
    {
        return RunManager.Instance.DebugOnlyGetState() is RunState runState && runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any();
    }
}

internal static class ParallelTurnMatchStateRegistry
{
    private sealed class MatchState
    {
        public bool Ended { get; set; }
    }

    private static readonly ConditionalWeakTable<RunState, MatchState> StateTable = new();

    public static void MarkStarted(RunState runState)
    {
        StateTable.GetOrCreateValue(runState).Ended = false;
    }

    public static void MarkEnded(RunState runState)
    {
        StateTable.GetOrCreateValue(runState).Ended = true;
    }

    public static bool IsEnded(RunState runState)
    {
        if (runState == null)
        {
            return false;
        }

        if (StateTable.TryGetValue(runState, out MatchState? state) && state.Ended)
        {
            return true;
        }

        return runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().FirstOrDefault() is { MatchEnded: true };
    }
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetUpCombat))]
public static class ParallelTurnCombatSetupPatch
{
    static void Postfix(CombatState state)
    {
        if (state.RunState is not RunState runState || !runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            return;
        }

        PvpNetBridge.EnsureRegistered();
        ParallelTurnMatchStateRegistry.MarkStarted(runState);
        // Round-1 runtime initialization is intentionally deferred to first tracked action
        // (lazy init in PvpMatchRuntime.EnsureRoundInitialized). This avoids host/client
        // timing skew during SetUpCombat where frontline summon state can differ.
        PvpRuntimeRegistry.GetOrCreate(runState);
        Log.Info("[ParallelTurnPvp] Deferred round-1 runtime init to first tracked action.");

        foreach (var player in state.Players)
        {
            var targets = ParallelTurnFrontlineHelper.GetSelectableEnemyTargets(state, player);
            Log.Info($"[ParallelTurnPvp] Debug arena combat initialized for player {player.NetId}. selectableTargets=[{string.Join(", ", targets.Select(target => target.ToString()))}]");
        }
    }
}

[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom._Ready))]
public static class ParallelTurnIntentOverlayAttachPatch
{
    static void Postfix(NCombatRoom __instance)
    {
        if (!ParallelTurnPatchContext.IsActiveDebugArena())
        {
            return;
        }

        ParallelTurnIntentOverlay.EnsureAttached(__instance);
    }
}

[HarmonyPatch(typeof(CombatManager), "SwitchSides")]
public static class ParallelTurnSwitchSidesPatch
{
    static void Prefix()
    {
        CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState == null ||
            combatState.RunState is not RunState runStateForGuard ||
            PvpRuntimeRegistry.TryGet(combatState) is not { } runtime)
        {
            return;
        }

        if (ParallelTurnMatchStateRegistry.IsEnded(runStateForGuard))
        {
            Log.Info("[ParallelTurnPvp] SwitchSides prefix skipped because match has ended.");
            return;
        }

        if (RunManager.Instance.NetService.Type == NetGameType.Client)
        {
            PvpCombatSnapshot? pendingSnapshot = runtime.ConsumePendingAuthoritativeSnapshot();
            if (pendingSnapshot != null && combatState.RunState is RunState runStateForSnapshot)
            {
                PvpNetBridge.ApplyLiveSnapshot(runStateForSnapshot, pendingSnapshot);
                Log.Info($"[ParallelTurnPvp] Applied queued authoritative snapshot in SwitchSides prefix. round={pendingSnapshot.RoundIndex} snapshotVersion={pendingSnapshot.SnapshotVersion}");
            }
        }

        if (combatState.CurrentSide != CombatSide.Player)
        {
            return;
        }

        Log.Info($"[ParallelTurnPvp] SwitchSides prefix currentSide={combatState.CurrentSide} round={combatState.RoundNumber} pvpRound={runtime.CurrentRound.RoundIndex} phase={runtime.CurrentRound.Phase} resolved={runtime.CurrentRound.HasResolved}");
        if (!runtime.CanResolveRound(combatState.RoundNumber))
        {
            Log.Info($"[ParallelTurnPvp] SwitchSides prefix skipped resolve. liveRound={combatState.RoundNumber} pvpRound={runtime.CurrentRound.RoundIndex} phase={runtime.CurrentRound.Phase} resolved={runtime.CurrentRound.HasResolved}");
            return;
        }

        if (RunManager.Instance.NetService.Type == NetGameType.Client &&
            ParallelTurnFrontlineHelper.IsSplitRoomActive(runStateForGuard))
        {
            runtime.MarkClientAwaitAuthoritativeResult(combatState.RoundNumber, runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion);
            Log.Info($"[ParallelTurnPvp] Client host-authority resolve path. Waiting for host authoritative round result. round={combatState.RoundNumber} snapshotVersion={runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion}");
            return;
        }

        int delayedApplied = PvpDelayedExecution.ApplyDelayedLiveEffects(runtime, combatState);
        if (delayedApplied > 0)
        {
            Log.Info($"[ParallelTurnPvp] SwitchSides prefix applied delayed live effects. round={combatState.RoundNumber} operations={delayedApplied}");
        }

        Player? rewardedPlayer = runtime.ApplyFirstLockRewardIfPending();
        if (rewardedPlayer != null)
        {
            Log.Info($"[ParallelTurnPvp] Pre-resolve early lock reward applied for player {rewardedPlayer.NetId} for live round {combatState.RoundNumber}.");
        }

        PvpRoundResult result = runtime.ResolveLiveRound(combatState);
        new PvpNetBridge().BroadcastRoundResult(result);
        Log.Info($"[ParallelTurnPvp] SwitchSides prefix resolved round={result.RoundIndex} snapshotVersion={result.FinalSnapshot.SnapshotVersion}");
    }

    static void Postfix()
    {
        CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState == null || combatState.RunState is not RunState runState || !runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            return;
        }

        if (ParallelTurnMatchStateRegistry.IsEnded(runState))
        {
            Log.Info("[ParallelTurnPvp] SwitchSides postfix skipped because match has ended.");
            return;
        }

        Log.Info($"[ParallelTurnPvp] SwitchSides postfix currentSide={combatState.CurrentSide} round={combatState.RoundNumber} enemies=[{string.Join(", ", combatState.Enemies.Select(enemy => enemy.ToString()))}]");
        if (combatState.CurrentSide != CombatSide.Player)
        {
            return;
        }

        var runtime = PvpRuntimeRegistry.GetOrCreate(runState);

        if (RunManager.Instance.NetService.Type == NetGameType.Client &&
            ParallelTurnFrontlineHelper.IsSplitRoomActive(runState))
        {
            Log.Info($"[ParallelTurnPvp] SwitchSides postfix skipped local round start on client host-authority mode. liveRound={combatState.RoundNumber} pvpRound={runtime.CurrentRound.RoundIndex} phase={runtime.CurrentRound.Phase}");
            return;
        }

        if (!runtime.ShouldStartRound(combatState.RoundNumber))
        {
            Log.Info($"[ParallelTurnPvp] SwitchSides postfix skipped round start. liveRound={combatState.RoundNumber} pvpRound={runtime.CurrentRound.RoundIndex} phase={runtime.CurrentRound.Phase} resolved={runtime.CurrentRound.HasResolved}");
            return;
        }

        runtime.StartRoundFromLiveState(combatState, combatState.RoundNumber);
        new PvpNetBridge().BroadcastRoundState(runtime.CurrentRound);
        new PvpNetBridge().BroadcastPlanningFrame(runtime.BuildPlanningFrame());
        Log.Info($"[ParallelTurnPvp] SwitchSides postfix started player round={runtime.CurrentRound.RoundIndex}");

        foreach (var player in combatState.Players)
        {
            var targets = ParallelTurnFrontlineHelper.GetSelectableEnemyTargets(combatState, player);
            Log.Info($"[ParallelTurnPvp] Round {combatState.RoundNumber} selectableTargets for player {player.NetId} -> [{string.Join(", ", targets.Select(target => target.ToString()))}]");
        }
    }
}

[HarmonyPatch(typeof(HoveredModelTracker), "OnPlayerStateChanged")]
public static class ParallelTurnHoveredModelTrackerGuardPatch
{
    static Exception? Finalizer(Exception? __exception, ulong playerId)
    {
        if (__exception == null)
        {
            return null;
        }

        if (RunManager.Instance.DebugOnlyGetState() is RunState runState && runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any() && __exception is ArgumentOutOfRangeException)
        {
            Log.Warn($"[ParallelTurnPvp] Suppressed HoveredModelTracker out-of-range sync for player {playerId}: {__exception.Message}");
            return null;
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(NMultiplayerPlayerIntentHandler), "_Process")]
public static class ParallelTurnRemoteIntentHandlerNullGuardPatch
{
    static Exception? Finalizer(Exception? __exception)
    {
        if (__exception == null)
        {
            return null;
        }

        if (RunManager.Instance.DebugOnlyGetState() is RunState runState &&
            runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any() &&
            ParallelTurnFrontlineHelper.IsSplitRoomActive(runState) &&
            __exception is NullReferenceException)
        {
            Log.Warn($"[ParallelTurnPvp] Suppressed remote intent handler null reference in split-room mode: {__exception.Message}");
            return null;
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(HoveredModelTracker), nameof(HoveredModelTracker.OnLocalRelicHovered))]
public static class ParallelTurnSkipRelicHoverSyncPatch
{
    static bool Prefix()
    {
        return !ParallelTurnPatchContext.IsActiveDebugArena();
    }
}

[HarmonyPatch(typeof(HoveredModelTracker), nameof(HoveredModelTracker.OnLocalRelicUnhovered))]
public static class ParallelTurnSkipRelicUnhoverSyncPatch
{
    static bool Prefix()
    {
        return !ParallelTurnPatchContext.IsActiveDebugArena();
    }
}

[HarmonyPatch(typeof(HoveredModelTracker), nameof(HoveredModelTracker.OnLocalPotionHovered))]
public static class ParallelTurnSkipPotionHoverSyncPatch
{
    static bool Prefix()
    {
        return !ParallelTurnPatchContext.IsActiveDebugArena();
    }
}

[HarmonyPatch(typeof(HoveredModelTracker), nameof(HoveredModelTracker.OnLocalPotionUnhovered))]
public static class ParallelTurnSkipPotionUnhoverSyncPatch
{
    static bool Prefix()
    {
        return !ParallelTurnPatchContext.IsActiveDebugArena();
    }
}

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.CheckWinCondition))]
public static class ParallelTurnCustomWinConditionPatch
{
    static bool Prefix(CombatManager __instance, ref Task<bool> __result)
    {
        if (__instance.DebugOnlyGetState() is not CombatState combatState || combatState.RunState is not RunState runState)
        {
            return true;
        }

        if (runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().FirstOrDefault() is not { } modifier)
        {
            return true;
        }

        if (ParallelTurnMatchStateRegistry.IsEnded(runState))
        {
            __result = Task.FromResult(true);
            return false;
        }

        IReadOnlyList<Player> alivePlayers = combatState.Players.Where(player => player.Creature.IsAlive).ToList();
        if (alivePlayers.Count > 1)
        {
            return true;
        }

        ulong winnerNetId = alivePlayers.Count == 1 ? alivePlayers[0].NetId : 0UL;
        __result = ParallelTurnMatchEndFlow.EndMatchAsync(runState, modifier, winnerNetId, combatState.RoundNumber);
        return false;
    }
}

[HarmonyPatch(typeof(CombatManager), "SwitchFromPlayerToEnemySide")]
public static class ParallelTurnSkipEnemyTurnAfterMatchEndPatch
{
    static bool Prefix(CombatManager __instance, ref Task __result)
    {
        if (__instance.DebugOnlyGetState() is not CombatState combatState ||
            combatState.RunState is not RunState runState ||
            !runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any() ||
            !ParallelTurnMatchStateRegistry.IsEnded(runState))
        {
            return true;
        }

        __result = Task.CompletedTask;
        Log.Info("[ParallelTurnPvp] Skipped SwitchFromPlayerToEnemySide after match end.");
        return false;
    }
}

[HarmonyPatch(typeof(CombatManager), "StartTurn")]
public static class ParallelTurnSuppressMatchEndStartTurnErrorPatch
{
    private static bool IsDebugArenaRun(CombatManager manager, out RunState? runState)
    {
        runState = null;
        if (manager.DebugOnlyGetState() is not CombatState combatState ||
            combatState.RunState is not RunState state ||
            !state.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            return false;
        }

        runState = state;
        return true;
    }

    static bool Prefix(CombatManager __instance, ref Task __result)
    {
        if (!IsDebugArenaRun(__instance, out RunState? runState) ||
            runState == null ||
            !ParallelTurnMatchStateRegistry.IsEnded(runState))
        {
            return true;
        }

        __result = Task.CompletedTask;
        Log.Info("[ParallelTurnPvp] Skipped StartTurn after match end.");
        return false;
    }

    static Exception? Finalizer(CombatManager __instance, Exception? __exception)
    {
        if (__exception == null)
        {
            return null;
        }

        if (!IsDebugArenaRun(__instance, out RunState? runState) || runState == null)
        {
            return __exception;
        }

        if (__exception is InvalidOperationException && __exception.Message.Contains("Nullable object", StringComparison.OrdinalIgnoreCase))
        {
            bool matchEnded = ParallelTurnMatchStateRegistry.IsEnded(runState);
            Log.Warn($"[ParallelTurnPvp] Suppressed StartTurn nullable exception in debug arena. matchEnded={matchEnded} message={__exception.Message}");
            return null;
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(CombatManager), "SetupPlayerTurn")]
public static class ParallelTurnSkipSetupPlayerTurnAfterMatchEndPatch
{
    static bool Prefix(CombatManager __instance, ref Task __result)
    {
        if (__instance.DebugOnlyGetState() is not CombatState combatState ||
            combatState.RunState is not RunState runState ||
            !runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any() ||
            !ParallelTurnMatchStateRegistry.IsEnded(runState))
        {
            return true;
        }

        __result = Task.CompletedTask;
        Log.Info("[ParallelTurnPvp] Skipped SetupPlayerTurn after match end.");
        return false;
    }
}

[HarmonyPatch(typeof(CombatState), nameof(CombatState.GetOpponentsOf))]
public static class ParallelTurnGetOpponentsPatch
{
    static void Postfix(CombatState __instance, Creature creature, ref IReadOnlyList<Creature> __result)
    {
        if (__instance.RunState is not RunState runState || !runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            return;
        }

        var viewer = ParallelTurnFrontlineHelper.GetOwner(creature);
        if (viewer == null)
        {
            return;
        }

        __result = ParallelTurnFrontlineHelper.GetSelectableEnemyTargets(__instance, viewer);
        Log.Info($"[ParallelTurnPvp] GetOpponentsOf {creature} -> [{string.Join(", ", __result.Select(opponent => opponent.ToString()))}]");
    }
}

[HarmonyPatch(typeof(CombatState), "get_HittableEnemies")]
public static class ParallelTurnHittableEnemiesPatch
{
    static void Postfix(CombatState __instance, ref IReadOnlyList<Creature> __result)
    {
        if (__instance.RunState is not RunState runState || !runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            return;
        }

        var me = LocalContext.GetMe(__instance.RunState);
        if (me == null)
        {
            return;
        }

        __result = ParallelTurnFrontlineHelper.GetSelectableEnemyTargets(__instance, me)
            .Where(creature => creature.IsAlive && creature.IsHittable)
            .ToList();

        Log.Info($"[ParallelTurnPvp] HittableEnemies -> [{string.Join(", ", __result.Select(enemy => enemy.ToString()))}]");
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.IsValidTarget))]
public static class ParallelTurnIsValidTargetPatch
{
    static void Postfix(CardModel __instance, Creature? target, ref bool __result)
    {
        if (target == null || __instance.TargetType != TargetType.AnyEnemy)
        {
            return;
        }

        if (__instance.Owner.RunState is not RunState runState || !runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            return;
        }

        __result = ParallelTurnFrontlineHelper.IsSelectableEnemyTarget(__instance.Owner, target);
        if (__result)
        {
            Log.Info($"[ParallelTurnPvp] IsValidTarget override card={__instance.Id.Entry} target={target} result={__result}");
        }
    }
}

[HarmonyPatch(typeof(NTargetManager), "AllowedToTargetCreature")]
public static class ParallelTurnAllowedToTargetPatch
{
    static void Postfix(NTargetManager __instance, Creature creature, ref bool __result)
    {
        if (RunManager.Instance.DebugOnlyGetState() is not RunState runState || !runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            return;
        }

        TargetType targetType = Traverse.Create(__instance).Field("_validTargetsType").GetValue<TargetType>();
        if (targetType != TargetType.AnyEnemy)
        {
            return;
        }

        var me = LocalContext.GetMe(runState);
        if (me == null)
        {
            return;
        }

        __result = ParallelTurnFrontlineHelper.IsSelectableEnemyTarget(me, creature);
        Log.Info($"[ParallelTurnPvp] AllowedToTargetCreature override target={creature} result={__result}");
    }
}

[HarmonyPatch(typeof(NTargetManager), "OnCreatureHovered")]
public static class ParallelTurnTargetHoverGuardPatch
{
    static Exception? Finalizer(Exception? __exception, NCreature creature)
    {
        if (__exception == null)
        {
            return null;
        }

        if (RunManager.Instance.DebugOnlyGetState() is RunState runState && runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            Log.Error($"[ParallelTurnPvp] OnCreatureHovered failed for {creature?.Entity?.ToString() ?? "<null>"}: {__exception}");
            return null;
        }

        return __exception;
    }
}

[HarmonyPatch(typeof(Creature), nameof(Creature.TakeTurn))]
public static class ParallelTurnSkipDummyTurnPatch
{
    static bool Prefix(Creature __instance)
    {
        if (RunManager.Instance.DebugOnlyGetState() is RunState runState && runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            if (ParallelTurnFrontlineHelper.IsDebugArenaDummy(__instance))
            {
                return false;
            }
        }

        return true;
    }

    static void Postfix(Creature __instance, ref Task __result)
    {
        if (RunManager.Instance.DebugOnlyGetState() is RunState runState && runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            if (ParallelTurnFrontlineHelper.IsDebugArenaDummy(__instance))
            {
                __result = Task.CompletedTask;
            }
        }
    }
}

[HarmonyPatch(typeof(BattlewornDummyTimeLimitPower), nameof(BattlewornDummyTimeLimitPower.AfterTurnEnd))]
public static class ParallelTurnDisableDummyTimeoutPatch
{
    static bool Prefix(ref Task __result)
    {
        if (RunManager.Instance.DebugOnlyGetState() is RunState runState && runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            __result = Task.CompletedTask;
            Log.Info("[ParallelTurnPvp] Skipped BattlewornDummyTimeLimitPower.AfterTurnEnd for debug arena.");
            return false;
        }

        return true;
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.ShouldStopCombatFromEnding))]
public static class ParallelTurnPreventAutoCombatEndPatch
{
    static void Postfix(CombatState combatState, ref bool __result)
    {
        if (combatState.RunState is not RunState runState || runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().FirstOrDefault() is not { } modifier)
        {
            return;
        }

        if (ParallelTurnMatchStateRegistry.IsEnded(runState))
        {
            Log.Info($"[ParallelTurnPvp] Debug match already ended. allowing combat end check to continue. winner={modifier.WinnerNetId}");
            return;
        }

        __result = true;
        Log.Info($"[ParallelTurnPvp] Prevented auto combat end. round={combatState.RoundNumber} currentSide={combatState.CurrentSide} enemies=[{string.Join(", ", combatState.Enemies.Select(enemy => enemy.ToString()))}]");
    }
}

[HarmonyPatch(typeof(HookPlayerChoiceContext), nameof(HookPlayerChoiceContext.AssignTaskAndWaitForPauseOrCompletion))]
public static class ParallelTurnNullHookTaskGuardPatch
{
    static void Prefix(HookPlayerChoiceContext __instance, ref Task task)
    {
        if (task != null)
        {
            return;
        }

        if (RunManager.Instance.DebugOnlyGetState() is RunState runState && runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            Log.Error($"[ParallelTurnPvp] Hook task was null. source={__instance.Source?.GetType().FullName ?? "<null>"} owner={__instance.Owner?.NetId.ToString() ?? "<null>"}");
        }

        task = Task.CompletedTask;
    }
}

[HarmonyPatch(typeof(NCombatRoom), "CreateEnemyNodes")]
public static class ParallelTurnEnemyVisualBootstrapPatch
{
    static void Postfix(NCombatRoom __instance)
    {
        ParallelTurnCombatVisualLayout.Apply(__instance);
    }
}

[HarmonyPatch(typeof(NCombatRoom), nameof(NCombatRoom.AddCreature))]
public static class ParallelTurnEnemyVisualAddCreaturePatch
{
    static void Postfix(NCombatRoom __instance, Creature creature)
    {
        if (!creature.IsPlayer && creature.PetOwner == null)
        {
            return;
        }

        ParallelTurnCombatVisualLayout.Apply(__instance);
    }
}

internal static class ParallelTurnCombatVisualLayout
{
    private const float LocalRetreatX = -20f;
    private const float RemoteRetreatX = 60f;
    private const float LocalPetSpacingScale = 2f / 3f;
    private static readonly Vector2 SplitRoomDummyPosition = new(560f, 188f);
    private static readonly ConditionalWeakTable<NCreature, BasePositionHolder> BasePositions = new();

    public static void Apply(NCombatRoom room)
    {
        if (RunManager.Instance.DebugOnlyGetState() is not RunState runState || !runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            return;
        }

        PvpNetBridge.EnsureRegistered();

        Node enemyContainer = Traverse.Create(room).Field("_enemyContainer").GetValue<Node>();
        if (enemyContainer == null)
        {
            return;
        }

        List<NCreature> nodes = room.CreatureNodes.ToList();
        bool splitRoomActive = ParallelTurnFrontlineHelper.IsSplitRoomActive(runState);
        if (splitRoomActive)
        {
            ApplySplitRoomLayout(room, enemyContainer, nodes);
            return;
        }

        foreach (NCreature dummy in nodes.Where(node => ParallelTurnFrontlineHelper.IsDebugArenaDummy(node.Entity)))
        {
            dummy.Hide();
            dummy.ToggleIsInteractable(false);
            Log.Info($"[ParallelTurnPvp] Hid debug dummy visual node for {dummy.Entity}.");
        }

        List<NCreature> localPlayers = nodes
            .Where(node => node.Entity.IsPlayer && node.Entity.Player != null && LocalContext.IsMe(node.Entity.Player))
            .OrderBy(node => node.Entity.Player!.NetId)
            .ToList();

        foreach (NCreature localPlayer in localPlayers)
        {
            if (!TryGetBasePosition(localPlayer, out _))
            {
                continue;
            }

            List<NCreature> localPets = nodes
                .Where(node => node.Entity.PetOwner == localPlayer.Entity.Player && node.Entity.IsAlive)
                .OrderBy(node => node.Entity.Monster is null ? 1 : 0)
                .ToList();

            ApplyRetreatOffset(localPlayer, Vector2.Left * LocalRetreatX);
            foreach (NCreature pet in localPets)
            {
                if (!TryGetBasePosition(pet, out _))
                {
                    continue;
                }

                ApplyScaledCompanionOffset(localPlayer, pet, Vector2.Left * LocalRetreatX, LocalPetSpacingScale);
            }

            Log.Info($"[ParallelTurnPvp] Positioned local player {localPlayer.Entity} at {localPlayer.Position} with {localPets.Count} pets after local retreat.");
        }

        List<NCreature> remotePlayers = nodes
            .Where(node => node.Entity.IsPlayer && node.Entity.Player != null && !LocalContext.IsMe(node.Entity.Player))
            .OrderBy(node => node.Entity.Player!.NetId)
            .ToList();

        int playerIndex = 0;
        foreach (NCreature remotePlayer in remotePlayers)
        {
            EnsureParent(remotePlayer, enemyContainer);
            remotePlayer.Show();
            remotePlayer.ToggleIsInteractable(true);
            remotePlayer.Visuals.Bounds.Visible = true;
            EnsureFacingLeft(remotePlayer);

            List<NCreature> remotePets = nodes
                .Where(node => node.Entity.PetOwner == remotePlayer.Entity.Player && node.Entity.IsAlive)
                .OrderBy(node => node.Entity.Monster is null ? 1 : 0)
                .ToList();

            foreach (NCreature pet in remotePets)
            {
                EnsureParent(pet, enemyContainer);
                pet.Show();
                pet.ToggleIsInteractable(true);
                pet.Visuals.Bounds.Visible = true;
                EnsureFacingLeft(pet);
            }

            float heroX = 480f + RemoteRetreatX + playerIndex * 105f;
            float heroY = 184f - playerIndex * 22f;
            remotePlayer.Position = new Vector2(heroX, heroY);

            Creature? frontline = ParallelTurnFrontlineHelper.GetFrontline(remotePlayer.Entity.Player!);
            float frontlineX = heroX - 170f;
            float frontlineY = heroY + 8f;
            int extraPetIndex = 0;
            foreach (NCreature pet in remotePets)
            {
                if (frontline != null && pet.Entity == frontline)
                {
                    pet.Position = new Vector2(frontlineX, frontlineY);
                    Log.Info($"[ParallelTurnPvp] Positioned remote frontline {pet.Entity} at {pet.Position} for player {remotePlayer.Entity.Player!.NetId} facingScale={pet.Body.Scale}.");
                    continue;
                }

                pet.Position = new Vector2(heroX + 55f, heroY + 18f + extraPetIndex * 34f);
                extraPetIndex++;
                Log.Info($"[ParallelTurnPvp] Positioned extra remote pet {pet.Entity} at {pet.Position} for player {remotePlayer.Entity.Player!.NetId} facingScale={pet.Body.Scale}.");
            }

            Log.Info($"[ParallelTurnPvp] Positioned remote player {remotePlayer.Entity} at {remotePlayer.Position} with {remotePets.Count} pets in enemy container. facingScale={remotePlayer.Body.Scale}");
            playerIndex++;
        }
    }

    private static void ApplySplitRoomLayout(NCombatRoom room, Node enemyContainer, List<NCreature> nodes)
    {
        List<NCreature> localPlayers = nodes
            .Where(node => node.Entity.IsPlayer && node.Entity.Player != null && LocalContext.IsMe(node.Entity.Player))
            .OrderBy(node => node.Entity.Player!.NetId)
            .ToList();
        HashSet<ulong> localPlayerIds = localPlayers
            .Select(node => node.Entity.Player!.NetId)
            .ToHashSet();

        foreach (NCreature localPlayer in localPlayers)
        {
            localPlayer.Show();
            localPlayer.ToggleIsInteractable(true);
            localPlayer.Visuals.Bounds.Visible = true;

            if (!TryGetBasePosition(localPlayer, out _))
            {
                continue;
            }

            List<NCreature> localPets = nodes
                .Where(node => node.Entity.PetOwner == localPlayer.Entity.Player && node.Entity.IsAlive)
                .OrderBy(node => node.Entity.Monster is null ? 1 : 0)
                .ToList();

            ApplyRetreatOffset(localPlayer, Vector2.Left * LocalRetreatX);
            foreach (NCreature pet in localPets)
            {
                pet.Show();
                pet.ToggleIsInteractable(true);
                pet.Visuals.Bounds.Visible = true;
                if (!TryGetBasePosition(pet, out _))
                {
                    continue;
                }

                ApplyScaledCompanionOffset(localPlayer, pet, Vector2.Left * LocalRetreatX, LocalPetSpacingScale);
            }

            Log.Info($"[ParallelTurnPvp] Split-room visual: positioned local player {localPlayer.Entity} with {localPets.Count} pets.");
        }

        foreach (NCreature dummy in nodes.Where(node => ParallelTurnFrontlineHelper.IsDebugArenaDummy(node.Entity)))
        {
            EnsureParent(dummy, enemyContainer);
            dummy.Show();
            dummy.ToggleIsInteractable(true);
            dummy.Visuals.Bounds.Visible = true;
            EnsureFacingLeft(dummy);
            dummy.Position = SplitRoomDummyPosition;
            Log.Info($"[ParallelTurnPvp] Split-room visual: enabled dummy target {dummy.Entity} at {dummy.Position}.");
        }

        foreach (NCreature node in nodes.Where(node => IsRemoteOwnedCreature(node, localPlayerIds)))
        {
            node.Hide();
            node.ToggleIsInteractable(false);
            node.Visuals.Bounds.Visible = false;
        }

        Log.Info($"[ParallelTurnPvp] Split-room visual layout applied. localPlayers={localPlayers.Count} hiddenRemote={nodes.Count(node => IsRemoteOwnedCreature(node, localPlayerIds))}");
    }

    private static bool IsRemoteOwnedCreature(NCreature node, HashSet<ulong> localPlayerIds)
    {
        if (node.Entity.IsPlayer && node.Entity.Player != null)
        {
            return !localPlayerIds.Contains(node.Entity.Player.NetId);
        }

        if (node.Entity.PetOwner != null)
        {
            return !localPlayerIds.Contains(node.Entity.PetOwner.NetId);
        }

        return false;
    }

    private static void EnsureParent(Node node, Node expectedParent)
    {
        if (node.GetParent() == expectedParent)
        {
            return;
        }

        node.Reparent(expectedParent, true);
    }

    private static void ApplyRetreatOffset(NCreature creature, Vector2 offset)
    {
        if (TryGetBasePosition(creature, out Vector2 basePosition))
        {
            creature.Position = basePosition + offset;
        }
    }

    private static void ApplyScaledCompanionOffset(NCreature owner, NCreature companion, Vector2 ownerOffset, float spacingScale)
    {
        if (!TryGetBasePosition(owner, out Vector2 ownerBasePosition) || !TryGetBasePosition(companion, out Vector2 companionBasePosition))
        {
            return;
        }

        Vector2 scaledDelta = (companionBasePosition - ownerBasePosition) * spacingScale;
        companion.Position = ownerBasePosition + ownerOffset + scaledDelta;
    }

    private static bool TryGetBasePosition(NCreature creature, out Vector2 basePosition)
    {
        if (BasePositions.TryGetValue(creature, out BasePositionHolder? holder))
        {
            basePosition = holder.Position;
            return true;
        }

        if (!IsStableLocalPosition(creature.Position))
        {
            basePosition = default;
            return false;
        }

        holder = BasePositions.GetOrCreateValue(creature);
        holder.Position = creature.Position;
        basePosition = holder.Position;
        return true;
    }

    private static bool IsStableLocalPosition(Vector2 position)
    {
        return position.X < -100f && position.Y > 100f;
    }

    private static void EnsureFacingLeft(NCreature creature)
    {
        if (creature.Body.Scale.X > 0f)
        {
            creature.Body.Scale *= new Vector2(-1f, 1f);
        }
    }

    private sealed class BasePositionHolder
    {
        public Vector2 Position { get; set; }
    }
}

internal static class ParallelTurnMatchEndFlow
{
    private static readonly HashSet<RunState> EndingRuns = [];

    public static async Task<bool> EndMatchAsync(RunState runState, ParallelTurnPvpDebugModifier modifier, ulong winnerNetId, int roundNumber)
    {
        if (!EndingRuns.Add(runState))
        {
            return true;
        }

        try
        {
            ParallelTurnMatchStateRegistry.MarkEnded(runState);
            modifier.MatchEnded = true;
            modifier.WinnerNetId = winnerNetId;
            if (PvpRuntimeRegistry.TryGet(runState) is { } runtime)
            {
                runtime.CurrentRound.Phase = PvpMatchPhase.MatchEnd;
                runtime.CurrentRound.HasResolved = true;
                runtime.CurrentRound.PendingAuthoritativeSnapshot = null;
            }

            ulong localNetId = LocalContext.GetMe(runState)?.NetId ?? 0UL;
            bool isVictory = winnerNetId != 0UL && localNetId == winnerNetId;
            string winnerLabel = winnerNetId == 0UL ? "draw" : winnerNetId.ToString();
            Log.Info($"[ParallelTurnPvp] Match end detected. round={roundNumber} winner={winnerLabel} local={localNetId} isVictory={isVictory}");

            var serializableRun = RunManager.Instance.OnEnded(isVictory);
            if (NRun.Instance != null)
            {
                NRun.Instance.ShowGameOverScreen(serializableRun);
            }
            else
            {
                Log.Error("[ParallelTurnPvp] NRun.Instance was null while trying to show game over screen.");
            }

            await Task.CompletedTask;
            return true;
        }
        finally
        {
            EndingRuns.Remove(runState);
        }
    }
}



