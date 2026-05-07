using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using ParallelTurnPvp.Core;
using ParallelTurnPvp.Models;

namespace ParallelTurnPvp.Patches;

[HarmonyPatch(typeof(CombatManager), "SwitchSides")]
public static class PvpShopBridgeSwitchSidesPatch
{
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    static void Prefix()
    {
        if (!PvpShopFeatureFlags.IsShopDraftEnabled)
        {
            return;
        }

        CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState == null ||
            combatState.CurrentSide != CombatSide.Player ||
            combatState.RunState is not RunState runState ||
            !runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            return;
        }

        if (ParallelTurnMatchStateRegistry.IsEnded(runState))
        {
            return;
        }

        if (RunManager.Instance.NetService.Type == NetGameType.Client)
        {
            return;
        }

        if (PvpRuntimeRegistry.TryGet(combatState) is not { } runtime ||
            !runtime.CanResolveRound(combatState.RoundNumber))
        {
            return;
        }

        PvpShopRoundState? snapshotBeforeClose = PvpShopBridge.Snapshot(runState);
        if (snapshotBeforeClose == null)
        {
            return;
        }

        if (PvpShopBridge.TryCloseRound(runState))
        {
            new PvpShopNetBridge().BroadcastShopClosed(
                runState,
                snapshotBeforeClose.RoundIndex,
                snapshotBeforeClose.SnapshotVersion,
                snapshotBeforeClose.StateVersion);
            Log.Info($"[ParallelTurnPvp][ShopEngine] Closed shop before round resolve. round={runtime.CurrentRound.RoundIndex} snapshotVersion={snapshotBeforeClose.SnapshotVersion} shopStateVersion={snapshotBeforeClose.StateVersion}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    static void Postfix()
    {
        if (!PvpShopFeatureFlags.IsShopDraftEnabled)
        {
            return;
        }

        CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState == null ||
            combatState.CurrentSide != CombatSide.Player ||
            combatState.RunState is not RunState runState ||
            !runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            return;
        }

        if (ParallelTurnMatchStateRegistry.IsEnded(runState))
        {
            return;
        }

        if (RunManager.Instance.NetService.Type == NetGameType.Client)
        {
            return;
        }

        if (PvpRuntimeRegistry.TryGet(combatState) is not { } runtime ||
            runtime.CurrentRound.RoundIndex <= 0 ||
            runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion <= 0 ||
            runtime.CurrentRound.Phase != PvpMatchPhase.Planning)
        {
            return;
        }

        if (PvpShopBridge.TryOpenRound(
            runState,
            runtime.CurrentRound.RoundIndex,
            runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion,
            PvpShopDefaults.StandardModeId))
        {
            if (PvpShopBridge.Snapshot(runState) is { } snapshot)
            {
                new PvpShopNetBridge().BroadcastShopState(runState, snapshot);
            }

            Log.Info($"[ParallelTurnPvp][ShopEngine] Opened shop for round start. round={runtime.CurrentRound.RoundIndex} snapshotVersion={runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion}");
        }
    }
}
