using System.Linq;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Runs;

namespace ParallelTurnPvp.Core;

public enum PvpShopRequestRevisionDecision
{
    AcceptNew,
    DuplicateSamePayload,
    ConflictSameRevision,
    StaleRevision
}

public sealed class PvpShopSyncRuntime
{
    private readonly struct LocalPendingRequest
    {
        public LocalPendingRequest(int revision, DateTime sentAtUtc)
        {
            Revision = revision;
            SentAtUtc = sentAtUtc;
        }

        public int Revision { get; }
        public DateTime SentAtUtc { get; }
    }

    private readonly Dictionary<ulong, int> _lastProcessedRequestRevisionByPlayer = new();
    private readonly Dictionary<ulong, string> _lastProcessedRequestSignatureByPlayer = new();
    private readonly Dictionary<ulong, int> _lastAckedRequestRevisionByPlayer = new();
    private readonly Dictionary<ulong, int> _nextLocalRequestRevisionByPlayer = new();
    private readonly Dictionary<ulong, Dictionary<string, LocalPendingRequest>> _localPendingByPlayer = new();
    private readonly Dictionary<ulong, Dictionary<string, DateTime>> _localLastSentByPlayer = new();

    public int LastBroadcastRoundIndex { get; private set; }
    public int LastBroadcastShopStateVersion { get; private set; }
    public int LastReceivedRoundIndex { get; private set; }
    public int LastReceivedShopStateVersion { get; private set; }
    public int LastBroadcastClosedRoundIndex { get; private set; }
    public int LastBroadcastClosedShopStateVersion { get; private set; }
    public int LastReceivedClosedRoundIndex { get; private set; }
    public int LastReceivedClosedShopStateVersion { get; private set; }

    public bool TryMarkShopStateBroadcast(int roundIndex, int shopStateVersion)
    {
        if (roundIndex < LastBroadcastRoundIndex)
        {
            return false;
        }

        if (roundIndex == LastBroadcastRoundIndex && shopStateVersion <= LastBroadcastShopStateVersion)
        {
            return false;
        }

        LastBroadcastRoundIndex = roundIndex;
        LastBroadcastShopStateVersion = shopStateVersion;
        return true;
    }

    public bool TryMarkShopStateReceived(int roundIndex, int shopStateVersion)
    {
        if (roundIndex < LastReceivedRoundIndex)
        {
            return false;
        }

        if (roundIndex == LastReceivedRoundIndex && shopStateVersion <= LastReceivedShopStateVersion)
        {
            return false;
        }

        LastReceivedRoundIndex = roundIndex;
        LastReceivedShopStateVersion = shopStateVersion;
        return true;
    }

    public bool TryMarkShopClosedBroadcast(int roundIndex, int shopStateVersion)
    {
        if (roundIndex < LastBroadcastClosedRoundIndex)
        {
            return false;
        }

        if (roundIndex == LastBroadcastClosedRoundIndex && shopStateVersion <= LastBroadcastClosedShopStateVersion)
        {
            return false;
        }

        LastBroadcastClosedRoundIndex = roundIndex;
        LastBroadcastClosedShopStateVersion = shopStateVersion;
        return true;
    }

    public bool TryMarkShopClosedReceived(int roundIndex, int shopStateVersion)
    {
        if (roundIndex < LastReceivedClosedRoundIndex)
        {
            return false;
        }

        if (roundIndex == LastReceivedClosedRoundIndex && shopStateVersion <= LastReceivedClosedShopStateVersion)
        {
            return false;
        }

        LastReceivedClosedRoundIndex = roundIndex;
        LastReceivedClosedShopStateVersion = shopStateVersion;
        return true;
    }

    public int ReserveNextLocalRequestRevision(ulong playerId)
    {
        int nextRevision = _nextLocalRequestRevisionByPlayer.TryGetValue(playerId, out int current)
            ? current + 1
            : 1;
        _nextLocalRequestRevisionByPlayer[playerId] = nextRevision;
        return nextRevision;
    }

    public bool CanSendLocalRequest(
        ulong playerId,
        string requestKey,
        DateTime nowUtc,
        TimeSpan throttleWindow,
        TimeSpan pendingTimeout,
        out string reasonCode)
    {
        reasonCode = string.Empty;
        if (string.IsNullOrWhiteSpace(requestKey))
        {
            return true;
        }

        if (_localPendingByPlayer.TryGetValue(playerId, out Dictionary<string, LocalPendingRequest>? pendingByKey) &&
            pendingByKey.TryGetValue(requestKey, out LocalPendingRequest pending))
        {
            if (nowUtc - pending.SentAtUtc < pendingTimeout)
            {
                reasonCode = "pending";
                return false;
            }

            pendingByKey.Remove(requestKey);
        }

        if (_localLastSentByPlayer.TryGetValue(playerId, out Dictionary<string, DateTime>? lastSentByKey) &&
            lastSentByKey.TryGetValue(requestKey, out DateTime lastSentUtc) &&
            nowUtc - lastSentUtc < throttleWindow)
        {
            reasonCode = "throttled";
            return false;
        }

        return true;
    }

    public void MarkLocalRequestSent(ulong playerId, string requestKey, int revision, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(requestKey) || revision <= 0)
        {
            return;
        }

        if (!_localPendingByPlayer.TryGetValue(playerId, out Dictionary<string, LocalPendingRequest>? pendingByKey))
        {
            pendingByKey = new Dictionary<string, LocalPendingRequest>(StringComparer.Ordinal);
            _localPendingByPlayer[playerId] = pendingByKey;
        }

        pendingByKey[requestKey] = new LocalPendingRequest(revision, nowUtc);

        if (!_localLastSentByPlayer.TryGetValue(playerId, out Dictionary<string, DateTime>? lastSentByKey))
        {
            lastSentByKey = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            _localLastSentByPlayer[playerId] = lastSentByKey;
        }

        lastSentByKey[requestKey] = nowUtc;
    }

    public PvpShopRequestRevisionDecision ClassifyIncomingRequest(ulong playerId, int requestRevision, string payloadSignature)
    {
        if (requestRevision <= 0)
        {
            return PvpShopRequestRevisionDecision.StaleRevision;
        }

        if (!_lastProcessedRequestRevisionByPlayer.TryGetValue(playerId, out int lastRevision))
        {
            return PvpShopRequestRevisionDecision.AcceptNew;
        }

        if (requestRevision < lastRevision)
        {
            return PvpShopRequestRevisionDecision.StaleRevision;
        }

        if (requestRevision > lastRevision)
        {
            return PvpShopRequestRevisionDecision.AcceptNew;
        }

        if (_lastProcessedRequestSignatureByPlayer.TryGetValue(playerId, out string? lastSignature) &&
            string.Equals(lastSignature, payloadSignature, StringComparison.Ordinal))
        {
            return PvpShopRequestRevisionDecision.DuplicateSamePayload;
        }

        return PvpShopRequestRevisionDecision.ConflictSameRevision;
    }

    public void MarkRequestApplied(ulong playerId, int requestRevision, string payloadSignature)
    {
        _lastProcessedRequestRevisionByPlayer[playerId] = requestRevision;
        _lastProcessedRequestSignatureByPlayer[playerId] = payloadSignature;
    }

    public void MarkRequestAcked(ulong playerId, int requestRevision)
    {
        if (!_lastAckedRequestRevisionByPlayer.TryGetValue(playerId, out int lastRevision) || requestRevision >= lastRevision)
        {
            _lastAckedRequestRevisionByPlayer[playerId] = requestRevision;
        }

        if (!_localPendingByPlayer.TryGetValue(playerId, out Dictionary<string, LocalPendingRequest>? pendingByKey) ||
            pendingByKey.Count == 0)
        {
            return;
        }

        List<string> matchedKeys = pendingByKey
            .Where(entry => entry.Value.Revision == requestRevision)
            .Select(entry => entry.Key)
            .ToList();
        foreach (string key in matchedKeys)
        {
            pendingByKey.Remove(key);
        }
    }

    public bool TryGetLastProcessedRequest(ulong playerId, out int revision, out string signature)
    {
        signature = string.Empty;
        if (!_lastProcessedRequestRevisionByPlayer.TryGetValue(playerId, out revision))
        {
            return false;
        }

        if (_lastProcessedRequestSignatureByPlayer.TryGetValue(playerId, out string? storedSignature) && !string.IsNullOrEmpty(storedSignature))
        {
            signature = storedSignature;
        }

        return true;
    }
}

public static class PvpShopSyncRuntimeRegistry
{
    private static readonly ConditionalWeakTable<RunState, PvpShopSyncRuntime> Table = new();

    public static PvpShopSyncRuntime GetOrCreate(RunState runState)
    {
        ArgumentNullException.ThrowIfNull(runState);
        return Table.GetValue(runState, _ => new PvpShopSyncRuntime());
    }

    public static PvpShopSyncRuntime? TryGet(RunState? runState)
    {
        if (runState == null)
        {
            return null;
        }

        return Table.TryGetValue(runState, out PvpShopSyncRuntime? runtime) ? runtime : null;
    }
}
