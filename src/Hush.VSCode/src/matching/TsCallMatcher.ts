import * as ts from 'typescript';
import { MuteScope, MuteSpanDto, TsCallRuleDto } from '../sidecar/protocol';
import { globMatch } from './glob';

/**
 * TS-side analogue of `RoslynCallMatcher` for `tsCall` rules. Roslyn is C#-only,
 * so TS/JS/TSX/JSX go through the TypeScript Compiler API in-process instead of
 * the sidecar.
 *
 * Matching is purely syntactic (no semantic resolution): the rule's
 * `receiverTypeGlob` matches the textual *leftmost identifier* of the call's
 * member-access chain, and `methodNameGlob` matches the property name. That's
 * the same heuristic the C# matcher falls back to when no `SemanticModel` is
 * available.
 */
/**
 * Re-export for callers that want a non-DTO name. Matcher consumes the shape
 * straight from the sidecar wire format.
 */
export type TsCallRule = TsCallRuleDto;

export class TsCallMatcher {
  /**
   * Returns muted spans for `tsCall` rules against the given document content.
   * @param content full document text
   * @param languageId VS Code languageId (typescript, typescriptreact, javascript, javascriptreact)
   * @param rules the rule set (already filtered to enabled categories by the caller)
   */
  match(content: string, languageId: string, rules: readonly TsCallRule[]): MuteSpanDto[] {
    if (rules.length === 0) return [];
    const scriptKind = scriptKindFor(languageId);
    if (scriptKind === undefined) return [];

    const sf = ts.createSourceFile(
      'inline' + extensionFor(scriptKind),
      content,
      ts.ScriptTarget.Latest,
      /* setParentNodes */ true,
      scriptKind,
    );

    const spans: MuteSpanDto[] = [];
    const visit = (node: ts.Node) => {
      if (ts.isCallExpression(node)) {
        for (const rule of rules) {
          if (callMatches(node, rule)) {
            const span = resolveScope(node, rule.scope, sf);
            if (span.end > span.start) {
              spans.push({
                start: span.start,
                end: span.end,
                categoryKey: rule.category,
                ruleName: rule.name,
                scope: rule.scope,
              });
            }
            // first-rule-wins: a CallExpression doesn't get double-muted
            break;
          }
        }
      }
      ts.forEachChild(node, visit);
    };
    visit(sf);
    return spans;
  }
}

function callMatches(call: ts.CallExpression, rule: TsCallRule): boolean {
  const expr = call.expression;
  let methodName: string | undefined;
  let receiver: string | undefined;

  if (ts.isIdentifier(expr)) {
    methodName = expr.text;
  } else if (ts.isPropertyAccessExpression(expr)) {
    methodName = expr.name.text;
    receiver = leftmostIdentifier(expr.expression);
  } else {
    return false;
  }

  if (rule.methodNameGlob && !globMatch(rule.methodNameGlob, methodName ?? '')) return false;
  if (rule.receiverGlob && !globMatch(rule.receiverGlob, receiver ?? '')) return false;
  return true;
}

/**
 * Walks a member-access chain down to its leftmost simple identifier. Returns
 * undefined if the chain is rooted in something we can't textually identify
 * (call expressions, element access, `this`, etc.).
 */
function leftmostIdentifier(node: ts.Node): string | undefined {
  let cur: ts.Node = node;
  while (true) {
    if (ts.isIdentifier(cur)) return cur.text;
    if (ts.isPropertyAccessExpression(cur)) { cur = cur.expression; continue; }
    if (ts.isParenthesizedExpression(cur)) { cur = cur.expression; continue; }
    if (ts.isAsExpression(cur)) { cur = cur.expression; continue; }
    if (ts.isNonNullExpression(cur)) { cur = cur.expression; continue; }
    if (ts.isTypeAssertionExpression && ts.isTypeAssertionExpression(cur)) { cur = (cur as ts.TypeAssertion).expression; continue; }
    return undefined;
  }
}

function resolveScope(node: ts.CallExpression, scope: MuteScope, sf: ts.SourceFile): { start: number; end: number } {
  switch (scope) {
    case 'Match':
      return { start: node.getStart(sf), end: node.getEnd() };
    case 'WholeStatement': {
      // Only extend to the outer statement when the call IS the statement
      // (modulo transparent wrappers like `await`, parens, casts). If the
      // call is part of a larger expression — a JSX attribute, an argument
      // to another call, an arrow body returning a value — we stop at the
      // call itself. Otherwise muting `console.log` inside a TSX onClick
      // would grey out the whole `return <div>...</div>` line.
      let cur: ts.Node = node;
      while (cur.parent && isTransparentWrapper(cur.parent, cur)) {
        cur = cur.parent;
      }
      if (cur.parent && cur.parent.kind === ts.SyntaxKind.ExpressionStatement) {
        return { start: cur.parent.getStart(sf), end: cur.parent.getEnd() };
      }
      return { start: node.getStart(sf), end: node.getEnd() };
    }
    case 'ArgumentList': {
      // Range from the opening `(` to the closing `)`, exclusive of method name.
      const open = node.getChildren(sf).find((c) => c.kind === ts.SyntaxKind.OpenParenToken);
      const close = node.getChildren(sf).find((c) => c.kind === ts.SyntaxKind.CloseParenToken);
      if (open && close) return { start: open.getStart(sf), end: close.getEnd() };
      return { start: node.arguments.pos, end: node.arguments.end };
    }
    case 'SignatureRange':
    default:
      return { start: node.getStart(sf), end: node.getEnd() };
  }
}

/**
 * "Transparent" = preserves the call as the structurally-dominant value of
 * an expression. Walking through these doesn't change whether the call IS
 * a statement. `child` is the node we just walked up from — checked to
 * ensure we're on the wrapping path, not a sibling.
 */
function isTransparentWrapper(parent: ts.Node, child: ts.Node): boolean {
  switch (parent.kind) {
    case ts.SyntaxKind.ParenthesizedExpression:
      return (parent as ts.ParenthesizedExpression).expression === child;
    case ts.SyntaxKind.AwaitExpression:
      return (parent as ts.AwaitExpression).expression === child;
    case ts.SyntaxKind.NonNullExpression:
      return (parent as ts.NonNullExpression).expression === child;
    case ts.SyntaxKind.AsExpression:
      return (parent as ts.AsExpression).expression === child;
    case ts.SyntaxKind.TypeAssertionExpression:
      return (parent as ts.TypeAssertion).expression === child;
    case ts.SyntaxKind.VoidExpression:
      return (parent as ts.VoidExpression).expression === child;
    default:
      return false;
  }
}

function scriptKindFor(languageId: string): ts.ScriptKind | undefined {
  switch (languageId) {
    case 'typescript': return ts.ScriptKind.TS;
    case 'typescriptreact': return ts.ScriptKind.TSX;
    case 'javascript': return ts.ScriptKind.JS;
    case 'javascriptreact': return ts.ScriptKind.JSX;
    default: return undefined;
  }
}

function extensionFor(kind: ts.ScriptKind): string {
  switch (kind) {
    case ts.ScriptKind.TS: return '.ts';
    case ts.ScriptKind.TSX: return '.tsx';
    case ts.ScriptKind.JS: return '.js';
    case ts.ScriptKind.JSX: return '.jsx';
    default: return '.ts';
  }
}

export const TS_LANGUAGE_IDS = ['typescript', 'typescriptreact', 'javascript', 'javascriptreact'] as const;
export type TsLanguageId = (typeof TS_LANGUAGE_IDS)[number];

export function isTsLanguage(languageId: string): languageId is TsLanguageId {
  return (TS_LANGUAGE_IDS as readonly string[]).includes(languageId);
}
