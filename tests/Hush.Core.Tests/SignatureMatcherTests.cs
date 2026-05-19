using System.Linq;
using Hush.Core.Matching;
using Xunit;

namespace Hush.Core.Tests;

public class SignatureMatcherTests
{
    private const string Code = """
        class C
        {
            public int Add(int a, int b) { return a + b; }
            void Empty() { }
            int Expr(int x) => x + 1;
        }
        """;

    [Fact]
    public void Emits_signature_range_spans_for_each_method()
    {
        var ctx = MatchContext.FromCSharp(Code);
        var spans = new SignatureMatcher().Match(TestRuleSet.SignatureRule(), ctx).ToList();
        Assert.Equal(3, spans.Count);
    }

    [Fact]
    public void Signature_span_excludes_method_body()
    {
        var ctx = MatchContext.FromCSharp(Code);
        var spans = new SignatureMatcher().Match(TestRuleSet.SignatureRule(), ctx).ToList();
        var text = ctx.Text.ToString();
        var addSpan = spans.First(s => text.Substring(s.Span.Start, s.Span.Length).Contains("Add"));
        var addText = text.Substring(addSpan.Span.Start, addSpan.Span.Length);
        Assert.Contains("(int a, int b)", addText);
        Assert.DoesNotContain("return", addText);
    }
}
