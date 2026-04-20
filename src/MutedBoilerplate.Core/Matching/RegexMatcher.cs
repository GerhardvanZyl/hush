using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.Text;
using MutedBoilerplate.Core.Model;
using MutedBoilerplate.Core.Rules;

namespace MutedBoilerplate.Core.Matching;

public sealed class RegexMatcher : IRuleMatcher
{
    public string Kind => RuleKind.Regex;

    public IEnumerable<MuteSpan> Match(MuteRule rule, MatchContext ctx)
    {
        var pattern = rule.Pattern.Regex;
        if (string.IsNullOrEmpty(pattern)) yield break;

        var regex = new Regex(pattern!,
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        var text = ctx.Text.ToString();
        foreach (Match m in regex.Matches(text))
        {
            var span = TextSpan.FromBounds(m.Index, m.Index + m.Length);
            // For text-only contexts WholeStatement falls back to "the whole matched line".
            if (rule.Scope == MuteScope.WholeStatement)
            {
                var line = ctx.Text.Lines.GetLineFromPosition(m.Index);
                span = TextSpan.FromBounds(line.Start, line.EndIncludingLineBreak);
            }
            yield return new MuteSpan(span, rule.Category, rule.Name, rule.Scope);
        }
    }
}

public sealed class RegexExclusionMatcher : IExclusionMatcher
{
    public string Kind => RuleKind.Regex;

    public IEnumerable<TextSpan> Match(ExclusionRule rule, MatchContext ctx)
    {
        var pattern = rule.Pattern.Regex;
        if (string.IsNullOrEmpty(pattern)) yield break;

        var regex = new Regex(pattern!,
            RegexOptions.Multiline | RegexOptions.CultureInvariant);

        var text = ctx.Text.ToString();
        foreach (Match m in regex.Matches(text))
        {
            yield return TextSpan.FromBounds(m.Index, m.Index + m.Length);
        }
    }
}
