import * as vscode from 'vscode';
import { themeColorIdForCategory } from '../colors/builtInColors';
import { GetSpansResponse, MuteSpanDto, MuteStyleDto } from '../sidecar/protocol';
import { SidecarClient } from '../sidecar/SidecarClient';
import { MuteStateClient } from '../state/MuteStateClient';
import { UserSlotMap } from '../state/UserSlotMap';

/**
 * Renders MuteSpan[] results as TextEditor decorations. One DecorationType per category
 * (built-ins use ThemeColor, user categories fall back to the literal hex from MuteStyle).
 * Mirrors `MutedClassifier` from the VS extension.
 */
export class DecorationManager implements vscode.Disposable {
  private readonly typesByCategory = new Map<string, vscode.TextEditorDecorationType>();
  private readonly cachedSpans = new Map<string, MuteSpanDto[]>();
  private readonly debounceHandles = new Map<string, NodeJS.Timeout>();
  private debounceMs: number;
  private readonly _onSpansUpdated = new vscode.EventEmitter<{ uri: string; spans: MuteSpanDto[] }>();
  readonly onSpansUpdated = this._onSpansUpdated.event;

  constructor(
    private readonly sidecar: SidecarClient,
    private readonly state: MuteStateClient,
    private readonly userSlots: UserSlotMap,
    private readonly output: vscode.OutputChannel,
  ) {
    this.debounceMs = vscode.workspace.getConfiguration('mutedBoilerplate').get<number>('debounceMs', 200);
  }

  setDebounceMs(ms: number) { this.debounceMs = Math.max(0, ms); }

  refreshAllVisibleEditors(): void {
    for (const editor of vscode.window.visibleTextEditors) {
      this.scheduleRefresh(editor.document, 0);
    }
  }

  /** Drop all per-document caches and decoration types, e.g., on rule reload. */
  invalidateAll(): void {
    this.cachedSpans.clear();
    for (const type of this.typesByCategory.values()) type.dispose();
    this.typesByCategory.clear();
  }

  scheduleRefresh(doc: vscode.TextDocument, delayMs = this.debounceMs): void {
    const key = doc.uri.toString();
    const existing = this.debounceHandles.get(key);
    if (existing) clearTimeout(existing);
    this.debounceHandles.set(key, setTimeout(() => {
      this.debounceHandles.delete(key);
      void this.refresh(doc);
    }, delayMs));
  }

  async refresh(doc: vscode.TextDocument): Promise<MuteSpanDto[]> {
    let response: GetSpansResponse;
    try {
      response = await this.sidecar.getSpans({ uri: doc.uri.toString(), version: doc.version });
    } catch (e) {
      this.output.appendLine(`[decorations] getSpans failed for ${doc.uri.toString()}: ${e}`);
      return [];
    }

    // Stale response — older than current state/ruleset, drop on the floor.
    if (response.stateVersion < this.state.stateVersion || response.ruleSetVersion < this.state.ruleSetVersion) {
      return [];
    }

    // Stale snapshot — document moved on while sidecar was computing.
    if (response.version !== doc.version) return [];

    const spans = response.spans;
    this.cachedSpans.set(doc.uri.toString(), spans);
    this.applyToVisibleEditors(doc, spans);
    this._onSpansUpdated.fire({ uri: doc.uri.toString(), spans });
    return spans;
  }

  getCachedSpans(uri: string): readonly MuteSpanDto[] {
    return this.cachedSpans.get(uri) ?? [];
  }

  clearCachedSpans(uri: string): void {
    this.cachedSpans.delete(uri);
  }

  private applyToVisibleEditors(doc: vscode.TextDocument, spans: readonly MuteSpanDto[]): void {
    const editors = vscode.window.visibleTextEditors.filter((e) => e.document === doc);
    if (editors.length === 0) return;

    const grouped = new Map<string, vscode.Range[]>();
    for (const s of spans) {
      const list = grouped.get(s.categoryKey) ?? [];
      list.push(new vscode.Range(doc.positionAt(s.start), doc.positionAt(s.end)));
      grouped.set(s.categoryKey, list);
    }

    const enabledKeys = new Set<string>();
    for (const c of this.state.categories) if (c.enabled) enabledKeys.add(c.key.toLowerCase());

    for (const c of this.state.categories) {
      const type = this.ensureDecorationType(c.key, c.style);
      if (!type) continue;
      const lower = c.key.toLowerCase();
      const ranges = c.enabled && enabledKeys.has(lower) ? grouped.get(c.key) ?? [] : [];
      for (const editor of editors) editor.setDecorations(type, ranges);
    }
  }

  private ensureDecorationType(categoryKey: string, style: MuteStyleDto): vscode.TextEditorDecorationType | undefined {
    const lower = categoryKey.toLowerCase();
    let existing = this.typesByCategory.get(lower);
    if (existing) return existing;

    // Assign a user slot if this is a custom category, so it picks up its slot's color/keybinding.
    let resolvedKey = lower;
    const builtInIds = ['telemetry', 'logging', 'signature', 'guards'];
    if (!builtInIds.includes(lower) && !/^user\d+$/.test(lower)) {
      // assign() is async but the call site is sync; fire-and-forget — next refresh will re-resolve.
      void this.userSlots.assign(categoryKey);
    }

    const themeId = themeColorIdForCategory(resolvedKey);
    const foreground = themeId
      ? new vscode.ThemeColor(themeId)
      : (style.foreground ? style.foreground : undefined);

    const opts: vscode.DecorationRenderOptions = {
      rangeBehavior: vscode.DecorationRangeBehavior.ClosedClosed,
    };
    if (foreground) opts.color = foreground;
    if (style.italic) opts.fontStyle = 'italic';
    if (style.bold) opts.fontWeight = 'bold';
    if (style.opacity > 0 && style.opacity < 1) {
      opts.opacity = style.opacity.toString();
    }

    const type = vscode.window.createTextEditorDecorationType(opts);
    this.typesByCategory.set(lower, type);
    return type;
  }

  dispose(): void {
    for (const h of this.debounceHandles.values()) clearTimeout(h);
    this.debounceHandles.clear();
    for (const type of this.typesByCategory.values()) type.dispose();
    this.typesByCategory.clear();
    this._onSpansUpdated.dispose();
  }
}
