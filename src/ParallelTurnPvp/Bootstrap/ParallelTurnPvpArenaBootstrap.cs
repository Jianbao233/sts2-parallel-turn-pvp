using Godot;
using HarmonyLib;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Platform.Steam;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using ParallelTurnPvp.Core;
using ParallelTurnPvp.Models;
using ParallelTurnPvp.Models.Cards;
using ParallelTurnPvp.Models.Potions;
using ParallelTurnPvp.Models.Relics;
using System.Runtime.CompilerServices;

namespace ParallelTurnPvp.Bootstrap;

public static class ParallelTurnPvpArenaBootstrap
{
    private const string DebugScreenMetaKey = "ParallelTurnPvpDebug";
    private static bool _pendingHostDebugStart;
    private static readonly ConditionalWeakTable<RunState, ArenaPreparationState> PreparationStates = new();

    public static bool IsDebugModifier(ModifierModel modifier)
    {
        return modifier is ParallelTurnPvpDebugModifier;
    }

    public static bool IsDebugLobby(StartRunLobby lobby)
    {
        return lobby.Modifiers.Any(IsDebugModifier);
    }

    public static IReadOnlyList<ModifierModel> CreateLockedModifierList()
    {
        return new List<ModifierModel>
        {
            ModelDb.Modifier<ParallelTurnPvpDebugModifier>().ToMutable()
        };
    }

    public static async Task StartHostDebugAsync(Control loadingOverlay, NSubmenuStack stack)
    {
        Log.Info("[ParallelTurnPvp] Requested debug host flow from multiplayer submenu.");
        DirectConnectIpCompat.TryEnableEnetHostMode();
        _pendingHostDebugStart = true;
        try
        {
            await NMultiplayerHostSubmenu.StartHostAsync(GameMode.Custom, loadingOverlay, stack);
        }
        finally
        {
            if (_pendingHostDebugStart)
            {
                Log.Info("[ParallelTurnPvp] Debug host flow finished without consuming pending flag; resetting.");
                _pendingHostDebugStart = false;
            }
        }
    }

    public static bool ConsumePendingHostDebugStart()
    {
        if (!_pendingHostDebugStart)
        {
            return false;
        }

        _pendingHostDebugStart = false;
        Log.Info("[ParallelTurnPvp] Consumed pending debug host flag while initializing custom run screen.");
        return true;
    }

    public static void ConfigureCustomLobbyScreen(NCustomRunScreen screen, bool forceDebug = false)
    {
        PvpNetBridge.EnsureRegistered();

        StartRunLobby? lobby = Traverse.Create(screen).Field("_lobby").GetValue<StartRunLobby>();
        if (lobby == null)
        {
            return;
        }

        if (forceDebug && lobby.NetService.Type != NetGameType.Client && !IsDebugLobby(lobby))
        {
            lobby.SetModifiers(CreateLockedModifierList().ToList());
        }

        bool isDebug = forceDebug || IsDebugLobby(lobby);
        screen.SetMeta(DebugScreenMetaKey, isDebug);
        if (!isDebug)
        {
            return;
        }

        Log.Info($"[ParallelTurnPvp] Configuring debug custom run screen. forceDebug={forceDebug}, netType={lobby.NetService.Type}");
        ForceLocalCharacter(lobby);
        GetNodeOrNull<MegaLabel>(screen, "%CustomModeTitle")?.SetTextAutoSize("ParallelTurn PvP");
        HideNode(screen, "%AscensionPanel");
        HideNode(screen, "%ModifiersList");
        HideNode(screen, "%ModifiersTitle");
        HideNode(screen, "%ModifiersHotkeyIcon");
        HideNode(screen, "%SeedInput");
        HideNode(screen, "%SeedLabel");
    }

    public static bool IsDebugScreen(Node screen)
    {
        Variant value = screen.GetMeta(DebugScreenMetaKey, false);
        return value.VariantType == Variant.Type.Bool && value.AsBool();
    }

    public static void ForceLocalCharacter(StartRunLobby lobby)
    {
        if (lobby.LocalPlayer.character is not Necrobinder)
        {
            lobby.SetLocalCharacter(ModelDb.Character<Necrobinder>());
        }
    }

    public static async Task RunPreparationAsync(EventModel eventModel)
    {
        if (eventModel.Owner == null || eventModel.Owner.RunState is not RunState runState)
        {
            return;
        }

        ArenaPreparationState state = PreparationStates.GetOrCreateValue(runState);
        Log.Info($"[ParallelTurnPvp] RunPreparationAsync start. owner={eventModel.Owner.NetId}, prepared={state.PreparedPlayers.Count}/{runState.Players.Count}, combatEntryStarted={state.CombatEntryStarted}");

        if (state.TryMarkPrepared(eventModel.Owner.NetId))
        {
            PrepareLoadout(eventModel.Owner);
        }
        else
        {
            Log.Warn($"[ParallelTurnPvp] Duplicate preparation request ignored for player {eventModel.Owner.NetId}.");
        }

        while (!AllPlayersPrepared(runState))
        {
            await Task.Delay(100);
        }

        if (!state.TryBeginCombatEntry())
        {
            Log.Info($"[ParallelTurnPvp] Combat entry already started for run. owner={eventModel.Owner.NetId}");
            return;
        }

        await EnterCombatFromNeowAsync(eventModel, runState);
    }

    public static void PrepareLoadout(Player player)
    {
        if (player.RunState is not RunState runState)
        {
            return;
        }

        foreach (CardModel card in player.Deck.Cards.ToList())
        {
            player.Deck.RemoveInternal(card, true);
            runState.RemoveCard(card);
        }

        foreach (RelicModel relic in player.Relics.ToList())
        {
            player.RemoveRelicInternal(relic, true);
        }

        foreach (PotionModel potion in player.Potions.ToList())
        {
            player.DiscardPotionInternal(potion, true);
        }

        while (player.MaxPotionCount < 4)
        {
            player.AddToMaxPotionCount(1);
        }

        Log.Info($"[ParallelTurnPvp] Preparing loadout for player {player.NetId}. maxPotions={player.MaxPotionCount}");
        AddDeckCard<StrikeNecrobinder>(runState, player);
        AddDeckCard<StrikeNecrobinder>(runState, player);
        AddDeckCard<StrikeNecrobinder>(runState, player);
        AddDeckCard<DefendNecrobinder>(runState, player);
        AddDeckCard<DefendNecrobinder>(runState, player);
        AddDeckCard<DefendNecrobinder>(runState, player);
        AddDeckCard<Afterlife>(runState, player);
        AddDeckCard<Poke>(runState, player);
        AddDeckCard<FrontlineBrace>(runState, player);
        AddDeckCard<BreakFormation>(runState, player);

        AddRelic<BoundPhylactery>(player);
        AddRelic<OpeningSignal>(player);

        AddPotion<BlockPotion>(player);
        AddPotion<EnergyPotion>(player);
        AddPotion<BloodPotion>(player);
        AddPotion<FrontlineSalve>(player);

        Log.Info($"[ParallelTurnPvp] Prepared loadout for player {player.NetId}. deck=[{string.Join(", ", player.Deck.Cards.Select(card => card.Id.Entry))}] relics=[{string.Join(", ", player.Relics.Select(relic => relic.Id.Entry))}] potions=[{string.Join(", ", player.Potions.Select(potion => potion.Id.Entry))}]");
    }

    public static bool AllPlayersPrepared(RunState runState)
    {
        return runState.Players.All(player =>
            player.Deck.Cards.Count == PvpWhitelist.ExpectedDeckSize
            && player.Relics.Any(relic => relic is BoundPhylactery)
            && player.Relics.Any(relic => relic is OpeningSignal));
    }

    private static async Task EnterCombatFromNeowAsync(EventModel eventModel, RunState runState)
    {
        if (CombatManager.Instance.IsInProgress || eventModel is not Neow)
        {
            Log.Warn($"[ParallelTurnPvp] EnterCombatFromNeowAsync skipped. isInProgress={CombatManager.Instance.IsInProgress}, eventType={eventModel.GetType().Name}");
            return;
        }

        Log.Info($"[ParallelTurnPvp] EnterCombatFromNeowAsync starting. players={string.Join(", ", runState.Players.Select(player => player.NetId))}");
        BattlewornDummyEventEncounter encounter = (BattlewornDummyEventEncounter)ModelDb.Encounter<BattlewornDummyEventEncounter>().ToMutable();
        encounter.Setting = BattlewornDummyEventEncounter.DummySetting.Setting1;
        CombatRoom combatRoom = new(encounter, runState)
        {
            ShouldResumeParentEventAfterCombat = false,
            ParentEventId = eventModel.Id
        };

        await RunManager.Instance.EnterRoomWithoutExitingCurrentRoom(combatRoom, true);
        Log.Info("[ParallelTurnPvp] EnterCombatFromNeowAsync completed.");
    }

    private static void AddDeckCard<T>(RunState runState, Player player) where T : CardModel
    {
        CardModel card = ModelDb.Card<T>().ToMutable();
        card.FloorAddedToDeck = 1;
        runState.AddCard(card, player);
        player.Deck.AddInternal(card, -1, true);
        Log.Info($"[ParallelTurnPvp] Added card to player {player.NetId}: {card.Id.Entry}");
    }

    private static void AddRelic<T>(Player player) where T : RelicModel
    {
        RelicModel relic = ModelDb.Relic<T>().ToMutable();
        relic.FloorAddedToDeck = 1;
        player.AddRelicInternal(relic, -1, true);
        Log.Info($"[ParallelTurnPvp] Added relic to player {player.NetId}: {relic.Id.Entry}");
    }

    private static void AddPotion<T>(Player player) where T : PotionModel
    {
        PotionModel potion = ModelDb.Potion<T>().ToMutable();
        var result = player.AddPotionInternal(potion, -1, false);
        int slotIndex = player.PotionSlots.ToList().IndexOf(potion);
        Log.Info($"[ParallelTurnPvp] Added potion to player {player.NetId}: {potion.Id.Entry}, success={result.success}, slot={slotIndex}, failure={result.failureReason}");
    }

    private static void HideNode(Node owner, string path)
    {
        if (owner.HasNode(path))
        {
            owner.GetNode<CanvasItem>(path).Hide();
        }
    }

    private static T? GetNodeOrNull<T>(Node owner, string path) where T : Node
    {
        return owner.HasNode(path) ? owner.GetNode<T>(path) : null;
    }

    private sealed class ArenaPreparationState
    {
        private readonly object _lock = new();

        public HashSet<ulong> PreparedPlayers { get; } = [];

        public bool CombatEntryStarted { get; private set; }

        public bool TryMarkPrepared(ulong playerId)
        {
            lock (_lock)
            {
                return PreparedPlayers.Add(playerId);
            }
        }

        public bool TryBeginCombatEntry()
        {
            lock (_lock)
            {
                if (CombatEntryStarted)
                {
                    return false;
                }

                CombatEntryStarted = true;
                return true;
            }
        }
    }
}


