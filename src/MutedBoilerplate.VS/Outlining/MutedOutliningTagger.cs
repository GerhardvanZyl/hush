using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using MutedBoilerplate.VS.Integration;
using MutedBoilerplate.VS.Options;

namespace MutedBoilerplate.VS.Outlining;

internal sealed class MutedOutliningTagger : ITagger<IOutliningRegionTag>
{
    private readonly ITextBuffer _buffer;
    private readonly MuteStateService _state;
    private readonly SnapshotMuteCache _cache;

    public MutedOutliningTagger(ITextBuffer buffer, MuteStateService state)
    {
        _buffer = buffer;
        _state = state;
        _cache = SnapshotMuteCache.For(buffer, state);
        _buffer.Changed += (_, _) => RaiseAll();
        _state.Changed += OnStateChanged;
        _cache.ResultUpdated += (_, _) => RaiseAll();
    }

    private void OnStateChanged(object? sender, MuteStateChangedEventArgs e)
    {
        // Phase 7: outlining regions only appear for AutoCollapse categories.
        // If the toggled category isn't AutoCollapse, nothing in the outliner
        // changes. For AutoCollapse categories we still need to reraise over
        // the affected spans so VS refreshes the outlining adornments.
        //
        // Enable-side fix: see MutedClassifier.OnStateChanged. The pre-toggle
        // cache has no spans of the just-enabled category, so the per-category
        // walk finds nothing and would otherwise raise no event. Fall back to
        // RaiseAll() and bypass the recompute debounce so the regions appear
        // promptly on a deliberate user toggle.
        if (e.AffectsAllCategories)
        {
            RaiseAll();
            _cache.RequestImmediateRefresh();
            return;
        }

        if (e.CategoryKey is null || !_state.RuleSet.StyleFor(e.CategoryKey).AutoCollapse) return;

        var cached = _cache.TryPeekCurrent();
        if (cached is null
            || cached.Spans.Length == 0
            || !TryRaiseForCategory(cached, e.CategoryKey))
        {
            RaiseAll();
        }

        _cache.RequestImmediateRefresh();
    }

    private bool TryRaiseForCategory(SnapshotMuteCache.CachedResult cached, string categoryKey)
    {
        var handler = TagsChanged;
        if (handler is null) return true;

        var snap = cached.Snapshot;
        var spans = cached.Spans;
        int runStart = -1;
        int runEnd = -1;
        bool fired = false;

        for (int i = 0; i < spans.Length; i++)
        {
            var s = spans[i];
            if (!string.Equals(s.CategoryKey, categoryKey, StringComparison.OrdinalIgnoreCase)) continue;
            var ss = s.Span.Start;
            var se = s.Span.End;
            if (runStart < 0) { runStart = ss; runEnd = se; continue; }
            if (ss <= runEnd) { if (se > runEnd) runEnd = se; }
            else
            {
                FireRange(handler, snap, runStart, runEnd);
                fired = true;
                runStart = ss;
                runEnd = se;
            }
        }
        if (runStart >= 0)
        {
            FireRange(handler, snap, runStart, runEnd);
            fired = true;
        }

        return fired;
    }

    private void FireRange(EventHandler<SnapshotSpanEventArgs> handler, ITextSnapshot snap, int start, int end)
    {
        var s = Math.Max(0, Math.Min(start, snap.Length));
        var e = Math.Max(s, Math.Min(end, snap.Length));
        if (e <= s) return;
        handler(this, new SnapshotSpanEventArgs(new SnapshotSpan(snap, s, e - s)));
    }

    public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

    public IEnumerable<ITagSpan<IOutliningRegionTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
        if (spans.Count == 0) yield break;

        SnapshotMuteCache.CachedResult cached;
        try { cached = _cache.Get(); }
        catch { yield break; }

        var snap = cached.Snapshot;
        var ruleSet = _state.RuleSet;
        var index = cached.Index;

        // Phase 4: honor the requested span set. Previously this tagger computed
        // over the whole buffer (requested = [0..snap.Length)); the `spans`
        // parameter was ignored. For large files the editor only asks about a
        // few visible regions at a time — iterate each and use the interval
        // index to bound the scan.
        for (int sp = 0; sp < spans.Count; sp++)
        {
            var requested = spans[sp];
            var reqStart = requested.Start.Position;
            var reqEnd = requested.End.Position;
            index.GetCandidateRange(reqStart, reqEnd, out var lo, out var hi);

            for (int i = lo; i < hi; i++)
            {
                var s = index[i];
                if (s.Span.End <= reqStart) continue;

                var style = ruleSet.StyleFor(s.CategoryKey);
                if (!style.AutoCollapse) continue;

                var start = Math.Max(0, Math.Min(s.Span.Start, snap.Length));
                var end = Math.Max(start, Math.Min(s.Span.End, snap.Length));
                if (end <= start) continue;

                var span = new SnapshotSpan(snap, start, end - start);
                // Phase 8: collapsed form comes from a precomputed per-category
                // cache — no per-tag allocation. The hint form used to be
                // span.GetText(), which allocated the whole collapsed region's
                // text per tag emission. Reusing the collapsed form as the
                // hint trades a subtler hover preview for zero allocation.
                var collapsed = _state.GetCollapsedForm(s.CategoryKey);
                yield return new TagSpan<IOutliningRegionTag>(
                    span,
                    new OutliningRegionTag(isDefaultCollapsed: true, isImplementation: false,
                        collapsedForm: collapsed, collapsedHintForm: collapsed));
            }
        }
    }

    private void RaiseAll()
    {
        var snap = _buffer.CurrentSnapshot;
        TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snap, 0, snap.Length)));
    }
}
