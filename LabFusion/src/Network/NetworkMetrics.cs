using System.Diagnostics;
using LabFusion.Utilities;
using System.Threading;

namespace LabFusion.Network;

public static class NetworkMetrics
{
    private static readonly long[] _bytesSentByTag = new long[byte.MaxValue + 1];
    private static readonly long[] _bytesReceivedByTag = new long[byte.MaxValue + 1];
    private static readonly long[] _packetsSentByTag = new long[byte.MaxValue + 1];
    private static readonly long[] _packetsReceivedByTag = new long[byte.MaxValue + 1];
    private static readonly double[] _tickSamplesMs = new double[256];
    private static readonly double[] _tickSortScratch = new double[256];
    private static readonly long[] _handlerTicksByTag = new long[byte.MaxValue + 1];
    private static readonly long[] _handlerCallsByTag = new long[byte.MaxValue + 1];
    private static int _tickIndex;
    private static long _tickStart;
    private static long _allocatedAtTickStart;
    private static long _relayRecipients;
    private static long _lastSummaryTimestamp;
    private static long _lastSummaryBytesUp;
    private static long _lastSummaryBytesDown;

    public static long RelayRecipients => Interlocked.Read(ref _relayRecipients);
    public static long AllocatedBytesLastTick { get; private set; }
    public static double LastTickMilliseconds { get; private set; }
    public static double WorstTickMilliseconds { get; private set; }
    public static double P95TickMilliseconds { get; private set; }
    public static int ActiveMovingProps { get; internal set; }

    internal static void RecordSend(byte tag, int bytes, int recipients = 1)
    {
        Interlocked.Add(ref _bytesSentByTag[tag], (long)bytes * recipients);
        Interlocked.Add(ref _packetsSentByTag[tag], recipients);
        Interlocked.Add(ref _relayRecipients, recipients);
    }

    internal static void RecordReceive(byte tag, int bytes)
    {
        Interlocked.Add(ref _bytesReceivedByTag[tag], bytes);
        Interlocked.Increment(ref _packetsReceivedByTag[tag]);
    }

    public static long GetBytesSent(byte tag) => Interlocked.Read(ref _bytesSentByTag[tag]);
    public static long GetBytesReceived(byte tag) => Interlocked.Read(ref _bytesReceivedByTag[tag]);
    public static long GetPacketsSent(byte tag) => Interlocked.Read(ref _packetsSentByTag[tag]);
    public static long GetPacketsReceived(byte tag) => Interlocked.Read(ref _packetsReceivedByTag[tag]);
    public static double GetAverageHandlerMilliseconds(byte tag)
    {
        long calls = Interlocked.Read(ref _handlerCallsByTag[tag]);
        return calls == 0 ? 0d : Interlocked.Read(ref _handlerTicksByTag[tag]) * 1000d / Stopwatch.Frequency / calls;
    }

    internal static void RecordHandler(byte tag, long elapsedTicks)
    {
        Interlocked.Add(ref _handlerTicksByTag[tag], elapsedTicks);
        Interlocked.Increment(ref _handlerCallsByTag[tag]);
    }

    internal static void BeginTick()
    {
        _tickStart = Stopwatch.GetTimestamp();
        _allocatedAtTickStart = GC.GetAllocatedBytesForCurrentThread();
    }

    internal static void EndTick()
    {
        LastTickMilliseconds = (Stopwatch.GetTimestamp() - _tickStart) * 1000d / Stopwatch.Frequency;
        AllocatedBytesLastTick = GC.GetAllocatedBytesForCurrentThread() - _allocatedAtTickStart;
        _tickSamplesMs[_tickIndex++ & 255] = LastTickMilliseconds;
        if ((_tickIndex & 63) == 0)
        {
            Array.Copy(_tickSamplesMs, _tickSortScratch, _tickSamplesMs.Length);
            Array.Sort(_tickSortScratch);
            P95TickMilliseconds = _tickSortScratch[(int)(_tickSortScratch.Length * 0.95)];
            WorstTickMilliseconds = _tickSortScratch[^1];
        }


        long now = Stopwatch.GetTimestamp();
        if (_lastSummaryTimestamp == 0)
            _lastSummaryTimestamp = now;
        if ((now - _lastSummaryTimestamp) >= Stopwatch.Frequency * 10 && NetworkInfo.HasServer)
        {
            long bytesUp = 0;
            long bytesDown = 0;
            for (var i = 0; i < _bytesSentByTag.Length; i++)
            {
                bytesUp += Interlocked.Read(ref _bytesSentByTag[i]);
                bytesDown += Interlocked.Read(ref _bytesReceivedByTag[i]);
            }
            FusionLogger.Log($"[Network Profile] up={(bytesUp - _lastSummaryBytesUp) / 10:N0} B/s down={(bytesDown - _lastSummaryBytesDown) / 10:N0} B/s tick={LastTickMilliseconds:F2}ms p95={P95TickMilliseconds:F2}ms worst={WorstTickMilliseconds:F2}ms alloc={AllocatedBytesLastTick:N0}B movingProps={ActiveMovingProps} relays={RelayRecipients:N0}");
            _lastSummaryBytesUp = bytesUp;
            _lastSummaryBytesDown = bytesDown;
            _lastSummaryTimestamp = now;
        }
    }
}
