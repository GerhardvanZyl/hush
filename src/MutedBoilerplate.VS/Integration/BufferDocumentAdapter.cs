using Microsoft.CodeAnalysis;
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
    public static MatchContext Build(ITextBuffer buffer)
    {
        var snapshot = buffer.CurrentSnapshot;
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
        var standaloneTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceText);
        return new MatchContext(sourceText, standaloneTree);
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
}
