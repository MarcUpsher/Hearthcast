import { Injectable, effect, inject, untracked } from "@angular/core";
import { Workspace } from "../core/workspace";
import { SpendingHistoryPage } from "./models";

export interface SpendingHistoryFilters { from: string; to: string; includeVoided: boolean; }
export interface SpendingHistoryState {
  householdId: string;
  filters: SpendingHistoryFilters;
  applied: SpendingHistoryFilters;
  page: SpendingHistoryPage;
  cursor: string | null;
  previous: (string | null)[];
  scroll: [number, number];
  loaded: boolean;
  refreshNeeded: boolean;
}

// Navigation state is kept in memory for the selected household, never in a receipt.
@Injectable({ providedIn: "root" })
export class SpendingHistoryNavigation {
  private readonly w = inject(Workspace);
  private home: string | null = null;
  private state: SpendingHistoryState | null = null;
  private notice: { householdId: string; recordId: string; text: string } | null = null;
  constructor() {
    effect(() => { const home = this.w.current()?.id ?? null; untracked(() => this.enter(home)); });
  }
  enter(home: string | null) {
    if (home === this.home) return;
    this.home = home; this.state = null; this.notice = null;
  }
  read(home: string) {
    this.enter(home);
    return this.w.current()?.id === home && this.state?.householdId === home ? this.state : null;
  }
  save(state: SpendingHistoryState) {
    if (this.w.current()?.id !== state.householdId) return;
    this.enter(state.householdId); this.state = state;
  }
  changed(home: string) {
    if (this.w.current()?.id === home && this.state?.householdId === home) this.state.refreshNeeded = true;
  }
  savedNotice(home: string, recordId: string, text: string) {
    if (this.w.current()?.id === home) this.notice = { householdId: home, recordId, text };
  }
  takeNotice(home: string, recordId: string) {
    if (this.notice?.householdId !== home || this.notice.recordId !== recordId) return "";
    const text = this.notice.text; this.notice = null; return text;
  }
}
