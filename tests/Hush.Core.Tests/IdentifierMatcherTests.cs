using System.Linq;
using Hush.Core.Matching;
using Hush.Core.Model;
using Hush.Core.Rules;
using Xunit;

namespace Hush.Core.Tests;

public class IdentifierMatcherTests
{
    [Fact]
    public void Matches_identifier_and_returns_enclosing_statement_span_for_whole_statement()
    {
        var code = """
            class C { void M(){ var x = _auditLogger.LogCritical("a"); var y = _other.LogInformation("b"); } }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var rule = new MuteRule
        {
            Name = "audit",
            Category = "x",
            Kind = RuleKind.Identifier,
            Pattern = new RulePattern { Identifier = "_auditLogger" },
            Scope = MuteScope.WholeStatement,
        };

        var spans = new IdentifierMatcher().Match(rule, ctx).ToList();
        Assert.Single(spans);
        var text = ctx.Text.ToString();
        var snippet = text.Substring(spans[0].Span.Start, spans[0].Span.Length);
        Assert.Contains("_auditLogger", snippet);
        Assert.DoesNotContain("_other", snippet);
    }
}
