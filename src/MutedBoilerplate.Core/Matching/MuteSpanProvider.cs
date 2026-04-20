using System;
using System.Collections.Generic;
using System.Linq;
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
        var candidates = new List<(MuteRule, MuteSpan)>();

        foreach (var rule in ruleSet.Rules)
        {
            if (!state.IsEnabled(rule.Category)) continue;
            if (!_matchers.TryGetValue(rule.Kind, out var matcher)) continue;
            foreach (var span in matcher.Match(rule, ctx))
            {
                candidates.Add((rule, span));
            }
        }

        if (!state.ExclusionsEnabled)
        {
            return candidates.Select(c => c.Item2).ToList();
        }

        return _exclusions.Apply(candidates, ctx, ruleSet).ToList();
    }
}
