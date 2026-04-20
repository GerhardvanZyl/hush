using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace MutedBoilerplate.Core.Matching;

public sealed class MatchContext
{
    public MatchContext(SourceText text, SyntaxTree? tree = null, SemanticModel? semantics = null)
    {
        Text = text;
        Tree = tree;
        Semantics = semantics;
    }

    public SourceText Text { get; }
    public SyntaxTree? Tree { get; }
    public SemanticModel? Semantics { get; }

    public static MatchContext FromCSharp(string code)
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
        return new MatchContext(tree.GetText(), tree);
    }

    public static MatchContext FromCSharpWithSemantics(string code)
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
        var refs = new[]
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        };
        var comp = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "MutedBoilerplate.Core.Tests.Inline",
            new[] { tree },
            refs);
        return new MatchContext(tree.GetText(), tree, comp.GetSemanticModel(tree));
    }
}
