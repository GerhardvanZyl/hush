import * as vscode from 'vscode';
import { CategoryDto, TsCallRuleDto } from '../sidecar/protocol';
import { SidecarClient } from '../sidecar/SidecarClient';

/**
 * Local mirror of sidecar mute state. Tracks current category list, enabled flags,
 * stateVersion + ruleSetVersion (for stale-result filtering on getSpans), and persists
 * built-in category toggles to workspace settings.
 *
 * Mirrors the role of `MuteStateService` in the VS extension
 * (src/MutedBoilerplate.VS/Options/MuteStateService.cs).
 */
export class MuteStateClient implements vscode.Disposable {
  private categoriesByKey = new Map<string, CategoryDto>();
  private _stateVersion = 0;
  private _ruleSetVersion = 0;
  private _exclusionsEnabled = true;
  private _tsCallRules: readonly TsCallRuleDto[] = [];
  private readonly _changed = new vscode.EventEmitter<void>();
  readonly onChanged = this._changed.event;

  constructor(private readonly sidecar: SidecarClient) {}

  get stateVersion() { return this._stateVersion; }
  get ruleSetVersion() { return this._ruleSetVersion; }
  get exclusionsEnabled() { return this._exclusionsEnabled; }
  get categories(): readonly CategoryDto[] {
    return Array.from(this.categoriesByKey.values());
  }

  /**
   * Rules of kind `tsCall` shipped by the sidecar at initialize/reload time.
   * The TS-side matcher applies these in-process for ts/tsx/js/jsx documents
   * (Roslyn isn't usable for those languages).
   */
  get tsCallRules(): readonly TsCallRuleDto[] { return this._tsCallRules; }

  /** Only rules whose category is currently enabled. */
  enabledTsCallRules(): readonly TsCallRuleDto[] {
    return this._tsCallRules.filter((r) => this.isEnabled(r.category));
  }

  isEnabled(categoryKey: string): boolean {
    const c = this.categoriesByKey.get(categoryKey.toLowerCase());
    return c?.enabled ?? false;
  }

  styleFor(categoryKey: string): CategoryDto['style'] | undefined {
    return this.categoriesByKey.get(categoryKey.toLowerCase())?.style;
  }

  hydrateFromInitialize(
    categories: readonly CategoryDto[],
    stateVersion: number,
    ruleSetVersion: number,
    exclusionsEnabled: boolean,
    tsCallRules: readonly TsCallRuleDto[],
  ): void {
    this.categoriesByKey.clear();
    for (const c of categories) this.categoriesByKey.set(c.key.toLowerCase(), c);
    this._stateVersion = stateVersion;
    this._ruleSetVersion = ruleSetVersion;
    this._exclusionsEnabled = exclusionsEnabled;
    this._tsCallRules = tsCallRules;
    this._changed.fire();
  }

  hydrateFromReload(
    categories: readonly CategoryDto[],
    ruleSetVersion: number,
    tsCallRules: readonly TsCallRuleDto[],
  ): void {
    this.categoriesByKey.clear();
    for (const c of categories) this.categoriesByKey.set(c.key.toLowerCase(), c);
    this._ruleSetVersion = ruleSetVersion;
    this._tsCallRules = tsCallRules;
    this._changed.fire();
  }

  applyStateChange(stateVersion: number, exclusionsEnabled: boolean, updates: ReadonlyArray<{ key: string; enabled: boolean }>): void {
    this._stateVersion = stateVersion;
    this._exclusionsEnabled = exclusionsEnabled;
    for (const u of updates) {
      const existing = this.categoriesByKey.get(u.key.toLowerCase());
      if (existing) existing.enabled = u.enabled;
    }
    this._changed.fire();
  }

  async toggle(categoryKey: string): Promise<void> {
    const next = !this.isEnabled(categoryKey);
    const resp = await this.sidecar.setMuteState({ categoryKey, enabled: next });
    this.applyStateChange(resp.stateVersion, resp.exclusionsEnabled, resp.categories);
    await this.persistCategory(categoryKey, next);
  }

  async toggleAll(): Promise<void> {
    const resp = await this.sidecar.toggleAll();
    this.applyStateChange(resp.stateVersion, resp.exclusionsEnabled, resp.categories);
    for (const c of resp.categories) await this.persistCategory(c.key, c.enabled);
  }

  async toggleExclusions(): Promise<void> {
    const next = !this._exclusionsEnabled;
    const resp = await this.sidecar.setExclusionsEnabled({ enabled: next });
    this.applyStateChange(resp.stateVersion, resp.exclusionsEnabled, resp.categories);
    await vscode.workspace.getConfiguration('mutedBoilerplate').update(
      'exclusionsEnabled', resp.exclusionsEnabled, vscode.ConfigurationTarget.Workspace,
    );
  }

  private async persistCategory(categoryKey: string, enabled: boolean): Promise<void> {
    // Built-in keys round-trip through the settings JSON for cross-session persistence.
    const known = new Set(['telemetry', 'logging', 'signature', 'guards']);
    if (!known.has(categoryKey.toLowerCase())) return;
    const cfg = vscode.workspace.getConfiguration('mutedBoilerplate');
    await cfg.update(`categories.${categoryKey.toLowerCase()}.enabled`, enabled, vscode.ConfigurationTarget.Workspace);
  }

  dispose(): void {
    this._changed.dispose();
  }
}
