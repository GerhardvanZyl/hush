import * as vscode from 'vscode';
import { DecorationManager } from '../decorations/DecorationManager';
import { MuteStateClient } from '../state/MuteStateClient';

/**
 * VS Code's FoldingRangeProvider has no per-range "default collapsed" flag.
 * To mirror VS's auto-collapse behavior, we run `editor.fold` against each AutoCollapse
 * range once per document open, after the first span result lands.
 */
export class AutoFoldOnOpen implements vscode.Disposable {
  private readonly foldedOnce = new Set<string>();
  private readonly disposables: vscode.Disposable[] = [];

  constructor(
    private readonly decorations: DecorationManager,
    private readonly state: MuteStateClient,
    private readonly output: vscode.OutputChannel,
  ) {
    this.disposables.push(
      decorations.onSpansUpdated(({ uri }) => this.foldIfFirstTime(uri)),
      vscode.workspace.onDidCloseTextDocument((doc) => this.foldedOnce.delete(doc.uri.toString())),
    );
  }

  private async foldIfFirstTime(uri: string): Promise<void> {
    if (this.foldedOnce.has(uri)) return;
    const config = vscode.workspace.getConfiguration('mutedBoilerplate');
    if (!config.get<boolean>('autoCollapse', true)) {
      this.foldedOnce.add(uri); // don't try again this session
      return;
    }

    const editor = vscode.window.visibleTextEditors.find((e) => e.document.uri.toString() === uri);
    if (!editor) return;

    const spans = this.decorations.getCachedSpans(uri);
    const lines: number[] = [];
    const seen = new Set<number>();
    for (const s of spans) {
      const style = this.state.styleFor(s.categoryKey);
      if (!style?.autoCollapse) continue;
      if (!this.state.isEnabled(s.categoryKey)) continue;
      const start = editor.document.positionAt(s.start).line;
      const end = editor.document.positionAt(s.end).line;
      if (end > start && !seen.has(start)) {
        seen.add(start);
        lines.push(start);
      }
    }

    this.foldedOnce.add(uri);
    if (lines.length === 0) return;

    try {
      await vscode.commands.executeCommand('editor.fold', { selectionLines: lines });
    } catch (e) {
      this.output.appendLine(`[autofold] editor.fold failed for ${uri}: ${e}`);
    }
  }

  dispose(): void {
    for (const d of this.disposables) d.dispose();
    this.foldedOnce.clear();
  }
}
