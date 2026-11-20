import { afterNextRender, Component, DestroyRef, Injector, effect, inject, signal, untracked } from "@angular/core";
import { ViewportScroller } from "@angular/common";
import { RouterLink } from "@angular/router";
import { FormsModule } from "@angular/forms";
import { MatButtonModule } from "@angular/material/button";
import { MatCheckboxModule } from "@angular/material/checkbox";
import { MatInputModule } from "@angular/material/input";
import { Api } from "../core/api";
import { message } from "../core/api-errors";
import { Workspace } from "../core/workspace";
import { SpendingNavigation } from "./navigation";
import { recordedMoney, spendingDate, validSpendingDate } from "./amounts";
import { SpendingHistoryPage } from "./models";
import { SpendingHistoryFilters, SpendingHistoryNavigation } from "./history-state";

const emptyPage = (): SpendingHistoryPage => ({ records: [], nextCursor: null, pageSize: 25 });
type Direction = "refresh" | "next" | "previous" | "current";
@Component({
  imports: [RouterLink, FormsModule, MatButtonModule, MatCheckboxModule, MatInputModule, SpendingNavigation],
  templateUrl: "records.html", styleUrl: "records.scss",
})
export class SpendingRecords {
  readonly w = inject(Workspace);
  private readonly api = inject(Api);
  private readonly navigation = inject(SpendingHistoryNavigation);
  private readonly viewport = inject(ViewportScroller);
  private readonly injector = inject(Injector);
  private home: string | null = null;
  private generation = 0;
  private destroyed = false;
  private historyVersion = 0;
  private cursor: string | null = null;
  private loaded = false;
  readonly refreshNeeded = signal(false);
  readonly previous = signal<(string | null)[]>([]);
  readonly page = signal(emptyPage());
  readonly historyLoading = signal(false);
  readonly historyError = signal("");
  readonly retryDirection = signal<Direction>("refresh");
  readonly filters = signal<SpendingHistoryFilters>({ from: "", to: "", includeVoided: false });
  readonly applied = signal<SpendingHistoryFilters>({ from: "", to: "", includeVoided: false });
  readonly money = recordedMoney;
  readonly date = spendingDate;

  constructor() {
    effect(() => {
      const home = this.w.current()?.id ?? null;
      untracked(() => {
        if (home === this.home) return;
        this.home = home; this.generation++;
        this.navigation.enter(home);
        this.page.set(emptyPage()); this.historyLoading.set(false); this.historyError.set("");
        this.filters.set({ from: "", to: "", includeVoided: false }); this.applied.set(this.filters());
        this.cursor = null; this.previous.set([]); this.loaded = false; this.refreshNeeded.set(false);
        const saved = home ? this.navigation.read(home) : null;
        if (saved) {
          this.filters.set({ ...saved.filters }); this.applied.set({ ...saved.applied }); this.page.set(saved.page);
          this.cursor = saved.cursor; this.previous.set([...saved.previous]); this.loaded = saved.loaded;
          this.refreshNeeded.set(saved.refreshNeeded); this.restoreScroll(saved.scroll);
        } else this.restoreScroll([0, 0]);
        if (home && (!saved || !saved.loaded || saved.refreshNeeded)) void this.loadHistory(saved ? "current" : "refresh");
      });
    });
    inject(DestroyRef).onDestroy(() => {
      if (this.home) this.navigation.save({ householdId: this.home, filters: { ...this.filters() }, applied: { ...this.applied() },
        page: this.page(), cursor: this.cursor, previous: [...this.previous()], scroll: this.viewport.getScrollPosition(),
        loaded: this.loaded, refreshNeeded: this.refreshNeeded() });
      this.destroyed = true; this.generation++;
    });
  }
  private current(generation: number, home: string) { return !this.destroyed && generation === this.generation && this.w.current()?.id === home; }
  private restoreScroll(position: [number, number]) {
    const generation = this.generation;
    afterNextRender(() => { if (!this.destroyed && this.generation === generation) this.viewport.scrollToPosition(position); }, { injector: this.injector });
  }
  changeFilter(field: keyof SpendingHistoryFilters, value: string | boolean) { this.filters.update(filters => ({ ...filters, [field]: value })); }
  filtersChanged() { return JSON.stringify(this.filters()) !== JSON.stringify(this.applied()); }
  async loadHistory(direction: Direction = "refresh") {
    const home = this.home, generation = this.generation;
    if (!home || !this.current(generation, home)) return;
    const filters = { ...(direction === "refresh" ? this.filters() : this.applied()) };
    if ((filters.from && !validSpendingDate(filters.from)) || (filters.to && !validSpendingDate(filters.to)) ||
      (filters.from && filters.to && filters.from > filters.to)) {
      this.historyError.set("Use valid dates with From on or before To."); this.retryDirection.set(direction); return;
    }
    const cursor = direction === "next" ? this.page().nextCursor : direction === "previous" ? this.previous().at(-1) ?? null : direction === "current" ? this.cursor : null;
    if ((direction === "next" && !cursor) || (direction === "previous" && !this.previous().length)) return;
    const version = ++this.historyVersion;
    const current = () => this.current(generation, home) && version === this.historyVersion;
    const query = new URLSearchParams({ includeVoided: String(filters.includeVoided), pageSize: "25" });
    if (filters.from) query.set("from", filters.from);
    if (filters.to) query.set("to", filters.to);
    if (cursor) query.set("cursor", cursor);
    this.historyLoading.set(true); this.historyError.set(""); this.retryDirection.set(direction);
    try {
      const page = await this.api.get<SpendingHistoryPage>(`households/${home}/spending/records?${query}`);
      if (!current()) return;
      if (direction === "refresh") this.previous.set([]);
      else if (direction === "next") this.previous.update(previous => [...previous, this.cursor]);
      else if (direction === "previous") this.previous.update(previous => previous.slice(0, -1));
      this.cursor = cursor; this.applied.set(filters); this.page.set(page); this.loaded = true; this.refreshNeeded.set(false);
    } catch (error) { if (current()) this.historyError.set(message(error)); }
    finally { if (current()) this.historyLoading.set(false); }
  }
}
