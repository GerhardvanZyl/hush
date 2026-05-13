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
            foreach (var span in Detect(method, ctx.Semantics))
                yield return new MuteSpan(span, rule.Category, rule.Name, rule.Scope);
        }
    }

    public static List<TextSpan> Detect(BaseMethodDeclarationSyntax method, SemanticModel? semantics = null)
    {
        var result = new List<TextSpan>();
        var body = method.Body;
        if (body is null) return result;

        var parameters = method.ParameterList?.Parameters;
        if (parameters is null || parameters.Value.Count == 0) return result;

        HashSet<string>? paramNames = null;
        foreach (var p in parameters.Value)
        {
            var name = p.Identifier.Text;
            if (string.IsNullOrEmpty(name)) continue;
            (paramNames ??= new HashSet<string>(StringComparer.Ordinal)).Add(name);
        }
        if (paramNames is null) return result;

        // Walk the entry block of the method. Contiguous guards collapse into a
        // single span; "bare call" statements (telemetry/logging/tracing) are
        // transparent — they don't end detection and don't get folded into the
        // span (their own rules mute them independently). Any other statement
        // ends the entry block.
        int runStart = -1;
        int runEnd = -1;
        foreach (var stmt in body.Statements)
        {
            if (IsGuardStatement(stmt, paramNames, semantics))
            {
                if (runStart < 0) runStart = stmt.SpanStart;
                runEnd = stmt.Span.End;
                continue;
            }
            if (IsBoilerplateCallStatement(stmt))
            {
                if (runStart >= 0)
                {
                    result.Add(TextSpan.FromBounds(runStart, runEnd));
                    runStart = -1;
                    runEnd = -1;
                }
                continue;
            }
            break;
        }

        if (runStart >= 0)
            result.Add(TextSpan.FromBounds(runStart, runEnd));
        return result;
    }

    private static bool IsBoilerplateCallStatement(StatementSyntax stmt)
    {
        if (stmt is not ExpressionStatementSyntax es) return false;
        var inner = es.Expression;
        while (true)
        {
            switch (inner)
            {
                case AwaitExpressionSyntax aw: inner = aw.Expression; continue;
                case ParenthesizedExpressionSyntax p: inner = p.Expression; continue;
            }
            break;
        }
        // Bare invocation (Foo(), x.Foo()) or null-conditional call (x?.Foo()).
        return inner is InvocationExpressionSyntax
            || inner is ConditionalAccessExpressionSyntax;
    }

    private static bool IsGuardStatement(StatementSyntax stmt, HashSet<string> paramNames, SemanticModel? semantics)
    {
        switch (stmt)
        {
            case IfStatementSyntax ifs:
                if (!IsGuardIf(ifs, paramNames)) return false;
                // An if-throw guard that mutates a value or calls a method
                // inline (beyond the throw's constructor and nameof) is doing
                // real work — keep it visible.
                return !InlineWorkDetector.HasInlineWorkOrMutation(ifs, anchor: null, semantics);
            case ExpressionStatementSyntax es:
                return IsGuardExpression(es.Expression, paramNames, semantics);
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

    private static bool IsGuardExpression(ExpressionSyntax expr, HashSet<string> paramNames, SemanticModel? semantics)
    {
        switch (expr)
        {
            case AssignmentExpressionSyntax ax:
                // Only the null-fallback forms qualify: `p ??= X` and `p = p ?? X`
                // where `p` is a parameter. A plain overwrite isn't a guard, and
                // a fallback that does real work (`p ??= GetX()`) stays visible.
                if (!IsNullDefaultAssignmentToParameter(ax, paramNames)) return false;
                return !InlineWorkDetector.HasInlineWorkOrMutation(ax.Right, anchor: null, semantics);
            case InvocationExpressionSyntax inv:
                if (!IsGuardInvocation(inv, paramNames)) return false;
                // The helper call itself is the anchor; any other invocation
                // or assignment inside its arguments is real work.
                return !InlineWorkDetector.HasInlineWorkOrMutation(inv, anchor: inv, semantics);
            case ThrowExpressionSyntax:
                return true;
        }
        return false;
    }

    private static bool IsNullDefaultAssignmentToParameter(AssignmentExpressionSyntax ax, HashSet<string> paramNames)
    {
        if (ax.Left is not IdentifierNameSyntax leftId) return false;
        var leftName = leftId.Identifier.Text;
        if (!paramNames.Contains(leftName)) return false;

        if (ax.IsKind(SyntaxKind.CoalesceAssignmentExpression)) return true;

        if (ax.IsKind(SyntaxKind.SimpleAssignmentExpression)
            && ax.Right is BinaryExpressionSyntax bin
            && bin.IsKind(SyntaxKind.CoalesceExpression)
            && bin.Left is IdentifierNameSyntax coalesceLeft
            && coalesceLeft.Identifier.Text == leftName)
        {
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
