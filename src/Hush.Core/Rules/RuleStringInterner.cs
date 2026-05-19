using System.Collections.Concurrent;

namespace Hush.Core.Rules;

// Phase 8: dedupe string references across MuteRules, ExclusionRules,
// CategoryDefinitions and the MuteSpans they produce. JSON deserialization
// allocates a fresh string per property; without interning, 10 roslynCall
// rules with Category="telemetry" would have 10 distinct "telemetry" string
// objects, and every emitted MuteSpan would hold one of those refs. After
// interning, they all point at the same instance — same for RuleName and
// AppliesTo. This is bounded: the cache grows with the total count of
// distinct rule/category/name strings, which is tiny (tens, not millions).
internal static class RuleStringInterner
{
    private static readonly ConcurrentDictionary<string, string> Cache =
        new(System.StringComparer.Ordinal);

    public static string Intern(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return Cache.GetOrAdd(value, value);
    }
}
