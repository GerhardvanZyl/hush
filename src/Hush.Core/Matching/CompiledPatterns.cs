using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Hush.Core.Diagnostics;
using Hush.Core.Rules;

namespace Hush.Core.Matching;

// Phase 3: compiled-pattern cache. Regexes and globs are expensive to compile
// and the same patterns show up thousands of times per session (one invocation
// of GetSpans against a 5k-line file = 10 rules × several tree walks). Process-
// wide ConcurrentDictionary by pattern string — patterns are never mutated, so
// keying on the string is safe and lets reloads re-use already-compiled regexes.
public static class CompiledPatterns
{
    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();

    private const RegexOptions DefaultOptions =
        RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.Compiled;

    public static Regex GetRegex(string pattern)
    {
        if (RegexCache.TryGetValue(pattern, out var cached))
        {
            PerfCounters.IncrementRegexCacheHits();
            return cached;
        }

        return RegexCache.GetOrAdd(pattern, p =>
        {
            PerfCounters.IncrementRegexCompiles();
            return new Regex(p, DefaultOptions);
        });
    }

    // Precompile every regex and glob referenced by a RuleSet so first paint pays
    // nothing. Call from MuteStateService on load and on ReloadFromOptions.
    public static void Warmup(RuleSet ruleSet)
    {
        foreach (var rule in ruleSet.Rules)
        {
            WarmPattern(rule.Kind, rule.Pattern);
            if (rule.Excludes != null)
            {
                foreach (var ex in rule.Excludes)
                    WarmPattern(ex.Kind, ex.Pattern);
            }
        }

        foreach (var ex in ruleSet.Exclusions)
            WarmPattern(ex.Kind, ex.Pattern);
    }

    private static void WarmPattern(string kind, RulePattern pattern)
    {
        if (kind == RuleKind.Regex && !string.IsNullOrEmpty(pattern.Regex))
            GetRegex(pattern.Regex!);

        if (!string.IsNullOrEmpty(pattern.MethodNameGlob))
            GlobPattern.IsMatch(pattern.MethodNameGlob, string.Empty);
        if (!string.IsNullOrEmpty(pattern.ReceiverTypeGlob))
            GlobPattern.IsMatch(pattern.ReceiverTypeGlob, string.Empty);
        if (!string.IsNullOrEmpty(pattern.Identifier))
            GlobPattern.IsMatch(pattern.Identifier, string.Empty);

        if (pattern.AttributesAny != null)
        {
            foreach (var a in pattern.AttributesAny)
                if (!string.IsNullOrEmpty(a))
                    GlobPattern.IsMatch(a, string.Empty);
        }
    }
}
