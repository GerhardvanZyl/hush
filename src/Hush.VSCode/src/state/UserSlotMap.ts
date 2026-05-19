import * as vscode from 'vscode';
import { UserSlotCount } from '../colors/builtInColors';

const STORAGE_KEY = 'hush.userSlotMap';

/**
 * Persistent first-seen-category → slot mapping, so a user-defined category
 * keeps the same hotkey across sessions. Mirrors `Options/UserSlotMap.cs` from
 * the VS extension.
 */
export class UserSlotMap {
  private categoryBySlot = new Map<number, string>();
  private slotByCategory = new Map<string, number>();

  constructor(private readonly state: vscode.Memento) {
    const persisted = state.get<Record<string, number>>(STORAGE_KEY, {});
    for (const [cat, slot] of Object.entries(persisted)) {
      if (typeof slot === 'number' && slot >= 1 && slot <= UserSlotCount) {
        this.slotByCategory.set(cat.toLowerCase(), slot);
        this.categoryBySlot.set(slot, cat);
      }
    }
  }

  /** Returns the slot for a category, allocating the lowest free slot if it's new. */
  async assign(categoryKey: string): Promise<number | undefined> {
    const k = categoryKey.toLowerCase();
    const existing = this.slotByCategory.get(k);
    if (existing) return existing;
    for (let slot = 1; slot <= UserSlotCount; slot++) {
      if (!this.categoryBySlot.has(slot)) {
        this.slotByCategory.set(k, slot);
        this.categoryBySlot.set(slot, categoryKey);
        await this.persist();
        return slot;
      }
    }
    return undefined; // all slots taken
  }

  categoryForSlot(slot: number): string | undefined {
    return this.categoryBySlot.get(slot);
  }

  private async persist(): Promise<void> {
    const map: Record<string, number> = {};
    for (const [cat, slot] of this.slotByCategory) map[cat] = slot;
    await this.state.update(STORAGE_KEY, map);
  }
}
