using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using MutedBoilerplate.Core.Diagnostics;
using MutedBoilerplate.Core.Model;
using MutedBoilerplate.Core.Rules;

namespace MutedBoilerplate.Core.Matching;

// Phase 2: one tree walk for all Roslyn-based rules (and exclusions) instead of
// one DescendantNodes().OfType<...>() call per rule. On a 50k-line file with
// ~10 rules this collapses ~10 full tree traversals per repaint into one.
//
// The walker is intentionally allocation-light on the hot path:
//  * rule lists pre-filtered by kind (no kind check per node)
//  * no LINQ, no yield return
//  * output list is the caller's buffer
internal sealed class FusedSyntaxWalker : CSharpSyntaxWalker
{
    private readonly IReadOnlyList<MuteRule> _callRules;
    private readonly IReadOnlyList<MuteRule> _identifierRules;
    private readonly IReadOnlyList<MuteRule> _signatureRules;
    private readonly SemanticModel? _semantics;
    private readonly List<(MuteRule, MuteSpan)> _spanOutput;

    private readonly IReadOnlyList<ExclusionRule>? _callExclusions;
    private readonly IReadOnlyList<ExclusionRule>? _identifierExclusions;
    private readonly List<(ExclusionRule, TextSpan)>? _exclusionOutput;
    private readonly TextSpan? _limitRange;

    public FusedSyntaxWalker(
        IReadOnlyList<MuteRule> callRules,
        IReadOnlyList<MuteRule> identifierRules,
        IReadOnlyList<MuteRule> signatureRules,
        SemanticModel? semantics,
        List<(MuteRule, MuteSpan)> spanOutput,
        IReadOnlyList<ExclusionRule>? callExclusions = null,
        IReadOnlyList<ExclusionRule>? identifierExclusions = null,
        List<(ExclusionRule, TextSpan)>? exclusionOutput = null,
        TextSpan? limitRange = null)
    {
        _callRules = callRules;
        _identifierRules = identifierRules;
        _signatureRules = signatureRules;
        _semantics = semantics;
        _spanOutput = spanOutput;
        _callExclusions = callExclusions;
        _identifierExclusions = identifierExclusions;
        _exclusionOutput = exclusionOutput;
        _limitRange = limitRange;
    }

    public static void Walk(
        SyntaxNode root,
        IReadOnlyList<MuteRule> callRules,
        IReadOnlyList<MuteRule> identifierRules,
        IReadOnlyList<MuteRule> signatureRules,
        SemanticModel? semantics,
        List<(MuteRule, MuteSpan)> spanOutput,
        IReadOnlyList<ExclusionRule>? callExclusions = null,
        IReadOnlyList<ExclusionRule>? identifierExclusions = null,
        List<(ExclusionRule, TextSpan)>? exclusionOutput = null,
        TextSpan? limitRange = null)
    {
        PerfCounters.IncrementTreeWalks();
        var w = new FusedSyntaxWalker(
            callRules, identifierRules, signatureRules, semantics, spanOutput,
            callExclusions, identifierExclusions, exclusionOutput, limitRange);
        w.Visit(root);
    }

    // Phase 5: prune the walk to nodes that intersect the incremental dirty
    // range. Roslyn's WithChangedText preserves reference-equal subtrees for
    // unchanged code, so even a by-position filter cuts traversal down to
    // the edited method(s).
    public override void Visit(SyntaxNode? node)
    {
        if (node is null) return;
        if (_limitRange is { } lim && !node.Span.IntersectsWith(lim)) return;
        base.Visit(node);
    }

    public override void VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var hasCallRules = _callRules.Count > 0;
        var hasCallExclusions = _callExclusions is { Count: > 0 };

        if (hasCallRules || hasCallExclusions)
        {
            for (int i = 0; i < _callRules.Count; i++)
            {
                var rule = _callRules[i];
                if (RoslynCallMatcher.TryMatch(node, rule.Pattern, _semantics))
                {
                    var span = ScopeResolver.Resolve(node, rule.Scope);
                    _spanOutput.Add((rule, new MuteSpan(span, rule.Category, rule.Name, rule.Scope)));
                }
            }

            if (hasCallExclusions)
            {
                for (int i = 0; i < _callExclusions!.Count; i++)
                {
                    var ex = _callExclusions[i];
                    if (RoslynCallMatcher.TryMatch(node, ex.Pattern, _semantics))
                    {
                        var stmt = node.FirstAncestorOrSelf<StatementSyntax>();
                        _exclusionOutput!.Add((ex, stmt?.Span ?? node.Span));
                    }
                }
            }
        }

        base.VisitInvocationExpression(node);
    }

    public override void VisitIdentifierName(IdentifierNameSyntax node)
    {
        var hasIdRules = _identifierRules.Count > 0;
        var hasIdExclusions = _identifierExclusions is { Count: > 0 };

        if (hasIdRules || hasIdExclusions)
        {
            var text = node.Identifier.Text;

            for (int i = 0; i < _identifierRules.Count; i++)
            {
                var rule = _identifierRules[i];
                var pattern = rule.Pattern.Identifier;
                if (string.IsNullOrEmpty(pattern)) continue;
                if (GlobPattern.IsMatch(pattern, text))
                {
                    var span = ScopeResolver.Resolve(node, rule.Scope);
                    _spanOutput.Add((rule, new MuteSpan(span, rule.Category, rule.Name, rule.Scope)));
                }
            }

            if (hasIdExclusions)
            {
                for (int i = 0; i < _identifierExclusions!.Count; i++)
                {
                    var ex = _identifierExclusions[i];
                    var pattern = ex.Pattern.Identifier;
                    if (string.IsNullOrEmpty(pattern)) continue;
                    if (GlobPattern.IsMatch(pattern, text))
                    {
                        var stmt = node.FirstAncestorOrSelf<StatementSyntax>();
                        _exclusionOutput!.Add((ex, stmt?.Span ?? node.Span));
                    }
                }
            }
        }

        base.VisitIdentifierName(node);
    }

    public override void VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        VisitMethodLike(node);
        base.VisitMethodDeclaration(node);
    }

    public override void VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        VisitMethodLike(node);
        base.VisitConstructorDeclaration(node);
    }

    public override void VisitDestructorDeclaration(DestructorDeclarationSyntax node)
    {
        VisitMethodLike(node);
        base.VisitDestructorDeclaration(node);
    }

    public override void VisitOperatorDeclaration(OperatorDeclarationSyntax node)
    {
        VisitMethodLike(node);
        base.VisitOperatorDeclaration(node);
    }

    public override void VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
    {
        VisitMethodLike(node);
        base.VisitConversionOperatorDeclaration(node);
    }

    private void VisitMethodLike(BaseMethodDeclarationSyntax method)
    {
        if (_signatureRules.Count == 0) return;
        for (int i = 0; i < _signatureRules.Count; i++)
        {
            var rule = _signatureRules[i];
            if (!SignatureMatcher.ModifiersMatch(method, rule.Pattern)) continue;
            if (!SignatureMatcher.AttributesMatch(method, rule.Pattern)) continue;
            var span = rule.Scope == MuteScope.SignatureRange
                ? ScopeResolver.SignatureSpan(method)
                : ScopeResolver.Resolve(method, rule.Scope);
            _spanOutput.Add((rule, new MuteSpan(span, rule.Category, rule.Name, rule.Scope)));
        }
    }
}
