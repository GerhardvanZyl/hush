using System.Linq;
using MutedBoilerplate.Core.Matching;
using MutedBoilerplate.Core.Model;
using Xunit;

namespace MutedBoilerplate.Core.Tests;

public class RoslynCallMatcherTests
{
    private const string LoggerCode = """
        class C
        {
            private readonly ILogger _logger;
            void M()
            {
                _logger.LogInformation("hi");
                Compute();
                _logger.LogError("oops");
            }
            int Compute() => 1;
        }
        """;

    [Fact]
    public void Matches_logger_calls_via_heuristic_receiver_name()
    {
        var ctx = MatchContext.FromCSharp(LoggerCode);
        var matcher = new RoslynCallMatcher();
        var rule = TestRuleSet.LoggerCallRule();

        var spans = matcher.Match(rule, ctx).ToList();

        Assert.Equal(2, spans.Count);
        Assert.All(spans, s =>
        {
            Assert.Equal(MuteCategory.LoggingKey, s.CategoryKey);
            Assert.Equal(MuteScope.WholeStatement, s.Scope);
        });
    }

    [Fact]
    public void Skips_unrelated_calls()
    {
        var ctx = MatchContext.FromCSharp(LoggerCode);
        var matcher = new RoslynCallMatcher();
        var rule = TestRuleSet.LoggerCallRule();

        var spans = matcher.Match(rule, ctx).ToList();

        var text = ctx.Text.ToString();
        Assert.DoesNotContain(spans, s => text.Substring(s.Span.Start, s.Span.Length).Contains("Compute"));
    }

    [Fact]
    public void Static_call_matches_by_type_name()
    {
        var code = "class C { void M() { Console.WriteLine(\"hi\"); } }";
        var ctx = MatchContext.FromCSharp(code);
        var rule = new MutedBoilerplate.Core.Rules.MuteRule
        {
            Name = "console",
            Category = MuteCategory.LoggingKey,
            Kind = MutedBoilerplate.Core.Rules.RuleKind.RoslynCall,
            Pattern = new MutedBoilerplate.Core.Rules.RulePattern { ReceiverTypeGlob = "Console", MethodNameGlob = "WriteLine" },
            Scope = MuteScope.WholeStatement,
        };

        var spans = new RoslynCallMatcher().Match(rule, ctx).ToList();

        Assert.Single(spans);
    }
}
