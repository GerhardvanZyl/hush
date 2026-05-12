using System.Linq;
using MutedBoilerplate.Core.Matching;
using MutedBoilerplate.Core.Model;
using MutedBoilerplate.Core.Rules;
using Xunit;

namespace MutedBoilerplate.Core.Tests;

public class GuardMatcherTests
{
    private static MuteRule GuardRule() => new()
    {
        Name = "guards",
        Category = MuteCategory.GuardsKey,
        Kind = RuleKind.Guard,
        Pattern = new RulePattern(),
        Scope = MuteScope.Match,
    };

    private static string Snippet(MatchContext ctx, MuteSpan span) =>
        ctx.Text.ToString().Substring(span.Span.Start, span.Span.Length);

    [Fact]
    public void Matches_null_check_if_throw_for_parameter()
    {
        var code = """
            class C
            {
                void M(string x)
                {
                    if (x == null) throw new System.ArgumentNullException(nameof(x));
                    var y = x.Length;
                }
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Single(spans);
        var snippet = Snippet(ctx, spans[0]);
        Assert.Contains("if (x == null)", snippet);
        Assert.DoesNotContain("var y", snippet);
    }

    [Fact]
    public void Matches_is_null_pattern_check()
    {
        var code = """
            class C { void M(string s) { if (s is null) throw new System.ArgumentNullException(); s.Trim(); } }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Single(spans);
        Assert.Contains("is null", Snippet(ctx, spans[0]));
    }

    [Fact]
    public void Matches_range_comparison_guard()
    {
        var code = """
            class C { void M(int count) { if (count < 0) throw new System.ArgumentOutOfRangeException(); Use(count); } void Use(int n){} }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Single(spans);
        Assert.Contains("count < 0", Snippet(ctx, spans[0]));
    }

    [Fact]
    public void Matches_argument_null_exception_throw_if_null()
    {
        var code = """
            class C { void M(string x) { System.ArgumentNullException.ThrowIfNull(x); var y = x.Length; } }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Single(spans);
        Assert.Contains("ThrowIfNull(x)", Snippet(ctx, spans[0]));
    }

    [Fact]
    public void Matches_guard_helper_invocation()
    {
        var code = """
            class C { void M(string name) { Guard.Against.Null(name, nameof(name)); Do(name); } void Do(string s){} }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Single(spans);
        Assert.Contains("Guard.Against.Null(name", Snippet(ctx, spans[0]));
    }

    [Fact]
    public void Matches_null_coalesce_assignment_to_parameter()
    {
        var code = """
            class C { void M(string s) { s ??= "default"; System.Console.WriteLine(s); } }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Single(spans);
        Assert.Contains("s ??=", Snippet(ctx, spans[0]));
    }

    [Fact]
    public void Matches_plain_coalesce_reassignment_to_parameter()
    {
        var code = """
            class C { void M(string s) { s = s ?? "default"; System.Console.WriteLine(s); } }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Single(spans);
        var snippet = Snippet(ctx, spans[0]);
        Assert.Contains("s = s ??", snippet);
        Assert.DoesNotContain("WriteLine", snippet);
    }

    [Fact]
    public void Coalesces_contiguous_guards_into_single_span()
    {
        var code = """
            class C
            {
                void M(string a, string b)
                {
                    if (a == null) throw new System.ArgumentNullException(nameof(a));
                    if (b == null) throw new System.ArgumentNullException(nameof(b));
                    b ??= "";
                    System.Console.WriteLine(a + b);
                }
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Single(spans);
        var snippet = Snippet(ctx, spans[0]);
        Assert.Contains("if (a ==", snippet);
        Assert.Contains("if (b ==", snippet);
        Assert.Contains("b ??=", snippet);
        Assert.DoesNotContain("WriteLine", snippet);
    }

    [Fact]
    public void Stops_at_first_non_guard_statement()
    {
        var code = """
            class C
            {
                void M(string x)
                {
                    var y = 42;
                    if (x == null) throw new System.ArgumentNullException(nameof(x));
                }
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Ignores_if_that_references_only_locals_not_parameters()
    {
        var code = """
            class C
            {
                void M()
                {
                    var local = 0;
                    if (local == 0) throw new System.Exception();
                }
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Ignores_if_without_abort_body()
    {
        var code = """
            class C
            {
                int M(int x)
                {
                    if (x == 0) System.Console.WriteLine("zero");
                    return x + 1;
                }
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Returns_nothing_for_methods_with_no_parameters()
    {
        var code = "class C { void M() { if (true) throw new System.Exception(); } }";
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Returns_nothing_for_methods_without_body()
    {
        var code = "abstract class C { public abstract void M(string s); }";
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Detects_guards_in_constructor()
    {
        var code = """
            class C
            {
                private readonly string _s;
                public C(string s)
                {
                    if (s == null) throw new System.ArgumentNullException(nameof(s));
                    _s = s;
                }
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Single(spans);
        Assert.Contains("if (s ==", Snippet(ctx, spans[0]));
    }

    [Fact]
    public void Does_not_match_if_return_is_interpolated_string()
    {
        // GetLevelName-style dispatcher: each branch returns a computed value, not a guard fallback.
        var code = """
            class C
            {
                public string GetLevelName(Device device)
                {
                    if (device.HasA && device.HasB)
                    {
                        return $"{device.A} - {device.B}";
                    }
                    if (device.HasA)
                    {
                        return $"{device.A}";
                    }
                    return string.Empty;
                }
            }
            class Device { public bool HasA; public bool HasB; public string A; public string B; }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Does_not_match_if_return_invokes_a_method()
    {
        var code = """
            class C
            {
                int M(int x)
                {
                    if (x > 0) { return Compute(x); }
                    return 0;
                }
                int Compute(int x) => x;
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Does_not_match_if_return_dereferences_chain()
    {
        var code = """
            class C
            {
                string M(Holder h)
                {
                    if (h.HasName) { return h.Inner.Name; }
                    return null;
                }
            }
            class Holder { public bool HasName; public Inner Inner; }
            class Inner { public string Name; }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Matches_return_null_guard()
    {
        var code = """
            class C { string M(string x) { if (x == null) return null; return x.Trim(); } }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Single(spans);
        Assert.Contains("return null", Snippet(ctx, spans[0]));
    }

    [Fact]
    public void Matches_return_string_empty_guard()
    {
        var code = """
            class C { string M(string x) { if (x == null) return string.Empty; return x; } }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Single(spans);
        Assert.Contains("string.Empty", Snippet(ctx, spans[0]));
    }

    [Fact]
    public void Matches_return_default_guard()
    {
        var code = """
            class C { int M(int x) { if (x < 0) return default; return x + 1; } }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Single(spans);
        Assert.Contains("return default", Snippet(ctx, spans[0]));
    }

    [Fact]
    public void Matches_return_array_empty_guard()
    {
        var code = """
            class C { int[] M(int[] xs) { if (xs == null) return System.Array.Empty<int>(); return xs; } }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Single(spans);
        Assert.Contains("Array.Empty", Snippet(ctx, spans[0]));
    }

    [Fact]
    public void Matches_return_null_forgiving_guard()
    {
        var code = """
            #nullable enable
            class C { string M(string? x) { if (x == null) return null!; return x; } }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Single(spans);
        Assert.Contains("return null!", Snippet(ctx, spans[0]));
    }

    [Fact]
    public void Default_ruleset_includes_guards_category_and_rule()
    {
        var rs = RuleSet.LoadDefaults();
        Assert.Contains(rs.AllCategories(), c => c.Key == MuteCategory.GuardsKey);
        Assert.Contains(rs.Rules, r => r.Kind == RuleKind.Guard);
    }

    [Fact]
    public void End_to_end_default_rules_emit_guards_span()
    {
        var code = """
            class Service
            {
                public int Process(string input)
                {
                    if (input == null) throw new System.ArgumentNullException(nameof(input));
                    return input.Length;
                }
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var rs = RuleSet.LoadDefaults();
        var spans = MuteSpanProvider.CreateDefault().GetSpans(ctx, TestRuleSet.AllOn(rs), rs).ToList();
        Assert.Contains(spans, s => s.CategoryKey == MuteCategory.GuardsKey);
    }

    [Fact]
    public void Disabling_guards_category_filters_out_guard_spans()
    {
        var code = """
            class Service
            {
                public int Process(string input)
                {
                    if (input == null) throw new System.ArgumentNullException(nameof(input));
                    return input.Length;
                }
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var rs = RuleSet.LoadDefaults();
        var state = TestRuleSet.AllOn(rs);
        state.Set(MuteCategory.GuardsKey, false);
        var spans = MuteSpanProvider.CreateDefault().GetSpans(ctx, state, rs).ToList();
        Assert.DoesNotContain(spans, s => s.CategoryKey == MuteCategory.GuardsKey);
    }
}
