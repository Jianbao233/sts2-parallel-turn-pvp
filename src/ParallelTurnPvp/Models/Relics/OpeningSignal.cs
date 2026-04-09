using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace ParallelTurnPvp.Models.Relics;

public sealed class OpeningSignal : RelicModel
{
    private bool _grantedThisCombat;

    public override RelicRarity Rarity => RelicRarity.Starter;

    protected override IEnumerable<DynamicVar> CanonicalVars
    {
        get
        {
            return new[] { new EnergyVar(1) };
        }
    }

    protected override IEnumerable<IHoverTip> ExtraHoverTips => new[] { HoverTipFactory.ForEnergy(this) };

    public override string PackedIconPath => ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.BoundPhylactery>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/bound_phylactery.tres";
    protected override string BigIconPath => "res://images/relics/bound_phylactery.png";

    public override Task BeforeCombatStart()
    {
        _grantedThisCombat = false;
        return Task.CompletedTask;
    }

    public override async Task AfterEnergyResetLate(Player player)
    {
        if (player != Owner || _grantedThisCombat)
        {
            return;
        }

        if (player.Creature.CombatState?.RoundNumber == 1)
        {
            _grantedThisCombat = true;
            Flash();
            await PlayerCmd.GainEnergy(DynamicVars.Energy.BaseValue, player);
        }
    }
}

