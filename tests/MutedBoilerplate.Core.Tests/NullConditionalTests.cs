using System.Linq;
using MutedBoilerplate.Core.Matching;
using MutedBoilerplate.Core.Model;
using MutedBoilerplate.Core.Rules;
using Xunit;

namespace MutedBoilerplate.Core.Tests;

public class NullConditionalTests
{
    [Fact]
    public void Null_conditional_invocation_matches_via_member_binding_expression()
    {
        var code = """
            class Activity { public void SetTag(string k, object? v) { } }
            class C
            {
                Activity? activity;
                void M() { activity?.SetTag("k", 1); }
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var rule = new MuteRule
        {
            Name = "activity-tag",
            Category = MuteCategory.TelemetryKey,
            Kind = RuleKind.RoslynCall,
            Pattern = new RulePattern { ReceiverTypeGlob = "Activity", MethodNameGlob = "SetTag" },
            Scope = MuteScope.WholeStatement,
        };

        var spans = new RoslynCallMatcher().Match(rule, ctx).ToList();

        Assert.Single(spans);
        var text = ctx.Text.ToString();
        Assert.Contains("activity?.SetTag", text.Substring(spans[0].Span.Start, spans[0].Span.Length));
    }

    [Fact]
    public void ActivitySource_StartActivity_matches_via_default_rules()
    {
        var code = """
            class C
            {
                static readonly ActivitySource ActivitySource = new("x");
                void M() { using var a = ActivitySource.StartActivity("op"); }
            }
            class ActivitySource { public ActivitySource(string s) {} public Activity? StartActivity(string n) => null; }
            class Activity {}
            """;
        var ctx = MatchContext.FromCSharp(code);
        var rs = RuleSet.LoadDefaults();
        var spans = MuteSpanProvider.CreateDefault().GetSpans(ctx, TestRuleSet.AllOn(rs), rs).ToList();

        Assert.Contains(spans, s =>
            s.CategoryKey == MuteCategory.TelemetryKey &&
            ctx.Text.ToString().Substring(s.Span.Start, s.Span.Length).Contains("StartActivity"));
    }
}
