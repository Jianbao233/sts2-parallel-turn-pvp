using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ParallelTurnPvp.Core;

namespace ParallelTurnPvp.Ui;

public partial class ParallelTurnIntentOverlay : Control
{
    private const string OverlayNodeName = "ParallelTurnIntentOverlay";
    private Label _title = null!;
    private RichTextLabel _body = null!;
    private double _refreshAccumulator;
    private string _lastRendered = string.Empty;

    public static void EnsureAttached(NCombatRoom room)
    {
        if (room.GetNodeOrNull<ParallelTurnIntentOverlay>(OverlayNodeName) != null)
        {
            return;
        }

        var overlay = new ParallelTurnIntentOverlay
        {
            Name = OverlayNodeName
        };
        room.Ui.AddChild(overlay);
        Log.Info("[ParallelTurnPvp] Intent overlay attached to combat UI.");
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
            Text = "Opponent Intent"
        };
        layout.AddChild(_title);

        _body = new RichTextLabel
        {
            BbcodeEnabled = false,
            ScrollActive = false,
            FitContent = true,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore
        };
        _body.SizeFlagsVertical = SizeFlags.ExpandFill;
        layout.AddChild(_body);

        RefreshNow();
    }

    public override void _Process(double delta)
    {
        _refreshAccumulator += delta;
        if (_refreshAccumulator < 0.15d)
        {
            return;
        }

        _refreshAccumulator = 0d;
        RefreshNow();
    }

    private void RefreshNow()
    {
        if (RunManager.Instance.DebugOnlyGetState() is not RunState runState || !runState.Modifiers.Any(mod => mod.GetType().Name == "ParallelTurnPvpDebugModifier"))
        {
            Visible = false;
            return;
        }

        var me = LocalContext.GetMe(runState);
        if (me == null)
        {
            Visible = false;
            return;
        }

        var opponent = runState.Players.FirstOrDefault(player => player.NetId != me.NetId);
        if (opponent == null)
        {
            Visible = false;
            return;
        }

        var runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        var view = runtime.GetIntentView(me.NetId, opponent.NetId);
        if (view == null)
        {
            Visible = false;
            return;
        }

        string rendered = BuildText(view, runtime.LastAuthoritativeResult, me.NetId, opponent.NetId);
        _title.Text = $"Opponent Intent  R{view.RoundIndex}";
        if (_lastRendered != rendered)
        {
            _body.Text = rendered;
            _lastRendered = rendered;
        }

        Visible = true;
    }

    private static string BuildText(PvpIntentView view, PvpRoundResult? lastResult, ulong meId, ulong opponentId)
    {
        int totalCount = view.VisibleCount + view.HiddenCount;
        var lines = new List<string>
        {
            "[Intent]",
            $"Start Energy: {view.RoundStartEnergy}",
            $"State: {(view.Locked ? "Locked" : "Planning")}{(view.IsFirstFinisher ? " | First lock" : string.Empty)}",
            $"Reveal: {(totalCount == 0 ? 0 : view.VisibleCount)}/{totalCount}",
            $"Visible Budget: {view.RevealBudget}"
        };

        if (view.VisibleCount == 0 && view.HiddenCount == 0)
        {
            lines.Add("No actions submitted.");
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
        lines.Add("[Last Resolved Round]");
        if (lastResult == null)
        {
            lines.Add("No resolved round yet.");
        }
        else
        {
            lines.Add($"Round: {lastResult.RoundIndex}");
            foreach (string eventLine in BuildRoundSummary(lastResult, meId, opponentId))
            {
                lines.Add(eventLine);
            }
        }

        return string.Join('\n', lines);
    }

    private static IEnumerable<string> BuildRoundSummary(PvpRoundResult result, ulong meId, ulong opponentId)
    {
        if (result.Events.Count == 0)
        {
            yield return "No summary events.";
            yield break;
        }

        foreach (PvpResolvedEvent resolvedEvent in result.Events.TakeLast(10))
        {
            yield return ReplacePlayerIds(resolvedEvent.Text, meId, opponentId);
        }
    }

    private static string ReplacePlayerIds(string text, ulong meId, ulong opponentId)
    {
        return text
            .Replace(meId.ToString(), "You")
            .Replace(opponentId.ToString(), "Opponent")
            .Replace("PlayCard", "Card")
            .Replace("UsePotion", "Potion")
            .Replace("EndRound", "EndTurn");
    }

    private static string FormatCategory(PvpIntentCategory category)
    {
        return category switch
        {
            PvpIntentCategory.Attack => "Attack",
            PvpIntentCategory.Guard => "Guard",
            PvpIntentCategory.Buff => "Buff",
            PvpIntentCategory.Debuff => "Debuff",
            PvpIntentCategory.Summon => "Summon",
            PvpIntentCategory.Recover => "Recover",
            PvpIntentCategory.Resource => "Resource",
            _ => "Unknown"
        };
    }

    private static string FormatSide(PvpIntentTargetSide side)
    {
        return side switch
        {
            PvpIntentTargetSide.Self => "Self",
            PvpIntentTargetSide.Enemy => "Enemy",
            _ => "-"
        };
    }
}
