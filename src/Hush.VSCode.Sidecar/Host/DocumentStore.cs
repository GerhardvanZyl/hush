using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Hush.VSCode.Sidecar.Host;

/// <summary>
/// Server-side mirror of open editor documents. Tracks (uri → version, source, syntaxTree).
/// On didChange we use <see cref="SyntaxTree.WithChangedText"/> for unchanged-subtree reuse,
/// matching the pattern in <c>BufferDocumentAdapter</c> standalone path.
/// </summary>
internal sealed class DocumentStore
{
    public sealed record Entry(string Uri, string LanguageId, int Version, SourceText Text, SyntaxTree? Tree);

    private readonly ConcurrentDictionary<string, Entry> _docs = new();

    public void DidOpen(string uri, string languageId, int version, string content)
    {
        var text = SourceText.From(content);
        var tree = ParseFor(languageId, text);
        _docs[uri] = new Entry(uri, languageId, version, text, tree);
    }

    public bool DidChange(string uri, int version, IReadOnlyList<TextChangeRange> changes, IReadOnlyList<string> insertedTexts)
    {
        if (!_docs.TryGetValue(uri, out var prev)) return false;

        // Apply edits in reverse order so absolute offsets stay valid.
        var text = prev.Text;
        for (int i = changes.Count - 1; i >= 0; i--)
        {
            var change = new TextChange(changes[i].Span, insertedTexts[i]);
            text = text.WithChanges(change);
        }

        SyntaxTree? tree = prev.Tree?.WithChangedText(text);
        _docs[uri] = new Entry(uri, prev.LanguageId, version, text, tree);
        return true;
    }

    public bool DidClose(string uri) => _docs.TryRemove(uri, out _);

    public Entry? Get(string uri) => _docs.TryGetValue(uri, out var e) ? e : null;

    public IEnumerable<Entry> All() => _docs.Values;

    private static SyntaxTree? ParseFor(string languageId, SourceText text)
    {
        return languageId.Equals("csharp", System.StringComparison.OrdinalIgnoreCase)
            ? CSharpSyntaxTree.ParseText(text)
            : null;
    }
}
