import { Component, DestroyRef, HostListener, effect, inject, signal, untracked, viewChild } from "@angular/core";
import { FormsModule } from "@angular/forms";
import { MatButtonModule } from "@angular/material/button";
import { MatCheckboxModule } from "@angular/material/checkbox";
import { MatInputModule } from "@angular/material/input";
import { Api } from "../core/api";
import { message } from "../core/api-errors";
import { Workspace } from "../core/workspace";
import { SpendingCapture } from "./capture";
import { SpendingNavigation } from "./navigation";
import { recordedMoney, spendingDate, validSpendingDate } from "./amounts";
import { SpendingCurrency, SpendingHistoryPage, SpendingItem, SpendingRecord, SpendingSetupEntry } from "./models";

interface HistoryFilters { from: string; to: string; includeVoided: boolean; }
const emptyPage = (): SpendingHistoryPage => ({ records: [], nextCursor: null, pageSize: 25 });

@Component({
  imports: [FormsModule, MatButtonModule, MatCheckboxModule, MatInputModule, SpendingNavigation, SpendingCapture],
  templateUrl: "records.html", styleUrl: "records.scss",
})
export class SpendingRecords {
  readonly w = inject(Workspace);
  private readonly api = inject(Api);
  readonly capture = viewChild(SpendingCapture);
  private home: string | null = null;
  private generation = 0;
  private destroyed = false;
  private lookupVersion = 0;
  private historyVersion = 0;
  private detailVersion = 0;
  private cursor: string | null = null;
  readonly previous = signal<(string | null)[]>([]);
  readonly categories = signal<SpendingSetupEntry[]>([]);
  readonly merchants = signal<SpendingSetupEntry[]>([]);
  readonly items = signal<SpendingItem[]>([]);
  readonly currencies = signal<SpendingCurrency[]>([]);
  readonly lookupLoading = signal(false);
  readonly lookupError = signal("");
  readonly page = signal(emptyPage());
  readonly historyLoading = signal(false);
  readonly historyError = signal("");
  readonly filters = signal<HistoryFilters>({ from: "", to: "", includeVoided: false });
  readonly applied = signal<HistoryFilters>({ from: "", to: "", includeVoided: false });
  readonly selected = signal<SpendingRecord | null>(null);
  readonly editing = signal<SpendingRecord | null>(null);
  readonly detailLoading = signal(false);
  readonly detailError = signal("");
  readonly voiding = signal(false);
  readonly needsReload = signal(new Set<string>());
  readonly notice = signal("");
  readonly money = recordedMoney;
  readonly date = spendingDate;

  constructor() {
    effect(() => {
      const home = this.w.current()?.id ?? null;
      untracked(() => {
        if (home === this.home) return;
        this.home = home; this.generation++;
        this.categories.set([]); this.merchants.set([]); this.items.set([]); this.currencies.set([]);
        this.lookupLoading.set(false); this.lookupError.set("");
        this.page.set(emptyPage()); this.historyLoading.set(false); this.historyError.set("");
        this.selected.set(null); this.editing.set(null); this.detailLoading.set(false); this.detailError.set("");
        this.voiding.set(false); this.needsReload.set(new Set()); this.notice.set("");
        this.filters.set({ from: "", to: "", includeVoided: false }); this.applied.set(this.filters());
        this.cursor = null; this.previous.set([]);
        if (home) { void this.loadLookups(); void this.loadHistory(); }
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.generation++; });
  }
  private current(generation: number, home: string) { return !this.destroyed && generation === this.generation && this.w.current()?.id === home; }
  async loadLookups() {
    const home = this.home, generation = this.generation;
    if (!home || !this.current(generation, home)) return;
    const version = ++this.lookupVersion;
    const current = () => this.current(generation, home) && version === this.lookupVersion;
    this.lookupLoading.set(true); this.lookupError.set("");
    try {
      const [categories, merchants, items, currencies] = await Promise.all([
        this.api.get<SpendingSetupEntry[]>(`households/${home}/spending/categories?includeRetired=true`),
        this.api.get<SpendingSetupEntry[]>(`households/${home}/spending/merchants?includeRetired=true`),
        this.api.get<SpendingItem[]>(`households/${home}/spending/items?includeRetired=true`),
        this.api.get<SpendingCurrency[]>(`households/${home}/spending/currencies`),
      ]);
      if (current()) { this.categories.set(categories); this.merchants.set(merchants); this.items.set(items); this.currencies.set(currencies); }
    } catch (error) { if (current()) this.lookupError.set(message(error)); }
    finally { if (current()) this.lookupLoading.set(false); }
  }
  changeFilter(field: keyof HistoryFilters, value: string | boolean) { this.filters.update(filters => ({ ...filters, [field]: value })); }
  filtersChanged() { return JSON.stringify(this.filters()) !== JSON.stringify(this.applied()); }
  async loadHistory(direction: "refresh" | "next" | "previous" = "refresh", keepFilters = false) {
    const home = this.home, generation = this.generation;
    if (!home || !this.current(generation, home)) return;
    const filters = direction === "refresh" && !keepFilters ? { ...this.filters() } : { ...this.applied() };
    if ((filters.from && !validSpendingDate(filters.from)) || (filters.to && !validSpendingDate(filters.to)) ||
      (filters.from && filters.to && filters.from > filters.to)) {
      this.historyError.set("Use valid dates with From on or before To."); return;
    }
    const cursor = direction === "next" ? this.page().nextCursor : direction === "previous" ? this.previous().at(-1) ?? null : null;
    if ((direction === "next" && !cursor) || (direction === "previous" && !this.previous().length)) return;
    const version = ++this.historyVersion;
    const current = () => this.current(generation, home) && version === this.historyVersion;
    const query = new URLSearchParams({ includeVoided: String(filters.includeVoided), pageSize: "25" });
    if (filters.from) query.set("from", filters.from);
    if (filters.to) query.set("to", filters.to);
    if (cursor) query.set("cursor", cursor);
    this.historyLoading.set(true); this.historyError.set("");
    try {
      const page = await this.api.get<SpendingHistoryPage>(`households/${home}/spending/records?${query}`);
      if (!current()) return;
      if (direction === "refresh") this.previous.set([]);
      else if (direction === "next") this.previous.update(previous => [...previous, this.cursor]);
      else this.previous.update(previous => previous.slice(0, -1));
      this.cursor = cursor;
      this.applied.set(filters); this.page.set(page);
    } catch (error) { if (current()) this.historyError.set(message(error)); }
    finally { if (current()) this.historyLoading.set(false); }
  }
  onSaved(record: SpendingRecord) {
    const edited = !!this.editing();
    this.editing.set(null);
    this.view(record);
    this.notice.set(record.isVoided ? "The retained saved record is voided." : edited ? "Purchase changes saved." : "Purchase saved.");
    void this.loadHistory("refresh", true);
  }
  onItemCreated(item: SpendingItem) {
    if (item.householdId !== this.home || this.home !== this.w.current()?.id) return;
    // A lookup started before the item was saved must not remove it afterwards.
    this.lookupVersion++; this.lookupLoading.set(false);
    this.items.update(items => [...items.filter(row => row.id !== item.id), item]);
  }
  onReloaded(record: SpendingRecord) {
    if (record.householdId !== this.home || this.w.current()?.id !== this.home || this.editing()?.id !== record.id) return;
    this.selected.set(record);
    this.needsReload.update(ids => { const next = new Set(ids); next.delete(record.id); return next; });
    void this.loadHistory("refresh", true);
  }
  async beginEdit() {
    const selected = this.selected(), home = this.home, generation = this.generation;
    if (!selected || !home || !this.current(generation, home) || selected.isVoided || selected.kind !== "Payment" || this.editing() || this.busy() || this.detailLoading()) return;
    if (!(this.capture()?.canLeave() ?? true)) return;
    const version = ++this.detailVersion;
    const current = () => this.current(generation, home) && version === this.detailVersion && this.selected()?.id === selected.id;
    this.detailLoading.set(true); this.detailError.set("");
    try {
      const record = await this.api.get<SpendingRecord>(`households/${home}/spending/records/${selected.id}`);
      if (!current()) return;
      this.selected.set(record);
      if (record.isVoided) { this.detailError.set("This record has been voided and cannot be edited."); return; }
      this.editing.set(record); this.notice.set("");
    } catch (error) { if (current()) this.detailError.set(message(error)); }
    finally { if (current()) this.detailLoading.set(false); }
  }
  view(record: SpendingRecord) {
    if (this.voiding() || this.editing() || record.householdId !== this.home || this.home !== this.w.current()?.id) return;
    this.detailVersion++; this.detailLoading.set(false); this.detailError.set(""); this.selected.set(record);
  }
  async reloadDetail() {
    const home = this.home, generation = this.generation, selected = this.selected();
    if (!home || !selected || !this.current(generation, home) || this.voiding() || this.editing()) return;
    const version = ++this.detailVersion;
    const current = () => this.current(generation, home) && version === this.detailVersion && this.selected()?.id === selected.id;
    this.detailLoading.set(true); this.detailError.set("");
    try {
      const record = await this.api.get<SpendingRecord>(`households/${home}/spending/records/${selected.id}`);
      if (current()) {
        this.selected.set(record);
        this.needsReload.update(ids => { const next = new Set(ids); next.delete(record.id); return next; });
      }
    } catch (error) { if (current()) this.detailError.set(message(error)); }
    finally { if (current()) this.detailLoading.set(false); }
  }
  async voidRecord() {
    const selected = this.selected(), home = this.home, generation = this.generation;
    if (!home || !selected || !this.current(generation, home) || selected.householdId !== home || selected.isVoided || this.editing() || this.busy() || this.detailLoading() || this.needsReload().has(selected.id)) return;
    if (!window.confirm("Void this accidental record? It will be excluded from spending totals and retained in history.")) return;
    this.voiding.set(true); this.detailError.set(""); this.notice.set("");
    try {
      const record = await this.api.send<SpendingRecord>("POST", `households/${home}/spending/records/${selected.id}/void`, { expectedRevision: selected.revision });
      if (!this.current(generation, home)) return;
      this.selected.set(record); this.notice.set("Record voided and retained in history.");
      void this.loadHistory("refresh", true);
    } catch (error) {
      if (this.current(generation, home)) {
        this.needsReload.update(ids => new Set([...ids, selected.id]));
        this.detailError.set(message(error) + " Reload the record before trying again.");
      }
    } finally { if (this.current(generation, home)) this.voiding.set(false); }
  }
  busy() { return this.voiding() || (this.capture()?.busy() ?? false); }
  hasUnsavedChanges() { return this.capture()?.hasUnsavedChanges() ?? false; }
  canLeave() { return !this.voiding() && (this.capture()?.canLeave() ?? true); }
  @HostListener("window:beforeunload", ["$event"])
  beforeUnload(event: BeforeUnloadEvent) {
    if (this.busy() || this.hasUnsavedChanges()) { event.preventDefault(); event.returnValue = ""; }
  }
}
