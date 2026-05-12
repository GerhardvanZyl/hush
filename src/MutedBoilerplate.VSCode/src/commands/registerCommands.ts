import * as vscode from 'vscode';
import { BuiltInCategoryKeys } from '../sidecar/protocol';
import { SidecarClient } from '../sidecar/SidecarClient';
import { MuteStateClient } from '../state/MuteStateClient';
import { UserSlotMap } from '../state/UserSlotMap';

export function registerCommands(
  context: vscode.ExtensionContext,
  state: MuteStateClient,
  sidecar: SidecarClient,
  userSlots: UserSlotMap,
  output: vscode.OutputChannel,
): void {
  const reg = (id: string, fn: () => Promise<void> | void) =>
    context.subscriptions.push(vscode.commands.registerCommand(id, fn));

  reg('mutedBoilerplate.toggleTelemetry',  () => state.toggle(BuiltInCategoryKeys.Telemetry));
  reg('mutedBoilerplate.toggleLogging',    () => state.toggle(BuiltInCategoryKeys.Logging));
  reg('mutedBoilerplate.toggleSignature',  () => state.toggle(BuiltInCategoryKeys.Signature));
  reg('mutedBoilerplate.toggleGuards',     () => state.toggle(BuiltInCategoryKeys.Guards));
  reg('mutedBoilerplate.toggleAll',        () => state.toggleAll());
  reg('mutedBoilerplate.toggleExclusions', () => state.toggleExclusions());

  for (let slot = 1; slot <= 8; slot++) {
    reg(`mutedBoilerplate.toggleUser${slot}`, async () => {
      const category = userSlots.categoryForSlot(slot);
      if (!category) {
        output.appendLine(`[commands] user slot ${slot} has no bound category yet`);
        return;
      }
      await state.toggle(category);
    });
  }

  reg('mutedBoilerplate.reloadRules', async () => {
    const path = vscode.workspace.getConfiguration('mutedBoilerplate').get<string>('rulesPath', '');
    const resp = await sidecar.reloadRules({ path: path && path.trim().length > 0 ? path : undefined });
    state.hydrateFromReload(resp.categories, resp.ruleSetVersion);
    output.appendLine(`[commands] reloaded rules (version ${resp.ruleSetVersion})`);
  });
}
