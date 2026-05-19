import * as vscode from 'vscode';
import { DecorationManager } from '../decorations/DecorationManager';
import { MuteStateClient } from '../state/MuteStateClient';

/**
 * Surfaces muted regions with AutoCollapse=true as foldable ranges. Reads the cached spans
 * populated by `DecorationManager.refresh` — does NOT call getSpans on its own, since VS Code
 * invokes the provider on cadence and we don't want every fold-tooltip-hover to hit the sidecar.
 * Mirrors `HushOutliningTagger` from the VS extension.
 */
export class HushFoldingRangeProvider implements vscode.FoldingRangeProvider {
  private readonly _onDidChange = new vscode.EventEmitter<void>();
  readonly onDidChangeFoldingRanges = this._onDidChange.event;

  constructor(
    private readonly decorations: DecorationManager,
    private readonly state: MuteStateClient,
  ) {
    decorations.onSpansUpdated(() => this._onDidChange.fire());
    state.onChanged(() => this._onDidChange.fire());
  }

  provideFoldingRanges(doc: vscode.TextDocument): vscode.ProviderResult<vscode.FoldingRange[]> {
    const spans = this.decorations.getCachedSpans(doc.uri.toString());
    if (spans.length === 0) return [];
    const ranges: vscode.FoldingRange[] = [];
    for (const s of spans) {
      const style = this.state.styleFor(s.categoryKey);
      if (!style?.autoCollapse) continue;
      if (!this.state.isEnabled(s.categoryKey)) continue;
      const start = doc.positionAt(s.start).line;
      const end = doc.positionAt(s.end).line;
      if (end > start) ranges.push(new vscode.FoldingRange(start, end, vscode.FoldingRangeKind.Region));
    }
    return ranges;
  }

  dispose(): void {
    this._onDidChange.dispose();
  }
}
