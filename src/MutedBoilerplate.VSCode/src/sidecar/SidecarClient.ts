import { ChildProcess, spawn } from 'child_process';
import * as vscode from 'vscode';
import {
  createMessageConnection,
  MessageConnection,
  ParameterStructures,
  StreamMessageReader,
  StreamMessageWriter,
} from 'vscode-jsonrpc/node';
import {
  CategoryDto,
  DidChangeRequest,
  DidCloseRequest,
  DidOpenRequest,
  GetSpansRequest,
  GetSpansResponse,
  InitializeRequest,
  InitializeResponse,
  ReloadRulesRequest,
  ReloadRulesResponse,
  SetExclusionsEnabledRequest,
  SetMuteStateRequest,
  StateChangeResponse,
} from './protocol';

export interface SidecarOptions {
  binaryPath: string;
  output: vscode.OutputChannel;
  onUnexpectedExit?: () => void;
}

/**
 * Owns the lifetime of one sidecar process and a JSON-RPC connection over its stdio.
 * Crash supervision (auto-restart, replay) is layered on top by the caller — this class
 * only emits {@link onExit} when the child unexpectedly terminates.
 */
export class SidecarClient implements vscode.Disposable {
  private child: ChildProcess | undefined;
  private connection: MessageConnection | undefined;
  private readonly output: vscode.OutputChannel;
  private readonly _onExit = new vscode.EventEmitter<{ code: number | null; signal: NodeJS.Signals | null }>();
  readonly onExit = this._onExit.event;
  private stopping = false;

  constructor(private readonly options: SidecarOptions) {
    this.output = options.output;
  }

  start(): void {
    if (this.child) return;
    this.output.appendLine(`[sidecar] spawning ${this.options.binaryPath}`);
    const child = spawn(this.options.binaryPath, [], {
      stdio: ['pipe', 'pipe', 'pipe'],
      windowsHide: true,
    });

    child.stderr.on('data', (chunk) => {
      const s = chunk.toString('utf8').trimEnd();
      if (s.length > 0) this.output.appendLine(`[sidecar-stderr] ${s}`);
    });

    child.on('exit', (code, signal) => {
      this.output.appendLine(`[sidecar] exited code=${code} signal=${signal} stopping=${this.stopping}`);
      this.connection?.dispose();
      this.connection = undefined;
      this.child = undefined;
      if (!this.stopping) this._onExit.fire({ code, signal });
    });

    const reader = new StreamMessageReader(child.stdout!);
    const writer = new StreamMessageWriter(child.stdin!);
    const connection = createMessageConnection(reader, writer, {
      error: (msg) => this.output.appendLine(`[rpc-error] ${msg}`),
      warn: (msg) => this.output.appendLine(`[rpc-warn] ${msg}`),
      info: (msg) => this.output.appendLine(`[rpc-info] ${msg}`),
      log: (_msg) => { /* ignore verbose */ },
    });
    connection.listen();

    this.child = child;
    this.connection = connection;
  }

  initialize(request: InitializeRequest): Promise<InitializeResponse> {
    return this.invoke<InitializeResponse>('initialize', request);
  }

  didOpen(request: DidOpenRequest): Promise<void> {
    return this.invoke<void>('didOpen', request);
  }

  didChange(request: DidChangeRequest): Promise<boolean> {
    return this.invoke<boolean>('didChange', request);
  }

  didClose(request: DidCloseRequest): Promise<void> {
    return this.invoke<void>('didClose', request);
  }

  getSpans(request: GetSpansRequest): Promise<GetSpansResponse> {
    return this.invoke<GetSpansResponse>('getSpans', request);
  }

  setMuteState(request: SetMuteStateRequest): Promise<StateChangeResponse> {
    return this.invoke<StateChangeResponse>('setMuteState', request);
  }

  setExclusionsEnabled(request: SetExclusionsEnabledRequest): Promise<StateChangeResponse> {
    return this.invoke<StateChangeResponse>('setExclusionsEnabled', request);
  }

  toggleAll(): Promise<StateChangeResponse> {
    const c = this.connection;
    if (!c) return Promise.reject(new Error('Sidecar not started (method=toggleAll)'));
    return c.sendRequest<StateChangeResponse>('toggleAll', ParameterStructures.byPosition);
  }

  reloadRules(request: ReloadRulesRequest): Promise<ReloadRulesResponse> {
    return this.invoke<ReloadRulesResponse>('reloadRules', request);
  }

  private invoke<T>(method: string, params: unknown): Promise<T> {
    const c = this.connection;
    if (!c) return Promise.reject(new Error(`Sidecar not started (method=${method})`));
    // Force `params: [obj]` (positional, one element) so StreamJsonRpc treats the
    // whole object as the single C# parameter. Default behavior (auto) would send
    // a bare object as named-args, which StreamJsonRpc tries to spread across
    // multiple .NET parameters by JSON-key name.
    return c.sendRequest<T>(method, ParameterStructures.byPosition, params);
  }

  dispose(): void {
    this.stopping = true;
    try {
      this.connection?.dispose();
    } catch { /* ignore */ }
    if (this.child && !this.child.killed) {
      try {
        this.child.kill();
      } catch { /* ignore */ }
    }
    this.connection = undefined;
    this.child = undefined;
    this._onExit.dispose();
  }
}

export function categoryByKey(categories: readonly CategoryDto[]): Map<string, CategoryDto> {
  const m = new Map<string, CategoryDto>();
  for (const c of categories) m.set(c.key.toLowerCase(), c);
  return m;
}
