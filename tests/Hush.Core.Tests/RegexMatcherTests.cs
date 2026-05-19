using System.Linq;
using Hush.Core.Matching;
using Hush.Core.Model;
using Hush.Core.Rules;
using Xunit;

namespace Hush.Core.Tests;

public class RegexMatcherTests
{
    [Fact]
    public void Matches_lines_when_scope_is_whole_statement()
    {
        var code = """
            line one
            DEBUG: noisy
            line three
            DEBUG: more
            """;
        var ctx = MatchContext.FromCSharp(code);
        var rule = new MuteRule
        {
            Name = "debug-prefix",
            Category = "logging",
            Kind = RuleKind.Regex,
            Pattern = new RulePattern { Regex = "^DEBUG:" },
            Scope = MuteScope.WholeStatement,
        };
        var spans = new RegexMatcher().Match(rule, ctx).ToList();
        Assert.Equal(2, spans.Count);
        var text = ctx.Text.ToString();
        Assert.All(spans, s => Assert.Contains("DEBUG:", text.Substring(s.Span.Start, s.Span.Length)));
    }

    [Fact]
    public void Match_scope_returns_only_match_span()
    {
        var ctx = MatchContext.FromCSharp("hello world");
        var rule = new MuteRule
        {
            Name = "world",
            Category = "x",
            Kind = RuleKind.Regex,
            Pattern = new RulePattern { Regex = "world" },
            Scope = MuteScope.Match,
        };
        var spans = new RegexMatcher().Match(rule, ctx).ToList();
        Assert.Single(spans);
        Assert.Equal(5, spans[0].Span.Length);
    }
}
