using System.Collections.Generic;
using Hush.Core.Model;
using Hush.Core.Rules;

namespace Hush.Core.Matching;

public interface IRuleMatcher
{
    string Kind { get; }
    IEnumerable<MuteSpan> Match(MuteRule rule, MatchContext ctx);
}

public interface IExclusionMatcher
{
    string Kind { get; }
    IEnumerable<Microsoft.CodeAnalysis.Text.TextSpan> Match(ExclusionRule rule, MatchContext ctx);
}

public interface IMuteSpanProvider
{
    IEnumerable<MuteSpan> GetSpans(MatchContext ctx, MuteState state, RuleSet ruleSet);
}
