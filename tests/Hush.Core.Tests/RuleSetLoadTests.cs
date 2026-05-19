using System.Linq;
using Hush.Core.Model;
using Hush.Core.Rules;
using Xunit;

namespace Hush.Core.Tests;

public class RuleSetLoadTests
{
    [Fact]
    public void LoadDefaults_contains_three_built_in_categories_and_rules()
    {
        var rs = RuleSet.LoadDefaults();
        Assert.Contains(rs.Categories, c => c.Key == "telemetry");
        Assert.Contains(rs.Categories, c => c.Key == "logging");
        Assert.Contains(rs.Categories, c => c.Key == "signature");
        Assert.NotEmpty(rs.Rules.Where(r => r.Category == "logging"));
        Assert.NotEmpty(rs.Rules.Where(r => r.Category == "signature"));
    }

    [Fact]
    public void Round_trip_preserves_user_categories_and_exclusions()
    {
        var original = new RuleSet
        {
            Categories =
            {
                new CategoryDefinition { Key = "validation", DisplayName = "Validation",
                    Style = new MuteStyle { Foreground = "#445566", Italic = true, AutoCollapse = true } },
            },
            Rules =
            {
                new MuteRule
                {
                    Name = "guard",
                    Category = "validation",
                    Kind = RuleKind.RoslynCall,
                    Pattern = new RulePattern { ReceiverTypeGlob = "Guard", MethodNameGlob = "Against*" },
                    Scope = MuteScope.WholeStatement,
                    Excludes = new[]
                    {
                        new ExclusionRule { Name = "keep-critical", Kind = RuleKind.Identifier,
                            Pattern = new RulePattern { Identifier = "AgainstCritical" } },
                    },
                },
            },
            Exclusions =
            {
                new ExclusionRule { Name = "skip-audit", Kind = RuleKind.Identifier,
                    Pattern = new RulePattern { Identifier = "_auditLogger" }, AppliesTo = "*" },
            },
        };

        var json = original.ToJson();
        var round = RuleSet.Parse(json);

        Assert.Contains(round.Categories, c => c.Key == "validation" && c.DisplayName == "Validation");
        Assert.Equal("#445566", round.StyleFor("validation").Foreground);
        Assert.True(round.StyleFor("validation").AutoCollapse);
        Assert.Single(round.Rules);
        Assert.Single(round.Rules[0].Excludes!);
        Assert.Single(round.Exclusions);
    }

    [Fact]
    public void NormalizeCategories_auto_registers_user_categories_referenced_by_rules()
    {
        var rs = RuleSet.Parse("""
            {
              "rules": [
                { "name": "x", "category": "mapping", "kind": "regex", "pattern": { "regex": "AutoMap" }, "scope": "match" }
              ]
            }
            """);

        Assert.Contains(rs.Categories, c => c.Key == "mapping");
    }
}
