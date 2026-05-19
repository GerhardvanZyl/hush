using System.Diagnostics;
using System.Threading;

namespace Hush.Core.Diagnostics;

// Zero-cost in Release: the Increment* methods are [Conditional("DEBUG")] and the
// compiler strips every call site in non-DEBUG builds. The Snapshot/Reset readers
// stay live so tests and perf harnesses can inspect counters.
public static class PerfCounters
{
    private static long _getSpansCalls;
    private static long _treeWalks;
    private static long _regexCompiles;
    private static long _regexCacheHits;
    private static long _globCompiles;
    private static long _textStringAllocations;
    private static long _matchContextBuilds;
    private static long _snapshotCacheHits;
    private static long _exclusionMaterializations;
    private static long _spansEmitted;

    [Conditional("DEBUG")]
    public static void IncrementGetSpansCalls() => Interlocked.Increment(ref _getSpansCalls);

    [Conditional("DEBUG")]
    public static void IncrementTreeWalks() => Interlocked.Increment(ref _treeWalks);

    [Conditional("DEBUG")]
    public static void IncrementRegexCompiles() => Interlocked.Increment(ref _regexCompiles);

    [Conditional("DEBUG")]
    public static void IncrementRegexCacheHits() => Interlocked.Increment(ref _regexCacheHits);

    [Conditional("DEBUG")]
    public static void IncrementGlobCompiles() => Interlocked.Increment(ref _globCompiles);

    [Conditional("DEBUG")]
    public static void IncrementTextStringAllocations() => Interlocked.Increment(ref _textStringAllocations);

    [Conditional("DEBUG")]
    public static void IncrementMatchContextBuilds() => Interlocked.Increment(ref _matchContextBuilds);

    [Conditional("DEBUG")]
    public static void IncrementSnapshotCacheHits() => Interlocked.Increment(ref _snapshotCacheHits);

    [Conditional("DEBUG")]
    public static void IncrementExclusionMaterializations() => Interlocked.Increment(ref _exclusionMaterializations);

    [Conditional("DEBUG")]
    public static void AddSpansEmitted(long n) => Interlocked.Add(ref _spansEmitted, n);

    public static PerfSnapshot Snapshot() => new PerfSnapshot(
        Interlocked.Read(ref _getSpansCalls),
        Interlocked.Read(ref _treeWalks),
        Interlocked.Read(ref _regexCompiles),
        Interlocked.Read(ref _regexCacheHits),
        Interlocked.Read(ref _globCompiles),
        Interlocked.Read(ref _textStringAllocations),
        Interlocked.Read(ref _matchContextBuilds),
        Interlocked.Read(ref _snapshotCacheHits),
        Interlocked.Read(ref _exclusionMaterializations),
        Interlocked.Read(ref _spansEmitted));

    public static void Reset()
    {
        Interlocked.Exchange(ref _getSpansCalls, 0);
        Interlocked.Exchange(ref _treeWalks, 0);
        Interlocked.Exchange(ref _regexCompiles, 0);
        Interlocked.Exchange(ref _regexCacheHits, 0);
        Interlocked.Exchange(ref _globCompiles, 0);
        Interlocked.Exchange(ref _textStringAllocations, 0);
        Interlocked.Exchange(ref _matchContextBuilds, 0);
        Interlocked.Exchange(ref _snapshotCacheHits, 0);
        Interlocked.Exchange(ref _exclusionMaterializations, 0);
        Interlocked.Exchange(ref _spansEmitted, 0);
    }
}

public readonly struct PerfSnapshot
{
    public PerfSnapshot(long getSpansCalls, long treeWalks, long regexCompiles, long regexCacheHits,
        long globCompiles, long textStringAllocations, long matchContextBuilds, long snapshotCacheHits,
        long exclusionMaterializations, long spansEmitted)
    {
        GetSpansCalls = getSpansCalls;
        TreeWalks = treeWalks;
        RegexCompiles = regexCompiles;
        RegexCacheHits = regexCacheHits;
        GlobCompiles = globCompiles;
        TextStringAllocations = textStringAllocations;
        MatchContextBuilds = matchContextBuilds;
        SnapshotCacheHits = snapshotCacheHits;
        ExclusionMaterializations = exclusionMaterializations;
        SpansEmitted = spansEmitted;
    }

    public long GetSpansCalls { get; }
    public long TreeWalks { get; }
    public long RegexCompiles { get; }
    public long RegexCacheHits { get; }
    public long GlobCompiles { get; }
    public long TextStringAllocations { get; }
    public long MatchContextBuilds { get; }
    public long SnapshotCacheHits { get; }
    public long ExclusionMaterializations { get; }
    public long SpansEmitted { get; }

    public override string ToString() =>
        $"GetSpans={GetSpansCalls} TreeWalks={TreeWalks} RegexCompiles={RegexCompiles} " +
        $"RegexCacheHits={RegexCacheHits} GlobCompiles={GlobCompiles} " +
        $"TextStringAllocs={TextStringAllocations} Builds={MatchContextBuilds} " +
        $"SnapCacheHits={SnapshotCacheHits} ExclusionMat={ExclusionMaterializations} " +
        $"SpansEmitted={SpansEmitted}";
}
