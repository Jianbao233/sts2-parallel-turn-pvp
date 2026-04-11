using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using ParallelTurnPvp.Core;

namespace ParallelTurnPvp.Models.Cards;

public sealed class FrontlineBrace : CardModel
{
    public FrontlineBrace() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self, true)
    {
    }

    public override bool GainsBlock => true;

    protected override IEnumerable<DynamicVar> CanonicalVars
    {
        get
        {
            return new[] { new BlockVar(5m, ValueProp.Move) };
        }
    }

    public override string PortraitPath => ModelDb.Card<MegaCrit.Sts2.Core.Models.Cards.Afterlife>().PortraitPath;
    public override string BetaPortraitPath => ModelDb.Card<MegaCrit.Sts2.Core.Models.Cards.Afterlife>().BetaPortraitPath;

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (PvpDelayedExecution.ShouldDelayLiveApply(Owner, Id.Entry))
        {
            Log.Info($"[ParallelTurnPvp] Deferred immediate effect for {Id.Entry}. effect will be applied during round resolution.");
            await Task.CompletedTask;
            return;
        }

        var target = ParallelTurnFrontlineHelper.GetFrontline(Owner) ?? Owner.Creature;
        await CreatureCmd.GainBlock(target, DynamicVars.Block, cardPlay, false);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3m);
    }
}
