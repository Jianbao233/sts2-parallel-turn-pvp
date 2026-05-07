namespace ParallelTurnPvp.Core;

internal static class PvpDelayedPlanFingerprint
{
    private const uint FnvOffset = 2166136261;
    private const uint FnvPrime = 16777619;

    public static (uint Fingerprint, int CommandCount) Compute(PvpRoundDelayedCommandPlan? plan)
    {
        if (plan == null)
        {
            return (0U, 0);
        }

        uint hash = FnvOffset;
        MixInt(ref hash, plan.RoundIndex);
        int countedCommands = 0;

        foreach (PvpDelayedCommand command in plan.Commands)
        {
            if (!ShouldInclude(command))
            {
                continue;
            }

            countedCommands++;
            MixInt(ref hash, (int)command.Phase);
            MixInt(ref hash, (int)command.Kind);
            MixULong(ref hash, command.SourcePlayerId);
            MixULong(ref hash, command.TargetPlayerId);
            MixInt(ref hash, (int)command.TargetKind);
            MixInt(ref hash, command.Amount);
            MixInt(ref hash, command.Sequence);
            MixString(ref hash, command.ModelEntry);
            MixBool(ref hash, command.RuntimeActionId != null);
            if (command.RuntimeActionId != null)
            {
                MixUInt(ref hash, command.RuntimeActionId.Value);
            }
        }

        return (hash, countedCommands);
    }

    private static bool ShouldInclude(PvpDelayedCommand command)
    {
        if (command.Kind == PvpDelayedCommandKind.EndRoundMarker)
        {
            return false;
        }

        if (command.Amount <= 0)
        {
            return false;
        }

        return command.Kind is
            PvpDelayedCommandKind.GainBlock or
            PvpDelayedCommandKind.Heal or
            PvpDelayedCommandKind.GainResource or
            PvpDelayedCommandKind.GainMaxHp or
            PvpDelayedCommandKind.Damage or
            PvpDelayedCommandKind.SummonFrontline;
    }

    private static void MixBool(ref uint hash, bool value)
    {
        MixByte(ref hash, value ? (byte)1 : (byte)0);
    }

    private static void MixInt(ref uint hash, int value)
    {
        MixUInt(ref hash, unchecked((uint)value));
    }

    private static void MixUInt(ref uint hash, uint value)
    {
        MixByte(ref hash, (byte)(value & 0xFF));
        MixByte(ref hash, (byte)((value >> 8) & 0xFF));
        MixByte(ref hash, (byte)((value >> 16) & 0xFF));
        MixByte(ref hash, (byte)((value >> 24) & 0xFF));
    }

    private static void MixULong(ref uint hash, ulong value)
    {
        MixByte(ref hash, (byte)(value & 0xFF));
        MixByte(ref hash, (byte)((value >> 8) & 0xFF));
        MixByte(ref hash, (byte)((value >> 16) & 0xFF));
        MixByte(ref hash, (byte)((value >> 24) & 0xFF));
        MixByte(ref hash, (byte)((value >> 32) & 0xFF));
        MixByte(ref hash, (byte)((value >> 40) & 0xFF));
        MixByte(ref hash, (byte)((value >> 48) & 0xFF));
        MixByte(ref hash, (byte)((value >> 56) & 0xFF));
    }

    private static void MixString(ref uint hash, string text)
    {
        MixInt(ref hash, text.Length);
        foreach (char ch in text)
        {
            ushort value = ch;
            MixByte(ref hash, (byte)(value & 0xFF));
            MixByte(ref hash, (byte)((value >> 8) & 0xFF));
        }
    }

    private static void MixByte(ref uint hash, byte value)
    {
        hash ^= value;
        hash *= FnvPrime;
    }
}
