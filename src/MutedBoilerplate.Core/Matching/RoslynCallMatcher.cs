using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MutedBoilerplate.Core.Model;
using MutedBoilerplate.Core.Rules;

namespace MutedBoilerplate.Core.Matching;

public sealed class RoslynCallMatcher : IRuleMatcher
{
    public string Kind => RuleKind.RoslynCall;

    public IEnumerable<MuteSpan> Match(MuteRule rule, MatchContext ctx)
    {
        if (ctx.Tree is null) yield break;
        var root = ctx.Tree.GetRoot();
        var pattern = rule.Pattern;

        foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!TryMatch(inv, pattern, ctx.Semantics)) continue;
            var span = ScopeResolver.Resolve(inv, rule.Scope);
            yield return new MuteSpan(span, rule.Category, rule.Name, rule.Scope);
        }
    }

    internal static bool TryMatch(InvocationExpressionSyntax inv, RulePattern pattern, SemanticModel? semantics)
    {
        if (!TryGetMethodAndReceiver(inv, out var methodName, out var receiverName))
            return false;

        if (!GlobPattern.IsMatch(pattern.MethodNameGlob, methodName))
            return false;

        if (string.IsNullOrEmpty(pattern.ReceiverTypeGlob))
            return true;

        // Prefer semantic info when present.
        if (semantics is not null)
        {
            var sym = semantics.GetSymbolInfo(inv.Expression).Symbol as IMethodSymbol;
            var containing = sym?.ContainingType?.Name;
            if (!string.IsNullOrEmpty(containing) && GlobPattern.IsMatch(pattern.ReceiverTypeGlob, containing))
                return true;

            // For instance receivers, also check declared type of the receiver.
            if (inv.Expression is MemberAccessExpressionSyntax mae)
            {
                var typeInfo = semantics.GetTypeInfo(mae.Expression);
                var typeName = typeInfo.Type?.Name;
                if (!string.IsNullOrEmpty(typeName) && GlobPattern.IsMatch(pattern.ReceiverTypeGlob, typeName))
                    return true;
            }

            return false;
        }

        // Heuristic without semantic model.
        if (GlobPattern.IsMatch(pattern.ReceiverTypeGlob, receiverName)) return true;
        var pascal = ToPascal(receiverName);
        if (GlobPattern.IsMatch(pattern.ReceiverTypeGlob, pascal)) return true;
        if (GlobPattern.IsMatch(pattern.ReceiverTypeGlob, "I" + pascal)) return true;
        return false;
    }

    private static bool TryGetMethodAndReceiver(InvocationExpressionSyntax inv, out string methodName, out string receiverName)
    {
        methodName = "";
        receiverName = "";

        switch (inv.Expression)
        {
            case MemberAccessExpressionSyntax mae:
                methodName = mae.Name.Identifier.Text;
                receiverName = LeftmostIdentifier(mae.Expression);
                return !string.IsNullOrEmpty(methodName);

            case MemberBindingExpressionSyntax mbe:
                // Null-conditional invocation, e.g. `activity?.SetTag(...)`. The receiver
                // sits on the enclosing ConditionalAccessExpressionSyntax — walk up to it.
                methodName = mbe.Name.Identifier.Text;
                var conditional = inv.FirstAncestorOrSelf<ConditionalAccessExpressionSyntax>();
                if (conditional?.Expression is { } recv)
                    receiverName = LeftmostIdentifier(recv);
                return !string.IsNullOrEmpty(methodName);

            case IdentifierNameSyntax id:
                methodName = id.Identifier.Text;
                return !string.IsNullOrEmpty(methodName);

            case GenericNameSyntax gn:
                methodName = gn.Identifier.Text;
                return !string.IsNullOrEmpty(methodName);

            default:
                return false;
        }
    }

    private static string LeftmostIdentifier(ExpressionSyntax expr) => expr switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        MemberAccessExpressionSyntax mae => LeftmostIdentifier(mae.Expression),
        InvocationExpressionSyntax inv => LeftmostIdentifier(inv.Expression),
        _ => "",
    };

    internal static string ToPascal(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;
        var s = name.TrimStart('_');
        if (s.Length == 0) return s;
        return char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
