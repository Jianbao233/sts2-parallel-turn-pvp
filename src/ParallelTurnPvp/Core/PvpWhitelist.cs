using MegaCrit.Sts2.Core.Models;

namespace ParallelTurnPvp.Core;

public static class PvpWhitelist
{
    public const int ExpectedDeckSize = 10;

    public static readonly IReadOnlySet<string> CardIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "STRIKE_NECROBINDER",
        "DEFEND_NECROBINDER",
        "AFTERLIFE",
        "POKE",
        "FRONTLINE_BRACE",
        "BREAK_FORMATION"
    };

    public static readonly IReadOnlySet<string> PotionIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "BLOCK_POTION",
        "ENERGY_POTION",
        "BLOOD_POTION",
        "FRONTLINE_SALVE"
    };

    public static readonly IReadOnlySet<string> RelicIds = new HashSet<string>(StringComparer.Ordinal)
    {
        "BOUND_PHYLACTERY",
        "OPENING_SIGNAL"
    };

    public static bool IsAllowed(CardModel card) => CardIds.Contains(card.Id.Entry);
    public static bool IsAllowed(PotionModel potion) => PotionIds.Contains(potion.Id.Entry);
    public static bool IsAllowed(RelicModel relic) => RelicIds.Contains(relic.Id.Entry);
}
