using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using ParallelTurnPvp.Core;
using ParallelTurnPvp.Models;

namespace ParallelTurnPvp.Patches;

internal static class ParallelTurnDisconnectStateBridge
{
    public static void MarkDisconnected(string source, ulong remotePlayerId, NetErrorInfo info)
    {
        if (!RunManager.Instance.IsInProgress ||
            RunManager.Instance.DebugOnlyGetState() is not RunState runState ||
            !runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            return;
        }

        if (ParallelTurnMatchStateRegistry.IsEnded(runState))
        {
            return;
        }

        PvpMatchRuntime runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        bool wasDisconnectedPendingResume = runtime.IsDisconnectedPendingResume;
        runtime.MarkDisconnectedPendingResume(source, remotePlayerId, info.GetReason().ToString());
        if (!wasDisconnectedPendingResume)
        {
            ShowNativeDisconnectPopup(source, info, remotePlayerId);
        }

        Log.Warn($"[ParallelTurnPvp] 检测到联机中断：source={source} remote={remotePlayerId} reason={info.GetReason()} round={runtime.CurrentRound.RoundIndex}");
    }

    private static void ShowNativeDisconnectPopup(string source, NetErrorInfo info, ulong remotePlayerId)
    {
        if (NModalContainer.Instance == null)
        {
            Log.Warn("[ParallelTurnPvp] 联机中断弹窗显示失败：NModalContainer.Instance 为空。");
            return;
        }

        string remoteLabel = remotePlayerId == 0UL ? "-" : remotePlayerId.ToString();
        (string title, string body) = BuildDisconnectPopupText(source, info, remoteLabel);
        NErrorPopup? popup = NErrorPopup.Create(title, body, showReportBugButton: false);

        if (popup == null)
        {
            Log.Warn("[ParallelTurnPvp] 联机中断弹窗显示失败：NErrorPopup.Create 返回空。");
            return;
        }

        NModalContainer.Instance.Add(popup, true);
    }

    private static (string Title, string Body) BuildDisconnectPopupText(string source, NetErrorInfo info, string remoteLabel)
    {
        string reason = info.GetReason().ToString();
        return source switch
        {
            "client_disconnected_from_host" => (
                "与主机断开连接",
                $"你与主机的连接已中断（原因：{reason}）。\n本局已冻结，系统会尝试恢复当前会话；若恢复失败，再返回联机重开。"),
            "host_peer_disconnected" => (
                "客机断开连接",
                $"客机玩家 {remoteLabel} 已断开连接（原因：{reason}）。\n本局已冻结，请等待其重连恢复当前会话，或返回联机。"),
            "host_disconnected" => (
                "主机网络中断",
                $"主机连接已中断（原因：{reason}）。\n本局已冻结，系统会尝试恢复当前会话；若恢复失败，再返回联机重开。"),
            _ => (
                "联机中断",
                $"连接已断开（原因：{reason}，远端：{remoteLabel}）。\n本局已冻结，系统会尝试恢复当前会话；若恢复失败，再返回联机重开。")
        };
    }
}

[HarmonyPatch(typeof(NetClientGameService), nameof(NetClientGameService.OnDisconnectedFromHost))]
public static class ParallelTurnClientDisconnectedPatch
{
    static void Postfix(ulong hostNetId, NetErrorInfo info)
    {
        ParallelTurnDisconnectStateBridge.MarkDisconnected("client_disconnected_from_host", hostNetId, info);
    }
}

[HarmonyPatch(typeof(NetHostGameService), nameof(NetHostGameService.OnPeerDisconnected))]
public static class ParallelTurnHostPeerDisconnectedPatch
{
    static void Postfix(ulong peerId, NetErrorInfo info)
    {
        ParallelTurnDisconnectStateBridge.MarkDisconnected("host_peer_disconnected", peerId, info);
    }
}

[HarmonyPatch(typeof(NetHostGameService), nameof(NetHostGameService.OnDisconnected))]
public static class ParallelTurnHostDisconnectedPatch
{
    static void Postfix(NetErrorInfo info)
    {
        ParallelTurnDisconnectStateBridge.MarkDisconnected("host_disconnected", 0UL, info);
    }
}
