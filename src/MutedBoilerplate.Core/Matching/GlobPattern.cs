using System;
using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;

namespace MutedBoilerplate.Core.Matching;

/// <summary>
/// Tiny glob: <c>*</c> = any chars, <c>?</c> = one char, <c>|</c> at top level = alternation.
/// Case-insensitive.
/// </summary>
public static class GlobPattern
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new();

    public static bool IsMatch(string? glob, string? text)
    {
        if (string.IsNullOrEmpty(glob)) return true;
        if (text is null) return false;
        return GetRegex(glob!).IsMatch(text);
    }

    private static Regex GetRegex(string glob) =>
        Cache.GetOrAdd(glob, g => new Regex("^(?:" + Translate(g) + ")$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled));

    private static string Translate(string glob)
    {
        var alts = glob.Split('|');
        var sb = new StringBuilder();
        for (int i = 0; i < alts.Length; i++)
        {
            if (i > 0) sb.Append('|');
            sb.Append(TranslateOne(alts[i]));
        }
        return sb.ToString();
    }

    private static string TranslateOne(string glob)
    {
        var sb = new StringBuilder();
        foreach (var c in glob)
        {
            switch (c)
            {
                case '*': sb.Append(".*"); break;
                case '?': sb.Append('.'); break;
                default:
                    if ("\\.+()[]{}^$".IndexOf(c) >= 0) sb.Append('\\');
                    sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
