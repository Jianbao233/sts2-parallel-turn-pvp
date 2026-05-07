using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Checksums;
using MegaCrit.Sts2.Core.Runs;
using ParallelTurnPvp.Core;
using ParallelTurnPvp.Models;
using System.Reflection;

namespace ParallelTurnPvp.Patches;

[HarmonyPatch(typeof(ChecksumTracker))]
public static class ParallelTurnChecksumCompareBypassPatch
{
    private static int _suppressedCount;

    [HarmonyTargetMethod]
    static MethodBase? TargetMethod()
    {
        return AccessTools.GetDeclaredMethods(typeof(ChecksumTracker))
            .FirstOrDefault(method =>
            {
                if (!string.Equals(method.Name, "CompareChecksums", StringComparison.Ordinal))
                {
                    return false;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 3)
                {
                    return false;
                }

                return parameters[1].ParameterType == typeof(NetChecksumData) &&
                       parameters[2].ParameterType == typeof(ulong);
            });
    }

    static bool Prefix(object localChecksum, NetChecksumData remoteChecksum, ulong remoteId)
    {
        if (!ShouldBypassVanillaChecksumForDebugPvp())
        {
            return true;
        }

        int suppressed = Interlocked.Increment(ref _suppressedCount);
        if (suppressed <= 8 || suppressed % 20 == 0)
        {
            Log.Info($"[ParallelTurnPvp] Suppressed vanilla checksum compare. remote={remoteId} checksumId={remoteChecksum.id} count={suppressed}");
        }

        return false;
    }

    private static bool ShouldBypassVanillaChecksumForDebugPvp()
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
        {
            return false;
        }

        if (!runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            return false;
        }

        // Debug PvP no longer uses vanilla combat checksum parity as the primary
        // correctness source once host-authoritative read-only resolve is active.
        // That now includes:
        // 1. split-room mode, and
        // 2. shared-combat mode with client read-only resolve + authoritative snapshot apply.
        return ParallelTurnFrontlineHelper.IsSplitRoomActive(runState) ||
               PvpResolveConfig.IsClientReadOnlyResolveEnabledFor(runState);
    }
}

[HarmonyPatch(typeof(ChecksumTracker), "OnReceivedStateDivergenceMessage")]
public static class ParallelTurnSuppressStateDivergenceMessagePatch
{
    static bool Prefix(StateDivergenceMessage message, ulong senderId)
    {
        if (!ParallelTurnChecksumCompareBypassPatchAccess.ShouldBypass())
        {
            return true;
        }

        Log.Warn($"[ParallelTurnPvp] Suppressed vanilla StateDivergence message. sender={senderId} checksumId={message.senderChecksum.id}");
        return false;
    }
}

internal static class ParallelTurnChecksumCompareBypassPatchAccess
{
    public static bool ShouldBypass()
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
        {
            return false;
        }

        if (!runState.Modifiers.OfType<ParallelTurnPvpDebugModifier>().Any())
        {
            return false;
        }

        return ParallelTurnFrontlineHelper.IsSplitRoomActive(runState) ||
               PvpResolveConfig.IsClientReadOnlyResolveEnabledFor(runState);
    }
}
