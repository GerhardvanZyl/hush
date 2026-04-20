using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MutedBoilerplate.Core.Model;

namespace MutedBoilerplate.Core.Matching;

internal static class ScopeResolver
{
    public static TextSpan Resolve(SyntaxNode node, MuteScope scope)
    {
        switch (scope)
        {
            case MuteScope.WholeStatement:
            {
                var stmt = node.FirstAncestorOrSelf<StatementSyntax>();
                return stmt?.Span ?? node.Span;
            }
            case MuteScope.ArgumentList:
            {
                var inv = node.FirstAncestorOrSelf<InvocationExpressionSyntax>();
                return inv?.ArgumentList?.Span ?? node.Span;
            }
            case MuteScope.SignatureRange:
            {
                var method = node.FirstAncestorOrSelf<BaseMethodDeclarationSyntax>();
                if (method is null) return node.Span;
                return SignatureSpan(method);
            }
            case MuteScope.Match:
            default:
                return node.Span;
        }
    }

    public static TextSpan SignatureSpan(BaseMethodDeclarationSyntax method)
    {
        var start = method.AttributeLists.Count > 0
            ? method.AttributeLists[0].SpanStart
            : (method.Modifiers.Count > 0 ? method.Modifiers[0].SpanStart : method.SpanStart);

        int end;
        if (method.Body is { } body) end = body.SpanStart;
        else if (method.ExpressionBody is { } eb) end = eb.SpanStart;
        else if (method.ParameterList is { } pl) end = pl.Span.End;
        else end = method.Span.End;

        return TextSpan.FromBounds(start, end);
    }
}
