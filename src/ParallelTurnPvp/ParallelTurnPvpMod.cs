using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Saves.Runs;
using ParallelTurnPvp.Core;
using ParallelTurnPvp.Models;
using ParallelTurnPvp.Models.Cards;
using ParallelTurnPvp.Models.Potions;
using ParallelTurnPvp.Models.Relics;

namespace ParallelTurnPvp;

public static class ParallelTurnPvpMod
{
    public const string ModId = "ParallelTurnPvp";
    public const int ProtocolVersion = 35;
    public const int ContentVersion = 35;

    private static bool _initialized;

    public static void Initialize()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        SavedPropertiesTypeCache.InjectTypeIntoCache(typeof(ParallelTurnPvpDebugModifier));

        var harmony = new Harmony(ModId);
        harmony.PatchAll();
        DirectConnectIpCompat.TryPatchRunningRejoinPath(harmony);

        ScriptManagerBridge.LookupScriptsInAssembly(typeof(ParallelTurnPvpMod).Assembly);
        PvpNetBridge.EnsureRegistered();
        if (PvpShopFeatureFlags.IsShopDraftEnabled)
        {
            PvpShopNetBridge.EnsureRegistered();
        }

        ModHelper.AddModelToPool(typeof(NecrobinderCardPool), typeof(FrontlineBrace));
        ModHelper.AddModelToPool(typeof(NecrobinderCardPool), typeof(BreakFormation));
        ModHelper.AddModelToPool(typeof(NecrobinderRelicPool), typeof(OpeningSignal));
        ModHelper.AddModelToPool(typeof(NecrobinderPotionPool), typeof(FrontlineSalve));

        Log.Info($"[ParallelTurnPvp] initialized. cards=[{ModelDb.GetId<FrontlineBrace>().Entry}, {ModelDb.GetId<BreakFormation>().Entry}] relics=[{ModelDb.GetId<OpeningSignal>().Entry}] potions=[{ModelDb.GetId<FrontlineSalve>().Entry}] splitRoomEnabled={PvpSplitRoomConfig.IsSplitRoomEnabled} clientReadOnlyResolve={PvpResolveConfig.IsClientReadOnlyResolveEnabled}");
    }
}
