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
    }

    public void Deserialize(PacketReader reader)
    {
        roundIndex = reader.ReadInt();
        snapshotVersion = reader.ReadInt();
        hero1Hp = reader.ReadInt();
        hero2Hp = reader.ReadInt();
        hero1Block = reader.ReadInt();
        hero2Block = reader.ReadInt();
    }
}
