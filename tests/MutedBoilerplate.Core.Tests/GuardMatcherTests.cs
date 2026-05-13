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
    public void Does_not_match_null_coalesce_assignment_to_parameter()
    {
        // `s ??= "default"` mutates the parameter — leave it visible rather
        // than fold it into the guard span.
        var code = """
            class C { void M(string s) { s ??= "default"; System.Console.WriteLine(s); } }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Does_not_match_plain_coalesce_reassignment_to_parameter()
    {
        // `s = s ?? "default"` is a mutation; same reasoning as the ??= case.
        var code = """
            class C { void M(string s) { s = s ?? "default"; System.Console.WriteLine(s); } }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Empty(spans);
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
        Assert.DoesNotContain("WriteLine", snippet);
    }

    [Fact]
    public void Assignment_form_mutation_ends_the_guard_run()
    {
        // `b ??= ""` is no longer a guard — it mutates. The two preceding
        // if-throw guards are still coalesced, and detection stops there
        // (the mutation isn't a transparent boilerplate call either).
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
        Assert.DoesNotContain("??=", snippet);
        Assert.DoesNotContain("WriteLine", snippet);
    }

    [Fact]
    public void Matches_guard_after_leading_logging_call()
    {
        var code = """
            class C
            {
                void M(string x)
                {
                    System.Console.WriteLine("entering");
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
        Assert.DoesNotContain("WriteLine", snippet);
        Assert.DoesNotContain("var y", snippet);
    }

    [Fact]
    public void Matches_guard_after_multiple_leading_logging_and_telemetry_calls()
    {
        var code = """
            class C
            {
                void M(string p)
                {
                    _logger.LogInformation("entering with {p}", p);
                    _activity?.SetTag("p", p);
                    if (p == null) throw new System.ArgumentNullException(nameof(p));
                    Use(p);
                }
                void Use(string s) {}
                System.Diagnostics.Activity? _activity;
                Microsoft.Extensions.Logging.ILogger _logger = null!;
            }
            namespace Microsoft.Extensions.Logging { interface ILogger { void LogInformation(string s, params object[] a); } }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Single(spans);
        var snippet = Snippet(ctx, spans[0]);
        Assert.Contains("if (p == null)", snippet);
        Assert.DoesNotContain("LogInformation", snippet);
        Assert.DoesNotContain("SetTag", snippet);
    }

    [Fact]
    public void Emits_separate_spans_for_guards_separated_by_logging()
    {
        var code = """
            class C
            {
                void M(string a, string b)
                {
                    if (a == null) throw new System.ArgumentNullException(nameof(a));
                    System.Console.WriteLine("between");
                    if (b == null) throw new System.ArgumentNullException(nameof(b));
                    var x = 1;
                }
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Equal(2, spans.Count);
        Assert.Contains("if (a ==", Snippet(ctx, spans[0]));
        Assert.Contains("if (b ==", Snippet(ctx, spans[1]));
        Assert.DoesNotContain("WriteLine", Snippet(ctx, spans[0]));
        Assert.DoesNotContain("WriteLine", Snippet(ctx, spans[1]));
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
    public void Does_not_match_return_array_empty_guard()
    {
        // `Array.Empty<int>()` is a method call. Under the "don't mute lines
        // that call a method inline" rule this no longer counts as a guard,
        // even though it's effectively a literal — keep it visible.
        var code = """
            class C { int[] M(int[] xs) { if (xs == null) return System.Array.Empty<int>(); return xs; } }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Empty(spans);
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

    [Fact]
    public void Does_not_match_guard_helper_with_inline_method_argument()
    {
        // `ResolveParamName()` is real work — keep the guard visible.
        var code = """
            class C
            {
                void M(string name)
                {
                    Guard.Against.Null(name, ResolveParamName());
                    Do(name);
                }
                string ResolveParamName() => "name";
                void Do(string s) {}
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Does_not_match_if_throw_when_exception_constructor_calls_method_inline()
    {
        var code = """
            class C
            {
                void M(string x)
                {
                    if (x == null) throw new System.ArgumentNullException(ResolveParamName());
                    Use(x);
                }
                string ResolveParamName() => "x";
                void Use(string s) {}
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Does_not_match_guard_helper_with_user_get_method_argument()
    {
        // Only BCL `Get*` methods are exempt. A user-defined `h.GetName()`
        // is unknown work and disqualifies the guard.
        var code = """
            class C
            {
                void M(Holder h)
                {
                    Guard.Against.Null(h, h.GetName());
                    Do(h);
                }
                void Do(Holder h) {}
            }
            class Holder { public string GetName() => "x"; }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Empty(spans);
    }

    [Fact]
    public void Matches_if_throw_when_constructor_args_use_getter()
    {
        // `obj.GetType().Name` — GetType is exempt, .Name is property access.
        var code = """
            class C
            {
                void M(object x)
                {
                    if (x == null) throw new System.ArgumentNullException(x.GetType().Name);
                    Use(x);
                }
                void Use(object o) {}
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Single(spans);
        Assert.Contains("if (x == null)", Snippet(ctx, spans[0]));
    }

    [Fact]
    public void Does_not_match_if_throw_when_body_mutates()
    {
        var code = """
            class C
            {
                int _errors;
                void M(string x)
                {
                    if (x == null) { _errors++; throw new System.ArgumentNullException(nameof(x)); }
                    Use(x);
                }
                void Use(string s) {}
            }
            """;
        var ctx = MatchContext.FromCSharp(code);
        var spans = new GuardMatcher().Match(GuardRule(), ctx).ToList();
        Assert.Empty(spans);
    }
}
