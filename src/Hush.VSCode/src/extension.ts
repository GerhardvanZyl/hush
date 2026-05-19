import * as vscode from 'vscode';
import { resolveSidecarPath } from './sidecar/SidecarBinaryResolver';
import { SidecarClient } from './sidecar/SidecarClient';
import { CategoryStateDto } from './sidecar/protocol';
import { MuteStateClient } from './state/MuteStateClient';
import { UserSlotMap } from './state/UserSlotMap';
import { DecorationManager } from './decorations/DecorationManager';
import { HushFoldingRangeProvider } from './folding/HushFoldingRangeProvider';
import { AutoFoldOnOpen } from './folding/AutoFoldOnOpen';
import { registerCommands } from './commands/registerCommands';
import { isTsLanguage, TS_LANGUAGE_IDS } from './matching/TsCallMatcher';

const SIDECAR_LANGUAGES = ['csharp'];
const SUPPORTED_LANGUAGES = [...SIDECAR_LANGUAGES, ...TS_LANGUAGE_IDS];
const isSidecarLanguage = (languageId: string) => SIDECAR_LANGUAGES.includes(languageId);
const MAX_CRASHES_IN_WINDOW = 3;
const CRASH_WINDOW_MS = 60_000;

export async function activate(context: vscode.ExtensionContext): Promise<void> {
  const output = vscode.window.createOutputChannel('Hush');
  context.subscriptions.push(output);
  output.appendLine('[activate] Hush extension activating');

  let sidecarPath: string;
  try {
    const overridePath = vscode.workspace.getConfiguration('hush').get<string>('sidecarPath', '');
    sidecarPath = resolveSidecarPath(context.extensionPath, overridePath);
  } catch (e) {
    output.appendLine(`[activate] sidecar resolution failed: ${e}`);
    void vscode.window.showErrorMessage(
      `Hush: could not locate the .NET sidecar binary. ${(e as Error).message}`,
    );
    return;
  }

  const crashTimestamps: number[] = [];
  const sidecar = new SidecarClient({ binaryPath: sidecarPath, output });
  context.subscriptions.push(sidecar);
  sidecar.start();

  const userSlots = new UserSlotMap(context.globalState);
  const state = new MuteStateClient(sidecar);
  context.subscriptions.push(state);

  await initializeSidecar(sidecar, state, output);

  const decorations = new DecorationManager(sidecar, state, userSlots, output);
  context.subscriptions.push(decorations);

  const foldingProvider = new HushFoldingRangeProvider(decorations, state);
  context.subscriptions.push(foldingProvider);
  for (const language of SUPPORTED_LANGUAGES) {
    context.subscriptions.push(
      vscode.languages.registerFoldingRangeProvider({ language, scheme: 'file' }, foldingProvider),
    );
  }

  const autoFold = new AutoFoldOnOpen(decorations, state, output);
  context.subscriptions.push(autoFold);

  registerCommands(context, state, sidecar, userSlots, output);

  // Wire document lifecycle. Sidecar-languages (csharp) push state to the
  // sidecar's DocumentStore so server-side caching works. TS/JS languages
  // bypass the sidecar entirely — the TS matcher runs in-process from
  // doc.getText() directly.
  const handleOpen = async (doc: vscode.TextDocument) => {
    if (!SUPPORTED_LANGUAGES.includes(doc.languageId)) return;
    if (isSidecarLanguage(doc.languageId)) {
      try {
        await sidecar.didOpen({
          uri: doc.uri.toString(),
          languageId: doc.languageId,
          version: doc.version,
          content: doc.getText(),
        });
      } catch (e) {
        output.appendLine(`[lifecycle] didOpen failed: ${e}`);
        return;
      }
    }
    decorations.scheduleRefresh(doc, 0);
  };
  const handleChange = async (e: vscode.TextDocumentChangeEvent) => {
    if (!SUPPORTED_LANGUAGES.includes(e.document.languageId)) return;
    if (e.contentChanges.length === 0) return;
    if (isSidecarLanguage(e.document.languageId)) {
      try {
        await sidecar.didChange({
          uri: e.document.uri.toString(),
          version: e.document.version,
          changes: e.contentChanges.map((c) => ({
            start: c.rangeOffset,
            length: c.rangeLength,
            text: c.text,
          })),
        });
      } catch (err) {
        output.appendLine(`[lifecycle] didChange failed: ${err}`);
        return;
      }
    }
    decorations.scheduleRefresh(e.document);
  };
  const handleClose = async (doc: vscode.TextDocument) => {
    if (!SUPPORTED_LANGUAGES.includes(doc.languageId)) return;
    decorations.clearCachedSpans(doc.uri.toString());
    if (isSidecarLanguage(doc.languageId)) {
      try {
        await sidecar.didClose({ uri: doc.uri.toString() });
      } catch (e) {
        output.appendLine(`[lifecycle] didClose failed: ${e}`);
      }
    }
  };

  context.subscriptions.push(
    vscode.workspace.onDidOpenTextDocument(handleOpen),
    vscode.workspace.onDidChangeTextDocument(handleChange),
    vscode.workspace.onDidCloseTextDocument(handleClose),
    vscode.window.onDidChangeVisibleTextEditors(() => decorations.refreshAllVisibleEditors()),
    state.onChanged(() => decorations.refreshAllVisibleEditors()),
  );

  // Crash supervision: restart sidecar on unexpected exit and replay didOpen for known docs.
  context.subscriptions.push(
    sidecar.onExit(async () => {
      const now = Date.now();
      crashTimestamps.push(now);
      while (crashTimestamps.length > 0 && now - crashTimestamps[0] > CRASH_WINDOW_MS) {
        crashTimestamps.shift();
      }
      if (crashTimestamps.length >= MAX_CRASHES_IN_WINDOW) {
        void vscode.window.showErrorMessage(
          'Hush sidecar has crashed repeatedly; auto-restart disabled. Reload the window to retry.',
        );
        return;
      }
      output.appendLine('[supervisor] restarting sidecar');
      sidecar.start();
      await initializeSidecar(sidecar, state, output);
      // Replay didOpen for sidecar-handled docs only (TS docs need no replay).
      for (const doc of vscode.workspace.textDocuments) {
        if (isSidecarLanguage(doc.languageId)) await handleOpen(doc);
      }
    }),
  );

  // Settings hot-reload: rules path / debounce / sidecar path changes.
  context.subscriptions.push(
    vscode.workspace.onDidChangeConfiguration(async (e) => {
      if (e.affectsConfiguration('hush.debounceMs')) {
        decorations.setDebounceMs(
          vscode.workspace.getConfiguration('hush').get<number>('debounceMs', 200),
        );
      }
      if (e.affectsConfiguration('hush.rulesPath')) {
        const path = vscode.workspace.getConfiguration('hush').get<string>('rulesPath', '');
        try {
          const resp = await sidecar.reloadRules({ path: path && path.trim().length > 0 ? path : undefined });
          state.hydrateFromReload(resp.categories, resp.ruleSetVersion, resp.tsCallRules);
          decorations.invalidateAll();
          decorations.refreshAllVisibleEditors();
        } catch (err) {
          output.appendLine(`[config] reloadRules failed: ${err}`);
        }
      }
    }),
  );

  // Initial pass over already-open documents.
  for (const doc of vscode.workspace.textDocuments) await handleOpen(doc);
  // Repaint any already-visible TS docs whose state arrived after activation.
  decorations.refreshAllVisibleEditors();
}

async function initializeSidecar(
  sidecar: SidecarClient,
  state: MuteStateClient,
  output: vscode.OutputChannel,
): Promise<void> {
  const cfg = vscode.workspace.getConfiguration('hush');
  const rulesPath = cfg.get<string>('rulesPath', '');
  const exclusionsEnabled = cfg.get<boolean>('exclusionsEnabled', true);

  const initialState: CategoryStateDto[] = [];
  for (const key of ['telemetry', 'logging', 'signature', 'guards']) {
    const enabled = cfg.get<boolean>(`categories.${key}.enabled`, true);
    initialState.push({ key, enabled });
  }

  const workspaceFolders = vscode.workspace.workspaceFolders?.map((f) => f.uri.fsPath) ?? [];

  try {
    const resp = await sidecar.initialize({
      rulesPath: rulesPath && rulesPath.trim().length > 0 ? rulesPath : undefined,
      workspaceFolders,
      initialState,
      exclusionsEnabled,
    });
    state.hydrateFromInitialize(resp.categories, resp.stateVersion, resp.ruleSetVersion, resp.exclusionsEnabled, resp.tsCallRules ?? []);
    output.appendLine(`[init] categories=${resp.categories.length} tsCallRules=${(resp.tsCallRules ?? []).length} stateVer=${resp.stateVersion} ruleVer=${resp.ruleSetVersion}`);
  } catch (e) {
    output.appendLine(`[init] sidecar initialize failed: ${e}`);
    void vscode.window.showErrorMessage(
      `Hush: sidecar initialize failed. See the "Hush Boilerplate" output channel for details.`,
    );
  }
}

export function deactivate(): void {
  // VS Code disposes context.subscriptions automatically.
}
