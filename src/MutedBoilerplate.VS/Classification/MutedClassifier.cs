using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using MutedBoilerplate.Core.Model;
using MutedBoilerplate.VS.Integration;
using MutedBoilerplate.VS.Options;

namespace MutedBoilerplate.VS.Classification;

internal sealed class MutedClassifier : IClassifier
{
    private readonly ITextBuffer _buffer;
    private readonly IClassificationTypeRegistryService _registry;
    private readonly MuteStateService _state;
    private readonly SnapshotMuteCache _cache;

    public MutedClassifier(ITextBuffer buffer, IClassificationTypeRegistryService registry, MuteStateService state)
    {
        _buffer = buffer;
        _registry = registry;
        _state = state;
        _cache = SnapshotMuteCache.For(buffer, state);

        _buffer.Changed += (_, _) => RaiseAll();
        _state.Changed += (_, _) => RaiseAll();
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

            var spans = cached.Spans;
            for (int i = 0; i < spans.Length; i++)
            {
                var s = spans[i];
                if (!Intersects(s.Span.Start, s.Span.End, reqStart, reqEnd)) continue;

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

        var slot = _state.Slots.Get(categoryKey);
        if (slot is null) return null;
        return Constants.UserSlotClass(slot.Value);
    }

    private static bool Intersects(int aStart, int aEnd, int bStart, int bEnd) =>
        aStart < bEnd && bStart < aEnd;

    private void RaiseAll()
    {
        var snap = _buffer.CurrentSnapshot;
        ClassificationChanged?.Invoke(this,
            new ClassificationChangedEventArgs(new SnapshotSpan(snap, 0, snap.Length)));
    }
}
