using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Runs;
using ParallelTurnPvp.Models.Cards;

namespace ParallelTurnPvp.Core;

public static class PvpShopDefaults
{
    public const string ConfigPath = "res://ParallelTurnPvp/config/shopdraft.standard.v1.json";
    public const string StandardModeId = "shop_draft_standard";
    public const string StandardModeVersion = "1.0.0";
    public const string StandardStrategyPackId = "shopdraft.standard.v1";
    public const string StandardStrategyVersion = "1.0.0";
    public const string RngVersion = "v1";
    public const int SchemaVersion = 1;
    public const int DefaultStartGold = 100;
    public const int DefaultSeenHistory = 16;
    public const int DefaultDeleteCost = 20;
}

public enum PvpShopRefreshType
{
    Normal,
    ClassBias,
    RoleFix,
    ArchetypeTrace
}

public enum PvpShopSlotKind
{
    CoreArchetype,
    RoleFix,
    ClassBias,
    Pivot,
    HighCeiling
}

public enum PvpShopClassBias
{
    None,
    Attack,
    Skill,
    Power
}

public sealed class PvpShopModeContext
{
    public string ModeId { get; init; } = string.Empty;
    public string ModeVersion { get; init; } = string.Empty;
    public string StrategyPackId { get; init; } = string.Empty;
    public string StrategyVersion { get; init; } = string.Empty;
    public string RngVersion { get; init; } = string.Empty;
    public int SchemaVersion { get; init; } = PvpShopDefaults.SchemaVersion;
}

public sealed class PvpShopModeDefinition
{
    public string ModeId { get; init; } = string.Empty;
    public string ModeVersion { get; init; } = PvpShopDefaults.StandardModeVersion;
    public string StrategyPackId { get; init; } = string.Empty;
    public int Slots { get; init; } = 5;
    public IReadOnlyList<PvpShopSlotKind> Template { get; init; } =
    [
        PvpShopSlotKind.CoreArchetype,
        PvpShopSlotKind.RoleFix,
        PvpShopSlotKind.ClassBias,
        PvpShopSlotKind.Pivot,
        PvpShopSlotKind.HighCeiling
    ];
    public IReadOnlyDictionary<PvpShopRefreshType, int> RefreshBaseCosts { get; init; } =
        new Dictionary<PvpShopRefreshType, int>
        {
            [PvpShopRefreshType.Normal] = 20,
            [PvpShopRefreshType.ClassBias] = 30,
            [PvpShopRefreshType.RoleFix] = 30,
            [PvpShopRefreshType.ArchetypeTrace] = 45
        };
    public float RefreshCostGrowth { get; init; } = 0.35f;
}

public sealed class PvpShopStrategyPack
{
    public string Id { get; init; } = string.Empty;
    public string Version { get; init; } = PvpShopDefaults.StandardStrategyVersion;
    public float ArchetypeFitWeight { get; init; } = 1.0f;
    public float RoleNeedWeight { get; init; } = 1.0f;
    public float CurveFitWeight { get; init; } = 0.8f;
    public float ClassIntentWeight { get; init; } = 0.9f;
    public float NoveltyWeight { get; init; } = 0.8f;
    public float HighCeilingWeight { get; init; } = 0.7f;
    public int MinSupportOffers { get; init; } = 2;
}

public sealed class PvpShopEngineConfig
{
    public int SchemaVersion { get; init; } = PvpShopDefaults.SchemaVersion;
    public string RngVersion { get; init; } = PvpShopDefaults.RngVersion;
    public int StartGold { get; init; } = PvpShopDefaults.DefaultStartGold;
    public int MaxSeenHistory { get; init; } = PvpShopDefaults.DefaultSeenHistory;
}

public sealed class PvpShopOffer
{
    public int SlotIndex { get; init; }
    public PvpShopSlotKind SlotKind { get; init; }
    public string CardId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int Price { get; set; }
    public bool Available { get; set; } = true;
    public float DebugScore { get; set; }
}

public sealed class PvpShopPlayerState
{
    public ulong PlayerId { get; init; }
    public int Gold { get; set; }
    public int RefreshCount { get; set; }
    public int StateVersion { get; set; } = 1;
    public string LastStatusText { get; set; } = string.Empty;
    public List<string> SeenCardIds { get; } = new();
    public List<string> PurchasedCardIds { get; } = new();
    public List<string> RemovedCardIds { get; } = new();
    public List<PvpShopOffer> Offers { get; } = new();
}

public sealed class PvpShopRoundState
{
    public bool IsOpen { get; init; } = true;
    public int RoundIndex { get; init; }
    public int SnapshotVersion { get; init; }
    public int StateVersion { get; set; } = 1;
    public PvpShopModeContext ModeContext { get; init; } = new();
    public Dictionary<ulong, PvpShopPlayerState> PlayerStates { get; init; } = new();
    public DateTime OpenedAtUtc { get; init; } = DateTime.UtcNow;
}

public sealed class PvpShopViewModel
{
    public bool IsOpen { get; init; }
    public int RoundIndex { get; init; }
    public int SnapshotVersion { get; init; }
    public int StateVersion { get; init; }
    public ulong PlayerId { get; init; }
    public int Gold { get; init; }
    public int DeleteCost { get; init; }
    public int RefreshCount { get; init; }
    public IReadOnlyList<PvpShopOffer> Offers { get; init; } = Array.Empty<PvpShopOffer>();
    public IReadOnlyDictionary<PvpShopRefreshType, int> RefreshCosts { get; init; } = new Dictionary<PvpShopRefreshType, int>();
    public string StatusText { get; init; } = string.Empty;
    public string ModeVersion { get; init; } = string.Empty;
    public string StrategyVersion { get; init; } = string.Empty;
    public string RngVersion { get; init; } = string.Empty;
}

public sealed class PvpShopDeckProfile
{
    public PvpShopClassBias DominantClassBias { get; init; } = PvpShopClassBias.Skill;
    public string PrimaryArchetype { get; init; } = "Tempo";
    public string SecondaryArchetype { get; init; } = string.Empty;
    public int BlockCards { get; init; }
    public int DeckSize { get; init; }
    public bool NeedsBlock { get; init; }
    public bool NeedsLowerCurve { get; init; }
}

public interface IPvpShopModeRegistry
{
    void Register(PvpShopModeDefinition modeDefinition);
    bool TryGet(string modeId, out PvpShopModeDefinition modeDefinition);
    IReadOnlyCollection<PvpShopModeDefinition> GetAll();
}

public interface IPvpShopStrategyRegistry
{
    void Register(PvpShopStrategyPack strategyPack);
    bool TryGet(string strategyPackId, out PvpShopStrategyPack strategyPack);
    IReadOnlyCollection<PvpShopStrategyPack> GetAll();
}

public interface IPvpShopEngine
{
    bool IsRoundOpen { get; }
    PvpShopRoundState? CurrentRound { get; }
    bool TryOpenRound(int roundIndex, int snapshotVersion, string modeId, out PvpShopRoundState state);
    bool TryGetView(ulong playerId, out PvpShopViewModel view);
    bool TryRefresh(ulong playerId, PvpShopRefreshType refreshType, out string reason);
    bool TryPurchase(ulong playerId, int slotIndex, out string reason);
    bool TryDeleteCard(ulong playerId, int deckCardIndex, out string reason);
    void ApplyAuthoritativeState(PvpShopRoundState? state);
    bool TryCloseRound();
}

public sealed class PvpShopModeRegistry : IPvpShopModeRegistry
{
    private readonly Dictionary<string, PvpShopModeDefinition> _modes = new(StringComparer.OrdinalIgnoreCase);

    public void Register(PvpShopModeDefinition modeDefinition)
    {
        ArgumentNullException.ThrowIfNull(modeDefinition);
        if (string.IsNullOrWhiteSpace(modeDefinition.ModeId))
        {
            throw new ArgumentException("modeId cannot be empty.", nameof(modeDefinition));
        }

        if (string.IsNullOrWhiteSpace(modeDefinition.StrategyPackId))
        {
            throw new ArgumentException("strategyPackId cannot be empty.", nameof(modeDefinition));
        }

        _modes[modeDefinition.ModeId] = modeDefinition;
        Log.Info($"[ParallelTurnPvp][ShopEngine] Registered mode. modeId={modeDefinition.ModeId} modeVersion={modeDefinition.ModeVersion} strategyPackId={modeDefinition.StrategyPackId} slots={modeDefinition.Slots}");
    }

    public bool TryGet(string modeId, out PvpShopModeDefinition modeDefinition)
    {
        if (string.IsNullOrWhiteSpace(modeId))
        {
            modeDefinition = default!;
            return false;
        }

        return _modes.TryGetValue(modeId, out modeDefinition!);
    }

    public IReadOnlyCollection<PvpShopModeDefinition> GetAll()
    {
        return _modes.Values.ToList();
    }
}

public sealed class PvpShopStrategyRegistry : IPvpShopStrategyRegistry
{
    private readonly Dictionary<string, PvpShopStrategyPack> _strategyPacks = new(StringComparer.OrdinalIgnoreCase);

    public void Register(PvpShopStrategyPack strategyPack)
    {
        ArgumentNullException.ThrowIfNull(strategyPack);
        if (string.IsNullOrWhiteSpace(strategyPack.Id))
        {
            throw new ArgumentException("strategyPackId cannot be empty.", nameof(strategyPack));
        }

        _strategyPacks[strategyPack.Id] = strategyPack;
        Log.Info($"[ParallelTurnPvp][ShopEngine] Registered strategy pack. strategyPackId={strategyPack.Id} strategyVersion={strategyPack.Version}");
    }

    public bool TryGet(string strategyPackId, out PvpShopStrategyPack strategyPack)
    {
        if (string.IsNullOrWhiteSpace(strategyPackId))
        {
            strategyPack = default!;
            return false;
        }

        return _strategyPacks.TryGetValue(strategyPackId, out strategyPack!);
    }

    public IReadOnlyCollection<PvpShopStrategyPack> GetAll()
    {
        return _strategyPacks.Values.ToList();
    }
}

public sealed class PvpShopEngine : IPvpShopEngine
{
    private static readonly IReadOnlyDictionary<string, PvpShopCardDefinition> CardLibrary = CreateCardLibrary();
    private static readonly bool EnableShopCombatPileInjection = !IsEnabled("PTPVP_DISABLE_SHOP_COMBAT_INJECTION");
    private static readonly MethodInfo[] AddGeneratedCardToCombatMethods = typeof(CardPileCmd)
        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
        .Where(method => method.Name.Equals("AddGeneratedCardToCombat", StringComparison.Ordinal))
        .ToArray();
    private static readonly PropertyInfo? CardOwnerProperty = typeof(CardModel).GetProperty("Owner", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? CardOwnerBackingField = typeof(CardModel).GetField("<Owner>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CardOwnerField = typeof(CardModel).GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        .FirstOrDefault(field => field.Name.Equals("_owner", StringComparison.OrdinalIgnoreCase));
    private static readonly MethodInfo? CardSetOwnerMethod = typeof(CardModel).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
        .FirstOrDefault(method =>
            method.Name.Contains("SetOwner", StringComparison.OrdinalIgnoreCase) &&
            method.GetParameters().Length == 1);
    private static bool _loggedCombatInjectionDisabled;
    private static bool _loggedGeneratedInjectionMethodMissing;

    private readonly RunState _runState;
    private readonly PvpShopEngineConfig _engineConfig;
    private readonly IPvpShopModeRegistry _modeRegistry;
    private readonly IPvpShopStrategyRegistry _strategyRegistry;

    public PvpShopEngine(
        RunState runState,
        PvpShopEngineConfig engineConfig,
        IPvpShopModeRegistry modeRegistry,
        IPvpShopStrategyRegistry strategyRegistry)
    {
        _runState = runState;
        _engineConfig = engineConfig;
        _modeRegistry = modeRegistry;
        _strategyRegistry = strategyRegistry;
    }

    public bool IsRoundOpen => CurrentRound?.IsOpen == true;
    public PvpShopRoundState? CurrentRound { get; private set; }

    public bool TryOpenRound(int roundIndex, int snapshotVersion, string modeId, out PvpShopRoundState state)
    {
        state = default!;
        if (roundIndex <= 0 || snapshotVersion <= 0)
        {
            return false;
        }

        if (CurrentRound is { IsOpen: true })
        {
            return false;
        }

        if (!_modeRegistry.TryGet(modeId, out PvpShopModeDefinition modeDefinition))
        {
            Log.Warn($"[ParallelTurnPvp][ShopEngine] Open round failed. Unknown modeId={modeId}");
            return false;
        }

        if (!_strategyRegistry.TryGet(modeDefinition.StrategyPackId, out PvpShopStrategyPack strategyPack))
        {
            Log.Warn($"[ParallelTurnPvp][ShopEngine] Open round failed. Missing strategyPackId={modeDefinition.StrategyPackId} modeId={modeDefinition.ModeId}");
            return false;
        }

        var roundState = new PvpShopRoundState
        {
            IsOpen = true,
            RoundIndex = roundIndex,
            SnapshotVersion = snapshotVersion,
            StateVersion = 1,
            ModeContext = new PvpShopModeContext
            {
                ModeId = modeDefinition.ModeId,
                ModeVersion = modeDefinition.ModeVersion,
                StrategyPackId = strategyPack.Id,
                StrategyVersion = strategyPack.Version,
                RngVersion = _engineConfig.RngVersion,
                SchemaVersion = _engineConfig.SchemaVersion
            }
        };

        foreach (Player player in _runState.Players)
        {
            var playerState = new PvpShopPlayerState
            {
                PlayerId = player.NetId,
                Gold = _engineConfig.StartGold,
                RefreshCount = 0,
                StateVersion = 1,
                LastStatusText = $"回合 {roundIndex} 商店已开启。"
            };
            playerState.Offers.AddRange(GenerateOffers(player, playerState, roundState, modeDefinition, strategyPack, PvpShopRefreshType.Normal));
            roundState.PlayerStates[player.NetId] = playerState;
        }

        CurrentRound = roundState;
        state = CloneRoundState(roundState);
        LogRoundSummary("Round opened", roundState);
        return true;
    }

    public bool TryGetView(ulong playerId, out PvpShopViewModel view)
    {
        view = default!;
        if (CurrentRound is not { IsOpen: true } roundState || !roundState.PlayerStates.TryGetValue(playerId, out PvpShopPlayerState? playerState))
        {
            return false;
        }

        if (!_modeRegistry.TryGet(roundState.ModeContext.ModeId, out PvpShopModeDefinition modeDefinition))
        {
            return false;
        }

        view = new PvpShopViewModel
        {
            IsOpen = true,
            RoundIndex = roundState.RoundIndex,
            SnapshotVersion = roundState.SnapshotVersion,
            StateVersion = playerState.StateVersion,
            PlayerId = playerId,
            Gold = playerState.Gold,
            DeleteCost = PvpShopDefaults.DefaultDeleteCost,
            RefreshCount = playerState.RefreshCount,
            Offers = playerState.Offers.Select(CloneOffer).ToList(),
            RefreshCosts = Enum.GetValues<PvpShopRefreshType>().ToDictionary(type => type, type => GetRefreshCost(modeDefinition, playerState, type)),
            StatusText = playerState.LastStatusText,
            ModeVersion = roundState.ModeContext.ModeVersion,
            StrategyVersion = roundState.ModeContext.StrategyVersion,
            RngVersion = roundState.ModeContext.RngVersion
        };
        return true;
    }

    public bool TryRefresh(ulong playerId, PvpShopRefreshType refreshType, out string reason)
    {
        reason = string.Empty;
        if (CurrentRound is not { IsOpen: true } roundState)
        {
            reason = "商店未开启。";
            return false;
        }

        if (!roundState.PlayerStates.TryGetValue(playerId, out PvpShopPlayerState? playerState))
        {
            reason = $"未知玩家 {playerId}。";
            return false;
        }

        if (!_modeRegistry.TryGet(roundState.ModeContext.ModeId, out PvpShopModeDefinition modeDefinition) ||
            !_strategyRegistry.TryGet(roundState.ModeContext.StrategyPackId, out PvpShopStrategyPack strategyPack))
        {
            reason = "商店模式配置缺失。";
            return false;
        }

        int refreshCost = GetRefreshCost(modeDefinition, playerState, refreshType);
        if (playerState.Gold < refreshCost)
        {
            reason = $"金币不足：需要 {refreshCost}，当前 {playerState.Gold}。";
            playerState.LastStatusText = reason;
            return false;
        }

        Player? player = _runState.Players.FirstOrDefault(entry => entry.NetId == playerId);
        if (player == null)
        {
            reason = $"找不到玩家实体 {playerId}。";
            playerState.LastStatusText = reason;
            return false;
        }

        playerState.Gold -= refreshCost;
        playerState.RefreshCount++;
        playerState.StateVersion++;
        roundState.StateVersion++;
        playerState.LastStatusText = $"刷新成功：{refreshType}，花费 {refreshCost} 金。";
        playerState.Offers.Clear();
        playerState.Offers.AddRange(GenerateOffers(player, playerState, roundState, modeDefinition, strategyPack, refreshType));
        reason = playerState.LastStatusText;

        Log.Info($"[ParallelTurnPvp][ShopEngine] Refresh accepted. round={roundState.RoundIndex} player={playerId} type={refreshType} refreshCount={playerState.RefreshCount} gold={playerState.Gold} stateVersion={playerState.StateVersion}");
        return true;
    }

    public bool TryPurchase(ulong playerId, int slotIndex, out string reason)
    {
        reason = string.Empty;
        if (CurrentRound is not { IsOpen: true } roundState)
        {
            reason = "商店未开启。";
            return false;
        }

        if (!roundState.PlayerStates.TryGetValue(playerId, out PvpShopPlayerState? playerState))
        {
            reason = $"未知玩家 {playerId}。";
            return false;
        }

        PvpShopOffer? offer = playerState.Offers.FirstOrDefault(entry => entry.SlotIndex == slotIndex);
        if (offer == null)
        {
            reason = $"槽位 {slotIndex} 不存在。";
            playerState.LastStatusText = reason;
            return false;
        }

        if (!offer.Available)
        {
            reason = $"槽位 {slotIndex} 已售出。";
            playerState.LastStatusText = reason;
            return false;
        }

        if (playerState.Gold < offer.Price)
        {
            reason = $"金币不足：需要 {offer.Price}，当前 {playerState.Gold}。";
            playerState.LastStatusText = reason;
            return false;
        }

        Player? player = _runState.Players.FirstOrDefault(entry => entry.NetId == playerId);
        if (player == null)
        {
            reason = $"找不到玩家实体 {playerId}。";
            playerState.LastStatusText = reason;
            return false;
        }

        if (!TryAddCardToDeck(player, offer.CardId))
        {
            reason = $"无法创建卡牌 {offer.CardId}。";
            playerState.LastStatusText = reason;
            return false;
        }

        playerState.Gold -= offer.Price;
        offer.Available = false;
        playerState.PurchasedCardIds.Add(offer.CardId);
        TrimSeenHistory(playerState.SeenCardIds);
        playerState.StateVersion++;
        roundState.StateVersion++;
        playerState.LastStatusText = $"已购买 {offer.DisplayName}，花费 {offer.Price} 金。";
        reason = playerState.LastStatusText;

        Log.Info($"[ParallelTurnPvp][ShopEngine] Purchase accepted. round={roundState.RoundIndex} player={playerId} slot={slotIndex} card={offer.CardId} cost={offer.Price} gold={playerState.Gold} stateVersion={playerState.StateVersion}");
        return true;
    }

    public bool TryDeleteCard(ulong playerId, int deckCardIndex, out string reason)
    {
        reason = string.Empty;
        if (CurrentRound is not { IsOpen: true } roundState)
        {
            reason = "商店未开启。";
            return false;
        }

        if (!roundState.PlayerStates.TryGetValue(playerId, out PvpShopPlayerState? playerState))
        {
            reason = $"未知玩家 {playerId}。";
            return false;
        }

        if (deckCardIndex < 0)
        {
            reason = "删卡索引无效。";
            playerState.LastStatusText = reason;
            return false;
        }

        Player? player = _runState.Players.FirstOrDefault(entry => entry.NetId == playerId);
        if (player == null)
        {
            reason = $"找不到玩家实体 {playerId}。";
            playerState.LastStatusText = reason;
            return false;
        }

        List<CardModel> deckCards = player.Deck.Cards.ToList();
        if (deckCardIndex >= deckCards.Count)
        {
            reason = $"删卡索引越界：index={deckCardIndex}，deck={deckCards.Count}。";
            playerState.LastStatusText = reason;
            return false;
        }

        int deleteCost = PvpShopDefaults.DefaultDeleteCost;
        if (playerState.Gold < deleteCost)
        {
            reason = $"金币不足：删卡需要 {deleteCost}，当前 {playerState.Gold}。";
            playerState.LastStatusText = reason;
            return false;
        }

        CardModel targetCard = deckCards[deckCardIndex];
        string cardId = targetCard.Id.Entry;
        if (!TryRemoveCardFromDeck(_runState, player, targetCard, out string removeReason))
        {
            reason = $"删卡失败：{removeReason}";
            playerState.LastStatusText = reason;
            return false;
        }

        playerState.Gold -= deleteCost;
        playerState.RemovedCardIds.Add(cardId);
        TrimSeenHistory(playerState.SeenCardIds);
        playerState.StateVersion++;
        roundState.StateVersion++;
        playerState.LastStatusText = $"已删卡 {cardId}，花费 {deleteCost} 金。";
        reason = playerState.LastStatusText;

        Log.Info($"[ParallelTurnPvp][ShopEngine] Delete accepted. round={roundState.RoundIndex} player={playerId} deckIndex={deckCardIndex} card={cardId} cost={deleteCost} gold={playerState.Gold} stateVersion={playerState.StateVersion}");
        return true;
    }

    public void ApplyAuthoritativeState(PvpShopRoundState? state)
    {
        if (state == null || !state.IsOpen)
        {
            if (CurrentRound is { IsOpen: true } current)
            {
                Log.Info($"[ParallelTurnPvp][ShopEngine] Applied closed shop state. round={current.RoundIndex} stateVersion={current.StateVersion}");
            }

            CurrentRound = null;
            return;
        }

        SyncDeckMutations(CurrentRound, state);
        CurrentRound = CloneRoundState(state);
        LogRoundSummary("Applied authoritative shop state", CurrentRound);
    }

    public bool TryCloseRound()
    {
        if (CurrentRound is not { IsOpen: true } roundState)
        {
            return false;
        }

        Log.Info($"[ParallelTurnPvp][ShopEngine] Round closed. round={roundState.RoundIndex} stateVersion={roundState.StateVersion} players={string.Join(",", roundState.PlayerStates.Keys.OrderBy(id => id))}");
        CurrentRound = null;
        return true;
    }

    public PvpShopRoundState? CreateSnapshot()
    {
        return CurrentRound == null ? null : CloneRoundState(CurrentRound);
    }

    private static void SyncDeckMutations(PvpShopRoundState? previousState, PvpShopRoundState authoritativeState)
    {
        RunState? runState = PvpShopRuntimeRegistry.TryGetRunState(authoritativeState);
        if (runState == null)
        {
            return;
        }

        foreach ((ulong playerId, PvpShopPlayerState incomingState) in authoritativeState.PlayerStates)
        {
            int previousPurchaseCount = 0;
            if (previousState != null && previousState.PlayerStates.TryGetValue(playerId, out PvpShopPlayerState? oldState))
            {
                previousPurchaseCount = oldState.PurchasedCardIds.Count;
            }

            if (incomingState.PurchasedCardIds.Count <= previousPurchaseCount)
            {
                continue;
            }

            Player? player = runState.Players.FirstOrDefault(entry => entry.NetId == playerId);
            if (player == null)
            {
                continue;
            }

            foreach (string cardId in incomingState.PurchasedCardIds.Skip(previousPurchaseCount))
            {
                TryAddCardToDeck(runState, player, cardId);
            }

            int previousRemoveCount = 0;
            if (previousState != null && previousState.PlayerStates.TryGetValue(playerId, out PvpShopPlayerState? oldStateForRemove))
            {
                previousRemoveCount = oldStateForRemove.RemovedCardIds.Count;
            }

            if (incomingState.RemovedCardIds.Count <= previousRemoveCount)
            {
                continue;
            }

            foreach (string cardId in incomingState.RemovedCardIds.Skip(previousRemoveCount))
            {
                TryRemoveCardFromDeck(runState, player, cardId);
            }
        }
    }

    private static bool TryAddCardToDeck(Player player, string cardId)
    {
        return player.RunState is RunState runState && TryAddCardToDeck(runState, player, cardId, "purchase");
    }

    private static bool TryAddCardToDeck(RunState runState, Player player, string cardId)
    {
        return TryAddCardToDeck(runState, player, cardId, "authoritative_sync");
    }

    private static bool TryAddCardToDeck(RunState runState, Player player, string cardId, string sourceTag)
    {
        if (!CardLibrary.TryGetValue(cardId, out PvpShopCardDefinition? definition))
        {
            return false;
        }

        CardModel persistentCard = definition.Factory();
        persistentCard.FloorAddedToDeck = 1;
        runState.AddCard(persistentCard, player);
        player.Deck.AddInternal(persistentCard, -1, true);

        int deckCount = player.Deck?.Cards?.Count ?? -1;
        Log.Info($"[ParallelTurnPvp][ShopEngine] 商店加卡已写入牌组。player={player.NetId} card={persistentCard.Id.Entry} source={sourceTag} deckCount={deckCount}");

        if (EnableShopCombatPileInjection)
        {
            TryQueueCombatPileInjection(runState, player, definition, sourceTag);
        }
        else if (!_loggedCombatInjectionDisabled)
        {
            _loggedCombatInjectionDisabled = true;
            Log.Info("[ParallelTurnPvp][ShopEngine] 商店战斗牌堆即时注入已关闭（PTPVP_DISABLE_SHOP_COMBAT_INJECTION=1）。仅写入牌组。");
        }
        return true;
    }

    private static bool TryRemoveCardFromDeck(RunState runState, Player player, string cardId)
    {
        CardModel? target = player.Deck.Cards.FirstOrDefault(card => string.Equals(card.Id.Entry, cardId, StringComparison.Ordinal));
        if (target == null)
        {
            Log.Warn($"[ParallelTurnPvp][ShopEngine] 同步删卡失败：牌组中不存在 card={cardId} player={player.NetId}");
            return false;
        }

        return TryRemoveCardFromDeck(runState, player, target, out _);
    }

    private static bool TryRemoveCardFromDeck(RunState runState, Player player, CardModel card, out string reason)
    {
        reason = string.Empty;
        try
        {
            CardPile? activePile = card.Pile;
            if (activePile != null && activePile.Type != PileType.Deck && activePile.Cards.Contains(card))
            {
                activePile.RemoveInternal(card, false);
            }

            if (player.Deck.Cards.Contains(card))
            {
                player.Deck.RemoveInternal(card, true);
            }

            CombatState? combatState = player.Creature?.CombatState;
            if (combatState != null && combatState.ContainsCard(card))
            {
                combatState.RemoveCard(card);
            }

            if (runState.ContainsCard(card))
            {
                runState.RemoveCard(card);
            }
            int deckCount = player.Deck?.Cards?.Count ?? -1;
            Log.Info($"[ParallelTurnPvp][ShopEngine] 商店删卡已执行。player={player.NetId} card={card.Id.Entry} deckCount={deckCount}");
            return true;
        }
        catch (Exception ex)
        {
            reason = ex.Message;
            Log.Warn($"[ParallelTurnPvp][ShopEngine] 商店删卡失败。player={player.NetId} card={card.Id.Entry} error={ex.Message}");
            return false;
        }
    }

    private static void TryQueueCombatPileInjection(RunState runState, Player player, PvpShopCardDefinition definition, string sourceTag)
    {
        try
        {
            bool hasCombatDiscard = CardPile.Get(PileType.Discard, player) != null;
            bool hasCombatDraw = CardPile.Get(PileType.Draw, player) != null;
            if (!hasCombatDiscard && !hasCombatDraw)
            {
                Log.Info($"[ParallelTurnPvp][ShopEngine] 当前无战斗牌堆，跳过战斗注入。player={player.NetId} source={sourceTag}");
                return;
            }

            TaskHelper.RunSafely(AddCardToCombatPileAsync(runState, player, definition, sourceTag));
        }
        catch (Exception ex)
        {
            Log.Warn($"[ParallelTurnPvp][ShopEngine] 计划战斗注入失败。player={player.NetId} source={sourceTag} error={ex.Message}");
        }
    }

    private static async Task AddCardToCombatPileAsync(RunState runState, Player player, PvpShopCardDefinition definition, string sourceTag)
    {
        if (AddGeneratedCardToCombatMethods.Length == 0)
        {
            if (!_loggedGeneratedInjectionMethodMissing)
            {
                _loggedGeneratedInjectionMethodMissing = true;
                Log.Warn("[ParallelTurnPvp][ShopEngine] 未找到 CardPileCmd.AddGeneratedCardToCombat，跳过即时注入。");
            }

            return;
        }

        if (CreateGeneratedCardForCombatInjection(runState, player, definition) is not { } handCard)
        {
            Log.Warn($"[ParallelTurnPvp][ShopEngine] 无法创建具备 owner 的生成卡，跳过即时注入。player={player.NetId} card={definition.Id} source={sourceTag}");
            return;
        }

        if (await TryInvokeAddGeneratedCardToCombatAsync(player, handCard, PileType.Hand))
        {
            int handCount = CardPile.Get(PileType.Hand, player)?.Cards?.Count ?? -1;
            Log.Info($"[ParallelTurnPvp][ShopEngine] 商店加卡已即时注入手牌。player={player.NetId} card={handCard.Id.Entry} source={sourceTag} handCount={handCount}");
            return;
        }

        if (CreateGeneratedCardForCombatInjection(runState, player, definition) is not { } discardCard)
        {
            Log.Warn($"[ParallelTurnPvp][ShopEngine] 创建弃牌注入卡失败。player={player.NetId} card={definition.Id} source={sourceTag}");
            return;
        }

        if (await TryInvokeAddGeneratedCardToCombatAsync(player, discardCard, PileType.Discard))
        {
            int discardCount = CardPile.Get(PileType.Discard, player)?.Cards?.Count ?? -1;
            Log.Info($"[ParallelTurnPvp][ShopEngine] 商店加卡已即时注入弃牌堆。player={player.NetId} card={discardCard.Id.Entry} source={sourceTag} discardCount={discardCount}");
            return;
        }

        if (CreateGeneratedCardForCombatInjection(runState, player, definition) is not { } drawCard)
        {
            Log.Warn($"[ParallelTurnPvp][ShopEngine] 创建抽牌注入卡失败。player={player.NetId} card={definition.Id} source={sourceTag}");
            return;
        }

        if (await TryInvokeAddGeneratedCardToCombatAsync(player, drawCard, PileType.Draw))
        {
            int drawCount = CardPile.Get(PileType.Draw, player)?.Cards?.Count ?? -1;
            Log.Info($"[ParallelTurnPvp][ShopEngine] 商店加卡已即时注入抽牌堆。player={player.NetId} card={drawCard.Id.Entry} source={sourceTag} drawCount={drawCount}");
            return;
        }

        Log.Warn($"[ParallelTurnPvp][ShopEngine] 商店加卡即时注入失败，保留牌组写入。player={player.NetId} card={definition.Id} source={sourceTag}");
    }

    private static async Task<bool> TryInvokeAddGeneratedCardToCombatAsync(Player player, CardModel card, PileType pileType)
    {
        foreach (MethodInfo method in AddGeneratedCardToCombatMethods)
        {
            if (!TryBuildGeneratedInjectionArgs(method, player, card, pileType, out object?[] args))
            {
                continue;
            }

            try
            {
                if (await TryInvokeGeneratedMethodOnceAsync(method, args))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Exception root = ex is TargetInvocationException { InnerException: not null } tie ? tie.InnerException! : ex;
                Log.Warn($"[ParallelTurnPvp][ShopEngine] 调用 AddGeneratedCardToCombat 失败。method={method} pile={pileType} card={card.Id.Entry} error={root.Message}");
                continue;
            }

            // Some game builds invert the boolean semantic in this API.
            int[] boolIndices = method.GetParameters()
                .Select((parameter, index) => (parameter, index))
                .Where(tuple => tuple.parameter.ParameterType == typeof(bool))
                .Select(tuple => tuple.index)
                .ToArray();
            if (boolIndices.Length == 0)
            {
                continue;
            }

            object?[] toggledArgs = (object?[])args.Clone();
            foreach (int boolIndex in boolIndices)
            {
                toggledArgs[boolIndex] = !(bool)toggledArgs[boolIndex]!;
            }

            try
            {
                if (await TryInvokeGeneratedMethodOnceAsync(method, toggledArgs))
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                Exception root = ex is TargetInvocationException { InnerException: not null } tie ? tie.InnerException! : ex;
                Log.Warn($"[ParallelTurnPvp][ShopEngine] 调用 AddGeneratedCardToCombat 失败（bool 翻转重试）。method={method} pile={pileType} card={card.Id.Entry} error={root.Message}");
            }
        }

        return false;
    }

    private static async Task<bool> TryInvokeGeneratedMethodOnceAsync(MethodInfo method, object?[] args)
    {
        object? invocationResult = method.Invoke(null, args);
        if (invocationResult is Task taskResult)
        {
            await taskResult;
        }

        return true;
    }

    private static bool TryBuildGeneratedInjectionArgs(MethodInfo method, Player player, CardModel card, PileType pileType, out object?[] args)
    {
        ParameterInfo[] parameters = method.GetParameters();
        args = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            Type parameterType = parameters[i].ParameterType;
            if (parameterType == typeof(CardModel))
            {
                args[i] = card;
                continue;
            }

            if (parameterType == typeof(PileType))
            {
                args[i] = pileType;
                continue;
            }

            if (parameterType == typeof(CardPilePosition))
            {
                args[i] = CardPilePosition.Bottom;
                continue;
            }

            if (parameterType == typeof(Player))
            {
                args[i] = player;
                continue;
            }

            if (parameterType == typeof(bool))
            {
                args[i] = false;
                continue;
            }

            if (!parameterType.IsValueType && (typeof(AbstractModel).IsAssignableFrom(parameterType) || parameterType.Name.EndsWith("Context", StringComparison.Ordinal)))
            {
                args[i] = null;
                continue;
            }

            return false;
        }

        return true;
    }

    private static CardModel? CreateGeneratedCardForCombatInjection(RunState runState, Player player, PvpShopCardDefinition definition)
    {
        CardModel generated = definition.Factory();
        generated.FloorAddedToDeck = 1;

        // IMPORTANT: register into RunState first, so Owner assignment happens through
        // canonical path and avoids "already has an owner" assignment failures.
        try
        {
            if (!runState.ContainsCard(generated))
            {
                runState.AddCard(generated, player);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[ParallelTurnPvp][ShopEngine] 生成卡加入 RunState 失败。player={player.NetId} card={generated.Id.Entry} error={ex.Message}");
            return null;
        }

        if (generated.Owner == null && !TryAssignCardOwner(generated, player))
        {
            Log.Warn($"[ParallelTurnPvp][ShopEngine] 生成卡 owner 绑定失败。player={player.NetId} card={generated.Id.Entry}");
            return null;
        }

        return EnsureCardPresentInState(runState, player, generated) ? generated : null;
    }

    private static bool TryAssignCardOwner(CardModel card, Player player)
    {
        try
        {
            if (CardOwnerProperty != null && CardOwnerProperty.PropertyType.IsAssignableFrom(player.GetType()))
            {
                CardOwnerProperty.SetValue(card, player);
                return true;
            }
        }
        catch
        {
        }

        try
        {
            if (CardOwnerBackingField != null && CardOwnerBackingField.FieldType.IsAssignableFrom(player.GetType()))
            {
                CardOwnerBackingField.SetValue(card, player);
                return true;
            }
        }
        catch
        {
        }

        try
        {
            if (CardOwnerField != null && CardOwnerField.FieldType.IsAssignableFrom(player.GetType()))
            {
                CardOwnerField.SetValue(card, player);
                return true;
            }
        }
        catch
        {
        }

        try
        {
            if (CardSetOwnerMethod != null)
            {
                ParameterInfo parameter = CardSetOwnerMethod.GetParameters()[0];
                if (parameter.ParameterType.IsAssignableFrom(player.GetType()))
                {
                    CardSetOwnerMethod.Invoke(card, [player]);
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    private static bool EnsureCardPresentInState(RunState runState, Player player, CardModel card)
    {
        try
        {
            if (!runState.ContainsCard(card))
            {
                if (card.Owner == null)
                {
                    TryAssignCardOwner(card, player);
                }
                runState.AddCard(card, player);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"[ParallelTurnPvp][ShopEngine] 生成卡加入 RunState 失败。player={player.NetId} card={card.Id.Entry} error={ex.Message}");
            return false;
        }

        CombatState? combatState = player.Creature?.CombatState;
        if (combatState != null)
        {
            try
            {
                if (!combatState.ContainsCard(card))
                {
                    combatState.AddCard(card, player);
                }
            }
            catch (Exception ex)
            {
                Log.Warn($"[ParallelTurnPvp][ShopEngine] 生成卡加入 CombatState 失败。player={player.NetId} card={card.Id.Entry} error={ex.Message}");
                return false;
            }
        }

        return true;
    }

    private static bool IsEnabled(string envName)
    {
        string? value = System.Environment.GetEnvironmentVariable(envName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value == "1" ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }

    private List<PvpShopOffer> GenerateOffers(
        Player player,
        PvpShopPlayerState playerState,
        PvpShopRoundState roundState,
        PvpShopModeDefinition modeDefinition,
        PvpShopStrategyPack strategyPack,
        PvpShopRefreshType refreshType)
    {
        PvpShopDeckProfile profile = AnalyzeDeck(player);
        List<PvpShopOffer> offers = new(modeDefinition.Template.Count);
        HashSet<string> chosenThisRoll = new(StringComparer.Ordinal);

        for (int slotIndex = 0; slotIndex < modeDefinition.Template.Count; slotIndex++)
        {
            PvpShopSlotKind slotKind = modeDefinition.Template[slotIndex];
            (PvpShopCardDefinition card, float score) = SelectOfferForSlot(
                profile,
                playerState,
                roundState,
                slotKind,
                slotIndex,
                refreshType,
                strategyPack,
                chosenThisRoll);

            offers.Add(new PvpShopOffer
            {
                SlotIndex = slotIndex,
                SlotKind = slotKind,
                CardId = card.Id,
                DisplayName = card.DisplayName,
                Price = card.Price,
                Available = true,
                DebugScore = score
            });
            chosenThisRoll.Add(card.Id);
            playerState.SeenCardIds.Add(card.Id);
        }

        EnforceSupportGuarantee(offers, profile, playerState, roundState, strategyPack, refreshType);
        TrimSeenHistory(playerState.SeenCardIds);
        return offers;
    }

    private (PvpShopCardDefinition Card, float Score) SelectOfferForSlot(
        PvpShopDeckProfile profile,
        PvpShopPlayerState playerState,
        PvpShopRoundState roundState,
        PvpShopSlotKind slotKind,
        int slotIndex,
        PvpShopRefreshType refreshType,
        PvpShopStrategyPack strategyPack,
        HashSet<string> chosenThisRoll)
    {
        List<(PvpShopCardDefinition Card, float Score)> candidates = new();
        bool allowDistinctOnly = chosenThisRoll.Count < CardLibrary.Count;
        foreach (PvpShopCardDefinition candidate in CardLibrary.Values)
        {
            if (allowDistinctOnly && chosenThisRoll.Contains(candidate.Id))
            {
                continue;
            }

            float score = ScoreCandidate(candidate, profile, playerState, slotKind, refreshType, strategyPack);
            if (score > 0.0001f)
            {
                candidates.Add((candidate, score));
            }
        }

        if (candidates.Count == 0)
        {
            foreach (PvpShopCardDefinition candidate in CardLibrary.Values)
            {
                float score = ScoreCandidate(candidate, profile, playerState, slotKind, refreshType, strategyPack) * 0.35f;
                if (score > 0.0001f)
                {
                    candidates.Add((candidate, score));
                }
            }
        }

        if (candidates.Count == 0)
        {
            PvpShopCardDefinition fallback = CardLibrary.Values.First();
            return (fallback, 1.0f);
        }

        int seed = ComputeDeterministicSeed(
            roundState.RoundIndex,
            roundState.SnapshotVersion,
            roundState.StateVersion,
            (int)playerState.PlayerId,
            playerState.RefreshCount,
            slotIndex,
            (int)slotKind,
            (int)refreshType,
            playerState.StateVersion);
        Random random = new(seed);
        float totalWeight = candidates.Sum(entry => MathF.Max(entry.Score, 0.001f));
        float roll = (float)(random.NextDouble() * totalWeight);
        float cursor = 0f;
        foreach ((PvpShopCardDefinition card, float score) in candidates.OrderByDescending(entry => entry.Score))
        {
            cursor += MathF.Max(score, 0.001f);
            if (roll <= cursor)
            {
                return (card, score);
            }
        }

        (PvpShopCardDefinition Card, float Score) best = candidates.OrderByDescending(entry => entry.Score).First();
        return best;
    }

    private static float ScoreCandidate(
        PvpShopCardDefinition card,
        PvpShopDeckProfile profile,
        PvpShopPlayerState playerState,
        PvpShopSlotKind slotKind,
        PvpShopRefreshType refreshType,
        PvpShopStrategyPack strategyPack)
    {
        float score = card.BaseWeight;

        score *= slotKind switch
        {
            PvpShopSlotKind.CoreArchetype => 1.0f + GetArchetypeFit(card, profile.PrimaryArchetype) * strategyPack.ArchetypeFitWeight,
            PvpShopSlotKind.RoleFix => 1.0f + GetRoleNeed(card, profile) * strategyPack.RoleNeedWeight,
            PvpShopSlotKind.ClassBias => 1.0f + GetClassIntent(card, profile.DominantClassBias) * strategyPack.ClassIntentWeight,
            PvpShopSlotKind.Pivot => 1.0f + GetPivotFit(card, profile) * strategyPack.ArchetypeFitWeight,
            PvpShopSlotKind.HighCeiling => 1.0f + (card.IsHighCeiling ? strategyPack.HighCeilingWeight : 0.1f),
            _ => 1.0f
        };

        score *= 1.0f + GetCurveFit(card, profile) * strategyPack.CurveFitWeight;
        score *= 1.0f + GetRefreshBias(card, profile, refreshType, strategyPack);
        score *= 1.0f + GetNoveltyFactor(card, playerState, strategyPack.NoveltyWeight);
        return MathF.Max(score, 0.001f);
    }

    private static float GetArchetypeFit(PvpShopCardDefinition card, string archetype)
    {
        if (string.IsNullOrWhiteSpace(archetype))
        {
            return 0.1f;
        }

        return card.Archetypes.Contains(archetype) ? 0.65f : 0.1f;
    }

    private static float GetPivotFit(PvpShopCardDefinition card, PvpShopDeckProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.SecondaryArchetype) && card.Archetypes.Contains(profile.SecondaryArchetype))
        {
            return 0.55f;
        }

        return card.PrimaryClassBias != profile.DominantClassBias ? 0.25f : 0.08f;
    }

    private static float GetRoleNeed(PvpShopCardDefinition card, PvpShopDeckProfile profile)
    {
        if (profile.NeedsBlock && card.Roles.Contains("Block"))
        {
            return 0.9f;
        }

        return card.Roles.Contains("Block") ? 0.25f : 0.1f;
    }

    private static float GetCurveFit(PvpShopCardDefinition card, PvpShopDeckProfile profile)
    {
        if (profile.NeedsLowerCurve && card.CurveCost <= 1)
        {
            return 0.35f;
        }

        return card.CurveCost <= 1 ? 0.12f : 0.05f;
    }

    private static float GetClassIntent(PvpShopCardDefinition card, PvpShopClassBias classBias)
    {
        if (classBias == PvpShopClassBias.None)
        {
            return 0.1f;
        }

        return card.PrimaryClassBias == classBias ? 0.6f : 0.08f;
    }

    private static float GetRefreshBias(
        PvpShopCardDefinition card,
        PvpShopDeckProfile profile,
        PvpShopRefreshType refreshType,
        PvpShopStrategyPack strategyPack)
    {
        return refreshType switch
        {
            PvpShopRefreshType.Normal => 0.05f,
            PvpShopRefreshType.ClassBias => GetClassIntent(card, profile.DominantClassBias) * strategyPack.ClassIntentWeight,
            PvpShopRefreshType.RoleFix => GetRoleNeed(card, profile) * strategyPack.RoleNeedWeight,
            PvpShopRefreshType.ArchetypeTrace => GetArchetypeFit(card, profile.PrimaryArchetype) * strategyPack.ArchetypeFitWeight,
            _ => 0.05f
        };
    }

    private static float GetNoveltyFactor(PvpShopCardDefinition card, PvpShopPlayerState playerState, float noveltyWeight)
    {
        int lastIndex = playerState.SeenCardIds.FindLastIndex(entry => string.Equals(entry, card.Id, StringComparison.Ordinal));
        if (lastIndex < 0)
        {
            return 0.35f * noveltyWeight;
        }

        int distanceFromTail = playerState.SeenCardIds.Count - 1 - lastIndex;
        if (distanceFromTail <= 2)
        {
            return -0.55f * noveltyWeight;
        }

        if (distanceFromTail <= 5)
        {
            return -0.25f * noveltyWeight;
        }

        return 0.08f * noveltyWeight;
    }

    private static void EnforceSupportGuarantee(
        List<PvpShopOffer> offers,
        PvpShopDeckProfile profile,
        PvpShopPlayerState playerState,
        PvpShopRoundState roundState,
        PvpShopStrategyPack strategyPack,
        PvpShopRefreshType refreshType)
    {
        int currentSupportCount = offers.Count(offer =>
            CardLibrary.TryGetValue(offer.CardId, out PvpShopCardDefinition? definition) && definition.Roles.Contains("Block"));
        if (currentSupportCount >= strategyPack.MinSupportOffers)
        {
            return;
        }

        List<PvpShopCardDefinition> supportCards = CardLibrary.Values.Where(card => card.Roles.Contains("Block")).ToList();
        if (supportCards.Count == 0)
        {
            return;
        }

        IEnumerable<PvpShopOffer> replaceTargets = offers
            .Where(offer => offer.SlotKind is not PvpShopSlotKind.RoleFix)
            .OrderBy(offer => offer.DebugScore)
            .ThenByDescending(offer => offer.SlotKind == PvpShopSlotKind.HighCeiling);

        foreach (PvpShopOffer target in replaceTargets)
        {
            if (currentSupportCount >= strategyPack.MinSupportOffers)
            {
                break;
            }

            PvpShopCardDefinition replacement = supportCards
                .OrderByDescending(card => ScoreCandidate(card, profile, playerState, target.SlotKind, refreshType, strategyPack))
                .First();

            target.CardId = replacement.Id;
            target.DisplayName = replacement.DisplayName;
            target.Price = replacement.Price;
            target.DebugScore = ScoreCandidate(replacement, profile, playerState, target.SlotKind, refreshType, strategyPack);
            currentSupportCount++;
        }
    }

    private PvpShopDeckProfile AnalyzeDeck(Player player)
    {
        List<CardModel> deck = player.Deck.Cards.ToList();
        if (deck.Count == 0)
        {
            return new PvpShopDeckProfile();
        }

        int attackCount = 0;
        int skillCount = 0;
        int powerCount = 0;
        int blockCount = 0;
        int cheapCount = 0;
        Dictionary<string, int> archetypeCounts = new(StringComparer.Ordinal);

        foreach (CardModel card in deck)
        {
            if (!CardLibrary.TryGetValue(card.Id.Entry, out PvpShopCardDefinition? definition))
            {
                continue;
            }

            switch (definition.PrimaryClassBias)
            {
                case PvpShopClassBias.Attack:
                    attackCount++;
                    break;
                case PvpShopClassBias.Skill:
                    skillCount++;
                    break;
                case PvpShopClassBias.Power:
                    powerCount++;
                    break;
            }

            if (definition.Roles.Contains("Block"))
            {
                blockCount++;
            }

            if (definition.CurveCost <= 1)
            {
                cheapCount++;
            }

            foreach (string archetype in definition.Archetypes)
            {
                archetypeCounts[archetype] = archetypeCounts.TryGetValue(archetype, out int value) ? value + 1 : 1;
            }
        }

        List<string> orderedArchetypes = archetypeCounts.OrderByDescending(entry => entry.Value).Select(entry => entry.Key).ToList();
        PvpShopClassBias dominantClassBias = attackCount >= skillCount && attackCount >= powerCount
            ? PvpShopClassBias.Attack
            : skillCount >= powerCount
                ? PvpShopClassBias.Skill
                : PvpShopClassBias.Power;
        int targetBlockCount = Math.Max(2, deck.Count / 4);

        return new PvpShopDeckProfile
        {
            DominantClassBias = dominantClassBias,
            PrimaryArchetype = orderedArchetypes.ElementAtOrDefault(0) ?? "Tempo",
            SecondaryArchetype = orderedArchetypes.ElementAtOrDefault(1) ?? string.Empty,
            BlockCards = blockCount,
            DeckSize = deck.Count,
            NeedsBlock = blockCount < targetBlockCount,
            NeedsLowerCurve = cheapCount < Math.Max(4, deck.Count / 2)
        };
    }

    private static int GetRefreshCost(PvpShopModeDefinition modeDefinition, PvpShopPlayerState playerState, PvpShopRefreshType refreshType)
    {
        int baseCost = modeDefinition.RefreshBaseCosts.TryGetValue(refreshType, out int configured) ? configured : 20;
        float multiplier = 1.0f + (playerState.RefreshCount * modeDefinition.RefreshCostGrowth);
        return (int)MathF.Ceiling(baseCost * multiplier);
    }

    private static void TrimSeenHistory(List<string> seenCardIds)
    {
        while (seenCardIds.Count > PvpShopDefaults.DefaultSeenHistory)
        {
            seenCardIds.RemoveAt(0);
        }
    }

    private static PvpShopRoundState CloneRoundState(PvpShopRoundState source)
    {
        return new PvpShopRoundState
        {
            IsOpen = source.IsOpen,
            RoundIndex = source.RoundIndex,
            SnapshotVersion = source.SnapshotVersion,
            StateVersion = source.StateVersion,
            OpenedAtUtc = source.OpenedAtUtc,
            ModeContext = new PvpShopModeContext
            {
                ModeId = source.ModeContext.ModeId,
                ModeVersion = source.ModeContext.ModeVersion,
                StrategyPackId = source.ModeContext.StrategyPackId,
                StrategyVersion = source.ModeContext.StrategyVersion,
                RngVersion = source.ModeContext.RngVersion,
                SchemaVersion = source.ModeContext.SchemaVersion
            },
            PlayerStates = source.PlayerStates.ToDictionary(
                entry => entry.Key,
                entry => ClonePlayerState(entry.Value))
        };
    }

    private static PvpShopPlayerState ClonePlayerState(PvpShopPlayerState source)
    {
        var clone = new PvpShopPlayerState
        {
            PlayerId = source.PlayerId,
            Gold = source.Gold,
            RefreshCount = source.RefreshCount,
            StateVersion = source.StateVersion,
            LastStatusText = source.LastStatusText
        };
        clone.SeenCardIds.AddRange(source.SeenCardIds);
        clone.PurchasedCardIds.AddRange(source.PurchasedCardIds);
        clone.RemovedCardIds.AddRange(source.RemovedCardIds);
        clone.Offers.AddRange(source.Offers.Select(CloneOffer));
        return clone;
    }

    private static PvpShopOffer CloneOffer(PvpShopOffer source)
    {
        return new PvpShopOffer
        {
            SlotIndex = source.SlotIndex,
            SlotKind = source.SlotKind,
            CardId = source.CardId,
            DisplayName = source.DisplayName,
            Price = source.Price,
            Available = source.Available,
            DebugScore = source.DebugScore
        };
    }

    private static int ComputeDeterministicSeed(params int[] values)
    {
        unchecked
        {
            int hash = (int)2166136261;
            foreach (int value in values)
            {
                hash ^= value;
                hash *= 16777619;
            }

            return hash;
        }
    }

    private static void LogRoundSummary(string action, PvpShopRoundState? state)
    {
        if (state == null)
        {
            return;
        }

        string summary = string.Join(", ", state.PlayerStates.OrderBy(entry => entry.Key).Select(entry =>
        {
            PvpShopPlayerState player = entry.Value;
            string offers = string.Join("/", player.Offers.Select(offer => $"{offer.SlotIndex}:{offer.CardId}:{offer.Price}:{(offer.Available ? "open" : "sold")}"));
            return $"{entry.Key}:gold={player.Gold},refresh={player.RefreshCount},state={player.StateVersion},offers=[{offers}]";
        }));
        Log.Info($"[ParallelTurnPvp][ShopEngine] {action}. round={state.RoundIndex} snapshotVersion={state.SnapshotVersion} stateVersion={state.StateVersion} players={summary}");
    }

    private sealed class PvpShopCardDefinition
    {
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
        public required Func<CardModel> Factory { get; init; }
        public required PvpShopClassBias PrimaryClassBias { get; init; }
        public required HashSet<string> Roles { get; init; }
        public required HashSet<string> Archetypes { get; init; }
        public required int CurveCost { get; init; }
        public required int BaseWeight { get; init; }
        public required int Price { get; init; }
        public bool IsHighCeiling { get; init; }
    }

    private static IReadOnlyDictionary<string, PvpShopCardDefinition> CreateCardLibrary()
    {
        return new Dictionary<string, PvpShopCardDefinition>(StringComparer.Ordinal)
        {
            ["STRIKE_NECROBINDER"] = new PvpShopCardDefinition
            {
                Id = "STRIKE_NECROBINDER",
                DisplayName = "Strike",
                Factory = () => ModelDb.Card<StrikeNecrobinder>().ToMutable(),
                PrimaryClassBias = PvpShopClassBias.Attack,
                Roles = new HashSet<string>(StringComparer.Ordinal) { "Damage" },
                Archetypes = new HashSet<string>(StringComparer.Ordinal) { "Tempo", "Burst" },
                CurveCost = 1,
                BaseWeight = 100,
                Price = 25
            },
            ["DEFEND_NECROBINDER"] = new PvpShopCardDefinition
            {
                Id = "DEFEND_NECROBINDER",
                DisplayName = "Defend",
                Factory = () => ModelDb.Card<DefendNecrobinder>().ToMutable(),
                PrimaryClassBias = PvpShopClassBias.Skill,
                Roles = new HashSet<string>(StringComparer.Ordinal) { "Block", "Support" },
                Archetypes = new HashSet<string>(StringComparer.Ordinal) { "Tempo", "Frontline" },
                CurveCost = 1,
                BaseWeight = 95,
                Price = 25
            },
            ["AFTERLIFE"] = new PvpShopCardDefinition
            {
                Id = "AFTERLIFE",
                DisplayName = "Afterlife",
                Factory = () => ModelDb.Card<Afterlife>().ToMutable(),
                PrimaryClassBias = PvpShopClassBias.Skill,
                Roles = new HashSet<string>(StringComparer.Ordinal) { "Scaling", "Support" },
                Archetypes = new HashSet<string>(StringComparer.Ordinal) { "Frontline", "Scaling" },
                CurveCost = 1,
                BaseWeight = 85,
                Price = 35,
                IsHighCeiling = true
            },
            ["POKE"] = new PvpShopCardDefinition
            {
                Id = "POKE",
                DisplayName = "Poke",
                Factory = () => ModelDb.Card<Poke>().ToMutable(),
                PrimaryClassBias = PvpShopClassBias.Attack,
                Roles = new HashSet<string>(StringComparer.Ordinal) { "Damage", "Tempo" },
                Archetypes = new HashSet<string>(StringComparer.Ordinal) { "Frontline", "Tempo" },
                CurveCost = 1,
                BaseWeight = 90,
                Price = 30
            },
            ["FRONTLINE_BRACE"] = new PvpShopCardDefinition
            {
                Id = "FRONTLINE_BRACE",
                DisplayName = "Frontline Brace",
                Factory = () => ModelDb.Card<FrontlineBrace>().ToMutable(),
                PrimaryClassBias = PvpShopClassBias.Skill,
                Roles = new HashSet<string>(StringComparer.Ordinal) { "Block", "Scaling" },
                Archetypes = new HashSet<string>(StringComparer.Ordinal) { "Frontline", "Scaling" },
                CurveCost = 1,
                BaseWeight = 105,
                Price = 30
            },
            ["BREAK_FORMATION"] = new PvpShopCardDefinition
            {
                Id = "BREAK_FORMATION",
                DisplayName = "Break Formation",
                Factory = () => ModelDb.Card<BreakFormation>().ToMutable(),
                PrimaryClassBias = PvpShopClassBias.Attack,
                Roles = new HashSet<string>(StringComparer.Ordinal) { "Damage", "Finisher" },
                Archetypes = new HashSet<string>(StringComparer.Ordinal) { "Burst", "Tempo" },
                CurveCost = 1,
                BaseWeight = 92,
                Price = 35,
                IsHighCeiling = true
            }
        };
    }
}

public static class PvpShopRuntimeRegistry
{
    private static readonly ConditionalWeakTable<RunState, PvpShopEngine> EngineTable = new();
    private static readonly ConditionalWeakTable<PvpShopRoundState, RunState> ShopStateOwnerTable = new();
    private static readonly Lazy<PvpShopConfigBundle> ConfigBundle = new(LoadConfigBundle);

    public static PvpShopEngine GetOrCreate(RunState runState)
    {
        ArgumentNullException.ThrowIfNull(runState);
        return EngineTable.GetValue(runState, state =>
        {
            PvpShopConfigBundle bundle = ConfigBundle.Value;
            var modeRegistry = new PvpShopModeRegistry();
            var strategyRegistry = new PvpShopStrategyRegistry();
            foreach (PvpShopStrategyPack strategyPack in bundle.StrategyPacks)
            {
                strategyRegistry.Register(strategyPack);
            }

            foreach (PvpShopModeDefinition mode in bundle.Modes)
            {
                modeRegistry.Register(mode);
            }

            Log.Info($"[ParallelTurnPvp][ShopEngine] Loaded config. schemaVersion={bundle.Engine.SchemaVersion} rngVersion={bundle.Engine.RngVersion} startGold={bundle.Engine.StartGold} modes={bundle.Modes.Count} strategyPacks={bundle.StrategyPacks.Count}");
            return new PvpShopEngine(state, bundle.Engine, modeRegistry, strategyRegistry);
        });
    }

    public static RunState? TryGetRunState(PvpShopRoundState state)
    {
        if (state == null)
        {
            return null;
        }

        return ShopStateOwnerTable.TryGetValue(state, out RunState? runState) ? runState : null;
    }

    public static PvpShopRoundState? Snapshot(RunState runState)
    {
        PvpShopEngine engine = GetOrCreate(runState);
        PvpShopRoundState? snapshot = engine.CreateSnapshot();
        if (snapshot != null)
        {
            SetSnapshotOwner(snapshot, runState);
        }

        return snapshot;
    }

    public static void RegisterSnapshotOwner(RunState runState, PvpShopRoundState state)
    {
        if (runState == null || state == null)
        {
            return;
        }

        SetSnapshotOwner(state, runState);
    }

    private static void SetSnapshotOwner(PvpShopRoundState state, RunState runState)
    {
        ShopStateOwnerTable.Remove(state);
        ShopStateOwnerTable.Add(state, runState);
    }

    private static PvpShopConfigBundle LoadConfigBundle()
    {
        PvpShopConfigBundle defaults = PvpShopConfigBundle.CreateDefault();
        try
        {
            if (!Godot.FileAccess.FileExists(PvpShopDefaults.ConfigPath))
            {
                Log.Warn($"[ParallelTurnPvp][ShopEngine] Config file missing. Using defaults. path={PvpShopDefaults.ConfigPath}");
                return defaults;
            }

            using Godot.FileAccess file = Godot.FileAccess.Open(PvpShopDefaults.ConfigPath, Godot.FileAccess.ModeFlags.Read);
            string json = file.GetAsText();
            PvpShopConfigDocument? document = JsonSerializer.Deserialize<PvpShopConfigDocument>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (document == null)
            {
                Log.Warn($"[ParallelTurnPvp][ShopEngine] Config parse returned null. Using defaults. path={PvpShopDefaults.ConfigPath}");
                return defaults;
            }

            return PvpShopConfigBundle.FromDocument(document, defaults);
        }
        catch (Exception ex)
        {
            Log.Error($"[ParallelTurnPvp][ShopEngine] Failed to load config. Using defaults. path={PvpShopDefaults.ConfigPath} error={ex.Message}");
            return defaults;
        }
    }

    private sealed class PvpShopConfigBundle
    {
        public required PvpShopEngineConfig Engine { get; init; }
        public required IReadOnlyList<PvpShopModeDefinition> Modes { get; init; }
        public required IReadOnlyList<PvpShopStrategyPack> StrategyPacks { get; init; }

        public static PvpShopConfigBundle CreateDefault()
        {
            return new PvpShopConfigBundle
            {
                Engine = new PvpShopEngineConfig(),
                Modes =
                [
                    new PvpShopModeDefinition
                    {
                        ModeId = PvpShopDefaults.StandardModeId,
                        ModeVersion = PvpShopDefaults.StandardModeVersion,
                        StrategyPackId = PvpShopDefaults.StandardStrategyPackId
                    }
                ],
                StrategyPacks =
                [
                    new PvpShopStrategyPack
                    {
                        Id = PvpShopDefaults.StandardStrategyPackId,
                        Version = PvpShopDefaults.StandardStrategyVersion
                    }
                ]
            };
        }

        public static PvpShopConfigBundle FromDocument(PvpShopConfigDocument document, PvpShopConfigBundle defaults)
        {
            PvpShopEngineConfig engine = new()
            {
                SchemaVersion = document.schemaVersion > 0 ? document.schemaVersion : defaults.Engine.SchemaVersion,
                RngVersion = string.IsNullOrWhiteSpace(document.engine?.rngVersion) ? defaults.Engine.RngVersion : document.engine!.rngVersion!,
                StartGold = document.engine?.startGold > 0 ? document.engine.startGold : defaults.Engine.StartGold,
                MaxSeenHistory = document.engine?.maxSeenHistory > 0 ? document.engine.maxSeenHistory : defaults.Engine.MaxSeenHistory
            };

            IReadOnlyList<PvpShopStrategyPack> strategies = (document.strategyPacks ?? [])
                .Where(entry => !string.IsNullOrWhiteSpace(entry.id))
                .Select(entry => new PvpShopStrategyPack
                {
                    Id = entry.id!,
                    Version = string.IsNullOrWhiteSpace(entry.strategyVersion) ? PvpShopDefaults.StandardStrategyVersion : entry.strategyVersion!,
                    ArchetypeFitWeight = entry.scoringWeights?.ArchetypeFit ?? defaults.StrategyPacks[0].ArchetypeFitWeight,
                    RoleNeedWeight = entry.scoringWeights?.RoleNeed ?? defaults.StrategyPacks[0].RoleNeedWeight,
                    CurveFitWeight = entry.scoringWeights?.CurveFit ?? defaults.StrategyPacks[0].CurveFitWeight,
                    ClassIntentWeight = entry.scoringWeights?.ClassIntent ?? defaults.StrategyPacks[0].ClassIntentWeight,
                    NoveltyWeight = entry.scoringWeights?.Novelty ?? defaults.StrategyPacks[0].NoveltyWeight,
                    HighCeilingWeight = entry.scoringWeights?.HighCeiling ?? defaults.StrategyPacks[0].HighCeilingWeight,
                    MinSupportOffers = entry.minSupportOffers > 0 ? entry.minSupportOffers : defaults.StrategyPacks[0].MinSupportOffers
                })
                .ToList();
            if (strategies.Count == 0)
            {
                strategies = defaults.StrategyPacks;
            }

            IReadOnlyList<PvpShopModeDefinition> modes = (document.modes ?? [])
                .Where(entry => !string.IsNullOrWhiteSpace(entry.modeId) && !string.IsNullOrWhiteSpace(entry.strategyPackId))
                .Select(entry => new PvpShopModeDefinition
                {
                    ModeId = entry.modeId!,
                    ModeVersion = string.IsNullOrWhiteSpace(entry.modeVersion) ? PvpShopDefaults.StandardModeVersion : entry.modeVersion!,
                    StrategyPackId = entry.strategyPackId!,
                    Slots = entry.shop?.slots > 0 ? entry.shop.slots : 5,
                    Template = ParseTemplate(entry.shop?.template),
                    RefreshBaseCosts = ParseRefreshCosts(entry.refresh?.baseCost),
                    RefreshCostGrowth = entry.refresh?.costGrowth > 0 ? entry.refresh.costGrowth : 0.35f
                })
                .ToList();
            if (modes.Count == 0)
            {
                modes = defaults.Modes;
            }

            return new PvpShopConfigBundle
            {
                Engine = engine,
                Modes = modes,
                StrategyPacks = strategies
            };
        }

        private static IReadOnlyList<PvpShopSlotKind> ParseTemplate(List<string>? template)
        {
            if (template == null || template.Count == 0)
            {
                return new PvpShopModeDefinition().Template;
            }

            List<PvpShopSlotKind> parsed = new(template.Count);
            foreach (string value in template)
            {
                if (Enum.TryParse(value, ignoreCase: true, out PvpShopSlotKind slotKind))
                {
                    parsed.Add(slotKind);
                }
            }

            return parsed.Count > 0 ? parsed : new PvpShopModeDefinition().Template;
        }

        private static IReadOnlyDictionary<PvpShopRefreshType, int> ParseRefreshCosts(PvpShopRefreshCostDocument? source)
        {
            Dictionary<PvpShopRefreshType, int> fallback = new PvpShopModeDefinition().RefreshBaseCosts.ToDictionary(entry => entry.Key, entry => entry.Value);
            if (source == null)
            {
                return fallback;
            }

            fallback[PvpShopRefreshType.Normal] = source.Normal > 0 ? source.Normal : fallback[PvpShopRefreshType.Normal];
            fallback[PvpShopRefreshType.ClassBias] = source.ClassBias > 0 ? source.ClassBias : fallback[PvpShopRefreshType.ClassBias];
            fallback[PvpShopRefreshType.RoleFix] = source.RoleFix > 0 ? source.RoleFix : fallback[PvpShopRefreshType.RoleFix];
            fallback[PvpShopRefreshType.ArchetypeTrace] = source.ArchetypeTrace > 0 ? source.ArchetypeTrace : fallback[PvpShopRefreshType.ArchetypeTrace];
            return fallback;
        }
    }

    private sealed class PvpShopConfigDocument
    {
        public int schemaVersion { get; set; }
        public PvpShopEngineDocument? engine { get; set; }
        public List<PvpShopModeDocument>? modes { get; set; }
        public List<PvpShopStrategyDocument>? strategyPacks { get; set; }
    }

    private sealed class PvpShopEngineDocument
    {
        public string? rngVersion { get; set; }
        public int startGold { get; set; }
        public int maxSeenHistory { get; set; }
    }

    private sealed class PvpShopModeDocument
    {
        public string? modeId { get; set; }
        public string? modeVersion { get; set; }
        public string? strategyPackId { get; set; }
        public PvpShopModeShopDocument? shop { get; set; }
        public PvpShopModeRefreshDocument? refresh { get; set; }
    }

    private sealed class PvpShopModeShopDocument
    {
        public int slots { get; set; }
        public List<string>? template { get; set; }
    }

    private sealed class PvpShopModeRefreshDocument
    {
        public PvpShopRefreshCostDocument? baseCost { get; set; }
        public float costGrowth { get; set; }
    }

    private sealed class PvpShopRefreshCostDocument
    {
        public int Normal { get; set; }
        public int ClassBias { get; set; }
        public int RoleFix { get; set; }
        public int ArchetypeTrace { get; set; }
    }

    private sealed class PvpShopStrategyDocument
    {
        public string? id { get; set; }
        public string? strategyVersion { get; set; }
        public PvpShopScoringWeightDocument? scoringWeights { get; set; }
        public int minSupportOffers { get; set; }
    }

    private sealed class PvpShopScoringWeightDocument
    {
        public float ArchetypeFit { get; set; }
        public float RoleNeed { get; set; }
        public float CurveFit { get; set; }
        public float ClassIntent { get; set; }
        public float Novelty { get; set; }
        public float HighCeiling { get; set; }
    }
}

public static class PvpShopBridge
{
    public static bool TryOpenRound(RunState runState, int roundIndex, int snapshotVersion, string? modeId = null)
    {
        if (runState == null)
        {
            return false;
        }

        string resolvedModeId = string.IsNullOrWhiteSpace(modeId) ? PvpShopDefaults.StandardModeId : modeId;
        return PvpShopRuntimeRegistry.GetOrCreate(runState).TryOpenRound(roundIndex, snapshotVersion, resolvedModeId, out _);
    }

    public static bool TryGetView(RunState runState, ulong playerId, out PvpShopViewModel view)
    {
        view = default!;
        return runState != null && PvpShopRuntimeRegistry.GetOrCreate(runState).TryGetView(playerId, out view);
    }

    public static bool TryRefresh(RunState runState, ulong playerId, PvpShopRefreshType refreshType, out string reason)
    {
        reason = string.Empty;
        return runState != null && PvpShopRuntimeRegistry.GetOrCreate(runState).TryRefresh(playerId, refreshType, out reason);
    }

    public static bool TryPurchase(RunState runState, ulong playerId, int slotIndex, out string reason)
    {
        reason = string.Empty;
        return runState != null && PvpShopRuntimeRegistry.GetOrCreate(runState).TryPurchase(playerId, slotIndex, out reason);
    }

    public static bool TryDeleteCard(RunState runState, ulong playerId, int deckCardIndex, out string reason)
    {
        reason = string.Empty;
        return runState != null && PvpShopRuntimeRegistry.GetOrCreate(runState).TryDeleteCard(playerId, deckCardIndex, out reason);
    }

    public static PvpShopRoundState? Snapshot(RunState runState)
    {
        return runState == null ? null : PvpShopRuntimeRegistry.Snapshot(runState);
    }

    public static void RegisterSnapshotOwner(RunState runState, PvpShopRoundState? state)
    {
        if (runState == null || state == null)
        {
            return;
        }

        PvpShopRuntimeRegistry.RegisterSnapshotOwner(runState, state);
    }

    public static bool TrySendRefreshRequest(RunState runState, ulong playerId, PvpShopRefreshType refreshType, out string reason)
    {
        reason = string.Empty;
        if (runState == null)
        {
            return false;
        }

        return new PvpShopNetBridge().TrySendRefreshRequest(runState, playerId, refreshType, out reason);
    }

    public static bool TrySendPurchaseRequest(RunState runState, ulong playerId, int slotIndex, out string reason)
    {
        reason = string.Empty;
        if (runState == null)
        {
            return false;
        }

        return new PvpShopNetBridge().TrySendPurchaseRequest(runState, playerId, slotIndex, out reason);
    }

    public static bool TrySendDeleteRequest(RunState runState, ulong playerId, int deckCardIndex, out string reason)
    {
        reason = string.Empty;
        if (runState == null)
        {
            return false;
        }

        return new PvpShopNetBridge().TrySendDeleteRequest(runState, playerId, deckCardIndex, out reason);
    }

    public static void ApplyAuthoritativeState(RunState runState, PvpShopRoundState? state)
    {
        if (runState == null)
        {
            return;
        }

        PvpShopRuntimeRegistry.GetOrCreate(runState).ApplyAuthoritativeState(state);
    }

    public static bool TryCloseRound(RunState runState)
    {
        return runState != null && PvpShopRuntimeRegistry.GetOrCreate(runState).TryCloseRound();
    }
}

public static class PvpShopFeatureFlags
{
    private const string ShopDraftEnvKey = "PTPVP_ENABLE_SHOP_DRAFT";
    private static readonly bool EnableShopDraft = ResolveShopDraftFlag();

    public static bool IsShopDraftEnabled => EnableShopDraft;

    private static bool ResolveShopDraftFlag()
    {
        string? raw = System.Environment.GetEnvironmentVariable(ShopDraftEnvKey);
        if (string.IsNullOrWhiteSpace(raw))
        {
            // Combat-engine focused mainline: keep shop draft OFF by default.
            // Can be force-enabled with PTPVP_ENABLE_SHOP_DRAFT=1.
            return false;
        }

        if (raw == "1" || raw.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (raw == "0" || raw.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return false;
    }
}
