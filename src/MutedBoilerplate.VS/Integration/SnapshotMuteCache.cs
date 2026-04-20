using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using MutedBoilerplate.Core.Diagnostics;
using MutedBoilerplate.Core.Matching;
using MutedBoilerplate.Core.Model;
using MutedBoilerplate.Core.Rules;
using MutedBoilerplate.VS.Options;

namespace MutedBoilerplate.VS.Integration;

// Phase 1 + 6: memoize the full (MatchContext + MuteSpan list) result per
// (snapshot, stateVersion, ruleSetVersion), computed on the ThreadPool.
//
// Get() never blocks the UI thread:
//   * fresh cache → atomic read, return immediately
//   * stale cache → return the stale result AND schedule a background
//                   recompute; ResultUpdated fires when it completes
//   * no cache yet → return an empty placeholder anchored on the requested
//                    snapshot, schedule recompute, same event
//
// One compute is in flight at a time (`_computing` CAS flag). If state or
// buffer moves during a compute, the result is installed anyway; Get()
// will see it as stale on the next UI pass and schedule another compute.
internal sealed class SnapshotMuteCache
{
    private readonly ITextBuffer _buffer;
    private readonly MuteStateService _state;
    private CachedResult? _current;
    private int _computing; // 0 = idle, 1 = background compute in flight

    public SnapshotMuteCache(ITextBuffer buffer, MuteStateService state)
    {
        _buffer = buffer;
        _state = state;
    }

    public static SnapshotMuteCache For(ITextBuffer buffer, MuteStateService state) =>
        buffer.Properties.GetOrCreateSingletonProperty(
            typeof(SnapshotMuteCache),
            () => new SnapshotMuteCache(buffer, state));

    // Fires when a background compute finishes and _current is swapped.
    // Classifier and tagger raise their own change events on receipt so
    // VS re-requests classification over the new data.
    public event EventHandler? ResultUpdated;

    public CachedResult? TryPeekCurrent() => Volatile.Read(ref _current);

    public CachedResult Get()
    {
        var snapshot = _buffer.CurrentSnapshot;
        var stateVer = _state.StateVersion;
        var ruleVer = _state.RuleSetVersion;

        var cached = Volatile.Read(ref _current);
        if (IsFresh(cached, snapshot, stateVer, ruleVer))
        {
            PerfCounters.IncrementSnapshotCacheHits();
            return cached!;
        }

        // Stale or missing — schedule a background compute but don't block.
        ScheduleCompute();

        if (cached is not null) return cached;

        // First call, nothing cached yet. Return an empty placeholder anchored
        // on the requested snapshot so VS still gets a valid span list. The
        // ResultUpdated event will prompt a reclassify once real data is ready.
        return new CachedResult(snapshot, stateVer, ruleVer,
            Array.Empty<MuteSpan>(), MuteSpanIndex.Empty);
    }

    private void ScheduleCompute()
    {
        if (Interlocked.CompareExchange(ref _computing, 1, 0) != 0) return;
        Task.Run(() =>
        {
            try { RunCompute(); }
            finally { Volatile.Write(ref _computing, 0); }
        });
    }

    private void RunCompute()
    {
        var snapshot = _buffer.CurrentSnapshot;
        var captured = _state.Capture();
        var prev = Volatile.Read(ref _current);

        MuteSpan[] spans;
        try
        {
            var ctx = BufferDocumentAdapter.Build(_buffer, snapshot);

            if (TryIncremental(prev, snapshot, captured, ctx, out var incremental))
            {
                spans = incremental;
            }
            else
            {
                var list = _state.Provider.GetSpans(ctx, captured.State, captured.RuleSet);
                spans = ToSortedArray(list);
            }
        }
        catch
        {
            // Parse failure, transient Roslyn issue, etc. Install an empty
            // result so Get() doesn't keep triggering the same failure.
            spans = Array.Empty<MuteSpan>();
        }

        var next = new CachedResult(
            snapshot,
            captured.StateVersion,
            captured.RuleSetVersion,
            spans,
            MuteSpanIndex.Build(spans));

        Volatile.Write(ref _current, next);
        ResultUpdated?.Invoke(this, EventArgs.Empty);
    }

    // Phase 5: try to reuse the previous result for text outside the edited
    // region. Preconditions:
    //   * previous result exists with matching state + ruleset versions
    //   * previous snapshot's version number is strictly before the new one
    //   * exclusions aren't active (exclusion spans outside the dirty range
    //     could veto candidates inside it; incremental can't see them)
    //   * dirty range is reasonably small (< ~30% of the new text length)
    //
    // On success, writes the merged spans (old outside-dirty shifted, new
    // inside-dirty fresh) into `spans` and returns true.
    private bool TryIncremental(
        CachedResult? prev,
        ITextSnapshot newSnapshot,
        MuteStateSnapshot captured,
        MatchContext ctx,
        out MuteSpan[] spans)
    {
        spans = Array.Empty<MuteSpan>();
        if (prev is null) return false;
        if (prev.StateVersion != captured.StateVersion) return false;
        if (prev.RuleSetVersion != captured.RuleSetVersion) return false;
        if (ReferenceEquals(prev.Snapshot, newSnapshot))
        {
            // No text change since last compute.
            spans = prev.Spans;
            return true;
        }
        if (prev.Snapshot.TextBuffer != newSnapshot.TextBuffer) return false;
        if (prev.Snapshot.Version.VersionNumber >= newSnapshot.Version.VersionNumber) return false;
        if (HasActiveExclusions(captured)) return false;
        if (_state.Provider is not MuteSpanProvider provider) return false;

        var oldText = prev.Snapshot.AsText();
        var newText = newSnapshot.AsText();
        var changes = newText.GetChangeRanges(oldText);
        if (changes.Count == 0)
        {
            spans = prev.Spans;
            return true;
        }

        int oldDirtyStart = int.MaxValue, oldDirtyEnd = int.MinValue;
        int netDelta = 0;
        foreach (var ch in changes)
        {
            if (ch.Span.Start < oldDirtyStart) oldDirtyStart = ch.Span.Start;
            if (ch.Span.End > oldDirtyEnd) oldDirtyEnd = ch.Span.End;
            netDelta += ch.NewLength - ch.Span.Length;
        }

        var newLength = newText.Length;
        if (newLength > 0)
        {
            var newDirtyLen = (oldDirtyEnd - oldDirtyStart) + netDelta;
            // Big paste / bulk replace — incremental overhead isn't worth it.
            if (newDirtyLen * 10 > newLength * 3) return false;
        }

        var newDirty = TextSpan.FromBounds(oldDirtyStart, oldDirtyEnd + netDelta);

        var fresh = provider.GetSpansInRange(ctx, captured.State, captured.RuleSet, newDirty);

        spans = MergeIncremental(prev.Spans, fresh, oldDirtyStart, oldDirtyEnd, netDelta);
        return true;
    }

    private static bool HasActiveExclusions(MuteStateSnapshot captured)
    {
        if (!captured.State.ExclusionsEnabled) return false;
        if (captured.RuleSet.Exclusions.Count > 0) return true;
        foreach (var rule in captured.RuleSet.Rules)
        {
            if (rule.Excludes is { Length: > 0 }) return true;
        }
        return false;
    }

    private static MuteSpan[] MergeIncremental(
        MuteSpan[] oldSpans,
        IEnumerable<MuteSpan> freshSpans,
        int oldDirtyStart,
        int oldDirtyEnd,
        int netDelta)
    {
        var merged = new List<MuteSpan>(oldSpans.Length + 16);

        for (int i = 0; i < oldSpans.Length; i++)
        {
            var s = oldSpans[i];
            int ss = s.Span.Start, se = s.Span.End;
            if (se <= oldDirtyStart)
            {
                merged.Add(s);
            }
            else if (ss >= oldDirtyEnd)
            {
                merged.Add(new MuteSpan(
                    TextSpan.FromBounds(ss + netDelta, se + netDelta),
                    s.CategoryKey, s.RuleName, s.Scope));
            }
            // overlap: drop — fresh walk regenerates inside dirty range
        }

        foreach (var f in freshSpans) merged.Add(f);

        var arr = merged.ToArray();
        Array.Sort(arr, CompareByStart);
        return arr;
    }

    private static bool IsFresh(CachedResult? c, ITextSnapshot snap, int stateVer, int ruleVer) =>
        c is not null
        && ReferenceEquals(c.Snapshot, snap)
        && c.StateVersion == stateVer
        && c.RuleSetVersion == ruleVer;

    private static MuteSpan[] ToSortedArray(IEnumerable<MuteSpan> source)
    {
        if (source is ICollection<MuteSpan> coll)
        {
            var a = new MuteSpan[coll.Count];
            coll.CopyTo(a, 0);
            Array.Sort(a, CompareByStart);
            return a;
        }
        var list = new List<MuteSpan>();
        foreach (var s in source) list.Add(s);
        var arr = list.ToArray();
        Array.Sort(arr, CompareByStart);
        return arr;
    }

    private static readonly Comparison<MuteSpan> CompareByStart =
        (a, b) => a.Span.Start.CompareTo(b.Span.Start);

    internal sealed class CachedResult
    {
        public CachedResult(ITextSnapshot snapshot, int stateVersion, int ruleSetVersion, MuteSpan[] spans, MuteSpanIndex index)
        {
            Snapshot = snapshot;
            StateVersion = stateVersion;
            RuleSetVersion = ruleSetVersion;
            Spans = spans;
            Index = index;
        }

        public ITextSnapshot Snapshot { get; }
        public int StateVersion { get; }
        public int RuleSetVersion { get; }
        public MuteSpan[] Spans { get; }
        public MuteSpanIndex Index { get; }
    }
}
