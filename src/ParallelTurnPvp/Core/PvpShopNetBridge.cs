using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace ParallelTurnPvp.Core;

public sealed class PvpShopNetBridge
{
    private static object? _registeredService;

    public static void EnsureRegistered()
    {
        RunManager? runManager = RunManager.Instance;
        if (runManager == null)
        {
            return;
        }

        var netService = runManager.NetService;
        if (netService == null || ReferenceEquals(_registeredService, netService))
        {
            return;
        }

        netService.RegisterMessageHandler<PvpShopStateMessage>(HandleShopStateMessage);
        netService.RegisterMessageHandler<PvpShopRequestMessage>(HandleShopRequestMessage);
        netService.RegisterMessageHandler<PvpShopRequestAckMessage>(HandleShopRequestAckMessage);
        netService.RegisterMessageHandler<PvpShopClosedMessage>(HandleShopClosedMessage);
        _registeredService = netService;
        Log.Info($"[ParallelTurnPvp][ShopSync] Registered shop message handlers. netType={netService.Type} inProgress={runManager.IsInProgress}");
    }

    public void BroadcastShopState(RunState runState, PvpShopRoundState state, bool force = false)
    {
        ArgumentNullException.ThrowIfNull(runState);
        ArgumentNullException.ThrowIfNull(state);
        EnsureRegistered();

        RunManager? runManager = RunManager.Instance;
        if (runManager == null || !runManager.IsInProgress || runManager.NetService.Type != NetGameType.Host)
        {
            return;
        }

        PvpMatchRuntime runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        PvpShopSyncRuntime syncRuntime = PvpShopSyncRuntimeRegistry.GetOrCreate(runState);
        if (!force && !syncRuntime.TryMarkShopStateBroadcast(state.RoundIndex, state.StateVersion))
        {
            Log.Info($"[ParallelTurnPvp][ShopSync] Skipped duplicate shop state broadcast. round={state.RoundIndex} snapshotVersion={state.SnapshotVersion} shopStateVersion={state.StateVersion}");
            return;
        }

        runManager.NetService.SendMessage(CreateStateMessage(runtime, state));
        Log.Info($"[ParallelTurnPvp][ShopSync] Broadcast shop state. round={state.RoundIndex} snapshotVersion={state.SnapshotVersion} shopStateVersion={state.StateVersion} mode={state.ModeContext.ModeId} players={state.PlayerStates.Count} force={force}");
    }

    public void BroadcastShopClosed(RunState runState, int roundIndex, int snapshotVersion, int shopStateVersion)
    {
        ArgumentNullException.ThrowIfNull(runState);
        EnsureRegistered();

        RunManager? runManager = RunManager.Instance;
        if (runManager == null || !runManager.IsInProgress || runManager.NetService.Type != NetGameType.Host)
        {
            return;
        }

        PvpMatchRuntime runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        PvpShopSyncRuntime syncRuntime = PvpShopSyncRuntimeRegistry.GetOrCreate(runState);
        if (!syncRuntime.TryMarkShopClosedBroadcast(roundIndex, shopStateVersion))
        {
            Log.Info($"[ParallelTurnPvp][ShopSync] Skipped duplicate shop close broadcast. round={roundIndex} snapshotVersion={snapshotVersion} shopStateVersion={shopStateVersion}");
            return;
        }

        runManager.NetService.SendMessage(new PvpShopClosedMessage
        {
            roomSessionId = runtime.RoomSession.SessionId,
            roomTopology = (int)runtime.RoomSession.Topology,
            roundIndex = roundIndex,
            snapshotVersion = snapshotVersion,
            shopStateVersion = shopStateVersion
        });
        Log.Info($"[ParallelTurnPvp][ShopSync] Broadcast shop closed. round={roundIndex} snapshotVersion={snapshotVersion} shopStateVersion={shopStateVersion}");
    }

    public bool TrySendRefreshRequest(RunState runState, ulong playerId, PvpShopRefreshType refreshType, out string reason)
    {
        return TrySendRequest(runState, playerId, PvpShopRequestKind.Refresh, refreshType, slotIndex: -1, out reason);
    }

    public bool TrySendPurchaseRequest(RunState runState, ulong playerId, int slotIndex, out string reason)
    {
        return TrySendRequest(runState, playerId, PvpShopRequestKind.Purchase, refreshType: PvpShopRefreshType.Normal, slotIndex, out reason);
    }

    public bool TrySendDeleteRequest(RunState runState, ulong playerId, int deckCardIndex, out string reason)
    {
        return TrySendRequest(runState, playerId, PvpShopRequestKind.DeleteCard, refreshType: PvpShopRefreshType.Normal, deckCardIndex, out reason);
    }

    private bool TrySendRequest(RunState runState, ulong playerId, PvpShopRequestKind requestKind, PvpShopRefreshType refreshType, int slotIndex, out string reason)
    {
        reason = string.Empty;
        ArgumentNullException.ThrowIfNull(runState);
        EnsureRegistered();

        RunManager? runManager = RunManager.Instance;
        if (runManager == null || !runManager.IsInProgress || runManager.NetService.Type != NetGameType.Client)
        {
            reason = "当前不是客户端联机状态。";
            return false;
        }

        PvpMatchRuntime runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        PvpShopRoundState? state = PvpShopRuntimeRegistry.GetOrCreate(runState).CurrentRound;
        if (state is not { IsOpen: true })
        {
            reason = "商店未开启。";
            return false;
        }

        ulong resolvedPlayerId = runtime.RoomSession.LocalPlayerId != 0 ? runtime.RoomSession.LocalPlayerId : playerId;
        if (resolvedPlayerId == 0)
        {
            reason = "无法解析本地玩家。";
            return false;
        }

        PvpShopSyncRuntime syncRuntime = PvpShopSyncRuntimeRegistry.GetOrCreate(runState);
        string requestKey = BuildLocalRequestKey(state, requestKind, refreshType, slotIndex);
        DateTime nowUtc = DateTime.UtcNow;
        if (!syncRuntime.CanSendLocalRequest(
                resolvedPlayerId,
                requestKey,
                nowUtc,
                throttleWindow: TimeSpan.FromMilliseconds(200),
                pendingTimeout: TimeSpan.FromSeconds(2),
                out string reasonCode))
        {
            reason = reasonCode switch
            {
                "pending" => "相同请求正在等待主机确认，请稍候。",
                "throttled" => "请求过于频繁，请稍候重试。",
                _ => "请求被本地门控拒绝。"
            };
            Log.Info($"[ParallelTurnPvp][ShopSync] Blocked local duplicate/throttled request. round={state.RoundIndex} snapshotVersion={state.SnapshotVersion} shopStateVersion={state.StateVersion} player={resolvedPlayerId} kind={requestKind} refresh={refreshType} slot={slotIndex} reason={reasonCode}");
            return false;
        }

        int requestRevision = syncRuntime.ReserveNextLocalRequestRevision(resolvedPlayerId);
        syncRuntime.MarkLocalRequestSent(resolvedPlayerId, requestKey, requestRevision, nowUtc);
        runManager.NetService.SendMessage(new PvpShopRequestMessage
        {
            roomSessionId = runtime.RoomSession.SessionId,
            roomTopology = (int)runtime.RoomSession.Topology,
            roundIndex = state.RoundIndex,
            snapshotVersion = state.SnapshotVersion,
            shopStateVersion = state.StateVersion,
            requestRevision = requestRevision,
            playerId = resolvedPlayerId,
            requestKind = (int)requestKind,
            refreshType = (int)refreshType,
            slotIndex = slotIndex
        });

        reason = requestKind switch
        {
            PvpShopRequestKind.Refresh => $"已发送刷新请求：{refreshType}。",
            PvpShopRequestKind.Purchase => $"已发送购买请求：slot={slotIndex}。",
            PvpShopRequestKind.DeleteCard => $"已发送删卡请求：deckIndex={slotIndex}。",
            _ => "已发送商店请求。"
        };
        Log.Info($"[ParallelTurnPvp][ShopSync] Sent shop request. round={state.RoundIndex} snapshotVersion={state.SnapshotVersion} shopStateVersion={state.StateVersion} player={resolvedPlayerId} revision={requestRevision} kind={requestKind} refresh={refreshType} slot={slotIndex}");
        return true;
    }

    private static void HandleShopStateMessage(PvpShopStateMessage message, ulong _)
    {
        if (message.roundIndex <= 0 || message.snapshotVersion <= 0 || message.shopStateVersion <= 0 || RunManager.Instance.DebugOnlyGetState() is not RunState runState)
        {
            return;
        }

        PvpMatchRuntime runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        if (!ValidateRoomContext(runtime, message.roomSessionId, message.roomTopology, nameof(PvpShopStateMessage)))
        {
            return;
        }

        PvpShopSyncRuntime syncRuntime = PvpShopSyncRuntimeRegistry.GetOrCreate(runState);
        if (!syncRuntime.TryMarkShopStateReceived(message.roundIndex, message.shopStateVersion))
        {
            Log.Info($"[ParallelTurnPvp][ShopSync] Ignored duplicate/stale shop state. round={message.roundIndex} snapshotVersion={message.snapshotVersion} shopStateVersion={message.shopStateVersion}");
            return;
        }

        PvpShopRoundState state = CreateRoundState(message);
        PvpShopRuntimeRegistry.RegisterSnapshotOwner(runState, state);
        PvpShopBridge.ApplyAuthoritativeState(runState, state);
        Log.Info($"[ParallelTurnPvp][ShopSync] Applied authoritative shop state. round={message.roundIndex} snapshotVersion={message.snapshotVersion} shopStateVersion={message.shopStateVersion} mode={message.modeId} players={message.players?.Count ?? 0}");
    }

    private static void HandleShopRequestMessage(PvpShopRequestMessage message, ulong senderPlayerId)
    {
        RunManager? runManager = RunManager.Instance;
        if (runManager == null || runManager.NetService.Type != NetGameType.Host)
        {
            return;
        }

        if (message.roundIndex <= 0 || message.snapshotVersion <= 0 || message.shopStateVersion <= 0 || message.requestRevision <= 0 || runManager.DebugOnlyGetState() is not RunState runState)
        {
            return;
        }

        PvpMatchRuntime runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        if (!ValidateRoomContext(runtime, message.roomSessionId, message.roomTopology, nameof(PvpShopRequestMessage)))
        {
            return;
        }

        if (message.playerId == 0 || message.playerId != senderPlayerId)
        {
            SendRequestAck(runtime, senderPlayerId, message.roundIndex, message.snapshotVersion, message.shopStateVersion, Math.Max(message.requestRevision, 1), accepted: false, note: "sender_player_mismatch");
            return;
        }

        PvpShopEngine engine = PvpShopRuntimeRegistry.GetOrCreate(runState);
        PvpShopRoundState? current = engine.CurrentRound;
        if (current is not { IsOpen: true })
        {
            SendRequestAck(runtime, senderPlayerId, message.roundIndex, message.snapshotVersion, Math.Max(message.shopStateVersion, 1), message.requestRevision, accepted: false, note: "shop_closed");
            return;
        }

        if (current.RoundIndex != message.roundIndex)
        {
            SendRequestAck(runtime, senderPlayerId, message.roundIndex, current.SnapshotVersion, current.StateVersion, message.requestRevision, accepted: false, note: "round_mismatch");
            return;
        }

        if (current.SnapshotVersion != message.snapshotVersion)
        {
            SendRequestAck(runtime, senderPlayerId, current.RoundIndex, current.SnapshotVersion, current.StateVersion, message.requestRevision, accepted: false, note: "snapshot_mismatch");
            return;
        }

        if (message.shopStateVersion < current.StateVersion)
        {
            SendRequestAck(runtime, senderPlayerId, current.RoundIndex, current.SnapshotVersion, current.StateVersion, message.requestRevision, accepted: false, note: "stale_shop_state");
            return;
        }

        if (message.shopStateVersion > current.StateVersion)
        {
            SendRequestAck(runtime, senderPlayerId, current.RoundIndex, current.SnapshotVersion, current.StateVersion, message.requestRevision, accepted: false, note: "future_shop_state");
            return;
        }

        if (!Enum.IsDefined(typeof(PvpShopRequestKind), message.requestKind))
        {
            SendRequestAck(runtime, senderPlayerId, current.RoundIndex, current.SnapshotVersion, current.StateVersion, message.requestRevision, accepted: false, note: "invalid_request_kind");
            return;
        }

        PvpShopRequestKind requestKind = (PvpShopRequestKind)message.requestKind;
        string payloadSignature = BuildRequestSignature(message);
        PvpShopSyncRuntime syncRuntime = PvpShopSyncRuntimeRegistry.GetOrCreate(runState);
        PvpShopRequestRevisionDecision revisionDecision = syncRuntime.ClassifyIncomingRequest(senderPlayerId, message.requestRevision, payloadSignature);
        switch (revisionDecision)
        {
            case PvpShopRequestRevisionDecision.StaleRevision:
                SendRequestAck(runtime, senderPlayerId, current.RoundIndex, current.SnapshotVersion, current.StateVersion, message.requestRevision, accepted: false, note: "stale_revision");
                return;
            case PvpShopRequestRevisionDecision.ConflictSameRevision:
                SendRequestAck(runtime, senderPlayerId, current.RoundIndex, current.SnapshotVersion, current.StateVersion, message.requestRevision, accepted: false, note: "conflicting_payload");
                return;
            case PvpShopRequestRevisionDecision.DuplicateSamePayload:
                SendRequestAck(runtime, senderPlayerId, current.RoundIndex, current.SnapshotVersion, current.StateVersion, message.requestRevision, accepted: true, note: "already_applied");
                new PvpShopNetBridge().BroadcastShopState(runState, current, force: true);
                return;
        }

        bool success;
        string reason;
        switch (requestKind)
        {
            case PvpShopRequestKind.Refresh:
                if (!Enum.IsDefined(typeof(PvpShopRefreshType), message.refreshType))
                {
                    SendRequestAck(runtime, senderPlayerId, current.RoundIndex, current.SnapshotVersion, current.StateVersion, message.requestRevision, accepted: false, note: "invalid_refresh_type");
                    return;
                }

                success = engine.TryRefresh(senderPlayerId, (PvpShopRefreshType)message.refreshType, out reason);
                break;
            case PvpShopRequestKind.Purchase:
                if (message.slotIndex < 0)
                {
                    SendRequestAck(runtime, senderPlayerId, current.RoundIndex, current.SnapshotVersion, current.StateVersion, message.requestRevision, accepted: false, note: "invalid_slot_index");
                    return;
                }

                success = engine.TryPurchase(senderPlayerId, message.slotIndex, out reason);
                break;
            case PvpShopRequestKind.DeleteCard:
                if (message.slotIndex < 0)
                {
                    SendRequestAck(runtime, senderPlayerId, current.RoundIndex, current.SnapshotVersion, current.StateVersion, message.requestRevision, accepted: false, note: "invalid_deck_index");
                    return;
                }

                success = engine.TryDeleteCard(senderPlayerId, message.slotIndex, out reason);
                break;
            default:
                SendRequestAck(runtime, senderPlayerId, current.RoundIndex, current.SnapshotVersion, current.StateVersion, message.requestRevision, accepted: false, note: "unsupported_request_kind");
                return;
        }

        if (!success)
        {
            SendRequestAck(runtime, senderPlayerId, current.RoundIndex, current.SnapshotVersion, current.StateVersion, message.requestRevision, accepted: false, note: NormalizeNote(reason));
            Log.Warn($"[ParallelTurnPvp][ShopSync] Rejected shop request. round={current.RoundIndex} snapshotVersion={current.SnapshotVersion} shopStateVersion={current.StateVersion} player={senderPlayerId} revision={message.requestRevision} kind={requestKind} reason={reason}");
            return;
        }

        syncRuntime.MarkRequestApplied(senderPlayerId, message.requestRevision, payloadSignature);
        PvpShopRoundState? snapshot = PvpShopRuntimeRegistry.Snapshot(runState);
        if (snapshot == null)
        {
            SendRequestAck(runtime, senderPlayerId, current.RoundIndex, current.SnapshotVersion, current.StateVersion, message.requestRevision, accepted: false, note: "snapshot_unavailable");
            return;
        }

        SendRequestAck(runtime, senderPlayerId, snapshot.RoundIndex, snapshot.SnapshotVersion, snapshot.StateVersion, message.requestRevision, accepted: true, note: "accepted");
        new PvpShopNetBridge().BroadcastShopState(runState, snapshot);
        Log.Info($"[ParallelTurnPvp][ShopSync] Accepted shop request. round={snapshot.RoundIndex} snapshotVersion={snapshot.SnapshotVersion} shopStateVersion={snapshot.StateVersion} player={senderPlayerId} revision={message.requestRevision} kind={requestKind} note={reason}");
    }

    private static void HandleShopRequestAckMessage(PvpShopRequestAckMessage message, ulong _)
    {
        RunManager? runManager = RunManager.Instance;
        if (runManager == null || runManager.NetService.Type != NetGameType.Client || message.requestRevision <= 0 || runManager.DebugOnlyGetState() is not RunState runState)
        {
            return;
        }

        PvpMatchRuntime runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        if (!ValidateRoomContext(runtime, message.roomSessionId, message.roomTopology, nameof(PvpShopRequestAckMessage)))
        {
            return;
        }

        if (message.playerId != runtime.RoomSession.LocalPlayerId)
        {
            return;
        }

        PvpShopSyncRuntimeRegistry.GetOrCreate(runState).MarkRequestAcked(message.playerId, message.requestRevision);
        if (message.accepted)
        {
            Log.Info($"[ParallelTurnPvp][ShopSync] Received shop ACK. round={message.roundIndex} snapshotVersion={message.snapshotVersion} shopStateVersion={message.shopStateVersion} player={message.playerId} revision={message.requestRevision} note={message.note}");
            return;
        }

        Log.Warn($"[ParallelTurnPvp][ShopSync] Received shop NACK. round={message.roundIndex} snapshotVersion={message.snapshotVersion} shopStateVersion={message.shopStateVersion} player={message.playerId} revision={message.requestRevision} note={message.note}");
    }

    private static void HandleShopClosedMessage(PvpShopClosedMessage message, ulong _)
    {
        if (message.roundIndex <= 0 || message.snapshotVersion <= 0 || message.shopStateVersion <= 0 || RunManager.Instance.DebugOnlyGetState() is not RunState runState)
        {
            return;
        }

        PvpMatchRuntime runtime = PvpRuntimeRegistry.GetOrCreate(runState);
        if (!ValidateRoomContext(runtime, message.roomSessionId, message.roomTopology, nameof(PvpShopClosedMessage)))
        {
            return;
        }

        PvpShopSyncRuntime syncRuntime = PvpShopSyncRuntimeRegistry.GetOrCreate(runState);
        if (!syncRuntime.TryMarkShopClosedReceived(message.roundIndex, message.shopStateVersion))
        {
            Log.Info($"[ParallelTurnPvp][ShopSync] Ignored duplicate/stale shop closed event. round={message.roundIndex} snapshotVersion={message.snapshotVersion} shopStateVersion={message.shopStateVersion}");
            return;
        }

        PvpShopBridge.ApplyAuthoritativeState(runState, null);
        Log.Info($"[ParallelTurnPvp][ShopSync] Applied shop closed event. round={message.roundIndex} snapshotVersion={message.snapshotVersion} shopStateVersion={message.shopStateVersion}");
    }

    private static void SendRequestAck(PvpMatchRuntime runtime, ulong playerId, int roundIndex, int snapshotVersion, int shopStateVersion, int requestRevision, bool accepted, string note)
    {
        RunManager? runManager = RunManager.Instance;
        if (runManager == null || !runManager.IsInProgress || runManager.NetService.Type != NetGameType.Host)
        {
            return;
        }

        runManager.NetService.SendMessage(new PvpShopRequestAckMessage
        {
            roomSessionId = runtime.RoomSession.SessionId,
            roomTopology = (int)runtime.RoomSession.Topology,
            roundIndex = roundIndex,
            snapshotVersion = snapshotVersion,
            shopStateVersion = shopStateVersion,
            playerId = playerId,
            requestRevision = requestRevision,
            accepted = accepted,
            note = note ?? string.Empty
        });
    }

    private static PvpShopStateMessage CreateStateMessage(PvpMatchRuntime runtime, PvpShopRoundState state)
    {
        return new PvpShopStateMessage
        {
            roomSessionId = runtime.RoomSession.SessionId,
            roomTopology = (int)runtime.RoomSession.Topology,
            roundIndex = state.RoundIndex,
            snapshotVersion = state.SnapshotVersion,
            shopStateVersion = state.StateVersion,
            modeId = state.ModeContext.ModeId,
            modeVersion = state.ModeContext.ModeVersion,
            strategyPackId = state.ModeContext.StrategyPackId,
            strategyVersion = state.ModeContext.StrategyVersion,
            rngVersion = state.ModeContext.RngVersion,
            players = state.PlayerStates.OrderBy(entry => entry.Key).Select(entry => new PvpShopPlayerStatePacket
            {
                playerId = entry.Key,
                gold = entry.Value.Gold,
                refreshCount = entry.Value.RefreshCount,
                playerStateVersion = entry.Value.StateVersion,
                statusText = entry.Value.LastStatusText,
                purchasedCardIds = entry.Value.PurchasedCardIds.ToList(),
                removedCardIds = entry.Value.RemovedCardIds.ToList(),
                offers = entry.Value.Offers.Select(offer => new PvpShopOfferPacket
                {
                    slotIndex = offer.SlotIndex,
                    slotKind = (int)offer.SlotKind,
                    cardId = offer.CardId,
                    displayName = offer.DisplayName,
                    price = offer.Price,
                    available = offer.Available
                }).ToList()
            }).ToList()
        };
    }

    private static PvpShopRoundState CreateRoundState(PvpShopStateMessage message)
    {
        Dictionary<ulong, PvpShopPlayerState> playerStates = new();
        foreach (PvpShopPlayerStatePacket playerPacket in message.players ?? new List<PvpShopPlayerStatePacket>())
        {
            var playerState = new PvpShopPlayerState
            {
                PlayerId = playerPacket.playerId,
                Gold = playerPacket.gold,
                RefreshCount = playerPacket.refreshCount,
                StateVersion = playerPacket.playerStateVersion,
                LastStatusText = playerPacket.statusText ?? string.Empty
            };
            playerState.PurchasedCardIds.AddRange(playerPacket.purchasedCardIds ?? new List<string>());
            playerState.RemovedCardIds.AddRange(playerPacket.removedCardIds ?? new List<string>());
            foreach (PvpShopOfferPacket offerPacket in playerPacket.offers ?? new List<PvpShopOfferPacket>())
            {
                playerState.Offers.Add(new PvpShopOffer
                {
                    SlotIndex = offerPacket.slotIndex,
                    SlotKind = Enum.IsDefined(typeof(PvpShopSlotKind), offerPacket.slotKind)
                        ? (PvpShopSlotKind)offerPacket.slotKind
                        : PvpShopSlotKind.CoreArchetype,
                    CardId = offerPacket.cardId ?? string.Empty,
                    DisplayName = offerPacket.displayName ?? string.Empty,
                    Price = offerPacket.price,
                    Available = offerPacket.available,
                    DebugScore = 0f
                });
            }

            playerStates[playerPacket.playerId] = playerState;
        }

        return new PvpShopRoundState
        {
            IsOpen = true,
            RoundIndex = message.roundIndex,
            SnapshotVersion = message.snapshotVersion,
            StateVersion = message.shopStateVersion,
            ModeContext = new PvpShopModeContext
            {
                ModeId = message.modeId ?? string.Empty,
                ModeVersion = message.modeVersion ?? string.Empty,
                StrategyPackId = message.strategyPackId ?? string.Empty,
                StrategyVersion = message.strategyVersion ?? string.Empty,
                RngVersion = message.rngVersion ?? string.Empty,
                SchemaVersion = PvpShopDefaults.SchemaVersion
            },
            PlayerStates = playerStates
        };
    }

    private static bool ValidateRoomContext(PvpMatchRuntime runtime, string? incomingRoomSessionId, int incomingRoomTopology, string messageName)
    {
        PvpArenaTopology incomingTopology = Enum.IsDefined(typeof(PvpArenaTopology), incomingRoomTopology)
            ? (PvpArenaTopology)incomingRoomTopology
            : PvpArenaTopology.SharedCombat;
        string roomSessionId = incomingRoomSessionId ?? string.Empty;

        if (string.IsNullOrWhiteSpace(roomSessionId))
        {
            Log.Warn($"[ParallelTurnPvp][ShopSync] Ignored {messageName}: missing room session id.");
            return false;
        }

        if (!string.Equals(runtime.RoomSession.SessionId, roomSessionId, StringComparison.Ordinal) || runtime.RoomSession.Topology != incomingTopology)
        {
            Log.Warn($"[ParallelTurnPvp][ShopSync] Ignored {messageName}: room context mismatch. incoming={roomSessionId}/{incomingTopology} local={runtime.RoomSession.SessionId}/{runtime.RoomSession.Topology}");
            return false;
        }

        return true;
    }

    private static string BuildRequestSignature(PvpShopRequestMessage message)
    {
        return $"kind={message.requestKind}|refresh={message.refreshType}|slot={message.slotIndex}|round={message.roundIndex}|snapshot={message.snapshotVersion}|shop={message.shopStateVersion}";
    }

    private static string BuildLocalRequestKey(PvpShopRoundState state, PvpShopRequestKind requestKind, PvpShopRefreshType refreshType, int slotIndex)
    {
        return $"{state.RoundIndex}|{state.SnapshotVersion}|{state.StateVersion}|{(int)requestKind}|{(int)refreshType}|{slotIndex}";
    }

    private static string NormalizeNote(string note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return "rejected";
        }

        return note.Replace('\r', ' ').Replace('\n', ' ').Trim();
    }
}
