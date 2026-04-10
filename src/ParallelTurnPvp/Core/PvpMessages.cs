using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ParallelTurnPvp.Core;

public struct PvpRoundStateMessage : INetMessage
{
    public int roundIndex;
    public int snapshotVersion;
    public int phase;

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(roundIndex);
        writer.WriteInt(snapshotVersion);
        writer.WriteInt(phase);
    }

    public void Deserialize(PacketReader reader)
    {
        roundIndex = reader.ReadInt();
        snapshotVersion = reader.ReadInt();
        phase = reader.ReadInt();
    }
}

public struct PvpRoundResultMessage : INetMessage
{
    public int roundIndex;
    public int snapshotVersion;
    public int hero1Hp;
    public int hero2Hp;
    public int hero1Block;
    public int hero2Block;
    public bool frontline1Exists;
    public bool frontline2Exists;
    public int frontline1Hp;
    public int frontline2Hp;
    public int frontline1Block;
    public int frontline2Block;
    public List<int> eventKinds = new();
    public List<string> eventTexts = new();

    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(roundIndex);
        writer.WriteInt(snapshotVersion);
        writer.WriteInt(hero1Hp);
        writer.WriteInt(hero2Hp);
        writer.WriteInt(hero1Block);
        writer.WriteInt(hero2Block);
        writer.WriteBool(frontline1Exists);
        writer.WriteBool(frontline2Exists);
        writer.WriteInt(frontline1Hp);
        writer.WriteInt(frontline2Hp);
        writer.WriteInt(frontline1Block);
        writer.WriteInt(frontline2Block);
        int eventCount = Math.Min(eventKinds?.Count ?? 0, eventTexts?.Count ?? 0);
        writer.WriteInt(eventCount);
        for (int i = 0; i < eventCount; i++)
        {
            writer.WriteInt(eventKinds[i]);
            writer.WriteString(eventTexts[i] ?? string.Empty);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        roundIndex = reader.ReadInt();
        snapshotVersion = reader.ReadInt();
        hero1Hp = reader.ReadInt();
        hero2Hp = reader.ReadInt();
        hero1Block = reader.ReadInt();
        hero2Block = reader.ReadInt();
        frontline1Exists = reader.ReadBool();
        frontline2Exists = reader.ReadBool();
        frontline1Hp = reader.ReadInt();
        frontline2Hp = reader.ReadInt();
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
