using System.Linq;
using Hush.Core.Matching;
using Hush.Core.Model;
using Hush.Core.Rules;
using Xunit;

namespace Hush.Core.Tests;

public class MuteSpanProviderTests
{
    private const string Code = """
        class Service
        {
            private readonly ILogger _logger;
            private readonly TelemetryClient _telemetryClient;

            public int Process(int x)
            {
                _logger.LogInformation("starting");
                _telemetryClient.TrackEvent("processed");
                return x * 2;
            }
        }
        """;

    [Fact]
    public void End_to_end_default_rules_produce_spans_for_built_ins()
    {
        var ctx = MatchContext.FromCSharp(Code);
        var rs = RuleSet.LoadDefaults();
        var spans = MuteSpanProvider.CreateDefault().GetSpans(ctx, TestRuleSet.AllOn(rs), rs).ToList();

        Assert.Contains(spans, s => s.CategoryKey == MuteCategory.LoggingKey);
        Assert.Contains(spans, s => s.CategoryKey == MuteCategory.TelemetryKey);
        Assert.Contains(spans, s => s.CategoryKey == MuteCategory.SignatureKey);
    }

    [Fact]
    public void Disabling_a_category_filters_out_its_spans()
    {
        var ctx = MatchContext.FromCSharp(Code);
        var rs = RuleSet.LoadDefaults();
        var state = TestRuleSet.AllOn(rs);
        state.Set(MuteCategory.LoggingKey, false);

        var spans = MuteSpanProvider.CreateDefault().GetSpans(ctx, state, rs).ToList();

        Assert.DoesNotContain(spans, s => s.CategoryKey == MuteCategory.LoggingKey);
        Assert.Contains(spans, s => s.CategoryKey == MuteCategory.TelemetryKey);
    }

    [Fact]
    public void User_category_rule_emits_user_category_spans()
    {
        var rs = RuleSet.LoadDefaults();
        rs.Categories.Add(new CategoryDefinition { Key = "validation", DisplayName = "Validation" });
        rs.Rules.Add(new MuteRule
        {
            Name = "guard-against",
            Category = "validation",
            Kind = RuleKind.RoslynCall,
            Pattern = new RulePattern { ReceiverTypeGlob = "Guard", MethodNameGlob = "Against*" },
            Scope = MuteScope.WholeStatement,
        });

        var code = "class C { void M(string s) { Guard.AgainstNull(s); var x = 1; } }";
        var ctx = MatchContext.FromCSharp(code);
        var spans = MuteSpanProvider.CreateDefault().GetSpans(ctx, TestRuleSet.AllOn(rs), rs).ToList();

        Assert.Contains(spans, s => s.CategoryKey == "validation");
    }
}
