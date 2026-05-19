using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Hush.Core.Model;
using Hush.Core.Rules;

namespace Hush.Core.Matching;

public sealed class IdentifierMatcher : IRuleMatcher
{
    public string Kind => RuleKind.Identifier;

    public IEnumerable<MuteSpan> Match(MuteRule rule, MatchContext ctx)
    {
        if (ctx.Tree is null) yield break;
        var pattern = rule.Pattern.Identifier;
        if (string.IsNullOrEmpty(pattern)) yield break;

        foreach (var node in EnumerateMatches(ctx.Tree.GetRoot(), pattern!))
        {
            var span = ScopeResolver.Resolve(node, rule.Scope);
            yield return new MuteSpan(span, rule.Category, rule.Name, rule.Scope);
        }
    }

    internal static IEnumerable<SyntaxNode> EnumerateMatches(SyntaxNode root, string pattern)
    {
        foreach (var id in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (GlobPattern.IsMatch(pattern, id.Identifier.Text))
                yield return id;
        }
    }
}

public sealed class IdentifierExclusionMatcher : IExclusionMatcher
{
    public string Kind => RuleKind.Identifier;

    public IEnumerable<TextSpan> Match(ExclusionRule rule, MatchContext ctx)
    {
        if (ctx.Tree is null) yield break;
        var pattern = rule.Pattern.Identifier;
        if (string.IsNullOrEmpty(pattern)) yield break;

        foreach (var node in IdentifierMatcher.EnumerateMatches(ctx.Tree.GetRoot(), pattern!))
        {
            // Exclusion span = the enclosing statement so a single matching identifier vetoes
            // the entire statement-scoped candidate it sits inside.
            var stmt = node.FirstAncestorOrSelf<StatementSyntax>();
            yield return stmt?.Span ?? node.Span;
        }
    }
}

public sealed class RoslynCallExclusionMatcher : IExclusionMatcher
{
    public string Kind => RuleKind.RoslynCall;

    public IEnumerable<TextSpan> Match(ExclusionRule rule, MatchContext ctx)
    {
        if (ctx.Tree is null) yield break;
        foreach (var inv in ctx.Tree.GetRoot().DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (RoslynCallMatcher.TryMatch(inv, rule.Pattern, ctx.Semantics))
            {
                var stmt = inv.FirstAncestorOrSelf<StatementSyntax>();
                yield return stmt?.Span ?? inv.Span;
            }
        }
    }
}
