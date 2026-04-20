using Microsoft.CodeAnalysis.Text;

namespace MutedBoilerplate.Core.Model;

public enum MuteScope
{
    Match,
    WholeStatement,
    ArgumentList,
    SignatureRange,
}

public readonly struct MuteSpan
{
    public MuteSpan(TextSpan span, string categoryKey, string ruleName, MuteScope scope)
    {
        Span = span;
        CategoryKey = categoryKey;
        RuleName = ruleName;
        Scope = scope;
    }

    public TextSpan Span { get; }
    public string CategoryKey { get; }
    public string RuleName { get; }
    public MuteScope Scope { get; }

    public override string ToString() =>
        $"{CategoryKey}:{RuleName} [{Span.Start}..{Span.End}) ({Scope})";
}
