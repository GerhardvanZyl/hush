using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Hush.Core.Matching;

// Decides whether a candidate mute site does "real work" the user probably wants
// to keep visible. A site is impure when it mutates a value (assignment, ++/--)
// or invokes a method other than the muted anchor itself. The following calls
// are treated as "free" and don't disqualify the site:
//   * `nameof(...)` — compile-time literal
//   * `ToString` — the canonical formatter used pervasively in log messages
//   * `Get*` methods that come from the .NET BCL — GetType, GetHashCode,
//     GetEnumerator, GetAwaiter, GetValueOrDefault, ... User-defined Get*
//     methods are NOT exempt: their behavior is unknown and the user wants
//     real work to stay visible.
// Property access is already exempt because it's `MemberAccessExpression`,
// not `InvocationExpression`. Object creation (`new Foo(...)`) is also exempt
// — without it `throw new ...` would never qualify as a guard — but the
// detector still descends into the constructor's argument list.
internal static class InlineWorkDetector
{
    public static bool HasInlineWorkOrMutation(
        SyntaxNode root,
        InvocationExpressionSyntax? anchor,
        SemanticModel? semantics = null)
    {
        foreach (var node in root.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case AssignmentExpressionSyntax:
                    return true;
                case PostfixUnaryExpressionSyntax pu
                    when pu.IsKind(SyntaxKind.PostIncrementExpression)
                      || pu.IsKind(SyntaxKind.PostDecrementExpression):
                    return true;
                case PrefixUnaryExpressionSyntax pre
                    when pre.IsKind(SyntaxKind.PreIncrementExpression)
                      || pre.IsKind(SyntaxKind.PreDecrementExpression):
                    return true;
                case InvocationExpressionSyntax inv when inv != anchor && !IsExempt(inv, semantics):
                    return true;
            }
        }
        return false;
    }

    private static bool IsExempt(InvocationExpressionSyntax inv, SemanticModel? semantics)
    {
        // `nameof` is contextual — only the bare-identifier form counts.
        if (inv.Expression is IdentifierNameSyntax { Identifier.Text: "nameof" })
            return true;

        var name = InvokedSimpleName(inv);
        if (name.Length == 0) return false;
        if (name == "ToString") return true;

        if (name.Length >= 3 && name[0] == 'G' && name[1] == 'e' && name[2] == 't')
            return IsBclGetMethod(inv, name, semantics);

        return false;
    }

    private static bool IsBclGetMethod(
        InvocationExpressionSyntax inv,
        string name,
        SemanticModel? semantics)
    {
        // Prefer semantic resolution: a `Get*` call is exempt iff its method
        // symbol lives in a BCL assembly. This correctly classifies user-defined
        // overrides of BCL methods as BCL (overridden methods chain back to the
        // base, but the containing assembly of the override is the user's — so
        // we additionally walk the OverriddenMethod chain).
        if (semantics is not null)
        {
            var sym = semantics.GetSymbolInfo(inv.Expression).Symbol as IMethodSymbol;
            while (sym is not null)
            {
                if (sym.ContainingAssembly is { } asm && IsBclAssembly(asm.Identity.Name))
                    return true;
                sym = sym.OverriddenMethod;
            }
            // Symbol resolved but isn't BCL — definitively user code, not exempt.
            // Symbol unresolved (null on first iteration) — fall through to the
            // name whitelist so we don't over-flag when semantics are stale.
            if (semantics.GetSymbolInfo(inv.Expression).Symbol is not null) return false;
        }

        return BclGetMethodNames.Contains(name);
    }

    private static readonly HashSet<string> BclGetMethodNames = new(StringComparer.Ordinal)
    {
        "GetType",
        "GetHashCode",
        "GetEnumerator",
        "GetAsyncEnumerator",
        "GetAwaiter",
        "GetValueOrDefault",
        "GetLength",
        "GetLongLength",
        "GetUpperBound",
        "GetLowerBound",
    };

    private static bool IsBclAssembly(string asmName)
    {
        if (string.IsNullOrEmpty(asmName)) return false;
        if (asmName == "mscorlib") return true;
        if (asmName == "netstandard") return true;
        if (asmName == "System") return true;
        if (asmName == "System.Private.CoreLib") return true;
        return asmName.StartsWith("System.", StringComparison.Ordinal);
    }

    private static string InvokedSimpleName(InvocationExpressionSyntax inv) => inv.Expression switch
    {
        IdentifierNameSyntax id => id.Identifier.Text,
        GenericNameSyntax gn => gn.Identifier.Text,
        MemberAccessExpressionSyntax mae => mae.Name.Identifier.Text,
        MemberBindingExpressionSyntax mbe => mbe.Name.Identifier.Text,
        _ => string.Empty,
    };
}
