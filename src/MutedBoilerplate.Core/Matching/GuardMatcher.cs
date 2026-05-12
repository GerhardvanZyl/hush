using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MutedBoilerplate.Core.Model;
using MutedBoilerplate.Core.Rules;

namespace MutedBoilerplate.Core.Matching;

/// <summary>
/// Detects guard clauses at the top of a method. A "guard" is a statement that
/// either compares / null-checks an input parameter or assigns an alternative
/// value to one. The detector collapses the contiguous run of guard statements
/// that sit at the top of the method body into a single span.
/// </summary>
public sealed class GuardMatcher : IRuleMatcher
{
    public string Kind => RuleKind.Guard;

    public IEnumerable<MuteSpan> Match(MuteRule rule, MatchContext ctx)
    {
        if (ctx.Tree is null) yield break;
        var root = ctx.Tree.GetRoot();
        foreach (var method in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
        {
            if (TryDetect(method, out var span))
                yield return new MuteSpan(span, rule.Category, rule.Name, rule.Scope);
        }
    }

    public static bool TryDetect(BaseMethodDeclarationSyntax method, out TextSpan span)
    {
        span = default;
        var body = method.Body;
        if (body is null) return false;

        var parameters = method.ParameterList?.Parameters;
        if (parameters is null || parameters.Value.Count == 0) return false;

        HashSet<string>? paramNames = null;
        foreach (var p in parameters.Value)
        {
            var name = p.Identifier.Text;
            if (string.IsNullOrEmpty(name)) continue;
            (paramNames ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
        }
        if (paramNames is null) return false;

        int firstStart = -1;
        int lastEnd = -1;
        foreach (var stmt in body.Statements)
        {
            if (!IsGuardStatement(stmt, paramNames)) break;
            if (firstStart < 0) firstStart = stmt.SpanStart;
            lastEnd = stmt.Span.End;
        }

        if (firstStart < 0) return false;
        span = TextSpan.FromBounds(firstStart, lastEnd);
        return true;
    }

    private static bool IsGuardStatement(StatementSyntax stmt, HashSet<string> paramNames)
    {
        switch (stmt)
        {
            case IfStatementSyntax ifs:
                return IsGuardIf(ifs, paramNames);
            case ExpressionStatementSyntax es:
                return IsGuardExpression(es.Expression, paramNames);
            default:
                return false;
        }
    }

    private static bool IsGuardIf(IfStatementSyntax ifs, HashSet<string> paramNames)
    {
        // The condition must reference at least one input parameter and the body
        // must be control-flow-aborting (throw / return) — otherwise it's plain
        // business logic, not a guard.
        if (!ReferencesParameter(ifs.Condition, paramNames)) return false;
        if (!BodyAborts(ifs.Statement)) return false;
        if (ifs.Else is { Statement: var elseStmt } && !BodyAborts(elseStmt)) return false;
        return true;
    }

    private static bool BodyAborts(StatementSyntax stmt)
    {
        if (stmt is BlockSyntax block)
        {
            foreach (var inner in block.Statements)
                if (IsAbortingStatement(inner)) return true;
            return false;
        }
        return IsAbortingStatement(stmt);
    }

    private static bool IsAbortingStatement(StatementSyntax stmt)
    {
        if (stmt is ThrowStatementSyntax) return true;
        if (stmt is ExpressionStatementSyntax es && es.Expression is ThrowExpressionSyntax) return true;
        if (stmt is ReturnStatementSyntax ret)
            return ret.Expression is null || IsTrivialReturnExpression(ret.Expression);
        return false;
    }

    // A return-based abort only counts as a guard if it returns a trivial fallback
    // (null, default, literal, simple identifier, Type.Member, Empty<T>()). Returning
    // a computed value (interpolation, arithmetic, method call) is business logic, not
    // a guard — see GetLevelName-style methods that just dispatch among returns.
    private static bool IsTrivialReturnExpression(ExpressionSyntax expr)
    {
        // Peel parentheses and the null-forgiving `!` so `return null!` still qualifies.
        while (true)
        {
            switch (expr)
            {
                case ParenthesizedExpressionSyntax p:
                    expr = p.Expression;
                    continue;
                case PostfixUnaryExpressionSyntax pu when pu.OperatorToken.IsKind(SyntaxKind.ExclamationToken):
                    expr = pu.Operand;
                    continue;
            }
            break;
        }

        switch (expr)
        {
            case LiteralExpressionSyntax:
            case DefaultExpressionSyntax:
            case IdentifierNameSyntax:
                return true;
            case MemberAccessExpressionSyntax m:
                // Allow simple `Type.Member` / `param.Member` (depth 1). Reject deeper
                // chains like `device.Location.Name`, which are data dereferences.
                return m.Expression is IdentifierNameSyntax or PredefinedTypeSyntax;
            case InvocationExpressionSyntax inv:
                return IsEmptyFactoryInvocation(inv);
            default:
                return false;
        }
    }

    private static bool IsEmptyFactoryInvocation(InvocationExpressionSyntax inv)
    {
        // Array.Empty<T>(), Enumerable.Empty<T>(), string.Empty (when written as a call) — no-arg "Empty".
        if (inv.ArgumentList is null || inv.ArgumentList.Arguments.Count != 0) return false;
        return GetTrailingName(inv.Expression) == "Empty";
    }

    private static string? GetTrailingName(ExpressionSyntax expr) => expr switch
    {
        MemberAccessExpressionSyntax m => GetSimpleName(m.Name),
        SimpleNameSyntax s => GetSimpleName(s),
        _ => null,
    };

    private static string? GetSimpleName(SimpleNameSyntax name) => name switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax g => g.Identifier.Text,
        _ => null,
    };

    private static bool IsGuardExpression(ExpressionSyntax expr, HashSet<string> paramNames)
    {
        switch (expr)
        {
            case AssignmentExpressionSyntax ax:
                // Assigning an alternative to an input parameter — covers
                // `p = p ?? default`, `p ??= default`, and plain overwrites.
                return IsParameterReference(ax.Left, paramNames);
            case InvocationExpressionSyntax inv:
                return IsGuardInvocation(inv, paramNames);
            case ThrowExpressionSyntax:
                return true;
        }
        return false;
    }

    private static readonly string[] GuardNameFragments =
    {
        "Throw", "Guard", "Require", "Check", "Ensure", "Assert", "Against", "Validate",
    };

    private static bool IsGuardInvocation(InvocationExpressionSyntax inv, HashSet<string> paramNames)
    {
        // Common guard helpers: ArgumentNullException.ThrowIfNull(p),
        // Guard.Against.Null(p), Requires.NotNull(p), Contract.Requires(p != null)…
        // We fuzzy-match on the invocation's method/receiver tokens and require
        // at least one argument to reference an input parameter.
        var hasGuardName = false;
        foreach (var token in inv.Expression.DescendantTokens())
        {
            var t = token.Text;
            if (string.IsNullOrEmpty(t)) continue;
            for (int i = 0; i < GuardNameFragments.Length; i++)
            {
                if (t.IndexOf(GuardNameFragments[i], StringComparison.Ordinal) >= 0)
                {
                    hasGuardName = true;
                    break;
                }
            }
            if (hasGuardName) break;
        }
        if (!hasGuardName) return false;

        if (inv.ArgumentList is null) return false;
        foreach (var arg in inv.ArgumentList.Arguments)
        {
            if (ReferencesParameter(arg.Expression, paramNames)) return true;
        }
        return false;
    }

    private static bool IsParameterReference(ExpressionSyntax expr, HashSet<string> paramNames) =>
        expr is IdentifierNameSyntax idn && paramNames.Contains(idn.Identifier.Text);

    private static bool ReferencesParameter(ExpressionSyntax expr, HashSet<string> paramNames)
    {
        foreach (var node in expr.DescendantNodesAndSelf())
        {
            if (node is IdentifierNameSyntax idn && paramNames.Contains(idn.Identifier.Text))
                return true;
        }
        return false;
    }
}
