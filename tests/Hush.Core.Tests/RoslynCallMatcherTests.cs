using System.Linq;
using Hush.Core.Matching;
using Hush.Core.Model;
using Xunit;

namespace Hush.Core.Tests;

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
        var rule = new Hush.Core.Rules.MuteRule
        {
            Name = "console",
            Category = MuteCategory.LoggingKey,
            Kind = Hush.Core.Rules.RuleKind.RoslynCall,
            Pattern = new Hush.Core.Rules.RulePattern { ReceiverTypeGlob = "Console", MethodNameGlob = "WriteLine" },
            Scope = MuteScope.WholeStatement,
        };

        var spans = new RoslynCallMatcher().Match(rule, ctx).ToList();

        Assert.Single(spans);
    }

    [Fact]
    public void Does_not_match_when_argument_calls_a_method_inline()
    {
        // Inline method invocation in the log call's argument list is real
        // work — keep the line visible rather than mute it.
        var code = """
            class C
            {
                private readonly ILogger _logger;
                void M()
                {
                    _logger.LogInformation("count: {C}", ComputeCount());
                }
                int ComputeCount() => 1;
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new RoslynCallMatcher().Match(TestRuleSet.LoggerCallRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Does_not_match_when_argument_calls_method_inside_interpolated_string()
    {
        var code = """
            class C
            {
                private readonly ILogger _logger;
                void M()
                {
                    _logger.LogInformation($"got {ComputeCount()}");
                }
                int ComputeCount() => 1;
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new RoslynCallMatcher().Match(TestRuleSet.LoggerCallRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Does_not_match_when_statement_assigns_a_value()
    {
        var code = """
            class C
            {
                private readonly ILogger _logger;
                string _last = "";
                void M(string s)
                {
                    _logger.LogInformation(_last = s);
                }
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new RoslynCallMatcher().Match(TestRuleSet.LoggerCallRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Does_not_match_when_receiver_chain_contains_inline_call()
    {
        var code = """
            class C
            {
                ILogger GetLogger() => null!;
                void M()
                {
                    GetLogger().LogInformation("hi");
                }
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new RoslynCallMatcher().Match(TestRuleSet.LoggerCallRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Matches_when_arguments_are_only_literals_and_identifiers_and_property_access()
    {
        // No inline method calls or mutations — still mute as before.
        var code = """
            class C
            {
                private readonly ILogger _logger;
                void M(string name, int[] items)
                {
                    _logger.LogInformation("hello {Name} {Count}", name, items.Length);
                }
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new RoslynCallMatcher().Match(TestRuleSet.LoggerCallRule(), ctx).ToList();
        Assert.Single(spans);
    }

    [Fact]
    public void Matches_when_argument_is_nameof()
    {
        // `nameof(...)` is a compile-time literal, not real work.
        var code = """
            class C
            {
                private readonly ILogger _logger;
                void M(string x)
                {
                    _logger.LogInformation("param: {P}", nameof(x));
                }
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new RoslynCallMatcher().Match(TestRuleSet.LoggerCallRule(), ctx).ToList();
        Assert.Single(spans);
    }

    [Fact]
    public void Matches_when_arguments_call_object_metadata_getters()
    {
        // GetType, GetHashCode, ToString are getter-like and treated as free.
        var code = """
            class C
            {
                private readonly ILogger _logger;
                void M(object o)
                {
                    _logger.LogInformation("t={T} h={H} s={S}", o.GetType(), o.GetHashCode(), o.ToString());
                }
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new RoslynCallMatcher().Match(TestRuleSet.LoggerCallRule(), ctx).ToList();
        Assert.Single(spans);
    }

    [Fact]
    public void Does_not_match_when_argument_calls_user_get_prefixed_method()
    {
        // Only BCL `Get*` methods are exempt — a user-defined `GetName()`
        // is unknown work and disqualifies the site.
        var code = """
            class C
            {
                private readonly ILogger _logger;
                void M()
                {
                    _logger.LogInformation("name={N}", GetName());
                }
                string GetName() => "n";
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new RoslynCallMatcher().Match(TestRuleSet.LoggerCallRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Matches_when_argument_calls_whitelisted_bcl_getter_without_semantics()
    {
        // No semantic model — the name-based fallback whitelist still
        // recognizes `GetEnumerator` as a BCL getter.
        var code = """
            class C
            {
                private readonly ILogger _logger;
                void M(System.Collections.Generic.List<int> xs)
                {
                    _logger.LogInformation("e={E}", xs.GetEnumerator());
                }
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new RoslynCallMatcher().Match(TestRuleSet.LoggerCallRule(), ctx).ToList();
        Assert.Single(spans);
    }

    [Fact]
    public void Does_not_match_user_get_method_when_semantics_resolves_to_user_assembly()
    {
        // With semantics, `GetCount()` resolves to user code (not BCL),
        // so it disqualifies even though the name starts with `Get`.
        var code = """
            class C
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;
                void M()
                {
                    _logger.LogInformation("c={C}", GetCount());
                }
                int GetCount() => 1;
            }
            namespace Microsoft.Extensions.Logging
            {
                interface ILogger
                {
                    void LogInformation(string s, params object[] a);
                }
            }
            """;
        var ctx = MatchContext.FromCSharpWithSemantics(code);
        var spans = new RoslynCallMatcher().Match(TestRuleSet.LoggerCallRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Matches_bcl_get_method_when_semantics_resolves_to_system_assembly()
    {
        // With semantics, `obj.GetType()` resolves to System.Object.GetType
        // in System.Private.CoreLib / mscorlib — exempt.
        var code = """
            class C
            {
                private readonly Microsoft.Extensions.Logging.ILogger _logger;
                void M(object o)
                {
                    _logger.LogInformation("t={T}", o.GetType());
                }
            }
            namespace Microsoft.Extensions.Logging
            {
                interface ILogger
                {
                    void LogInformation(string s, params object[] a);
                }
            }
            """;
        var ctx = MatchContext.FromCSharpWithSemantics(code);
        var spans = new RoslynCallMatcher().Match(TestRuleSet.LoggerCallRule(), ctx).ToList();
        Assert.Single(spans);
    }

    [Fact]
    public void Does_not_match_when_argument_calls_non_getter_method()
    {
        // `LoadData()` doesn't fit the getter convention — keep it visible.
        var code = """
            class C
            {
                private readonly ILogger _logger;
                void M()
                {
                    _logger.LogInformation("d={D}", LoadData());
                }
                int LoadData() => 1;
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new RoslynCallMatcher().Match(TestRuleSet.LoggerCallRule(), ctx).ToList();
        Assert.Empty(spans);
    }
}
