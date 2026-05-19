import { BuiltInCategoryKeys } from '../sidecar/protocol';

/**
 * Resolves a category key to a registered theme color ID, or undefined for non-built-in keys.
 * Theme color IDs are declared in package.json under `contributes.colors`.
 */
export function themeColorIdForCategory(categoryKey: string): string | undefined {
  const k = categoryKey.toLowerCase();
  switch (k) {
    case BuiltInCategoryKeys.Telemetry: return 'hush.telemetry.foreground';
    case BuiltInCategoryKeys.Logging:   return 'hush.logging.foreground';
    case BuiltInCategoryKeys.Signature: return 'hush.signature.foreground';
    case BuiltInCategoryKeys.Guards:    return 'hush.guards.foreground';
  }
  for (let slot = 1; slot <= 8; slot++) {
    if (k === `user${slot}`) return `hush.user${slot}.foreground`;
  }
  return undefined;
}

export const UserSlotCount = 8;
