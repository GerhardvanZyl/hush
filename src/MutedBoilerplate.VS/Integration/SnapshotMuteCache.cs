using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Text;
using MutedBoilerplate.Core.Diagnostics;
using MutedBoilerplate.Core.Matching;
using MutedBoilerplate.Core.Model;
using MutedBoilerplate.VS.Options;

namespace MutedBoilerplate.VS.Integration;

// Phase 1: memoize the full (MatchContext + MuteSpan list) result per
// (snapshot, stateVersion, ruleSetVersion). The classifier and outlining tagger
// both look it up via Get(buffer, state) so they stop duplicating the entire
// pipeline. Cache entries are per-buffer — one instance per ITextBuffer,
// pinned via the buffer's Properties bag.
//
// Thread-safety: `_current` is a reference assignment (atomic on all .NET
// runtimes we target). The compute path is guarded by `_gate` so two threads
// asking for the same snapshot don't both walk the tree; later arrivals block
// briefly on the lock and then see the cached result.
internal sealed class SnapshotMuteCache
{
    private readonly ITextBuffer _buffer;
    private readonly MuteStateService _state;
    private readonly object _gate = new();
    private CachedResult? _current;

    public SnapshotMuteCache(ITextBuffer buffer, MuteStateService state)
    {
        _buffer = buffer;
        _state = state;
    }

    public static SnapshotMuteCache For(ITextBuffer buffer, MuteStateService state) =>
        buffer.Properties.GetOrCreateSingletonProperty(
            typeof(SnapshotMuteCache),
            () => new SnapshotMuteCache(buffer, state));

    public CachedResult Get()
    {
        var snapshot = _buffer.CurrentSnapshot;
        var stateVer = _state.StateVersion;
        var ruleVer = _state.RuleSetVersion;

        var cached = _current;
        if (IsFresh(cached, snapshot, stateVer, ruleVer))
        {
            PerfCounters.IncrementSnapshotCacheHits();
            return cached!;
        }

        lock (_gate)
        {
            cached = _current;
            if (IsFresh(cached, snapshot, stateVer, ruleVer))
            {
                PerfCounters.IncrementSnapshotCacheHits();
                return cached!;
            }

            MuteSpan[] spans;
            try
            {
                var ctx = BufferDocumentAdapter.Build(_buffer, snapshot);
                var list = _state.Provider.GetSpans(ctx, _state.State, _state.RuleSet);
                spans = ToSortedArray(list);
            }
            catch
            {
                // Parse failure, transient Roslyn issue, etc. Cache the empty
                // result against this snapshot so we don't hammer the tree on
                // every repaint until the next edit bumps the snapshot.
                spans = Array.Empty<MuteSpan>();
            }

            var next = new CachedResult(snapshot, stateVer, ruleVer, spans, MuteSpanIndex.Build(spans));
            _current = next;
            return next;
        }
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
