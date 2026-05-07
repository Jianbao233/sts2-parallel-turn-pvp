using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using ParallelTurnPvp.Core;

namespace ParallelTurnPvp.Ui;

public readonly record struct PvpShopDeckEntry(int DeckIndex, string CardId);

public partial class PvpShopDebugPanel : PanelContainer
{
    private sealed class OfferRow
    {
        public required Label Label { get; init; }
        public required Button BuyButton { get; init; }
        public int SlotIndex { get; set; } = -1;
    }

    private readonly List<OfferRow> _offerRows = new();
    private readonly List<int> _deckIndices = new();
    private Label _metaLabel = null!;
    private Label _statusLabel = null!;
    private Button _refreshButton = null!;
    private ItemList _deckList = null!;
    private Button _deleteButton = null!;
    private Action<PvpShopRefreshType>? _onRefresh;
    private Action<int>? _onPurchase;
    private Action<int>? _onDelete;

    public void ConfigureActions(Action<PvpShopRefreshType> onRefresh, Action<int> onPurchase, Action<int> onDelete)
    {
        _onRefresh = onRefresh;
        _onPurchase = onPurchase;
        _onDelete = onDelete;
    }

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.TopRight);
        OffsetLeft = -740f;
        OffsetTop = 110f;
        OffsetRight = -380f;
        OffsetBottom = 590f;
        MouseFilter = MouseFilterEnum.Pass;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 10);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 10);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        AddChild(margin);

        var root = new VBoxContainer();
        root.AddThemeConstantOverride("separation", 6);
        margin.AddChild(root);

        var title = new Label
        {
            Text = "PvP商店"
        };
        root.AddChild(title);

        _metaLabel = new Label
        {
            Text = "等待商店状态..."
        };
        _metaLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_metaLabel);

        _refreshButton = new Button
        {
            Text = "普通刷新"
        };
        _refreshButton.Pressed += () => _onRefresh?.Invoke(PvpShopRefreshType.Normal);
        root.AddChild(_refreshButton);

        var offerHeader = new Label
        {
            Text = "卡牌报价（5槽位）"
        };
        root.AddChild(offerHeader);

        var offerList = new VBoxContainer();
        offerList.AddThemeConstantOverride("separation", 4);
        root.AddChild(offerList);

        for (int i = 0; i < 5; i++)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);

            var label = new Label
            {
                Text = $"#{i + 1} -"
            };
            label.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            row.AddChild(label);

            int rowIndex = i;
            var buyButton = new Button
            {
                Text = "购买"
            };
            buyButton.Pressed += () =>
            {
                if (rowIndex < 0 || rowIndex >= _offerRows.Count)
                {
                    return;
                }

                int slot = _offerRows[rowIndex].SlotIndex;
                if (slot >= 0)
                {
                    _onPurchase?.Invoke(slot);
                }
            };
            row.AddChild(buyButton);
            offerList.AddChild(row);

            _offerRows.Add(new OfferRow
            {
                Label = label,
                BuyButton = buyButton
            });
        }

        var removeHeader = new Label
        {
            Text = "删卡（从当前牌组）"
        };
        root.AddChild(removeHeader);

        _deckList = new ItemList
        {
            SelectMode = ItemList.SelectModeEnum.Single,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        _deckList.CustomMinimumSize = new Vector2(0f, 140f);
        root.AddChild(_deckList);

        _deleteButton = new Button
        {
            Text = "删卡"
        };
        _deleteButton.Pressed += OnDeletePressed;
        root.AddChild(_deleteButton);

        _statusLabel = new Label
        {
            Text = "最近动作：-"
        };
        _statusLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        root.AddChild(_statusLabel);
    }

    public void UpdateState(PvpShopViewModel? view, IReadOnlyList<PvpShopDeckEntry> deckEntries, NetGameType netType, string statusText)
    {
        _statusLabel.Text = $"最近动作：{(string.IsNullOrWhiteSpace(statusText) ? "-" : statusText)}";
        if (view == null || !view.IsOpen)
        {
            _metaLabel.Text = "商店未开启（等待主机开店）。";
            _refreshButton.Disabled = true;
            _deleteButton.Disabled = true;
            foreach (OfferRow row in _offerRows)
            {
                row.SlotIndex = -1;
                row.Label.Text = "-";
                row.BuyButton.Disabled = true;
            }

            _deckIndices.Clear();
            _deckList.Clear();
            return;
        }

        int refreshCost = view.RefreshCosts.TryGetValue(PvpShopRefreshType.Normal, out int configuredRefreshCost) ? configuredRefreshCost : 0;
        _metaLabel.Text = $"金币 {view.Gold} | 刷新次数 {view.RefreshCount} | 版本 {view.StateVersion} | 身份 {FormatNetRole(netType)}";
        _refreshButton.Text = $"普通刷新 (-{refreshCost})";
        _refreshButton.Disabled = false;
        _deleteButton.Text = $"删卡 (-{view.DeleteCost})";
        _deleteButton.Disabled = deckEntries.Count == 0;

        List<PvpShopOffer> offers = view.Offers
            .OrderBy(offer => offer.SlotIndex)
            .Take(5)
            .ToList();
        for (int i = 0; i < _offerRows.Count; i++)
        {
            OfferRow row = _offerRows[i];
            if (i >= offers.Count)
            {
                row.SlotIndex = -1;
                row.Label.Text = $"#{i + 1} -";
                row.BuyButton.Disabled = true;
                continue;
            }

            PvpShopOffer offer = offers[i];
            row.SlotIndex = offer.SlotIndex;
            row.Label.Text = $"#{i + 1} {offer.DisplayName} [{FormatSlotKind(offer.SlotKind)}] {offer.Price}金";
            bool affordable = view.Gold >= offer.Price;
            row.BuyButton.Disabled = !offer.Available || !affordable;
            row.BuyButton.Text = !offer.Available ? "已售" : affordable ? "购买" : "缺金";
        }

        int selectedDeckRow = -1;
        int[] selectedRows = _deckList.GetSelectedItems();
        if (selectedRows.Length > 0)
        {
            selectedDeckRow = selectedRows[0];
        }

        _deckIndices.Clear();
        _deckList.Clear();
        for (int i = 0; i < deckEntries.Count; i++)
        {
            PvpShopDeckEntry entry = deckEntries[i];
            _deckIndices.Add(entry.DeckIndex);
            _deckList.AddItem($"[{entry.DeckIndex}] {entry.CardId}");
        }

        if (_deckIndices.Count > 0)
        {
            int resolvedSelection = selectedDeckRow >= 0 && selectedDeckRow < _deckIndices.Count ? selectedDeckRow : 0;
            _deckList.Select(resolvedSelection);
        }
    }

    private void OnDeletePressed()
    {
        int[] selectedRows = _deckList.GetSelectedItems();
        if (selectedRows.Length == 0)
        {
            return;
        }

        int selectedRow = selectedRows[0];
        if (selectedRow < 0 || selectedRow >= _deckIndices.Count)
        {
            return;
        }

        _onDelete?.Invoke(_deckIndices[selectedRow]);
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

    private static string FormatSlotKind(PvpShopSlotKind slotKind)
    {
        return slotKind switch
        {
            PvpShopSlotKind.CoreArchetype => "核心",
            PvpShopSlotKind.RoleFix => "修复",
            PvpShopSlotKind.ClassBias => "定向",
            PvpShopSlotKind.Pivot => "转型",
            PvpShopSlotKind.HighCeiling => "高天花板",
            _ => slotKind.ToString()
        };
    }
}
