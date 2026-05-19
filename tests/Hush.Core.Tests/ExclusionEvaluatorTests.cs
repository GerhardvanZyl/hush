using System.Collections.Generic;
using System.Linq;
using Hush.Core.Matching;
using Hush.Core.Model;
using Hush.Core.Rules;
using Xunit;

namespace Hush.Core.Tests;

public class ExclusionEvaluatorTests
{
    // Inline ILogger so the SemanticModel can resolve _auditLogger's type without
    // pulling in the real Microsoft.Extensions.Logging assembly.
    private const string Code = """
        interface ILogger
        {
            void LogInformation(string s);
            void LogCritical(string s);
        }
        class C
        {
            private readonly ILogger _logger = null!;
            private readonly ILogger _auditLogger = null!;
            void M()
            {
                _logger.LogInformation("normal");
                _auditLogger.LogInformation("audit");
                _logger.LogCritical("boom");
            }
        }
        """;

    private static MatchContext Ctx() => MatchContext.FromCSharpWithSemantics(Code);

    [Fact]
    public void Global_identifier_exclusion_drops_audit_lines_only()
    {
        var rs = new RuleSet
        {
            Rules = { TestRuleSet.LoggerCallRule() },
            Exclusions = { new ExclusionRule
            {
                Name = "skip-audit",
                Kind = RuleKind.Identifier,
                Pattern = new RulePattern { Identifier = "_auditLogger" },
                AppliesTo = "*",
            } },
        };
        rs.NormalizeCategories();

        var spans = MuteSpanProvider.CreateDefault().GetSpans(Ctx(), TestRuleSet.AllOn(rs), rs).ToList();

        var text = Ctx().Text.ToString();
        Assert.Equal(2, spans.Count);
        Assert.DoesNotContain(spans, s => text.Substring(s.Span.Start, s.Span.Length).Contains("audit"));
    }

    [Fact]
    public void Category_scoped_exclusion_only_filters_that_category()
    {
        var rs = new RuleSet
        {
            Rules =
            {
                TestRuleSet.LoggerCallRule(),
                new MuteRule
                {
                    Name = "any-call-on-audit",
                    Category = "telemetry",
                    Kind = RuleKind.RoslynCall,
                    Pattern = new RulePattern { ReceiverTypeGlob = "ILogger*", MethodNameGlob = "*" },
                    Scope = MuteScope.WholeStatement,
                },
            },
            Exclusions = { new ExclusionRule
            {
                Name = "skip-audit-logging-only",
                Kind = RuleKind.Identifier,
                Pattern = new RulePattern { Identifier = "_auditLogger" },
                AppliesTo = "logging",
            } },
        };
        rs.NormalizeCategories();

        var ctx = Ctx();
        var spans = MuteSpanProvider.CreateDefault().GetSpans(ctx, TestRuleSet.AllOn(rs), rs).ToList();

        var text = ctx.Text.ToString();
        var auditSpans = spans
            .Where(s => text.Substring(s.Span.Start, s.Span.Length).Contains("_auditLogger"))
            .ToList();
        Assert.Single(auditSpans);
        Assert.Equal("telemetry", auditSpans[0].CategoryKey);
    }

    [Fact]
    public void Rule_local_exclusion_filters_only_that_rule()
    {
        var rule = TestRuleSet.LoggerCallRule();
        rule.Excludes = new[]
        {
            new ExclusionRule
            {
                Name = "no-critical",
                Kind = RuleKind.Identifier,
                Pattern = new RulePattern { Identifier = "LogCritical" },
            },
        };
        var rs = new RuleSet { Rules = { rule } };
        rs.NormalizeCategories();

        var ctx = Ctx();
        var spans = MuteSpanProvider.CreateDefault().GetSpans(ctx, TestRuleSet.AllOn(rs), rs).ToList();
        var text = ctx.Text.ToString();
        Assert.DoesNotContain(spans, s => text.Substring(s.Span.Start, s.Span.Length).Contains("LogCritical"));
        Assert.Equal(2, spans.Count);
    }

    [Fact]
    public void Disabling_exclusions_brings_filtered_spans_back()
    {
        var rs = new RuleSet
        {
            Rules = { TestRuleSet.LoggerCallRule() },
            Exclusions = { new ExclusionRule
            {
                Name = "skip-audit",
                Kind = RuleKind.Identifier,
                Pattern = new RulePattern { Identifier = "_auditLogger" },
                AppliesTo = "*",
            } },
        };
        rs.NormalizeCategories();

        var state = TestRuleSet.AllOn(rs);
        state.SetExclusionsEnabled(false);
        var spans = MuteSpanProvider.CreateDefault().GetSpans(Ctx(), state, rs).ToList();

        Assert.Equal(3, spans.Count);
    }
}
