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
        _state.Changed += (_, _) => RaiseAll();
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
                var collapsed = $"...{s.CategoryKey}...";
                yield return new TagSpan<IOutliningRegionTag>(
                    span,
                    new OutliningRegionTag(isDefaultCollapsed: true, isImplementation: false,
                        collapsedForm: collapsed, collapsedHintForm: span.GetText()));
            }
        }
    }

    private void RaiseAll()
    {
        var snap = _buffer.CurrentSnapshot;
        TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snap, 0, snap.Length)));
    }
}
