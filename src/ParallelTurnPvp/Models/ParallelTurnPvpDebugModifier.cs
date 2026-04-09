using System.Globalization;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using ParallelTurnPvp.Bootstrap;
using ParallelTurnPvp.Core;

namespace ParallelTurnPvp.Models;

public sealed class ParallelTurnPvpDebugModifier : ModifierModel
{
    private static readonly HashSet<string> LoggedDeniedCards = new(StringComparer.Ordinal);
    private ulong _winnerNetId;

    [SavedProperty(SerializationCondition.AlwaysSave, 0)]
    public int ProtocolVersionField { get; set; } = ParallelTurnPvpMod.ProtocolVersion;

    [SavedProperty(SerializationCondition.AlwaysSave, 1)]
    public int ContentVersionField { get; set; } = ParallelTurnPvpMod.ContentVersion;

    [SavedProperty(SerializationCondition.AlwaysSave, 2)]
    public bool MatchEnded { get; set; }

    public ulong WinnerNetId
    {
        get => _winnerNetId;
        set => _winnerNetId = value;
    }

    [SavedProperty(SerializationCondition.AlwaysSave, 3)]
    private string WinnerNetIdSerialized
    {
        get => _winnerNetId.ToString(CultureInfo.InvariantCulture);
        set => _winnerNetId = ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)
            ? parsed
            : 0uL;
    }

    [SavedProperty(SerializationCondition.AlwaysSave, 4)]
    public int CurrentRoundIndex { get; set; } = 1;

    public override bool ClearsPlayerDeck => true;

    public override LocString Title => new("modifiers", "ParallelTurnPvP_Debug.title");
    public override LocString Description => new("modifiers", "ParallelTurnPvP_Debug.description");
    public override LocString NeowOptionTitle => new("modifiers", "ParallelTurnPvP_Debug.neowTitle");
    public override LocString NeowOptionDescription => new("modifiers", "ParallelTurnPvP_Debug.neowDescription");

    public override Func<Task>? GenerateNeowOption(EventModel eventModel)
    {
        return () => ParallelTurnPvpArenaBootstrap.RunPreparationAsync(eventModel);
    }

    public override Creature ModifyUnblockedDamageTarget(Creature target, decimal amount, ValueProp props, Creature? dealer)
    {
        return ParallelTurnFrontlineHelper.ResolveProtectedTarget(target);
    }

    public override bool ShouldPlay(CardModel card, AutoPlayType autoPlayType)
    {
        bool allowed = PvpWhitelist.IsAllowed(card);
        if (!allowed && LoggedDeniedCards.Add(card.Id.Entry))
        {
            Log.Warn($"[ParallelTurnPvp] Card denied by whitelist: {card.Id.Entry}");
        }

        return allowed;
    }

    protected override void AfterRunCreated(RunState runState)
    {
        ProtocolVersionField = ParallelTurnPvpMod.ProtocolVersion;
        ContentVersionField = ParallelTurnPvpMod.ContentVersion;
        MatchEnded = false;
        WinnerNetId = 0;
        CurrentRoundIndex = 1;
    }
}
