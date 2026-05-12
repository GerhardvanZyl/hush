using System.Collections.Concurrent;
using MutedBoilerplate.Core.Model;

namespace MutedBoilerplate.VSCode.Sidecar.Host;

/// <summary>
/// Per-document memoization of <c>MuteSpan[]</c> keyed on (uri, docVersion, stateVersion, ruleSetVersion).
/// Lighter than the VS-side <c>SnapshotMuteCache</c> because the sidecar runs on the .NET ThreadPool,
/// not the UI thread — so we don't need the debounced background recompute / placeholder-on-first-call dance.
/// </summary>
internal sealed class SpanCache
{
    private sealed record Key(string Uri, int DocVersion, long StateVersion, long RuleSetVersion);

    private readonly ConcurrentDictionary<string, (Key Key, MuteSpan[] Spans)> _byUri = new();

    public bool TryGet(string uri, int docVersion, long stateVersion, long ruleSetVersion, out MuteSpan[] spans)
    {
        if (_byUri.TryGetValue(uri, out var entry)
            && entry.Key.DocVersion == docVersion
            && entry.Key.StateVersion == stateVersion
            && entry.Key.RuleSetVersion == ruleSetVersion)
        {
            spans = entry.Spans;
            return true;
        }
        spans = System.Array.Empty<MuteSpan>();
        return false;
    }

    public void Put(string uri, int docVersion, long stateVersion, long ruleSetVersion, MuteSpan[] spans)
    {
        _byUri[uri] = (new Key(uri, docVersion, stateVersion, ruleSetVersion), spans);
    }

    public void Invalidate(string uri) => _byUri.TryRemove(uri, out _);

    public void Clear() => _byUri.Clear();
}
