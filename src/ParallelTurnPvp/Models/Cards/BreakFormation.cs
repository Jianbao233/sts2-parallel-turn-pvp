using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using ParallelTurnPvp.Core;

namespace ParallelTurnPvp.Models.Cards;

public sealed class BreakFormation : CardModel
{
    public BreakFormation() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy, true)
    {
    }

    protected override IEnumerable<DynamicVar> CanonicalVars
    {
        get
        {
            return new[] { new DamageVar(8m, ValueProp.Move) };
        }
    }

    public override string PortraitPath => ModelDb.Card<MegaCrit.Sts2.Core.Models.Cards.StrikeNecrobinder>().PortraitPath;
    public override string BetaPortraitPath => ModelDb.Card<MegaCrit.Sts2.Core.Models.Cards.StrikeNecrobinder>().BetaPortraitPath;

    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (PvpDelayedExecution.ShouldDelayLiveApply(Owner, Id.Entry))
        {
            Log.Info($"[ParallelTurnPvp] Deferred immediate effect for {Id.Entry}. effect will be applied during round resolution.");
            await Task.CompletedTask;
            return;
        }

        ArgumentNullException.ThrowIfNull(cardPlay.Target);
        var target = ParallelTurnFrontlineHelper.ResolveEnemyFrontlineOrHero(Owner, cardPlay.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue)
            .FromCard(this)
            .Targeting(target)
            .WithHitFx("vfx/vfx_attack_slash", null, null)
            .Execute(choiceContext);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3m);
    }
}
