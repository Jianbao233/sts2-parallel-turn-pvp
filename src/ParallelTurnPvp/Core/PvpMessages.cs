using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ParallelTurnPvp.Core;

public struct PvpRoundStateMessage : INetMessage
{
    public string roomSessionId;
    public int roomTopology;
    public int roundIndex;
    public int snapshotVersion;
    public int phase;
    public int hero1Hp;
    public int hero2Hp;
    public int hero1MaxHp;
    public int hero2MaxHp;
    public int hero1Block;
    public int hero2Block;
    public bool frontline1Exists;
    public bool frontline2Exists;
    public int frontline1Hp;
    public int frontline2Hp;
    public int frontline1MaxHp;
    public int frontline2MaxHp;
    public int frontline1Block;
    public int frontline2Block;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(roomSessionId ?? string.Empty);
        writer.WriteInt(roomTopology);
        writer.WriteInt(roundIndex);
        writer.WriteInt(snapshotVersion);
        writer.WriteInt(phase);
        writer.WriteInt(hero1Hp);
        writer.WriteInt(hero2Hp);
        writer.WriteInt(hero1MaxHp);
        writer.WriteInt(hero2MaxHp);
        writer.WriteInt(hero1Block);
        writer.WriteInt(hero2Block);
        writer.WriteBool(frontline1Exists);
        writer.WriteBool(frontline2Exists);
        writer.WriteInt(frontline1Hp);
        writer.WriteInt(frontline2Hp);
        writer.WriteInt(frontline1MaxHp);
        writer.WriteInt(frontline2MaxHp);
        writer.WriteInt(frontline1Block);
        writer.WriteInt(frontline2Block);
    }

    public void Deserialize(PacketReader reader)
    {
        roomSessionId = reader.ReadString();
        roomTopology = reader.ReadInt();
        roundIndex = reader.ReadInt();
        snapshotVersion = reader.ReadInt();
        phase = reader.ReadInt();
        hero1Hp = reader.ReadInt();
        hero2Hp = reader.ReadInt();
        hero1MaxHp = reader.ReadInt();
        hero2MaxHp = reader.ReadInt();
        hero1Block = reader.ReadInt();
        hero2Block = reader.ReadInt();
        frontline1Exists = reader.ReadBool();
        frontline2Exists = reader.ReadBool();
        frontline1Hp = reader.ReadInt();
        frontline2Hp = reader.ReadInt();
        frontline1MaxHp = reader.ReadInt();
        frontline2MaxHp = reader.ReadInt();
        frontline1Block = reader.ReadInt();
        frontline2Block = reader.ReadInt();
    }
}

public struct PvpRoundResultMessage : INetMessage
{
    public string roomSessionId;
    public int roomTopology;
    public int roundIndex;
    public int snapshotVersion;
    public uint delayedCommandFingerprint;
    public int delayedCommandCount;
    public int hero1Hp;
    public int hero2Hp;
    public int hero1MaxHp;
    public int hero2MaxHp;
    public int hero1Block;
    public int hero2Block;
    public bool frontline1Exists;
    public bool frontline2Exists;
    public int frontline1Hp;
    public int frontline2Hp;
    public int frontline1MaxHp;
    public int frontline2MaxHp;
    public int frontline1Block;
    public int frontline2Block;
    public List<int>? eventKinds;
    public List<string>? eventTexts;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(roomSessionId ?? string.Empty);
        writer.WriteInt(roomTopology);
        writer.WriteInt(roundIndex);
        writer.WriteInt(snapshotVersion);
        writer.WriteUInt(delayedCommandFingerprint);
        writer.WriteInt(delayedCommandCount);
        writer.WriteInt(hero1Hp);
        writer.WriteInt(hero2Hp);
        writer.WriteInt(hero1MaxHp);
        writer.WriteInt(hero2MaxHp);
        writer.WriteInt(hero1Block);
        writer.WriteInt(hero2Block);
        writer.WriteBool(frontline1Exists);
        writer.WriteBool(frontline2Exists);
        writer.WriteInt(frontline1Hp);
        writer.WriteInt(frontline2Hp);
        writer.WriteInt(frontline1MaxHp);
        writer.WriteInt(frontline2MaxHp);
        writer.WriteInt(frontline1Block);
        writer.WriteInt(frontline2Block);
        List<int> eventKindsLocal = eventKinds ?? new List<int>();
        List<string> eventTextsLocal = eventTexts ?? new List<string>();
        int eventCount = Math.Min(eventKindsLocal.Count, eventTextsLocal.Count);
        writer.WriteInt(eventCount);
        for (int i = 0; i < eventCount; i++)
        {
            writer.WriteInt(eventKindsLocal[i]);
            writer.WriteString(eventTextsLocal[i] ?? string.Empty);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        roomSessionId = reader.ReadString();
        roomTopology = reader.ReadInt();
        roundIndex = reader.ReadInt();
        snapshotVersion = reader.ReadInt();
        delayedCommandFingerprint = reader.ReadUInt();
        delayedCommandCount = reader.ReadInt();
        hero1Hp = reader.ReadInt();
        hero2Hp = reader.ReadInt();
        hero1MaxHp = reader.ReadInt();
        hero2MaxHp = reader.ReadInt();
        hero1Block = reader.ReadInt();
        hero2Block = reader.ReadInt();
        frontline1Exists = reader.ReadBool();
        frontline2Exists = reader.ReadBool();
        frontline1Hp = reader.ReadInt();
        frontline2Hp = reader.ReadInt();
        frontline1MaxHp = reader.ReadInt();
        frontline2MaxHp = reader.ReadInt();
        frontline1Block = reader.ReadInt();
        frontline2Block = reader.ReadInt();
        int eventCount = reader.ReadInt();
        eventKinds = new List<int>(eventCount);
        eventTexts = new List<string>(eventCount);
        for (int i = 0; i < eventCount; i++)
        {
            eventKinds.Add(reader.ReadInt());
            eventTexts.Add(reader.ReadString());
        }
    }
}

public struct PvpPlannedActionPacket : IPacketSerializable
{
    public int sequence;
    public bool hasRuntimeActionId;
    public uint runtimeActionId;
    public int actionType;
    public string modelEntry;
    public ulong targetOwnerPlayerId;
    public int targetKind;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(sequence);
        writer.WriteBool(hasRuntimeActionId);
        if (hasRuntimeActionId)
        {
            writer.WriteUInt(runtimeActionId);
        }

        writer.WriteInt(actionType);
        writer.WriteString(modelEntry ?? string.Empty);
        writer.WriteULong(targetOwnerPlayerId);
        writer.WriteInt(targetKind);
    }

    public void Deserialize(PacketReader reader)
    {
        sequence = reader.ReadInt();
        hasRuntimeActionId = reader.ReadBool();
        runtimeActionId = hasRuntimeActionId ? reader.ReadUInt() : 0U;
        actionType = reader.ReadInt();
        modelEntry = reader.ReadString();
        targetOwnerPlayerId = reader.ReadULong();
        targetKind = reader.ReadInt();
    }
}

public struct PvpRoundSubmissionPacket : IPacketSerializable
{
    public int roundIndex;
    public ulong playerId;
    public int roundStartEnergy;
    public bool locked;
    public bool isFirstFinisher;
    public List<PvpPlannedActionPacket> actions;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(roundIndex);
        writer.WriteULong(playerId);
        writer.WriteInt(roundStartEnergy);
        writer.WriteBool(locked);
        writer.WriteBool(isFirstFinisher);
        writer.WriteList(actions ?? new List<PvpPlannedActionPacket>());
    }

    public void Deserialize(PacketReader reader)
    {
        roundIndex = reader.ReadInt();
        playerId = reader.ReadULong();
        roundStartEnergy = reader.ReadInt();
        locked = reader.ReadBool();
        isFirstFinisher = reader.ReadBool();
        actions = reader.ReadList<PvpPlannedActionPacket>();
    }
}

public struct PvpPlanningFrameMessage : INetMessage
{
    public string roomSessionId;
    public int roomTopology;
    public int roundIndex;
    public int snapshotVersion;
    public int phase;
    public int revision;
    public List<PvpRoundSubmissionPacket>? submissions;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(roomSessionId ?? string.Empty);
        writer.WriteInt(roomTopology);
        writer.WriteInt(roundIndex);
        writer.WriteInt(snapshotVersion);
        writer.WriteInt(phase);
        writer.WriteInt(revision);
        writer.WriteList(submissions ?? new List<PvpRoundSubmissionPacket>());
    }

    public void Deserialize(PacketReader reader)
    {
        roomSessionId = reader.ReadString();
        roomTopology = reader.ReadInt();
        roundIndex = reader.ReadInt();
        snapshotVersion = reader.ReadInt();
        phase = reader.ReadInt();
        revision = reader.ReadInt();
        submissions = reader.ReadList<PvpRoundSubmissionPacket>();
    }
}

public struct PvpClientSubmissionMessage : INetMessage
{
    public string roomSessionId;
    public int roomTopology;
    public int roundIndex;
    public int snapshotVersion;
    public int revision;
    public PvpRoundSubmissionPacket submission;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(roomSessionId ?? string.Empty);
        writer.WriteInt(roomTopology);
        writer.WriteInt(roundIndex);
        writer.WriteInt(snapshotVersion);
        writer.WriteInt(revision);
        submission.Serialize(writer);
    }

    public void Deserialize(PacketReader reader)
    {
        roomSessionId = reader.ReadString();
        roomTopology = reader.ReadInt();
        roundIndex = reader.ReadInt();
        snapshotVersion = reader.ReadInt();
        revision = reader.ReadInt();
        submission.Deserialize(reader);
    }
}

public struct PvpClientSubmissionAckMessage : INetMessage
{
    public string roomSessionId;
    public int roomTopology;
    public int roundIndex;
    public int snapshotVersion;
    public ulong playerId;
    public int revision;
    public bool accepted;
    public string note;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(roomSessionId ?? string.Empty);
        writer.WriteInt(roomTopology);
        writer.WriteInt(roundIndex);
        writer.WriteInt(snapshotVersion);
        writer.WriteULong(playerId);
        writer.WriteInt(revision);
        writer.WriteBool(accepted);
        writer.WriteString(note ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        roomSessionId = reader.ReadString();
        roomTopology = reader.ReadInt();
        roundIndex = reader.ReadInt();
        snapshotVersion = reader.ReadInt();
        playerId = reader.ReadULong();
        revision = reader.ReadInt();
        accepted = reader.ReadBool();
        note = reader.ReadString();
    }
}

public enum PvpShopRequestKind
{
    Refresh = 1,
    Purchase = 2,
    DeleteCard = 3
}

public struct PvpShopOfferPacket : IPacketSerializable
{
    public int slotIndex;
    public int slotKind;
    public string cardId;
    public string displayName;
    public int price;
    public bool available;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(slotIndex);
        writer.WriteInt(slotKind);
        writer.WriteString(cardId ?? string.Empty);
        writer.WriteString(displayName ?? string.Empty);
        writer.WriteInt(price);
        writer.WriteBool(available);
    }

    public void Deserialize(PacketReader reader)
    {
        slotIndex = reader.ReadInt();
        slotKind = reader.ReadInt();
        cardId = reader.ReadString();
        displayName = reader.ReadString();
        price = reader.ReadInt();
        available = reader.ReadBool();
    }
}

public struct PvpShopPlayerStatePacket : IPacketSerializable
{
    public ulong playerId;
    public int gold;
    public int refreshCount;
    public int playerStateVersion;
    public string statusText;
    public List<string> purchasedCardIds;
    public List<string> removedCardIds;
    public List<PvpShopOfferPacket> offers;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(playerId);
        writer.WriteInt(gold);
        writer.WriteInt(refreshCount);
        writer.WriteInt(playerStateVersion);
        writer.WriteString(statusText ?? string.Empty);

        List<string> purchasedCardsLocal = purchasedCardIds ?? new List<string>();
        writer.WriteInt(purchasedCardsLocal.Count);
        foreach (string cardId in purchasedCardsLocal)
        {
            writer.WriteString(cardId ?? string.Empty);
        }

        List<string> removedCardsLocal = removedCardIds ?? new List<string>();
        writer.WriteInt(removedCardsLocal.Count);
        foreach (string cardId in removedCardsLocal)
        {
            writer.WriteString(cardId ?? string.Empty);
        }

        writer.WriteList(offers ?? new List<PvpShopOfferPacket>());
    }

    public void Deserialize(PacketReader reader)
    {
        playerId = reader.ReadULong();
        gold = reader.ReadInt();
        refreshCount = reader.ReadInt();
        playerStateVersion = reader.ReadInt();
        statusText = reader.ReadString();

        int purchasedCardCount = reader.ReadInt();
        purchasedCardIds = new List<string>(purchasedCardCount);
        for (int i = 0; i < purchasedCardCount; i++)
        {
            purchasedCardIds.Add(reader.ReadString());
        }

        int removedCardCount = reader.ReadInt();
        removedCardIds = new List<string>(removedCardCount);
        for (int i = 0; i < removedCardCount; i++)
        {
            removedCardIds.Add(reader.ReadString());
        }

        offers = reader.ReadList<PvpShopOfferPacket>();
    }
}

public struct PvpShopStateMessage : INetMessage
{
    public string roomSessionId;
    public int roomTopology;
    public int roundIndex;
    public int snapshotVersion;
    public int shopStateVersion;
    public string modeId;
    public string modeVersion;
    public string strategyPackId;
    public string strategyVersion;
    public string rngVersion;
    public List<PvpShopPlayerStatePacket>? players;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(roomSessionId ?? string.Empty);
        writer.WriteInt(roomTopology);
        writer.WriteInt(roundIndex);
        writer.WriteInt(snapshotVersion);
        writer.WriteInt(shopStateVersion);
        writer.WriteString(modeId ?? string.Empty);
        writer.WriteString(modeVersion ?? string.Empty);
        writer.WriteString(strategyPackId ?? string.Empty);
        writer.WriteString(strategyVersion ?? string.Empty);
        writer.WriteString(rngVersion ?? string.Empty);
        writer.WriteList(players ?? new List<PvpShopPlayerStatePacket>());
    }

    public void Deserialize(PacketReader reader)
    {
        roomSessionId = reader.ReadString();
        roomTopology = reader.ReadInt();
        roundIndex = reader.ReadInt();
        snapshotVersion = reader.ReadInt();
        shopStateVersion = reader.ReadInt();
        modeId = reader.ReadString();
        modeVersion = reader.ReadString();
        strategyPackId = reader.ReadString();
        strategyVersion = reader.ReadString();
        rngVersion = reader.ReadString();
        players = reader.ReadList<PvpShopPlayerStatePacket>();
    }
}

public struct PvpShopRequestMessage : INetMessage
{
    public string roomSessionId;
    public int roomTopology;
    public int roundIndex;
    public int snapshotVersion;
    public int shopStateVersion;
    public int requestRevision;
    public ulong playerId;
    public int requestKind;
    public int refreshType;
    public int slotIndex;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(roomSessionId ?? string.Empty);
        writer.WriteInt(roomTopology);
        writer.WriteInt(roundIndex);
        writer.WriteInt(snapshotVersion);
        writer.WriteInt(shopStateVersion);
        writer.WriteInt(requestRevision);
        writer.WriteULong(playerId);
        writer.WriteInt(requestKind);
        writer.WriteInt(refreshType);
        writer.WriteInt(slotIndex);
    }

    public void Deserialize(PacketReader reader)
    {
        roomSessionId = reader.ReadString();
        roomTopology = reader.ReadInt();
        roundIndex = reader.ReadInt();
        snapshotVersion = reader.ReadInt();
        shopStateVersion = reader.ReadInt();
        requestRevision = reader.ReadInt();
        playerId = reader.ReadULong();
        requestKind = reader.ReadInt();
        refreshType = reader.ReadInt();
        slotIndex = reader.ReadInt();
    }
}

public struct PvpShopRequestAckMessage : INetMessage
{
    public string roomSessionId;
    public int roomTopology;
    public int roundIndex;
    public int snapshotVersion;
    public int shopStateVersion;
    public ulong playerId;
    public int requestRevision;
    public bool accepted;
    public string note;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(roomSessionId ?? string.Empty);
        writer.WriteInt(roomTopology);
        writer.WriteInt(roundIndex);
        writer.WriteInt(snapshotVersion);
        writer.WriteInt(shopStateVersion);
        writer.WriteULong(playerId);
        writer.WriteInt(requestRevision);
        writer.WriteBool(accepted);
        writer.WriteString(note ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        roomSessionId = reader.ReadString();
        roomTopology = reader.ReadInt();
        roundIndex = reader.ReadInt();
        snapshotVersion = reader.ReadInt();
        shopStateVersion = reader.ReadInt();
        playerId = reader.ReadULong();
        requestRevision = reader.ReadInt();
        accepted = reader.ReadBool();
        note = reader.ReadString();
    }
}

public struct PvpShopClosedMessage : INetMessage
{
    public string roomSessionId;
    public int roomTopology;
    public int roundIndex;
    public int snapshotVersion;
    public int shopStateVersion;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(roomSessionId ?? string.Empty);
        writer.WriteInt(roomTopology);
        writer.WriteInt(roundIndex);
        writer.WriteInt(snapshotVersion);
        writer.WriteInt(shopStateVersion);
    }

    public void Deserialize(PacketReader reader)
    {
        roomSessionId = reader.ReadString();
        roomTopology = reader.ReadInt();
        roundIndex = reader.ReadInt();
        snapshotVersion = reader.ReadInt();
        shopStateVersion = reader.ReadInt();
    }
}

public struct PvpResumeStateRequestMessage : INetMessage
{
    public string roomSessionId;
    public int roomTopology;
    public int requesterRoundIndex;
    public int requesterSnapshotVersion;
    public int requesterPlanningRevision;

    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(roomSessionId ?? string.Empty);
        writer.WriteInt(roomTopology);
        writer.WriteInt(requesterRoundIndex);
        writer.WriteInt(requesterSnapshotVersion);
        writer.WriteInt(requesterPlanningRevision);
    }

    public void Deserialize(PacketReader reader)
    {
        roomSessionId = reader.ReadString();
        roomTopology = reader.ReadInt();
        requesterRoundIndex = reader.ReadInt();
        requesterSnapshotVersion = reader.ReadInt();
        requesterPlanningRevision = reader.ReadInt();
    }
}

public struct PvpResumeStateMessage : INetMessage
{
    public string roomSessionId;
    public int roomTopology;
    public bool hasRoundState;
    public PvpRoundStateMessage roundState;
    public bool hasPlanningFrame;
    public PvpPlanningFrameMessage planningFrame;
    public bool hasRoundResult;
    public PvpRoundResultMessage roundResult;

    public bool ShouldBroadcast => false;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteString(roomSessionId ?? string.Empty);
        writer.WriteInt(roomTopology);

        writer.WriteBool(hasRoundState);
        if (hasRoundState)
        {
            roundState.Serialize(writer);
        }

        writer.WriteBool(hasPlanningFrame);
        if (hasPlanningFrame)
        {
            planningFrame.Serialize(writer);
        }

        writer.WriteBool(hasRoundResult);
        if (hasRoundResult)
        {
            roundResult.Serialize(writer);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        roomSessionId = reader.ReadString();
        roomTopology = reader.ReadInt();

        hasRoundState = reader.ReadBool();
        if (hasRoundState)
        {
            roundState = default;
            roundState.Deserialize(reader);
        }

        hasPlanningFrame = reader.ReadBool();
        if (hasPlanningFrame)
        {
            planningFrame = default;
            planningFrame.Deserialize(reader);
        }

        hasRoundResult = reader.ReadBool();
        if (hasRoundResult)
        {
            roundResult = default;
            roundResult.Deserialize(reader);
        }
    }
}
