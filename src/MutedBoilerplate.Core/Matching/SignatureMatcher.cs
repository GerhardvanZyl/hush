using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MutedBoilerplate.Core.Model;
using MutedBoilerplate.Core.Rules;

namespace MutedBoilerplate.Core.Matching;

public sealed class SignatureMatcher : IRuleMatcher
{
    public string Kind => RuleKind.Signature;

    public IEnumerable<MuteSpan> Match(MuteRule rule, MatchContext ctx)
    {
        if (ctx.Tree is null) yield break;
        var root = ctx.Tree.GetRoot();
        var pattern = rule.Pattern;

        foreach (var method in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
        {
            if (!ModifiersMatch(method, pattern)) continue;
            if (!AttributesMatch(method, pattern)) continue;

            var span = rule.Scope == MuteScope.SignatureRange
                ? ScopeResolver.SignatureSpan(method)
                : ScopeResolver.Resolve(method, rule.Scope);

            yield return new MuteSpan(span, rule.Category, rule.Name, rule.Scope);
        }
    }

    internal static bool ModifiersMatch(BaseMethodDeclarationSyntax method, RulePattern p)
    {
        if (p.ModifiersAny is null || p.ModifiersAny.Length == 0) return true;
        // Avoid LINQ here: called for every method in every tree walk.
        var modifiers = method.Modifiers;
        for (int i = 0; i < p.ModifiersAny.Length; i++)
        {
            var want = p.ModifiersAny[i];
            for (int j = 0; j < modifiers.Count; j++)
            {
                if (modifiers[j].Text == want) return true;
            }
        }
        return false;
    }

    internal static bool AttributesMatch(BaseMethodDeclarationSyntax method, RulePattern p)
    {
        if (p.AttributesAny is null || p.AttributesAny.Length == 0) return true;
        foreach (var list in method.AttributeLists)
        {
            foreach (var attr in list.Attributes)
            {
                var name = attr.Name.ToString();
                for (int i = 0; i < p.AttributesAny.Length; i++)
                {
                    if (GlobPattern.IsMatch(p.AttributesAny[i], name)) return true;
                }
            }
        }
        return false;
    }
}
