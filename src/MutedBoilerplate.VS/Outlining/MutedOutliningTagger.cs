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

    public MutedOutliningTagger(ITextBuffer buffer, MuteStateService state)
    {
        _buffer = buffer;
        _state = state;
        _buffer.Changed += (_, _) => RaiseAll();
        _state.Changed += (_, _) => RaiseAll();
    }

    public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

    public IEnumerable<ITagSpan<IOutliningRegionTag>> GetTags(NormalizedSnapshotSpanCollection spans)
    {
        if (spans.Count == 0) yield break;

        MutedBoilerplate.Core.Matching.MatchContext ctx;
        try { ctx = BufferDocumentAdapter.Build(_buffer); }
        catch { yield break; }

        var muteSpans = _state.Provider.GetSpans(ctx, _state.State, _state.RuleSet);
        var snap = _buffer.CurrentSnapshot;
        var requested = new SnapshotSpan(snap, 0, snap.Length);

        foreach (var s in muteSpans)
        {
            var style = _state.RuleSet.StyleFor(s.CategoryKey);
            if (!style.AutoCollapse) continue;

            var start = Math.Max(0, Math.Min(s.Span.Start, snap.Length));
            var end = Math.Max(start, Math.Min(s.Span.End, snap.Length));
            if (end <= start) continue;

            var span = new SnapshotSpan(snap, start, end - start);
            if (!requested.IntersectsWith(span)) continue;

            var collapsed = $"...{s.CategoryKey}...";
            yield return new TagSpan<IOutliningRegionTag>(
                span,
                new OutliningRegionTag(isDefaultCollapsed: true, isImplementation: false,
                    collapsedForm: collapsed, collapsedHintForm: span.GetText()));
        }
    }

    private void RaiseAll()
    {
        var snap = _buffer.CurrentSnapshot;
        TagsChanged?.Invoke(this, new SnapshotSpanEventArgs(new SnapshotSpan(snap, 0, snap.Length)));
    }
}
