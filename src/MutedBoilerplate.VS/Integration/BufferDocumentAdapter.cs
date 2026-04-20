using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.LanguageServices;
using Microsoft.VisualStudio.Text;
using MutedBoilerplate.Core.Matching;

namespace MutedBoilerplate.VS.Integration;

/// <summary>
/// Bridges a VS <see cref="ITextBuffer"/> to a Roslyn <see cref="Document"/> when
/// the buffer is part of a workspace; falls back to a text-only context otherwise
/// (e.g. miscellaneous files or non-C# buffers, where regex rules can still fire).
/// </summary>
internal static class BufferDocumentAdapter
{
    // Phase 9: incremental-parse cache for the workspace-less fallback. Before
    // this the misc-file path called CSharpSyntaxTree.ParseText() on every
    // repaint; on a 50k-line file that's a full parse per Build, even though
    // SnapshotMuteCache memoizes the result — the first hit per snapshot
    // still paid the full parse. Now we keep the previous tree alongside its
    // snapshot and use SyntaxTree.WithChangedText for subsequent snapshots,
    // which reuses unchanged subtrees.
    private static readonly object StandaloneCacheKey = new();

    public static MatchContext Build(ITextBuffer buffer) =>
        Build(buffer, buffer.CurrentSnapshot);

    public static MatchContext Build(ITextBuffer buffer, ITextSnapshot snapshot)
    {
        var sourceText = snapshot.AsText();

        var workspace = TryGetWorkspace(buffer);
        if (workspace is not null)
        {
            var docId = workspace.GetDocumentIdInCurrentContext(sourceText.Container);
            if (docId is not null)
            {
                var doc = workspace.CurrentSolution.GetDocument(docId);
                if (doc is not null)
                {
                    // Use the synchronous TryGet* APIs only — running async work synchronously
                    // from a classifier or tagger (UI thread) is a deadlock vector. Cached
                    // tree/model are the common case after a file has been touched once.
                    SyntaxTree? tree = doc.TryGetSyntaxTree(out var t) ? t : null;
                    SemanticModel? sm = doc.TryGetSemanticModel(out var s) ? s : null;
                    if (tree is not null)
                        return new MatchContext(sourceText, tree, sm);
                }
            }
        }

        // No workspace association; parse standalone so Roslyn-based matchers still work
        // structurally even though semantic info is absent.
        var standaloneTree = GetOrBuildStandaloneTree(buffer, snapshot, sourceText);
        return new MatchContext(sourceText, standaloneTree);
    }

    private static SyntaxTree GetOrBuildStandaloneTree(
        ITextBuffer buffer, ITextSnapshot snapshot, SourceText sourceText)
    {
        var entry = buffer.Properties.GetOrCreateSingletonProperty(
            StandaloneCacheKey,
            () => new StandaloneCacheEntry(snapshot, CSharpSyntaxTree.ParseText(sourceText)));

        lock (entry)
        {
            if (ReferenceEquals(entry.Snapshot, snapshot))
                return entry.Tree;

            // Incremental re-parse — Roslyn diffs the SourceText against the
            // previous tree's text and reuses unchanged subtrees.
            var newTree = entry.Tree.WithChangedText(sourceText);
            entry.Snapshot = snapshot;
            entry.Tree = newTree;
            return newTree;
        }
    }

    private static VisualStudioWorkspace? TryGetWorkspace(ITextBuffer buffer)
    {
        try
        {
            return buffer.GetWorkspace() as VisualStudioWorkspace;
        }
        catch
        {
            return null;
        }
    }

    private sealed class StandaloneCacheEntry
    {
        public StandaloneCacheEntry(ITextSnapshot snapshot, SyntaxTree tree)
        {
            Snapshot = snapshot;
            Tree = tree;
        }

        public ITextSnapshot Snapshot;
        public SyntaxTree Tree;
    }
}
