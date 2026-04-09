using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using ParallelTurnPvp.Core;

namespace ParallelTurnPvp.Models.Potions;

public sealed class FrontlineSalve : PotionModel
{
    private const string HealKey = "HealAmount";
    private static readonly HashSet<string> LoggedAssetOverrides = new(StringComparer.Ordinal);

    public override PotionRarity Rarity => PotionRarity.Common;
    public override PotionUsage Usage => PotionUsage.CombatOnly;
    public override TargetType TargetType => TargetType.AnyPlayer;

    public string CustomImagePath => ModelDb.Potion<BlockPotion>().ImagePath;
    public string? CustomOutlinePath => ModelDb.Potion<BlockPotion>().OutlinePath;
    public string CustomPackedImagePath => CustomImagePath;
    public string? CustomPackedOutlinePath => CustomOutlinePath;
    public Texture2D CustomImage => ModelDb.Potion<BlockPotion>().Image;
    public Texture2D? CustomOutline => ModelDb.Potion<BlockPotion>().Outline;

    protected override IEnumerable<DynamicVar> CanonicalVars
    {
        get
        {
            return new[] { new DynamicVar(HealKey, 8m) };
        }
    }

    protected override async Task OnUse(PlayerChoiceContext choiceContext, Creature? target)
    {
        var actualTarget = ParallelTurnFrontlineHelper.GetFrontline(Owner) ?? Owner.Creature;
        await CreatureCmd.Heal(actualTarget, DynamicVars[HealKey].BaseValue, true);
    }

    private static void LogAssetOverride(string propertyName, string value)
    {
        if (!LoggedAssetOverrides.Add(propertyName))
        {
            return;
        }

        Log.Info($"[ParallelTurnPvp] FrontlineSalve asset override {propertyName} -> {value}");
    }

    private static void LogAssetOverride(string propertyName, Texture2D value)
    {
        if (!LoggedAssetOverrides.Add(propertyName))
        {
            return;
        }

        Log.Info($"[ParallelTurnPvp] FrontlineSalve asset override {propertyName} -> {value.ResourcePath}");
    }

    [HarmonyPatch(typeof(PotionModel), nameof(ImagePath), MethodType.Getter)]
    private static class FrontlineSalveImagePathPatch
    {
        static bool Prefix(PotionModel __instance, ref string __result)
        {
            if (__instance is not FrontlineSalve model)
            {
                return true;
            }

            __result = model.CustomImagePath;
            LogAssetOverride(nameof(ImagePath), __result);
            return false;
        }
    }

    [HarmonyPatch(typeof(PotionModel), nameof(OutlinePath), MethodType.Getter)]
    private static class FrontlineSalveOutlinePathPatch
    {
        static bool Prefix(PotionModel __instance, ref string? __result)
        {
            if (__instance is not FrontlineSalve model)
            {
                return true;
            }

            __result = model.CustomOutlinePath;
            LogAssetOverride(nameof(OutlinePath), __result ?? "<null>");
            return false;
        }
    }

    [HarmonyPatch(typeof(PotionModel), "PackedImagePath", MethodType.Getter)]
    private static class FrontlineSalvePackedImagePatch
    {
        static bool Prefix(PotionModel __instance, ref string __result)
        {
            if (__instance is not FrontlineSalve model)
            {
                return true;
            }

            __result = model.CustomPackedImagePath;
            LogAssetOverride("PackedImagePath", __result);
            return false;
        }
    }

    [HarmonyPatch(typeof(PotionModel), "PackedOutlinePath", MethodType.Getter)]
    private static class FrontlineSalvePackedOutlinePatch
    {
        static bool Prefix(PotionModel __instance, ref string? __result)
        {
            if (__instance is not FrontlineSalve model)
            {
                return true;
            }

            __result = model.CustomPackedOutlinePath;
            LogAssetOverride("PackedOutlinePath", __result ?? "<null>");
            return false;
        }
    }

    [HarmonyPatch(typeof(PotionModel), nameof(Image), MethodType.Getter)]
    private static class FrontlineSalveImagePatch
    {
        static bool Prefix(PotionModel __instance, ref Texture2D __result)
        {
            if (__instance is not FrontlineSalve model)
            {
                return true;
            }

            __result = model.CustomImage;
            LogAssetOverride(nameof(Image), __result);
            return false;
        }
    }

    [HarmonyPatch(typeof(PotionModel), nameof(Outline), MethodType.Getter)]
    private static class FrontlineSalveOutlinePatch
    {
        static bool Prefix(PotionModel __instance, ref Texture2D? __result)
        {
            if (__instance is not FrontlineSalve model)
            {
                return true;
            }

            __result = model.CustomOutline;
            if (__result != null)
            {
                LogAssetOverride(nameof(Outline), __result);
            }
            else
            {
                LogAssetOverride(nameof(Outline), "<null>");
            }

            return false;
        }
    }
}
