using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ParallelTurnPvp.Core;
using System.Text.RegularExpressions;

namespace ParallelTurnPvp.Ui;

public partial class ParallelTurnIntentOverlay : Control
{
    private const string OverlayNodeName = "ParallelTurnIntentOverlay";
    private static WeakReference<ParallelTurnIntentOverlay>? _attachedOverlay;
    private Label _title = null!;
    private RichTextLabel _body = null!;
    private Control _floatLayer = null!;
    private double _refreshAccumulator;
    private string _lastRendered = string.Empty;
    private string _lastShopDebugStatus = "未执行商店调试动作。";
    private DateTime _lastShopDebugStatusUtc = DateTime.MinValue;
    private NCombatRoom _room = null!;
    private PvpShopDebugPanel? _shopPanel;
    private RunState? _shopPanelRunState;
    private ulong _shopPanelPlayerId;

    public static void EnsureAttached(NCombatRoom room)
    {
        if (room.GetNodeOrNull<ParallelTurnIntentOverlay>(OverlayNodeName) != null)
        {
            return;
        }

        if (_attachedOverlay != null && _attachedOverlay.TryGetTarget(out ParallelTurnIntentOverlay? existingOverlay) &&
            existingOverlay != null && GodotObject.IsInstanceValid(existingOverlay) && existingOverlay.IsInsideTree())
        {
            existingOverlay.QueueFree();
            Log.Info("[ParallelTurnPvp] Replaced stale intent overlay instance.");
        }

        var overlay = new ParallelTurnIntentOverlay
        {
            Name = OverlayNodeName
        };
        overlay._room = room;
        room.Ui.AddChild(overlay);
        _attachedOverlay = new WeakReference<ParallelTurnIntentOverlay>(overlay);
        Log.Info("[ParallelTurnPvp] Intent overlay attached to combat UI.");
    }

    public static void TryShowDelayedFloat(Creature target, string text, Color color)
    {
        if (_attachedOverlay == null || !_attachedOverlay.TryGetTarget(out ParallelTurnIntentOverlay? overlay))
        {
            return;
        }

        if (overlay == null || !GodotObject.IsInstanceValid(overlay) || !overlay.IsInsideTree())
        {
            return;
        }

        overlay.ShowDelayedFloatInternal(target, text, color);
    }


    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.TopRight);
        OffsetLeft = -360f;
        OffsetTop = 110f;
        OffsetRight = -18f;
        OffsetBottom = 470f;
        MouseFilter = MouseFilterEnum.Ignore;

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(LayoutPreset.FullRect);
        panel.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        panel.AddChild(margin);

        var layout = new VBoxContainer();
        layout.AddThemeConstantOverride("separation", 6);
        margin.AddChild(layout);

        _title = new Label
        {
            Text = "对手意图"
        };
        layout.AddChild(_title);

        _body = new RichTextLabel
        {
            BbcodeEnabled = false,
            ScrollActive = true,
            FitContent = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Pass
        };
        _body.SizeFlagsVertical = SizeFlags.ExpandFill;
        layout.AddChild(_body);

        _floatLayer = new Control
        {
            Name = "DelayedFloatLayer",
            MouseFilter = MouseFilterEnum.Ignore
        };
        _floatLayer.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_floatLayer);

        if (PvpShopFeatureFlags.IsShopDraftEnabled)
        {
            _shopPanel = new PvpShopDebugPanel();
            _shopPanel.ConfigureActions(OnShopPanelRefreshRequested, OnShopPanelPurchaseRequested, OnShopPanelDeleteRequested);
            AddChild(_shopPanel);
        }

        RefreshNow();
    }

    public override void _ExitTree()
    {
        if (_attachedOverlay != null && _attachedOverlay.TryGetTarget(out ParallelTurnIntentOverlay? overlay) && ReferenceEquals(overlay, this))
        {
            _attachedOverlay = null;
        }
    }

    public override void _Process(double delta)
    {
        if (!IsAttachedOverlayInstance(this))
        {
            return;
        }

        _refreshAccumulator += delta;
        if (_refreshAccumulator < 0.15d)
        {
            return;
        }

        _refreshAccumulator = 0d;
        RefreshNow();
    }

    public override void _Input(InputEvent @event)
    {
        if (!IsAttachedOverlayInstance(this))
        {
            return;
        }

        if (@event is InputEventMouseButton mouseButton &&
            mouseButton.Pressed &&
            GetGlobalRect().HasPoint(GetGlobalMousePosition()))
        {
            if (mouseButton.ButtonIndex == MouseButton.WheelUp)
            {
                ScrollBody(-70d);
                GetViewport().SetInputAsHandled();
                return;
            }

            if (mouseButton.ButtonIndex == MouseButton.WheelDown)
            {
                ScrollBody(70d);
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
        {
            return;
        }

        if (HandleScrollHotkeys(keyEvent))
        {
            GetViewport().SetInputAsHandled();
            return;
        }

        HandleShopDebugInput(keyEvent);
    }

    private bool HandleScrollHotkeys(InputEventKey keyEvent)
    {
        switch (keyEvent.Keycode)
        {
            case Key.Up:
                ScrollBody(-40d);
                return true;
            case Key.Down:
                ScrollBody(40d);
                return true;
            case Key.Pageup:
                ScrollBody(-280d);
                return true;
            case Key.Pagedown:
                ScrollBody(280d);
                return true;
            default:
                return false;
        }
    }

    private void ScrollBody(double delta)
    {
        if (_body.GetVScrollBar() is not { } scrollbar)
        {
            return;
        }

        double value = scrollbar.Value + delta;
        value = Math.Max(scrollbar.MinValue, Math.Min(scrollbar.MaxValue, value));
        scrollbar.Value = value;
    }

    private void RefreshNow()
    {
        if (RunManager.Instance.DebugOnlyGetState() is not RunState runState || !runState.Modifiers.Any(mod => mod.GetType().Name == "ParallelTurnPvpDebugModifier"))
        {
            Visible = false;
            if (_shopPanel != null)
            {
                _shopPanel.Visible = false;
            }
            return;
        }

        var me = LocalContext.GetMe(runState);
        if (me == null)
        {
            Visible = false;
            if (_shopPanel != null)
            {
                _shopPanel.Visible = false;
            }
            return;
        }

        var opponent = runState.Players.FirstOrDefault(player => player.NetId != me.NetId);
        if (opponent == null)
        {
            Visible = false;
            if (_shopPanel != null)
            {
                _shopPanel.Visible = false;
            }
            return;
        }

        _shopPanelRunState = runState;
        _shopPanelPlayerId = me.NetId;

        var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        PvpNetBridge.PumpClientResumeStateRequest(runState);
        PvpNetBridge.PumpClientPlanningFrameResync(runState);
        PvpNetBridge.PumpClientSubmissionRetry(runState);
        PvpNetBridge.PumpRoundAlignment(runState);
        PvpNetBridge.PumpHostResolveFallback(runState);
        var view = runtime.GetIntentView(me.NetId, opponent.NetId);

        string rendered = view != null
            ? BuildText(view, runtime, runtime.LastAuthoritativeResult, me.NetId, opponent.NetId)
            : runtime.IsDisconnectedPendingResume
                ? BuildDisconnectedText(runtime)
                : BuildNoIntentText(runtime);
        if (PvpShopFeatureFlags.IsShopDraftEnabled)
        {
            rendered = $"{rendered}\n\n{BuildShopDebugSection(runState, me.NetId)}";
        }

        int titleRound = view?.RoundIndex ?? Math.Max(runtime.CurrentRound.RoundIndex, 1);
        _title.Text = $"PvP意图  第{titleRound}回合";
        if (_lastRendered != rendered)
        {
            _body.Text = rendered;
            _lastRendered = rendered;
        }

        RefreshShopPanel(runState, me.NetId);
        Visible = true;
    }

    private void RefreshShopPanel(RunState runState, ulong localPlayerId)
    {
        if (_shopPanel == null)
        {
            return;
        }

        if (!PvpShopFeatureFlags.IsShopDraftEnabled)
        {
            _shopPanel.Visible = false;
            return;
        }

        _shopPanel.Visible = true;
        PvpShopViewModel? shopView = null;
        if (PvpShopBridge.TryGetView(runState, localPlayerId, out PvpShopViewModel view))
        {
            shopView = view;
        }

        List<PvpShopDeckEntry> deckEntries = new();
        Player? player = runState.Players.FirstOrDefault(entry => entry.NetId == localPlayerId);
        if (player != null)
        {
            for (int i = 0; i < player.Deck.Cards.Count; i++)
            {
                deckEntries.Add(new PvpShopDeckEntry(i, player.Deck.Cards[i].Id.Entry));
            }
        }

        _shopPanel.UpdateState(shopView, deckEntries, RunManager.Instance.NetService.Type, _lastShopDebugStatus);
    }

    private void OnShopPanelRefreshRequested(PvpShopRefreshType refreshType)
    {
        if (_shopPanelRunState == null || _shopPanelPlayerId == 0)
        {
            SetShopDebugStatus("刷新失败：商店上下文未就绪。");
            return;
        }

        TryTriggerShopRefresh(_shopPanelRunState, _shopPanelPlayerId, refreshType);
    }

    private void OnShopPanelPurchaseRequested(int slotIndex)
    {
        if (_shopPanelRunState == null || _shopPanelPlayerId == 0)
        {
            SetShopDebugStatus("购买失败：商店上下文未就绪。");
            return;
        }

        TryTriggerShopPurchase(_shopPanelRunState, _shopPanelPlayerId, slotIndex);
    }

    private void OnShopPanelDeleteRequested(int deckCardIndex)
    {
        if (_shopPanelRunState == null || _shopPanelPlayerId == 0)
        {
            SetShopDebugStatus("删卡失败：商店上下文未就绪。");
            return;
        }

        TryTriggerShopDelete(_shopPanelRunState, _shopPanelPlayerId, deckCardIndex);
    }

    private void HandleShopDebugInput(InputEventKey keyEvent)
    {
        if (!PvpShopFeatureFlags.IsShopDraftEnabled)
        {
            return;
        }

        if (!keyEvent.Pressed || keyEvent.Echo)
        {
            return;
        }

        if (RunManager.Instance.DebugOnlyGetState() is not RunState runState ||
            !runState.Modifiers.Any(mod => mod.GetType().Name == "ParallelTurnPvpDebugModifier"))
        {
            return;
        }

        var me = LocalContext.GetMe(runState);
        if (me == null)
        {
            return;
        }

        if (keyEvent.CtrlPressed)
        {
            return;
        }

        switch (keyEvent.Keycode)
        {
            case Key.F8:
                TryToggleShopRoundHost(runState);
                return;
            case Key.F9:
                TryTriggerShopRefresh(runState, me.NetId, PvpShopRefreshType.Normal);
                return;
            case Key.F10:
                TryTriggerShopRefresh(runState, me.NetId, PvpShopRefreshType.ClassBias);
                return;
            case Key.F11:
                TryTriggerShopRefresh(runState, me.NetId, PvpShopRefreshType.RoleFix);
                return;
            case Key.F12:
                TryTriggerShopRefresh(runState, me.NetId, PvpShopRefreshType.ArchetypeTrace);
                return;
        }

        if (TryResolvePurchaseSlotHotkey(keyEvent.Keycode, out int slotIndex))
        {
            TryTriggerShopPurchase(runState, me.NetId, slotIndex);
        }
    }

    private void TryToggleShopRoundHost(RunState runState)
    {
        NetGameType netType = RunManager.Instance.NetService.Type;
        if (netType != NetGameType.Host && netType != NetGameType.Singleplayer)
        {
            SetShopDebugStatus("商店开关仅主机可用。");
            return;
        }

        if (PvpRuntimeRegistry.TryGet(runState) is not { } runtime)
        {
            SetShopDebugStatus("商店开关失败：PvP runtime 未初始化。");
            return;
        }

        if (runtime.IsDisconnectedPendingResume)
        {
            SetShopDebugStatus($"商店开关失败：联机中断待恢复（{runtime.DisconnectReason}）。");
            return;
        }

        PvpShopRoundState? current = PvpShopBridge.Snapshot(runState);
        if (current is { IsOpen: true })
        {
            if (PvpShopBridge.TryCloseRound(runState))
            {
                new PvpShopNetBridge().BroadcastShopClosed(runState, current.RoundIndex, current.SnapshotVersion, current.StateVersion);
                SetShopDebugStatus($"主机关闭商店：round={current.RoundIndex} state={current.StateVersion}");
            }
            else
            {
                SetShopDebugStatus("主机关闭商店失败。");
            }

            return;
        }

        (int roundIndex, int snapshotVersion) = EnsureShopRoundContext(runtime, runState);
        if (roundIndex <= 0 || snapshotVersion <= 0)
        {
            SetShopDebugStatus($"主机开店失败：回合上下文无效 round={roundIndex} snapshot={snapshotVersion}");
            return;
        }

        if (!PvpShopBridge.TryOpenRound(runState, roundIndex, snapshotVersion, PvpShopDefaults.StandardModeId))
        {
            SetShopDebugStatus($"主机开店失败：TryOpenRound 返回 false（round={roundIndex}）。");
            return;
        }

        if (PvpShopBridge.Snapshot(runState) is { } snapshot)
        {
            new PvpShopNetBridge().BroadcastShopState(runState, snapshot, force: true);
            SetShopDebugStatus($"主机开店成功：round={snapshot.RoundIndex} state={snapshot.StateVersion}");
        }
        else
        {
            SetShopDebugStatus("主机开店失败：快照为空。");
        }
    }

    private static (int roundIndex, int snapshotVersion) EnsureShopRoundContext(PvpMatchRuntime runtime, RunState runState)
    {
        int roundIndex = runtime.CurrentRound.RoundIndex;
        int snapshotVersion = runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion;
        if (roundIndex > 0 && snapshotVersion > 0)
        {
            return (roundIndex, snapshotVersion);
        }

        if (CombatManager.Instance?.DebugOnlyGetState() is not { } combatState || combatState.RunState != runState)
        {
            return (roundIndex, snapshotVersion);
        }

        int liveRound = Math.Max(1, combatState.RoundNumber);
        runtime.StartRoundFromLiveState(combatState, liveRound);
        return (runtime.CurrentRound.RoundIndex, runtime.CurrentRound.SnapshotAtRoundStart.SnapshotVersion);
    }

    private void TryTriggerShopRefresh(RunState runState, ulong playerId, PvpShopRefreshType refreshType)
    {
        NetGameType netType = RunManager.Instance.NetService.Type;
        if (netType == NetGameType.Client)
        {
            bool sent = PvpShopBridge.TrySendRefreshRequest(runState, playerId, refreshType, out string reason);
            SetShopDebugStatus(sent
                ? $"客机发送刷新请求：{FormatShopRefreshType(refreshType)}。{reason}"
                : $"客机发送刷新失败：{reason}");
            return;
        }

        if (netType != NetGameType.Host && netType != NetGameType.Singleplayer)
        {
            SetShopDebugStatus("刷新失败：当前不在可用联机状态。");
            return;
        }

        bool success = PvpShopBridge.TryRefresh(runState, playerId, refreshType, out string hostReason);
        if (!success)
        {
            SetShopDebugStatus($"主机刷新失败：{hostReason}");
            return;
        }

        if (PvpShopBridge.Snapshot(runState) is { } snapshot)
        {
            new PvpShopNetBridge().BroadcastShopState(runState, snapshot, force: true);
        }

        SetShopDebugStatus($"主机刷新成功：{FormatShopRefreshType(refreshType)}。{hostReason}");
    }

    private void TryTriggerShopPurchase(RunState runState, ulong playerId, int slotIndex)
    {
        NetGameType netType = RunManager.Instance.NetService.Type;
        if (netType == NetGameType.Client)
        {
            bool sent = PvpShopBridge.TrySendPurchaseRequest(runState, playerId, slotIndex, out string reason);
            SetShopDebugStatus(sent
                ? $"客机发送购买请求：槽位{slotIndex + 1}。{reason}"
                : $"客机发送购买失败：{reason}");
            return;
        }

        if (netType != NetGameType.Host && netType != NetGameType.Singleplayer)
        {
            SetShopDebugStatus("购买失败：当前不在可用联机状态。");
            return;
        }

        bool success = PvpShopBridge.TryPurchase(runState, playerId, slotIndex, out string hostReason);
        if (!success)
        {
            SetShopDebugStatus($"主机购买失败：{hostReason}");
            return;
        }

        if (PvpShopBridge.Snapshot(runState) is { } snapshot)
        {
            new PvpShopNetBridge().BroadcastShopState(runState, snapshot, force: true);
        }

        SetShopDebugStatus($"主机购买成功：槽位{slotIndex + 1}。{hostReason}");
    }

    private void TryTriggerShopDelete(RunState runState, ulong playerId, int deckCardIndex)
    {
        NetGameType netType = RunManager.Instance.NetService.Type;
        if (netType == NetGameType.Client)
        {
            bool sent = PvpShopBridge.TrySendDeleteRequest(runState, playerId, deckCardIndex, out string reason);
            SetShopDebugStatus(sent
                ? $"客机发送删卡请求：牌组索引{deckCardIndex}。{reason}"
                : $"客机发送删卡失败：{reason}");
            return;
        }

        if (netType != NetGameType.Host && netType != NetGameType.Singleplayer)
        {
            SetShopDebugStatus("删卡失败：当前不在可用联机状态。");
            return;
        }

        bool success = PvpShopBridge.TryDeleteCard(runState, playerId, deckCardIndex, out string hostReason);
        if (!success)
        {
            SetShopDebugStatus($"主机删卡失败：{hostReason}");
            return;
        }

        if (PvpShopBridge.Snapshot(runState) is { } snapshot)
        {
            new PvpShopNetBridge().BroadcastShopState(runState, snapshot, force: true);
        }

        SetShopDebugStatus($"主机删卡成功：牌组索引{deckCardIndex}。{hostReason}");
    }

    private string BuildShopDebugSection(RunState runState, ulong localPlayerId)
    {
        NetGameType netType = RunManager.Instance.NetService.Type;
        List<string> lines =
        [
            "[商店调试]",
            $"功能开关：{(PvpShopFeatureFlags.IsShopDraftEnabled ? "已启用" : "未启用")}",
            $"本机联机身份：{FormatNetRole(netType)}"
        ];

        if (!PvpShopFeatureFlags.IsShopDraftEnabled)
        {
            lines.Add("设置环境变量 PTPVP_ENABLE_SHOP_DRAFT=1 后重启游戏。");
            return string.Join('\n', lines);
        }

        if (PvpShopBridge.TryGetView(runState, localPlayerId, out PvpShopViewModel view))
        {
            lines.Add($"状态：开启 | round={view.RoundIndex} snapshot={view.SnapshotVersion} state={view.StateVersion}");
            lines.Add($"金币：{view.Gold} | 已刷新：{view.RefreshCount}");
            lines.Add($"模式版本：{view.ModeVersion} | 策略版本：{view.StrategyVersion} | RNG：{view.RngVersion}");
            lines.Add($"状态文本：{view.StatusText}");
            lines.Add($"刷新费用：普通 {GetRefreshCost(view, PvpShopRefreshType.Normal)} / 大类 {GetRefreshCost(view, PvpShopRefreshType.ClassBias)} / 修复 {GetRefreshCost(view, PvpShopRefreshType.RoleFix)} / 流派 {GetRefreshCost(view, PvpShopRefreshType.ArchetypeTrace)}");
            lines.Add("槽位报价：");
            foreach (PvpShopOffer offer in view.Offers.OrderBy(item => item.SlotIndex).Take(5))
            {
                lines.Add($"#{offer.SlotIndex + 1} [{FormatShopSlotKind(offer.SlotKind)}] {offer.DisplayName} | {offer.Price} 金 | {(offer.Available ? "可买" : "已售")}");
            }
        }
        else
        {
            lines.Add("状态：未开启（等待主机开店或下一回合自动开店）。");
        }

        string statusTime = _lastShopDebugStatusUtc == DateTime.MinValue
            ? "-"
            : _lastShopDebugStatusUtc.ToLocalTime().ToString("HH:mm:ss");
        if (netType == NetGameType.Client)
        {
            lines.Add("说明：F8 仅主机可用；客机可用 F9/F10/F11/F12 与 1-5 发送请求。删卡建议使用商店面板按钮。");
        }

        lines.Add($"最近动作：{_lastShopDebugStatus} ({statusTime})");
        lines.Add("热键：F8 主机开关店 | F9/F10/F11/F12 刷新 | 小键盘1-5或数字1-5购买槽位");
        return string.Join('\n', lines);
    }

    private static int GetRefreshCost(PvpShopViewModel view, PvpShopRefreshType refreshType)
    {
        return view.RefreshCosts.TryGetValue(refreshType, out int cost) ? cost : 0;
    }

    private void SetShopDebugStatus(string text)
    {
        _lastShopDebugStatus = string.IsNullOrWhiteSpace(text) ? "无状态信息。" : text;
        _lastShopDebugStatusUtc = DateTime.UtcNow;
        Log.Info($"[ParallelTurnPvp][ShopOverlay] {_lastShopDebugStatus}");
    }

    private static string BuildDisconnectedText(PvpMatchRuntime runtime)
    {
        var lines = new List<string>
        {
            "[对手意图]",
            $"房间拓扑：{(runtime.RoomSession.Topology == PvpArenaTopology.SplitRoom ? "分房骨架" : "同房")}",
            $"房间会话：{runtime.RoomSession.SessionId}",
            $"联机状态：连接中断，等待恢复（原因：{runtime.DisconnectReason}）",
            "本地操作已冻结，等待会话恢复或重新开局。"
        };
        return string.Join('\n', lines);
    }

    private static bool IsAttachedOverlayInstance(ParallelTurnIntentOverlay overlay)
    {
        if (_attachedOverlay == null || !_attachedOverlay.TryGetTarget(out ParallelTurnIntentOverlay? attached) || attached == null)
        {
            return false;
        }

        return ReferenceEquals(attached, overlay);
    }

    private static string BuildNoIntentText(PvpMatchRuntime runtime)
    {
        var lines = new List<string>
        {
            "[对手意图]",
            $"房间拓扑：{(runtime.RoomSession.Topology == PvpArenaTopology.SplitRoom ? "分房骨架" : "同房")}",
            $"房间会话：{runtime.RoomSession.SessionId}",
            "意图视图：当前回合尚未产生可显示数据。"
        };
        return string.Join('\n', lines);
    }

    private static string BuildText(PvpIntentView view, PvpMatchRuntime runtime, PvpRoundResult? lastResult, ulong meId, ulong opponentId)
    {
        int totalCount = view.VisibleCount + view.HiddenCount;
        string topologyLabel = runtime.RoomSession.Topology == PvpArenaTopology.SplitRoom ? "分房骨架" : "同房";
        var lines = new List<string>
        {
            "[对手意图]",
            $"房间拓扑：{topologyLabel}",
            $"房间会话：{runtime.RoomSession.SessionId}",
            $"起始能量：{view.RoundStartEnergy}",
            $"状态：{(view.Locked ? "已锁定" : "规划中")}{(view.IsFirstFinisher ? " | 先锁定" : string.Empty)}",
            $"我方已出牌：{view.ViewerActionCount}",
            $"对方已提交槽位：{view.TargetActionCount}",
            $"揭示预算：{view.RevealBudget}",
            $"已显示槽位：{(totalCount == 0 ? 0 : view.VisibleCount)}/{totalCount}",
            "规则：只有卡牌增加揭示数，药水只占槽位",
            "当前战斗壳规则：前线受击时优先消耗前线自身格挡"
        };

        if (runtime.IsDisconnectedPendingResume)
        {
            lines.Add($"联机状态：连接中断，等待恢复（原因：{runtime.DisconnectReason}）");
            lines.Add("本地操作已冻结，系统正在尝试恢复当前会话。");
        }

        if (view.VisibleCount == 0 && view.HiddenCount == 0)
        {
            lines.Add("对方尚未提交动作。");
        }
        else
        {
            for (int i = 0; i < view.VisibleSlots.Count; i++)
            {
                PvpPublicIntentSlot slot = view.VisibleSlots[i];
                lines.Add($"{i + 1}. {FormatCategory(slot.Category)} -> {FormatSide(slot.TargetSide)}");
            }

            for (int i = 0; i < view.HiddenCount; i++)
            {
                lines.Add($"{view.VisibleCount + i + 1}. ?");
            }
        }

        lines.Add(string.Empty);
        lines.Add("[上一回合摘要]");
        if (lastResult == null)
        {
            lines.Add("尚无已结算回合。");
        }
        else
        {
            lines.Add($"回合：{lastResult.RoundIndex}");
            foreach (string eventLine in BuildRoundSummary(lastResult, meId, opponentId))
            {
                lines.Add(eventLine);
            }

            IReadOnlyList<string> snapshotLines = BuildAuthoritativeSnapshotSummary(lastResult, meId, opponentId);
            if (snapshotLines.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("[权威结算快照]");
                foreach (string snapshotLine in snapshotLines)
                {
                    lines.Add(snapshotLine);
                }
            }

            IReadOnlyList<string> delayedLines = BuildDelayedSummary(lastResult, meId, opponentId);
            if (delayedLines.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("[延迟结算候选]");
                foreach (string delayedLine in delayedLines)
                {
                    lines.Add(delayedLine);
                }
            }

            IReadOnlyList<string> delayedCommandLines = BuildDelayedCommandSummary(lastResult, meId, opponentId);
            if (delayedCommandLines.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("[延迟落地脚本]");
                foreach (string delayedCommandLine in delayedCommandLines)
                {
                    lines.Add(delayedCommandLine);
                }
            }

            IReadOnlyList<string> playbackLines = BuildPlaybackSummary(lastResult, meId, opponentId);
            if (playbackLines.Count > 0)
            {
                lines.Add(string.Empty);
                lines.Add("[回合回放]");
                foreach (string playbackLine in playbackLines)
                {
                    lines.Add(playbackLine);
                }
            }
        }

        return string.Join('\n', lines);
    }

    private void ShowDelayedFloatInternal(Creature target, string text, Color color)
    {
        Vector2 spawn = GetFallbackFloatPosition();
        if (_room != null)
        {
            NCreature? creatureNode = _room.CreatureNodes.FirstOrDefault(node => node.Entity == target);
            if (creatureNode != null)
            {
                Vector2 creatureGlobal = creatureNode.GetGlobalTransformWithCanvas().Origin;
                spawn = GetGlobalTransformWithCanvas().AffineInverse() * creatureGlobal + new Vector2(0f, -56f);
            }
        }

        var label = new Label
        {
            Text = text,
            Modulate = color,
            MouseFilter = MouseFilterEnum.Ignore
        };
        label.Position = spawn;
        label.AddThemeFontSizeOverride("font_size", 40);
        label.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.9f));
        label.AddThemeConstantOverride("outline_size", 6);
        _floatLayer.AddChild(label);

        Tween tween = CreateTween();
        tween.SetParallel(true);
        tween.TweenProperty(label, "position", spawn + new Vector2(0f, -46f), 0.70d);
        tween.TweenProperty(label, "modulate:a", 0.0f, 0.70d);
        tween.Finished += () =>
        {
            if (GodotObject.IsInstanceValid(label))
            {
                label.QueueFree();
            }
        };
    }


    private Vector2 GetFallbackFloatPosition()
    {
        float x = Size.X > 0 ? Size.X - 120f : 220f;
        return new Vector2(x, 88f);
    }

    private static IEnumerable<string> BuildRoundSummary(PvpRoundResult result, ulong meId, ulong opponentId)
    {
        if (result.Events.Count == 0)
        {
            yield return "没有摘要事件。";
            yield break;
        }

        foreach (PvpResolvedEvent resolvedEvent in result.Events.TakeLast(10))
        {
            yield return FormatResolvedEventForDisplay(resolvedEvent.Text, meId, opponentId);
        }
    }

    private static IReadOnlyList<string> BuildPlaybackSummary(PvpRoundResult result, ulong meId, ulong opponentId)
    {
        return result.Events
            .Where(resolvedEvent => resolvedEvent.Kind == PvpResolvedEventKind.PlaybackEventScheduled)
            .TakeLast(8)
            .Select(resolvedEvent => FormatResolvedEventForDisplay(resolvedEvent.Text, meId, opponentId))
            .ToList();
    }

    private static IReadOnlyList<string> BuildDelayedSummary(PvpRoundResult result, ulong meId, ulong opponentId)
    {
        return result.Events
            .Where(resolvedEvent => resolvedEvent.Kind is PvpResolvedEventKind.DelayedPlanBuilt or PvpResolvedEventKind.DelayedCandidateScheduled)
            .TakeLast(8)
            .Select(resolvedEvent => FormatResolvedEventForDisplay(resolvedEvent.Text, meId, opponentId))
            .ToList();
    }

    private static IReadOnlyList<string> BuildDelayedCommandSummary(PvpRoundResult result, ulong meId, ulong opponentId)
    {
        return result.Events
            .Where(resolvedEvent => resolvedEvent.Kind is PvpResolvedEventKind.DelayedCommandPlanBuilt or PvpResolvedEventKind.DelayedCommandScheduled)
            .TakeLast(8)
            .Select(resolvedEvent => FormatResolvedEventForDisplay(resolvedEvent.Text, meId, opponentId))
            .ToList();
    }

    private static IReadOnlyList<string> BuildAuthoritativeSnapshotSummary(PvpRoundResult result, ulong meId, ulong opponentId)
    {
        List<string> lines = new();
        PvpCombatSnapshot snapshot = result.FinalSnapshot;
        lines.Add($"快照版本：{snapshot.SnapshotVersion}");

        lines.Add(FormatSnapshotLine("我方本体", snapshot.Heroes, meId, assumeExists: true));
        lines.Add(FormatSnapshotLine("我方前线", snapshot.Frontlines, meId, assumeExists: false));
        lines.Add(FormatSnapshotLine("对方本体", snapshot.Heroes, opponentId, assumeExists: true));
        lines.Add(FormatSnapshotLine("对方前线", snapshot.Frontlines, opponentId, assumeExists: false));
        return lines;
    }

    private static string FormatSnapshotLine(
        string label,
        IReadOnlyDictionary<ulong, PvpCreatureSnapshot> source,
        ulong playerId,
        bool assumeExists)
    {
        if (!source.TryGetValue(playerId, out PvpCreatureSnapshot? snapshot))
        {
            return $"{label}：缺失";
        }

        bool exists = assumeExists || snapshot.Exists;
        if (!exists)
        {
            return $"{label}：不存在";
        }

        return $"{label}：{snapshot.CurrentHp}/{snapshot.MaxHp}，格挡 {snapshot.Block}";
    }

    private static string FormatResolvedEventForDisplay(string text, ulong meId, ulong opponentId)
    {
        string replaced = text
            .Replace(meId.ToString(), "我方")
            .Replace(opponentId.ToString(), "对方");

        replaced = TranslateStructuredEvent(replaced);
        return TranslateTokens(replaced);
    }

    private static string TranslateStructuredEvent(string text)
    {
        Match match;

        match = Regex.Match(text, @"^Delta Buff player (.+) #(\d+): GainBlock (-?\d+) -> SelfFrontline via FRONTLINE_BRACE \[actionId=(.+)\]$");
        if (match.Success)
        {
            return $"差量[增益] 玩家{match.Groups[1].Value} 第{match.Groups[2].Value}步：前线固守给予前线 {match.Groups[3].Value} 点格挡 [动作ID={match.Groups[4].Value}]";
        }

        match = Regex.Match(text, @"^Delta Buff player (.+) #(\d+): GainBlock (-?\d+) -> SelfHero via FRONTLINE_BRACE \[actionId=(.+)\]$");
        if (match.Success)
        {
            return $"差量[增益] 玩家{match.Groups[1].Value} 第{match.Groups[2].Value}步：前线固守给予本体 {match.Groups[3].Value} 点格挡（当前无前线） [动作ID={match.Groups[4].Value}]";
        }

        match = Regex.Match(text, @"^Delta Attack player (.+) #(\d+): Damage (-?\d+) -> EnemyFrontline via (.+) \[actionId=(.+)\]$");
        if (match.Success)
        {
            return $"差量[攻击] 玩家{match.Groups[1].Value} 第{match.Groups[2].Value}步：造成 {match.Groups[3].Value} 点伤害 -> 敌方前线（先消耗其自身格挡），来源={match.Groups[4].Value} [动作ID={match.Groups[5].Value}]";
        }

        match = Regex.Match(text, @"^Playback (\w+) #(\d+): (\w+) (-?\d+) -> (\w+) via (.+) \[actionId=(.+)\]$");
        if (match.Success)
        {
            return $"回放[{TranslatePhase(match.Groups[1].Value)}] 第{match.Groups[2].Value}项：{TranslatePlaybackKind(match.Groups[3].Value)} {match.Groups[4].Value} -> {TranslateTargetKind(match.Groups[5].Value)}，来源={TranslateTokens(match.Groups[6].Value)} [动作ID={match.Groups[7].Value}]";
        }

        match = Regex.Match(text, @"^Resolved round (\d+) with (\d+) planned actions\.$");
        if (match.Success)
        {
            return $"已结算第{match.Groups[1].Value}回合，共{match.Groups[2].Value}个计划动作。";
        }

        match = Regex.Match(text, @"^Built execution plan for round (\d+): phases=(\d+), steps=(\d+)\.$");
        if (match.Success)
        {
            return $"已生成第{match.Groups[1].Value}回合执行计划：阶段数={match.Groups[2].Value}，步骤数={match.Groups[3].Value}。";
        }

        match = Regex.Match(text, @"^Built delta plan for round (\d+): operations=(\d+)\.$");
        if (match.Success)
        {
            return $"已生成第{match.Groups[1].Value}回合差量计划：操作数={match.Groups[2].Value}。";
        }

        match = Regex.Match(text, @"^Built delayed plan for round (\d+): candidates=(\d+)\.$");
        if (match.Success)
        {
            return $"已生成第{match.Groups[1].Value}回合延迟结算候选：候选数={match.Groups[2].Value}。";
        }

        match = Regex.Match(text, @"^Built delayed command plan for round (\d+): commands=(\d+)\.$");
        if (match.Success)
        {
            return $"已生成第{match.Groups[1].Value}回合延迟落地脚本：命令数={match.Groups[2].Value}。";
        }

        match = Regex.Match(text, @"^Built playback plan for round (\d+): events=(\d+), frames=(\d+)\.$");
        if (match.Success)
        {
            return $"已生成第{match.Groups[1].Value}回合回放计划：事件数={match.Groups[2].Value}，关键帧数={match.Groups[3].Value}。";
        }

        match = Regex.Match(text, @"^Delayed (\w+) player (.+) #(\d+): (\w+) (-?\d+) -> (.+) via (.+) \[actionId=(.+)\]$");
        if (match.Success)
        {
            return $"候选[{FormatPhase(match.Groups[1].Value)}] 玩家{match.Groups[2].Value} 第{match.Groups[3].Value}步：{FormatDelayedKind(match.Groups[4].Value)} {match.Groups[5].Value} -> {FormatTargetKind(match.Groups[6].Value)}，来源={match.Groups[7].Value} [动作ID={match.Groups[8].Value}]";
        }

        match = Regex.Match(text, @"^DelayedCommand (\w+) player (.+) #(\d+): (\w+) (-?\d+) -> (.+) via (.+) \[actionId=(.+)\] executor=(.+)$");
        if (match.Success)
        {
            return $"脚本[{FormatPhase(match.Groups[1].Value)}] 玩家{match.Groups[2].Value} 第{match.Groups[3].Value}步：{FormatDelayedCommandKind(match.Groups[4].Value)} {match.Groups[5].Value} -> {FormatTargetKind(match.Groups[6].Value)}，来源={TranslateTokens(match.Groups[7].Value)} [动作ID={match.Groups[8].Value}]，执行器={match.Groups[9].Value}";
        }

        match = Regex.Match(text, @"^Predicted round (\d+) snapshot from delta plan\.$");
        if (match.Success)
        {
            return $"已根据差量计划生成第{match.Groups[1].Value}回合预测快照。";
        }

        match = Regex.Match(text, @"^Player (.+) submitted (\d+) planned actions \((.+)\), energy=(\d+), locked=(True|False), first=(True|False)\.$");
        if (match.Success)
        {
            return $"玩家{match.Groups[1].Value}提交了{match.Groups[2].Value}个计划动作（{TranslateActionSummary(match.Groups[3].Value)}），起始能量={match.Groups[4].Value}，已锁定={FormatBool(match.Groups[5].Value)}，先锁定={FormatBool(match.Groups[6].Value)}。";
        }

        match = Regex.Match(text, @"^Player (.+) locked round (\d+)( first)?\.$");
        if (match.Success)
        {
            return $"玩家{match.Groups[1].Value}锁定了第{match.Groups[2].Value}回合{(string.IsNullOrEmpty(match.Groups[3].Value) ? string.Empty : "（先锁定）")}。";
        }

        match = Regex.Match(text, @"^Phase (\w+) scheduled (\d+) step\(s\)\.$");
        if (match.Success)
        {
            return $"阶段[{FormatPhase(match.Groups[1].Value)}]已计划{match.Groups[2].Value}步。";
        }

        match = Regex.Match(text, @"^Phase (\w+) player (.+) #(\d+): (\w+) (.+) -> (.+) \[actionId=(.+)\]$");
        if (match.Success)
        {
            return $"阶段[{FormatPhase(match.Groups[1].Value)}] 玩家{match.Groups[2].Value} 第{match.Groups[3].Value}步：{FormatActionType(match.Groups[4].Value)} {match.Groups[5].Value} -> {FormatTargetKind(match.Groups[6].Value)} [动作ID={match.Groups[7].Value}]";
        }

        match = Regex.Match(text, @"^Delta (\w+) player (.+) #(\d+): (\w+) (-?\d+) -> (.+) via (.+) \[actionId=(.+)\]$");
        if (match.Success)
        {
            return $"差量[{FormatPhase(match.Groups[1].Value)}] 玩家{match.Groups[2].Value} 第{match.Groups[3].Value}步：{FormatDeltaKind(match.Groups[4].Value)} {match.Groups[5].Value} -> {FormatTargetKind(match.Groups[6].Value)}，来源={match.Groups[7].Value} [动作ID={match.Groups[8].Value}]";
        }

        match = Regex.Match(text, @"^(Predicted )?(Hero|Frontline) (.+) exists (True|False) -> (True|False)$");
        if (match.Success)
        {
            string prefix = string.IsNullOrEmpty(match.Groups[1].Value) ? string.Empty : "预测";
            return $"{prefix}{FormatCreatureLabel(match.Groups[2].Value)} {match.Groups[3].Value} 存在状态：{FormatBool(match.Groups[4].Value)} -> {FormatBool(match.Groups[5].Value)}";
        }

        match = Regex.Match(text, @"^(Predicted )?(Hero|Frontline) (.+) hp (\d+)\/(\d+) -> (\d+)\/(\d+), block (\d+) -> (\d+)$");
        if (match.Success)
        {
            string prefix = string.IsNullOrEmpty(match.Groups[1].Value) ? string.Empty : "预测";
            return $"{prefix}{FormatCreatureLabel(match.Groups[2].Value)} {match.Groups[3].Value} 生命 {match.Groups[4].Value}/{match.Groups[5].Value} -> {match.Groups[6].Value}/{match.Groups[7].Value}，格挡 {match.Groups[8].Value} -> {match.Groups[9].Value}";
        }

        match = Regex.Match(text, @"^(Hero|Frontline) (.+) prediction matched actual\.$");
        if (match.Success)
        {
            return $"{FormatCreatureLabel(match.Groups[1].Value)} {match.Groups[2].Value}：预测与实际一致。";
        }

        match = Regex.Match(text, @"^(Hero|Frontline) (.+) prediction drift: predicted exists=(True|False) hp (\d+)\/(\d+) block (\d+), actual exists=(True|False) hp (\d+)\/(\d+) block (\d+)$");
        if (match.Success)
        {
            return $"{FormatCreatureLabel(match.Groups[1].Value)} {match.Groups[2].Value}：预测与实际存在偏差。预测=存在{FormatBool(match.Groups[3].Value)} 生命{match.Groups[4].Value}/{match.Groups[5].Value} 格挡{match.Groups[6].Value}；实际=存在{FormatBool(match.Groups[7].Value)} 生命{match.Groups[8].Value}/{match.Groups[9].Value} 格挡{match.Groups[10].Value}";
        }

        return text;
    }

    private static string TranslateActionSummary(string summary)
    {
        return summary
            .Replace("cards=", "卡牌=")
            .Replace("potions=", "药水=")
            .Replace("endTurn=", "结束回合=");
    }

    private static string TranslateTokens(string text)
    {
        return text
            .Replace("You", "我方")
            .Replace("Opponent", "对方")
            .Replace("PlayCard", "出牌")
            .Replace("UsePotion", "喝药")
            .Replace("EndRound", "结束回合")
            .Replace("EndTurn", "结束回合")
            .Replace("EnemyFrontline", "敌方前线")
            .Replace("EnemyHero", "敌方本体")
            .Replace("SelfFrontline", "我方前线")
            .Replace("SelfHero", "我方本体")
            .Replace("Frontline", "前线")
            .Replace("Hero", "本体")
            .Replace("True", "是")
            .Replace("False", "否");
    }

    private static string TranslatePlaybackKind(string value)
    {
        return value switch
        {
            "PhaseStarted" => "阶段开始",
            "SummonApplied" => "召唤生效",
            "BuffApplied" => "增益生效",
            "RecoverApplied" => "回复生效",
            "ResourceApplied" => "资源生效",
            "DamageApplied" => "伤害生效",
            "EndRoundApplied" => "结束回合",
            "StateSync" => "状态同步",
            _ => value
        };
    }

    private static string TranslatePhase(string value)
    {
        return FormatPhase(value);
    }

    private static string TranslateTargetKind(string value)
    {
        return FormatTargetKind(value);
    }

    private static string FormatBool(string value)
    {
        return value == "True" ? "是" : "否";
    }

    private static string FormatPhase(string value)
    {
        return value switch
        {
            "Summon" => "召唤",
            "Buff" => "增益",
            "Debuff" => "减益",
            "Recover" => "恢复",
            "Resource" => "资源",
            "Attack" => "攻击",
            "EndRound" => "结束回合",
            _ => value
        };
    }

    private static string FormatActionType(string value)
    {
        return value switch
        {
            "PlayCard" => "出牌",
            "UsePotion" => "喝药",
            "EndRound" => "结束回合",
            _ => value
        };
    }

    private static string FormatDeltaKind(string value)
    {
        return value switch
        {
            "SummonFrontline" => "召唤前线",
            "GainMaxHp" => "增加生命上限",
            "Heal" => "治疗",
            "GainBlock" => "获得格挡",
            "Damage" => "造成伤害",
            "GainResource" => "获得资源",
            "EndRoundMarker" => "回合结束标记",
            _ => value
        };
    }

    private static string FormatDelayedKind(string value)
    {
        return value switch
        {
            "SafeSelfBlock" => "可延迟-自身格挡",
            "SafeSelfHeal" => "可延迟-自身治疗",
            "SafeSelfResource" => "可延迟-自身资源",
            "SafeSelfMaxHp" => "可延迟-自身生命上限",
            "SafeSelfSummon" => "可延迟-自身召唤",
            "CrossDamage" => "可延迟-跨方伤害",
            "EndRoundMarker" => "回合结束标记",
            _ => value
        };
    }

    private static string FormatDelayedCommandKind(string value)
    {
        return value switch
        {
            "GainBlock" => "落地-格挡",
            "Heal" => "落地-治疗",
            "GainResource" => "落地-资源",
            "GainMaxHp" => "落地-生命上限",
            "SummonFrontline" => "落地-召唤前线",
            "Damage" => "落地-伤害",
            "EndRoundMarker" => "落地-回合结束标记",
            _ => value
        };
    }

    private static string FormatCreatureLabel(string value)
    {
        return value switch
        {
            "Hero" => "本体",
            "Frontline" => "前线",
            _ => value
        };
    }

    private static string FormatTargetKind(string value)
    {
        return value switch
        {
            "EnemyFrontline" => "敌方前线",
            "EnemyHero" => "敌方本体",
            "SelfFrontline" => "我方前线",
            "SelfHero" => "我方本体",
            _ => value
        };
    }

    private static string FormatShopRefreshType(PvpShopRefreshType refreshType)
    {
        return refreshType switch
        {
            PvpShopRefreshType.Normal => "普通刷新",
            PvpShopRefreshType.ClassBias => "大类刷新",
            PvpShopRefreshType.RoleFix => "构筑修复",
            PvpShopRefreshType.ArchetypeTrace => "流派追踪",
            _ => refreshType.ToString()
        };
    }

    private static string FormatShopSlotKind(PvpShopSlotKind slotKind)
    {
        return slotKind switch
        {
            PvpShopSlotKind.CoreArchetype => "核心方向",
            PvpShopSlotKind.RoleFix => "功能修复",
            PvpShopSlotKind.ClassBias => "大类定向",
            PvpShopSlotKind.Pivot => "转型槽",
            PvpShopSlotKind.HighCeiling => "高天花板",
            _ => slotKind.ToString()
        };
    }

    private static string FormatNetRole(NetGameType netType)
    {
        return netType switch
        {
            NetGameType.Host => "主机",
            NetGameType.Client => "客机",
            NetGameType.Singleplayer => "单机",
            _ => netType.ToString()
        };
    }

    private static bool TryResolvePurchaseSlotHotkey(Key key, out int slotIndex)
    {
        slotIndex = key switch
        {
            Key.Kp1 or Key.Key1 => 0,
            Key.Kp2 or Key.Key2 => 1,
            Key.Kp3 or Key.Key3 => 2,
            Key.Kp4 or Key.Key4 => 3,
            Key.Kp5 or Key.Key5 => 4,
            _ => -1
        };

        return slotIndex >= 0;
    }

    private static string FormatCategory(PvpIntentCategory category)
    {
        return category switch
        {
            PvpIntentCategory.Attack => "攻击",
            PvpIntentCategory.Guard => "防御",
            PvpIntentCategory.Buff => "增益",
            PvpIntentCategory.Debuff => "减益",
            PvpIntentCategory.Summon => "召唤",
            PvpIntentCategory.Recover => "恢复",
            PvpIntentCategory.Resource => "资源",
            _ => "未知"
        };
    }

    private static string FormatSide(PvpIntentTargetSide side)
    {
        return side switch
        {
            PvpIntentTargetSide.Self => "我方",
            PvpIntentTargetSide.Enemy => "敌方",
            _ => "-"
        };
    }
}
