using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.Text;
using MutedBoilerplate.Core.Diagnostics;
using MutedBoilerplate.Core.Model;
using MutedBoilerplate.Core.Rules;

namespace MutedBoilerplate.Core.Matching;

public sealed class MuteSpanProvider : IMuteSpanProvider
{
    private readonly IReadOnlyDictionary<string, IRuleMatcher> _matchers;
    private readonly IReadOnlyDictionary<string, IExclusionMatcher> _exclusionMatchers;
    private readonly ExclusionEvaluator _exclusions;

    public MuteSpanProvider(IEnumerable<IRuleMatcher> matchers, IEnumerable<IExclusionMatcher> exclusionMatchers)
    {
        _matchers = matchers.ToDictionary(m => m.Kind, StringComparer.OrdinalIgnoreCase);
        var excMatchers = exclusionMatchers.ToList();
        _exclusionMatchers = excMatchers.ToDictionary(m => m.Kind, StringComparer.OrdinalIgnoreCase);
        _exclusions = new ExclusionEvaluator(excMatchers);
    }

    public static MuteSpanProvider CreateDefault() => new(
        new IRuleMatcher[]
        {
            new RoslynCallMatcher(),
            new SignatureMatcher(),
            new RegexMatcher(),
            new IdentifierMatcher(),
            new GuardMatcher(),
        },
        new IExclusionMatcher[]
        {
            new RoslynCallExclusionMatcher(),
            new IdentifierExclusionMatcher(),
            new RegexExclusionMatcher(),
        });

    public IEnumerable<MuteSpan> GetSpans(MatchContext ctx, MuteState state, RuleSet ruleSet) =>
        GetSpansCore(ctx, state, ruleSet, null);

    // Phase 5: compute only spans that intersect the given range. Used by the
    // incremental path in SnapshotMuteCache — caller merges with shifted
    // pre-existing spans from outside the range. Only correct when exclusions
    // aren't active (an exclusion outside the range could veto a candidate
    // inside). Callers must gate on that.
    public IEnumerable<MuteSpan> GetSpansInRange(MatchContext ctx, MuteState state, RuleSet ruleSet, TextSpan limitRange) =>
        GetSpansCore(ctx, state, ruleSet, limitRange);

    private IEnumerable<MuteSpan> GetSpansCore(MatchContext ctx, MuteState state, RuleSet ruleSet, TextSpan? limitRange)
    {
        PerfCounters.IncrementGetSpansCalls();

        // Partition active rules by kind so the fused walker can dispatch per
        // node without a kind check per rule per node.
        List<MuteRule>? callRules = null;
        List<MuteRule>? identifierRules = null;
        List<MuteRule>? signatureRules = null;
        List<MuteRule>? guardRules = null;
        List<MuteRule>? otherRules = null;
        var anyLocalExcludes = false;

        foreach (var rule in ruleSet.Rules)
        {
            if (!state.IsEnabled(rule.Category)) continue;
            if (rule.Excludes is { Length: > 0 }) anyLocalExcludes = true;

            switch (rule.Kind)
            {
                case RuleKind.RoslynCall: (callRules ??= new()).Add(rule); break;
                case RuleKind.Identifier: (identifierRules ??= new()).Add(rule); break;
                case RuleKind.Signature: (signatureRules ??= new()).Add(rule); break;
                case RuleKind.Guard: (guardRules ??= new()).Add(rule); break;
                default: (otherRules ??= new()).Add(rule); break;
            }
        }

        var hasExclusions = state.ExclusionsEnabled
            && (ruleSet.Exclusions.Count > 0 || anyLocalExcludes);

        List<ExclusionRule>? callExclusions = null;
        List<ExclusionRule>? idExclusions = null;
        List<ExclusionRule>? otherExclusions = null;
        if (hasExclusions && ruleSet.Exclusions.Count > 0)
        {
            foreach (var ex in ruleSet.Exclusions)
            {
                switch (ex.Kind)
                {
                    case RuleKind.RoslynCall: (callExclusions ??= new()).Add(ex); break;
                    case RuleKind.Identifier: (idExclusions ??= new()).Add(ex); break;
                    default: (otherExclusions ??= new()).Add(ex); break;
                }
            }
        }

        var candidates = new List<(MuteRule, MuteSpan)>();
        var globalExclusions = hasExclusions
            ? new List<(ExclusionRule, TextSpan)>()
            : null;

        // One tree walk for all Roslyn-based rules and Roslyn-kind exclusions.
        if (ctx.Tree is not null)
        {
            var needsWalk =
                (callRules?.Count ?? 0) > 0 ||
                (identifierRules?.Count ?? 0) > 0 ||
                (signatureRules?.Count ?? 0) > 0 ||
                (guardRules?.Count ?? 0) > 0 ||
                (callExclusions?.Count ?? 0) > 0 ||
                (idExclusions?.Count ?? 0) > 0;

            if (needsWalk)
            {
                if (hasExclusions)
                    PerfCounters.IncrementExclusionMaterializations();

                FusedSyntaxWalker.Walk(
                    ctx.Tree.GetRoot(),
                    (IReadOnlyList<MuteRule>?)callRules ?? Array.Empty<MuteRule>(),
                    (IReadOnlyList<MuteRule>?)identifierRules ?? Array.Empty<MuteRule>(),
                    (IReadOnlyList<MuteRule>?)signatureRules ?? Array.Empty<MuteRule>(),
                    (IReadOnlyList<MuteRule>?)guardRules ?? Array.Empty<MuteRule>(),
                    ctx.Semantics,
                    candidates,
                    callExclusions,
                    idExclusions,
                    globalExclusions,
                    limitRange);
            }
        }

        // Non-Roslyn rules (regex) — matcher dispatch unchanged.
        if (otherRules is not null)
        {
            for (int i = 0; i < otherRules.Count; i++)
            {
                var rule = otherRules[i];
                if (!_matchers.TryGetValue(rule.Kind, out var matcher)) continue;
                foreach (var span in matcher.Match(rule, ctx))
                {
                    if (limitRange is { } lim && !span.Span.IntersectsWith(lim)) continue;
                    candidates.Add((rule, span));
                }
            }
        }

        if (hasExclusions && otherExclusions is not null)
        {
            for (int i = 0; i < otherExclusions.Count; i++)
            {
                var ex = otherExclusions[i];
                if (!_exclusionMatchers.TryGetValue(ex.Kind, out var matcher)) continue;
                foreach (var span in matcher.Match(ex, ctx))
                    globalExclusions!.Add((ex, span));
            }
        }

        List<MuteSpan> result;
        if (!hasExclusions)
        {
            result = new List<MuteSpan>(candidates.Count);
            for (int i = 0; i < candidates.Count; i++) result.Add(candidates[i].Item2);
        }
        else
        {
            result = _exclusions.ApplyWithGlobals(candidates, globalExclusions!, ctx).ToList();
        }

        PerfCounters.AddSpansEmitted(result.Count);
        return result;
    }
}
