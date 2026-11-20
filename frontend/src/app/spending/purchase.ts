import { afterNextRender, Component, DestroyRef, HostListener, Injector, effect, inject, signal, untracked, viewChild } from "@angular/core";
import { ViewportScroller } from "@angular/common";
import { ActivatedRoute, Router, RouterLink } from "@angular/router";
import { toSignal } from "@angular/core/rxjs-interop";
import { map } from "rxjs";
import { HttpErrorResponse } from "@angular/common/http";
import { MatButtonModule } from "@angular/material/button";
import { Api } from "../core/api";
import { message } from "../core/api-errors";
import { Workspace } from "../core/workspace";
import { SpendingCapture } from "./capture";
import { SpendingNavigation } from "./navigation";
import { SpendingHistoryNavigation } from "./history-state";
import { recordedMoney, spendingDate } from "./amounts";
import { SpendingCurrency, SpendingItem, SpendingRecord, SpendingSetupEntry } from "./models";

@Component({
  imports: [RouterLink, MatButtonModule, SpendingNavigation, SpendingCapture],
  templateUrl: "purchase.html", styleUrl: "purchase.scss",
})
export class SpendingPurchase {
  readonly w = inject(Workspace);
  private readonly api = inject(Api);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly navigation = inject(SpendingHistoryNavigation);
  private readonly viewport = inject(ViewportScroller);
  private readonly injector = inject(Injector);
  readonly creating = this.route.snapshot.data["create"] === true;
  private readonly recordId = toSignal(this.route.paramMap.pipe(map(params => params.get("recordId"))), { initialValue: this.route.snapshot.paramMap.get("recordId") });
  readonly capture = viewChild(SpendingCapture);
  private home: string | null = null;
  private identity: string | null = null;
  private generation = 0;
  private destroyed = false;
  private lookupVersion = 0;
  private detailVersion = 0;
  readonly categories = signal<SpendingSetupEntry[]>([]);
  readonly merchants = signal<SpendingSetupEntry[]>([]);
  readonly items = signal<SpendingItem[]>([]);
  readonly currencies = signal<SpendingCurrency[]>([]);
  readonly lookupLoading = signal(false);
  readonly lookupError = signal("");
  readonly selected = signal<SpendingRecord | null>(null);
  readonly editing = signal<SpendingRecord | null>(null);
  readonly detailLoading = signal(false);
  readonly detailError = signal("");
  readonly voiding = signal(false);
  readonly needsReload = signal(new Set<string>());
  readonly notice = signal("");
  readonly money = recordedMoney;
  readonly date = spendingDate;
  purchaseLines(record: SpendingRecord) { return record.lines.filter(line => !line.isAdditionalCost); }
  costLines(record: SpendingRecord) { return record.lines.filter(line => line.isAdditionalCost); }
  showForm() { return !!this.editing() || (this.creating && !this.selected()); }

  constructor() {
    effect(() => {
      const home = this.w.current()?.id ?? null, id = this.recordId();
      untracked(() => {
        const identity = JSON.stringify([home, this.creating, id]);
        if (identity === this.identity) return;
        this.identity = identity; this.home = home; this.generation++; this.navigation.enter(home);
        this.categories.set([]); this.merchants.set([]); this.items.set([]); this.currencies.set([]);
        this.lookupLoading.set(false); this.lookupError.set("");
        this.selected.set(null); this.editing.set(null); this.detailLoading.set(false); this.detailError.set("");
        this.voiding.set(false); this.needsReload.set(new Set()); this.notice.set("");
        const generation = this.generation;
        afterNextRender(() => { if (!this.destroyed && generation === this.generation) this.viewport.scrollToPosition([0, 0]); }, { injector: this.injector });
        if (home) {
          if (this.creating) void this.loadLookups();
          else if (id) void this.loadDetail(id);
          else this.detailError.set("Choose a purchase from Purchases & history.");
        }
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.generation++; });
  }
  private current(generation: number, home: string) { return !this.destroyed && generation === this.generation && this.w.current()?.id === home; }
  private requireRecord(record: SpendingRecord, home: string, id: string) {
    if (record.householdId !== home || record.id !== id) throw new Error("The purchase response does not match the selected household and record.");
    return record;
  }
  private readError(error: unknown) {
    return error instanceof HttpErrorResponse && error.status === 404 ? "Purchase not found in the selected household." : message(error);
  }
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
  private async loadDetail(id: string) {
    const home = this.home, generation = this.generation;
    if (!home || !this.current(generation, home)) return;
    const version = ++this.detailVersion;
    const current = () => this.current(generation, home) && version === this.detailVersion && this.recordId() === id;
    this.detailLoading.set(true); this.detailError.set("");
    try {
      const record = this.requireRecord(await this.api.get<SpendingRecord>(`households/${home}/spending/records/${id}`), home, id);
      if (current()) {
        this.selected.set(record); this.needsReload.update(ids => { const next = new Set(ids); next.delete(id); return next; });
        this.notice.set(this.navigation.takeNotice(home, id));
      }
    } catch (error) { if (current()) this.detailError.set(this.readError(error)); }
    finally { if (current()) this.detailLoading.set(false); }
  }
  async reloadDetail() {
    if (this.voiding() || this.editing()) return;
    const id = this.selected()?.id ?? this.recordId();
    if (id) await this.loadDetail(id);
  }
  async beginEdit() {
    const selected = this.selected(), home = this.home, generation = this.generation;
    if (!selected || !home || !this.current(generation, home) || selected.isVoided || selected.kind !== "Payment" || this.editing() || this.busy() || this.detailLoading()) return;
    const version = ++this.detailVersion;
    const current = () => this.current(generation, home) && version === this.detailVersion && this.selected()?.id === selected.id;
    this.detailLoading.set(true); this.detailError.set("");
    try {
      const record = this.requireRecord(await this.api.get<SpendingRecord>(`households/${home}/spending/records/${selected.id}`), home, selected.id);
      if (!current()) return;
      this.selected.set(record);
      if (record.isVoided || record.kind !== "Payment") { this.detailError.set("This record cannot be edited."); return; }
      this.editing.set(record); this.notice.set("");
      await this.loadLookups();
    } catch (error) { if (current()) this.detailError.set(this.readError(error)); }
    finally { if (current()) this.detailLoading.set(false); }
  }
  async onSaved(record: SpendingRecord) {
    const home = this.home;
    if (!home || home !== this.w.current()?.id || record.householdId !== home || (this.editing() && this.editing()!.id !== record.id)) return;
    const edited = !!this.editing();
    this.editing.set(null); this.selected.set(record); this.navigation.changed(home);
    const text = record.isVoided ? "The retained saved record is voided." : edited ? "Purchase changes saved." : "Purchase saved.";
    this.notice.set(text);
    if (this.creating) {
      this.navigation.savedNotice(home, record.id, text);
      try { await this.router.navigate(["/spending/records", record.id], { replaceUrl: true }); }
      catch { this.detailError.set("The purchase was saved. Use its link to reopen the details."); }
    }
  }
  onMutationUncertain() { if (this.home) this.navigation.changed(this.home); }
  onItemCreated(item: SpendingItem) {
    if (item.householdId !== this.home || this.home !== this.w.current()?.id) return;
    this.lookupVersion++; this.lookupLoading.set(false);
    this.items.update(items => [...items.filter(row => row.id !== item.id), item]);
  }
  onReloaded(record: SpendingRecord) {
    if (record.householdId !== this.home || this.w.current()?.id !== this.home || this.editing()?.id !== record.id) return;
    this.selected.set(record); this.navigation.changed(this.home!);
    this.needsReload.update(ids => { const next = new Set(ids); next.delete(record.id); return next; });
  }
  async voidRecord() {
    const selected = this.selected(), home = this.home, generation = this.generation;
    if (!home || !selected || !this.current(generation, home) || selected.isVoided || this.editing() || this.busy() || this.detailLoading() || this.needsReload().has(selected.id)) return;
    if (!window.confirm("Void this accidental record? It will be excluded from spending totals and retained in history.")) return;
    this.voiding.set(true); this.detailError.set(""); this.notice.set("");
    try {
      const record = this.requireRecord(await this.api.send<SpendingRecord>("POST", `households/${home}/spending/records/${selected.id}/void`, { expectedRevision: selected.revision }), home, selected.id);
      if (!this.current(generation, home)) return;
      this.selected.set(record); this.notice.set("Record voided and retained in history."); this.navigation.changed(home);
    } catch (error) {
      if (this.current(generation, home)) {
        this.navigation.changed(home);
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
