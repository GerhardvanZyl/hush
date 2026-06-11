using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Hush.Core.Diagnostics;

namespace Hush.Core.Matching;

public sealed class MatchContext
{
    private string? _textString;

    public MatchContext(SourceText text, SyntaxTree? tree = null, SemanticModel? semantics = null)
    {
        Text = text;
        Tree = tree;
        Semantics = semantics;
        PerfCounters.IncrementMatchContextBuilds();
    }

    public SourceText Text { get; }
    public SyntaxTree? Tree { get; }
    public SemanticModel? Semantics { get; }

    // Lazy, memoized materialization of the whole buffer as a string. Regex-based
    // matchers used to allocate this once per matcher invocation (once per rule
    // per GetSpans call). With many regex rules on a 50k-line file that dwarfed
    // everything else. Benign race: losing thread's string is GCed immediately.
    public string AsString()
    {
        var cached = _textString;
        if (cached is not null) return cached;
        var s = Text.ToString();
        var prev = Interlocked.CompareExchange(ref _textString, s, null);
        if (prev is null)
        {
            PerfCounters.IncrementTextStringAllocations();
            return s;
        }
        return prev;
    }

    public static MatchContext FromCSharp(string code)
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
        return new MatchContext(tree.GetText(), tree);
    }

    public static MatchContext FromCSharpWithSemantics(string code)
    {
        var tree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(code);
        var refs = new System.Collections.Generic.List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
        };
        // On .NET Core/5+ the core types are facaded through System.Runtime —
        // without it extension-method signatures (e.g. Enumerable.Count over
        // IEnumerable<T>) don't bind. Absent on .NET Framework, hence the probe.
        var runtimeFacade = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(typeof(object).Assembly.Location) ?? "",
            "System.Runtime.dll");
        if (System.IO.File.Exists(runtimeFacade))
            refs.Add(MetadataReference.CreateFromFile(runtimeFacade));
        var comp = Microsoft.CodeAnalysis.CSharp.CSharpCompilation.Create(
            "Hush.Core.Tests.Inline",
            new[] { tree },
            refs);
        return new MatchContext(tree.GetText(), tree, comp.GetSemanticModel(tree));
    }
}
