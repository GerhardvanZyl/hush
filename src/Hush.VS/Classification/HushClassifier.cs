using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Hush.Core.Model;
using Hush.VS.Integration;
using Hush.VS.Options;

namespace Hush.VS.Classification;

internal sealed class HushClassifier : IClassifier
{
    private readonly ITextBuffer _buffer;
    private readonly IClassificationTypeRegistryService _registry;
    private readonly MuteStateService _state;
    private readonly SnapshotMuteCache _cache;

    public HushClassifier(ITextBuffer buffer, IClassificationTypeRegistryService registry, MuteStateService state)
    {
        _buffer = buffer;
        _registry = registry;
        _state = state;
        _cache = SnapshotMuteCache.For(buffer, state);

        _buffer.Changed += (_, _) => RaiseAll();
        _state.Changed += OnStateChanged;
        _cache.ResultUpdated += (_, _) => RaiseAll();
    }

    public event EventHandler<ClassificationChangedEventArgs>? ClassificationChanged;

    public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan snapshot)
    {
        var result = new List<ClassificationSpan>();
        try
        {
            var cached = _cache.Get();
            var bufferSnapshot = cached.Snapshot;
            var reqStart = snapshot.Start.Position;
            var reqEnd = snapshot.End.Position;

            var index = cached.Index;
            index.GetCandidateRange(reqStart, reqEnd, out var lo, out var hi);
            for (int i = lo; i < hi; i++)
            {
                var s = index[i];
                if (s.Span.End <= reqStart) continue;

                var classificationName = ResolveClassificationName(s.CategoryKey);
                if (classificationName is null) continue;

                var ct = _registry.GetClassificationType(classificationName);
                if (ct is null) continue;

                var start = Math.Max(0, Math.Min(s.Span.Start, bufferSnapshot.Length));
                var end = Math.Max(start, Math.Min(s.Span.End, bufferSnapshot.Length));
                if (end <= start) continue;

                result.Add(new ClassificationSpan(
                    new SnapshotSpan(bufferSnapshot, start, end - start), ct));
            }
        }
        catch
        {
            // Classifier swallowing is intentional: never let one bad parse take out
            // the whole editor session. The next change event re-runs us.
        }
        return result;
    }

    private string? ResolveClassificationName(string categoryKey)
    {
        if (categoryKey == MuteCategory.TelemetryKey) return Constants.ClassTelemetry;
        if (categoryKey == MuteCategory.LoggingKey) return Constants.ClassLogging;
        if (categoryKey == MuteCategory.SignatureKey) return Constants.ClassSignature;
        if (categoryKey == MuteCategory.GuardsKey) return Constants.ClassGuards;

        var slot = _state.Slots.Get(categoryKey);
        if (slot is null) return null;
        return Constants.UserSlotClass(slot.Value);
    }

    // Phase 7: a single-category toggle only changes spans of that category.
    // Raise ClassificationChanged for each contiguous run of that category's
    // spans in the pre-toggle cache (which still sits in _cache._current
    // because Get() hasn't been called yet on the new stateVersion). Whole-
    // buffer invalidation is still used for buffer edits, ToggleAll,
    // ToggleExclusions, and RuleSet reloads.
    //
    // Enable-side bug fix: when a category is being ENABLED, the pre-toggle
    // cache contains no spans of that category (state.IsEnabled filtered them
    // out in MuteSpanProvider). Walking the cache finds nothing, so without
    // the fallback below no ClassificationChanged event would fire and VS
    // would never re-query — leaving the mutes invisible until the next
    // buffer edit. Fall back to RaiseAll() in that case. We also bypass the
    // 1s recompute debounce: this is a deliberate user action, not a typing
    // flood, so coalescing buys us nothing and costs perceived snappiness.
    private void OnStateChanged(object? sender, MuteStateChangedEventArgs e)
    {
        if (e.AffectsAllCategories)
        {
            RaiseAll();
            _cache.RequestImmediateRefresh();
            return;
        }

        if (e.CategoryKey is null || ResolveClassificationName(e.CategoryKey) is null)
        {
            // Not a category we render — nothing to invalidate, no need to recompute.
            return;
        }

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
        var handler = ClassificationChanged;
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

            if (runStart < 0)
            {
                runStart = ss;
                runEnd = se;
                continue;
            }

            if (ss <= runEnd)
            {
                if (se > runEnd) runEnd = se;
            }
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

    private void FireRange(EventHandler<ClassificationChangedEventArgs> handler,
        ITextSnapshot snap, int start, int end)
    {
        var clampedStart = Math.Max(0, Math.Min(start, snap.Length));
        var clampedEnd = Math.Max(clampedStart, Math.Min(end, snap.Length));
        if (clampedEnd <= clampedStart) return;
        handler(this, new ClassificationChangedEventArgs(
            new SnapshotSpan(snap, clampedStart, clampedEnd - clampedStart)));
    }

    private void RaiseAll()
    {
        var snap = _buffer.CurrentSnapshot;
        ClassificationChanged?.Invoke(this,
            new ClassificationChangedEventArgs(new SnapshotSpan(snap, 0, snap.Length)));
    }
}
