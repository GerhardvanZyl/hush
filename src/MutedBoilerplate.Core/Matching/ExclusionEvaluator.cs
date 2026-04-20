using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.Text;
using MutedBoilerplate.Core.Diagnostics;
using MutedBoilerplate.Core.Model;
using MutedBoilerplate.Core.Rules;

namespace MutedBoilerplate.Core.Matching;

public sealed class ExclusionEvaluator
{
    private readonly IReadOnlyDictionary<string, IExclusionMatcher> _matchers;

    public ExclusionEvaluator(IEnumerable<IExclusionMatcher> matchers)
    {
        _matchers = matchers.ToDictionary(m => m.Kind, StringComparer.OrdinalIgnoreCase);
    }

    public IEnumerable<MuteSpan> Apply(
        IEnumerable<(MuteRule rule, MuteSpan span)> candidates,
        MatchContext ctx,
        RuleSet ruleSet)
    {
        var globalSpans = MaterializeExclusions(ruleSet.Exclusions, ctx);

        foreach (var (rule, span) in candidates)
        {
            if (IsExcluded(rule, span, globalSpans, ctx))
                continue;
            yield return span;
        }
    }

    private bool IsExcluded(
        MuteRule rule,
        MuteSpan span,
        List<(ExclusionRule ex, TextSpan span)> globals,
        MatchContext ctx)
    {
        foreach (var (ex, exSpan) in globals)
        {
            if (!Applies(ex, rule)) continue;
            if (Intersects(span.Span, exSpan)) return true;
        }

        if (rule.Excludes is { Length: > 0 })
        {
            foreach (var local in rule.Excludes)
            {
                if (!_matchers.TryGetValue(local.Kind, out var matcher)) continue;
                foreach (var localSpan in matcher.Match(local, ctx))
                {
                    if (Intersects(span.Span, localSpan)) return true;
                }
            }
        }

        return false;
    }

    private List<(ExclusionRule, TextSpan)> MaterializeExclusions(IEnumerable<ExclusionRule> exclusions, MatchContext ctx)
    {
        PerfCounters.IncrementExclusionMaterializations();
        var result = new List<(ExclusionRule, TextSpan)>();
        foreach (var ex in exclusions)
        {
            if (!_matchers.TryGetValue(ex.Kind, out var matcher)) continue;
            foreach (var span in matcher.Match(ex, ctx))
            {
                result.Add((ex, span));
            }
        }
        return result;
    }

    private static bool Applies(ExclusionRule ex, MuteRule rule)
    {
        if (string.IsNullOrEmpty(ex.AppliesTo) || ex.AppliesTo == "*") return true;
        if (string.Equals(ex.AppliesTo, rule.Category, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(ex.AppliesTo, rule.Name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static bool Intersects(TextSpan a, TextSpan b) =>
        a.Start < b.End && b.Start < a.End;
}
