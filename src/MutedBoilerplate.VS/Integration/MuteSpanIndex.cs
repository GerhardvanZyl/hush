using System;
using MutedBoilerplate.Core.Model;

namespace MutedBoilerplate.VS.Integration;

// Phase 4: narrow the "give me spans intersecting [qStart, qEnd)" query from
// O(n) to O(log n + k). The classifier asks this per visible region per repaint;
// on a 50k-line file that's the difference between "iterate tens of thousands
// of spans" and "iterate the handful that actually intersect".
//
// Implementation: spans are already sorted by Start (SnapshotMuteCache does
// this). We binary-search the upper bound by Start (first Start >= qEnd) for
// the right edge, and use the pre-computed MaxSpanLength to bound the left
// edge (first Start >= qStart - MaxSpanLength). Callers iterate that range
// and verify End > qStart per candidate.
//
// The MaxSpanLength trick is exact: any span intersecting qStart must have
// Start > qStart - Length, and Length <= MaxSpanLength for all spans.
internal sealed class MuteSpanIndex
{
    private static readonly int[] EmptyStarts = Array.Empty<int>();
    private static readonly MuteSpan[] EmptySpans = Array.Empty<MuteSpan>();

    public static readonly MuteSpanIndex Empty = new(EmptySpans, EmptyStarts, 0);

    private readonly MuteSpan[] _spans;
    private readonly int[] _starts;
    private readonly int _maxSpanLength;

    private MuteSpanIndex(MuteSpan[] spans, int[] starts, int maxSpanLength)
    {
        _spans = spans;
        _starts = starts;
        _maxSpanLength = maxSpanLength;
    }

    public static MuteSpanIndex Build(MuteSpan[] sortedSpans)
    {
        if (sortedSpans.Length == 0) return Empty;

        var starts = new int[sortedSpans.Length];
        int maxLen = 0;
        for (int i = 0; i < sortedSpans.Length; i++)
        {
            var span = sortedSpans[i].Span;
            starts[i] = span.Start;
            var len = span.Length;
            if (len > maxLen) maxLen = len;
        }
        return new MuteSpanIndex(sortedSpans, starts, maxLen);
    }

    public int Count => _spans.Length;
    public MuteSpan this[int i] => _spans[i];

    // Returns [lo, hi) — the slice of the sorted array that might intersect
    // the query. Callers must still check `_spans[i].Span.End > qStart` per
    // candidate since our lo-bound is conservative.
    public void GetCandidateRange(int qStart, int qEnd, out int lo, out int hi)
    {
        if (_spans.Length == 0) { lo = 0; hi = 0; return; }
        var loProbe = qStart - _maxSpanLength;
        lo = LowerBound(loProbe);
        hi = LowerBound(qEnd);
    }

    // First index with _starts[i] >= value.
    private int LowerBound(int value)
    {
        int l = 0, r = _spans.Length;
        while (l < r)
        {
            int m = (int)(((uint)l + (uint)r) >> 1);
            if (_starts[m] < value) l = m + 1;
            else r = m;
        }
        return l;
    }
}
