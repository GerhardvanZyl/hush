using System;
using System.Collections.Generic;
using System.Linq;
using MutedBoilerplate.Core.Diagnostics;
using MutedBoilerplate.Core.Model;
using MutedBoilerplate.Core.Rules;

namespace MutedBoilerplate.Core.Matching;

public sealed class MuteSpanProvider : IMuteSpanProvider
{
    private readonly IReadOnlyDictionary<string, IRuleMatcher> _matchers;
    private readonly ExclusionEvaluator _exclusions;

    public MuteSpanProvider(IEnumerable<IRuleMatcher> matchers, IEnumerable<IExclusionMatcher> exclusionMatchers)
    {
        _matchers = matchers.ToDictionary(m => m.Kind, StringComparer.OrdinalIgnoreCase);
        _exclusions = new ExclusionEvaluator(exclusionMatchers);
    }

    public static MuteSpanProvider CreateDefault() => new(
        new IRuleMatcher[]
        {
            new RoslynCallMatcher(),
            new SignatureMatcher(),
            new RegexMatcher(),
            new IdentifierMatcher(),
        },
        new IExclusionMatcher[]
        {
            new RoslynCallExclusionMatcher(),
            new IdentifierExclusionMatcher(),
            new RegexExclusionMatcher(),
        });

    public IEnumerable<MuteSpan> GetSpans(MatchContext ctx, MuteState state, RuleSet ruleSet)
    {
        PerfCounters.IncrementGetSpansCalls();

        var candidates = new List<(MuteRule, MuteSpan)>();
        var anyLocalExcludes = false;

        foreach (var rule in ruleSet.Rules)
        {
            if (!state.IsEnabled(rule.Category)) continue;
            if (!_matchers.TryGetValue(rule.Kind, out var matcher)) continue;
            if (rule.Excludes is { Length: > 0 }) anyLocalExcludes = true;
            foreach (var span in matcher.Match(rule, ctx))
            {
                candidates.Add((rule, span));
            }
        }

        // Short-circuit the evaluator entirely when there's no exclusion work
        // to do. Avoids a full tree walk per exclusion matcher on every call.
        var hasExclusions = state.ExclusionsEnabled
            && (ruleSet.Exclusions.Count > 0 || anyLocalExcludes);

        List<MuteSpan> result;
        if (!hasExclusions)
        {
            result = new List<MuteSpan>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++) result.Add(candidates[i].Item2);
        }
        else
        {
            result = _exclusions.Apply(candidates, ctx, ruleSet).ToList();
        }

        PerfCounters.AddSpansEmitted(result.Count);
        return result;
    }
}
