import { Component, DestroyRef, ElementRef, HostListener, effect, inject, signal, untracked, viewChild } from "@angular/core";
import { HttpErrorResponse } from "@angular/common/http";
import { FormsModule, NgForm } from "@angular/forms";
import { RouterLink } from "@angular/router";
import { MatButtonModule } from "@angular/material/button";
import { MatCheckboxModule } from "@angular/material/checkbox";
import { MatInputModule } from "@angular/material/input";
import { MatSelectModule } from "@angular/material/select";
import { Api } from "../core/api";
import { message } from "../core/api-errors";
import { Workspace } from "../core/workspace";
import { localToday, spendingDate } from "../spending/amounts";
import { SpendingSetupEntry } from "../spending/models";
import { SpendingNavigation } from "../spending/navigation";
import { BudgetPaymentDate } from "./payment-date";
import { validPaymentAnchor } from "./payment-schedule";
import { BudgetEffective, BudgetEntry, BudgetHistory, BudgetItem, BudgetMode, BudgetPlan, PlanRow, budgetRecurrence } from "./models";
import { budgetJson, parseBudget, planAmount, planMoney, planTotals } from "./plan-data";

const validDate = (date: string) => !!date && validPaymentAnchor(date);
interface CreatePayload { id: string; effectiveFrom: string; notes: string | null; copiedFromBudgetSnapshotId: string | null; entries: object[]; }

@Component({
  imports: [FormsModule, RouterLink, MatButtonModule, MatCheckboxModule, MatInputModule, MatSelectModule, SpendingNavigation, BudgetPaymentDate],
  templateUrl: "plans.html", styleUrl: "plans.scss",
})
export class OutgoingPlans {
  readonly w = inject(Workspace);
  private readonly api = inject(Api);
  private home: string | null = null;
  private generation = 0;
  private version = 0;
  private destroyed = false;
  private baseline = "";
  private initialDate = "";
  private pending: CreatePayload | null = null;
  private readonly correctionInput = viewChild<ElementRef<HTMLInputElement>>("correctionAmount");
  readonly items = signal<BudgetItem[]>([]);
  readonly categories = signal<SpendingSetupEntry[]>([]);
  readonly merchants = signal<SpendingSetupEntry[]>([]);
  readonly history = signal<BudgetPlan[]>([]);
  readonly nextBefore = signal<string | null>(null);
  readonly effective = signal<BudgetEffective | null>(null);
  readonly selected = signal<BudgetPlan | null>(null);
  readonly asOf = signal(localToday());
  readonly loadedAsOf = signal("");
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal("");
  readonly writeError = signal("");
  readonly notice = signal("");
  readonly creating = signal(false);
  readonly effectiveFrom = signal(localToday());
  readonly sourceDate = signal("");
  readonly source = signal<BudgetPlan | null>(null);
  readonly draftEnd = signal<string | null>(null);
  readonly notes = signal("");
  readonly rows = signal<PlanRow[]>([]);
  readonly locked = signal(false);
  readonly correcting = signal<BudgetEntry | null>(null);
  readonly correction = signal({ amount: "", confirmedOn: "", notes: "" });
  readonly correctionUncertain = signal(false);
  readonly revisionNeedsReload = signal(false);
  readonly date = spendingDate;
  readonly money = planMoney;
  readonly recurrence = budgetRecurrence;
  readonly totals = planTotals;
  readonly amountValid = (value: string) => planAmount(value) !== null;

  constructor() {
    effect(() => { this.correctionInput()?.nativeElement.focus(); });
    effect(() => {
      const home = this.w.current()?.id ?? null;
      untracked(() => {
        if (home === this.home) return;
        this.home = home; this.generation++; this.version++;
        this.items.set([]); this.categories.set([]); this.merchants.set([]); this.history.set([]); this.nextBefore.set(null);
        this.effective.set(null); this.selected.set(null); this.loading.set(false); this.saving.set(false);
        this.error.set(""); this.writeError.set(""); this.notice.set(""); this.resetDraft(); this.revisionNeedsReload.set(false);
        this.asOf.set(localToday()); this.loadedAsOf.set("");
        if (home) void this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.generation++; });
  }
  private current(home: string, generation: number, version?: number) {
    return !this.destroyed && this.w.current()?.id === home && this.generation === generation && (version === undefined || version === this.version);
  }
  private path(home: string) { return `households/${home}/budgets/snapshots`; }
  canWrite() { return !!this.home && this.w.current()?.id === this.home && !this.loading() && !this.saving() && !this.error() && !this.revisionNeedsReload(); }
  private async read<T>(path: string): Promise<T | null> { const text = await this.api.getText(path); return text ? parseBudget<T>(text) : null; }
  async load() {
    const home = this.home, generation = this.generation, asOf = this.asOf();
    if (!home || this.saving() || !this.current(home, generation) || !validDate(asOf) || !this.discardAllowed()) return;
    this.resetDraft(); const version = ++this.version, selectedId = this.loadedAsOf() === asOf ? this.selected()?.id : undefined;
    this.loading.set(true); this.error.set("");
    try {
      const [items, categories, merchants, page, effective, selected] = await Promise.all([
        this.api.get<BudgetItem[]>(`households/${home}/budgets/items?includeRetired=true`),
        this.api.get<SpendingSetupEntry[]>(`households/${home}/spending/categories?includeRetired=true`),
        this.api.get<SpendingSetupEntry[]>(`households/${home}/spending/merchants?includeRetired=true`),
        this.read<BudgetHistory>(this.path(home) + "?pageSize=25"),
        this.read<BudgetEffective>(this.path(home) + `/effective?asOf=${asOf}`),
        selectedId ? this.read<BudgetPlan>(this.path(home) + "/" + selectedId) : Promise.resolve(null),
      ]);
      if (!this.current(home, generation, version)) return;
      this.items.set(items); this.categories.set(categories); this.merchants.set(merchants);
      this.history.set(page!.records); this.nextBefore.set(page!.nextBefore); this.effective.set(effective);
      this.selected.set(selected ?? effective?.snapshot ?? null); this.loadedAsOf.set(asOf); this.revisionNeedsReload.set(false);
    } catch (error) { if (this.current(home, generation, version)) this.error.set(message(error)); }
    finally { if (this.current(home, generation, version)) this.loading.set(false); }
  }
  async more() {
    const home = this.home, generation = this.generation, before = this.nextBefore();
    if (!home || !before || !this.canWrite()) return;
    const version = ++this.version; this.loading.set(true); this.error.set("");
    try {
      const page = await this.read<BudgetHistory>(this.path(home) + `?pageSize=25&before=${before}`);
      if (this.current(home, generation, version)) {
        this.history.update(rows => [...rows, ...page!.records.filter(plan => !rows.some(row => row.id === plan.id))]); this.nextBefore.set(page!.nextBefore);
      }
    } catch (error) { if (this.current(home, generation, version)) this.error.set(message(error)); }
    finally { if (this.current(home, generation, version)) this.loading.set(false); }
  }
  async view(plan: BudgetPlan) {
    const home = this.home, generation = this.generation;
    if (!home || !this.canWrite() || plan.householdId !== home || !this.discardAllowed()) return;
    this.resetDraft(); const version = ++this.version; this.loading.set(true); this.error.set("");
    try {
      const saved = await this.read<BudgetPlan>(this.path(home) + "/" + plan.id);
      if (this.current(home, generation, version)) this.selected.set(saved);
    } catch (error) { if (this.current(home, generation, version)) this.error.set(message(error)); }
    finally { if (this.current(home, generation, version)) this.loading.set(false); }
  }
  async startNew() {
    if (!this.canWrite() || !this.discardAllowed()) return;
    this.resetDraft(); this.creating.set(true); this.effectiveFrom.set(localToday());
    this.initialDate = this.effectiveFrom();
    this.baseline = this.fingerprint();
    await this.loadSource();
  }
  async loadSource() {
    const home = this.home, generation = this.generation, date = this.effectiveFrom();
    if (!home || !this.canWrite() || !this.creating() || this.locked() || !validDate(date)) return;
    if (this.draftDirty() && !window.confirm("Replace these draft amounts with the plan before the chosen date?")) return;
    const version = ++this.version; this.sourceDate.set(""); this.loading.set(true); this.writeError.set("");
    try {
      const [source, end, items, categories, merchants] = await Promise.all([
        this.read<BudgetPlan>(this.path(home) + `/copy-source?effectiveFrom=${date}`), this.nextPlanDate(home, date),
        this.api.get<BudgetItem[]>(`households/${home}/budgets/items?includeRetired=true`),
        this.api.get<SpendingSetupEntry[]>(`households/${home}/spending/categories?includeRetired=true`),
        this.api.get<SpendingSetupEntry[]>(`households/${home}/spending/merchants?includeRetired=true`),
      ]);
      if (!this.current(home, generation, version)) return;
      if (source?.entries.some(entry => !items.some(item => item.id === entry.budgetItemId)))
        throw new Error("Outgoing definitions changed while preparing this plan. Reload previous amounts before saving.");
      this.items.set(items); this.categories.set(categories); this.merchants.set(merchants);
      this.source.set(source); this.sourceDate.set(date); this.draftEnd.set(end);
      this.rows.set(items.filter(item => !item.isRetired || source?.entries.some(entry => entry.budgetItemId === item.id)).map<PlanRow>(item => {
        const previous = source?.entries.find(entry => entry.budgetItemId === item.id) ?? null;
        const context: BudgetEntry = previous ?? { id: "", budgetItemId: item.id, itemCode: item.code, itemName: item.name,
          expenseCategoryId: item.expenseCategoryId, expenseCategoryName: categories.find(row => row.id === item.expenseCategoryId)?.name ?? "Category unavailable",
          merchantId: item.merchantId, merchantName: merchants.find(row => row.id === item.merchantId)?.name ?? null,
          plannedAmount: null, currencyCode: item.currencyCode, frequency: item.frequency, intervalCount: item.intervalCount, notes: item.notes,
          valueOrigin: "Unrecorded", lastConfirmedDate: null, carriedFromBudgetEntryId: null };
        return { item, context, source: previous, included: true, mode: previous ? "Carry" : "Confirm", amount: previous?.plannedAmount ?? "", confirmedOn: localToday(), refreshFromSetup: false };
      }).sort((a, b) => a.context.itemName.localeCompare(b.context.itemName)));
      this.baseline = this.fingerprint();
    } catch (error) { if (this.current(home, generation, version)) this.writeError.set(message(error)); }
    finally { if (this.current(home, generation, version)) this.loading.set(false); }
  }
  changeDate(date: string) { if (!this.locked() && !this.saving() && !this.loading()) this.effectiveFrom.set(date); }
  changeRow(id: string, change: Partial<Pick<PlanRow, "included" | "mode" | "amount" | "confirmedOn">>) {
    if (this.locked() || !this.canWrite()) return;
    this.rows.update(rows => rows.map(row => row.item.id === id ? { ...row, ...change } : row)); this.writeError.set("");
  }
  useSetup(row: PlanRow, use: boolean) {
    if (!row.source || !this.canWrite() || this.locked()) return;
    const item = row.item, saved = row.source;
    const context: BudgetEntry = use ? { ...saved, itemCode: item.code, itemName: item.name,
      expenseCategoryId: item.expenseCategoryId, expenseCategoryName: this.categories().find(category => category.id === item.expenseCategoryId)?.name ?? "Category unavailable",
      merchantId: item.merchantId, merchantName: this.merchants().find(merchant => merchant.id === item.merchantId)?.name ?? null,
      currencyCode: item.currencyCode, frequency: item.frequency, intervalCount: item.intervalCount, notes: item.notes } : saved;
    const compatible = context.currencyCode === row.context.currencyCode && context.frequency === row.context.frequency && context.intervalCount === row.context.intervalCount;
    this.rows.update(rows => rows.map(value => value.item.id === item.id ? { ...value, context, refreshFromSetup: use,
      ...(compatible ? {} : { mode: "Confirm" as BudgetMode, amount: "" }) } : value));
  }
  canCarry(row: PlanRow) {
    return !!row.source && row.source.currencyCode === row.context.currencyCode && row.source.frequency === row.context.frequency && row.source.intervalCount === row.context.intervalCount;
  }
  draftTotals() {
    return planTotals(this.rows().filter(row => row.included).map(row => ({ ...row.context,
      plannedAmount: row.mode === "Carry" ? row.source!.plannedAmount : row.mode === "Confirm" && this.amountValid(row.amount) ? row.amount : null })));
  }
  validDraft() {
    return validDate(this.effectiveFrom()) && this.sourceDate() === this.effectiveFrom() && this.rows().every(row => !row.included ||
      (row.mode === "Carry" ? this.canCarry(row) : row.mode === "Unrecorded" || (row.mode === "Confirm" && this.amountValid(row.amount) && validDate(row.confirmedOn))));
  }
  private fingerprint() { return JSON.stringify({ date: this.effectiveFrom(), notes: this.notes(), rows: this.rows() }); }
  private draftDirty() { return this.creating() && (!!this.notes() || this.effectiveFrom() !== this.initialDate || (!!this.baseline && this.baseline !== this.fingerprint())); }
  private resetDraft() {
    this.creating.set(false); this.source.set(null); this.sourceDate.set(""); this.draftEnd.set(null); this.rows.set([]); this.notes.set("");
    this.baseline = ""; this.initialDate = ""; this.pending = null; this.locked.set(false); this.correcting.set(null); this.correctionUncertain.set(false); this.writeError.set("");
  }
  cancel() { if (!this.saving() && !this.loading() && this.discardAllowed()) this.resetDraft(); }
  private async nextPlanDate(home: string, date: string) {
    let before: string | null = null, end: string | null = null;
    do {
      const page: BudgetHistory | null = await this.read<BudgetHistory>(this.path(home) + `?pageSize=100${before ? "&before=" + before : ""}`);
      for (const plan of page!.records) if (plan.effectiveFrom > date && (!end || plan.effectiveFrom < end)) end = plan.effectiveFrom;
      if (page!.records.some(plan => plan.effectiveFrom <= date)) break;
      before = page!.nextBefore;
    } while (before);
    return end;
  }
  async savePlan(form?: NgForm) {
    const home = this.home, generation = this.generation;
    if (!home || !this.canWrite() || !this.creating() || form?.invalid || (!this.pending && !this.validDraft())) return;
    if (!this.pending) {
      if (this.rows().every(row => !row.included) && !window.confirm("Save an empty plan? It replaces all earlier outgoing amounts from its effective date.")) return;
      if (this.effectiveFrom() < localToday() && !window.confirm(`Save this backdated plan from ${this.date(this.effectiveFrom())}? It replaces the previous plan for this interval${this.draftEnd() ? ", ending before " + this.date(this.draftEnd()!) : " until a later plan takes effect"}.`)) return;
      this.pending = { id: crypto.randomUUID(), effectiveFrom: this.effectiveFrom(), notes: this.notes().trim() || null,
        copiedFromBudgetSnapshotId: this.source()?.id ?? null,
        entries: this.rows().filter(row => row.included).map(row => ({ budgetItemId: row.item.id, mode: row.mode, refreshFromSetup: row.refreshFromSetup,
          ...(row.mode === "Confirm" ? { plannedAmount: planAmount(row.amount)!, confirmedOn: row.confirmedOn } : {}) })) };
    }
    this.saving.set(true); this.locked.set(true); this.writeError.set(""); this.notice.set("");
    try {
      const plan = parseBudget<BudgetPlan>(await this.api.sendText("POST", this.path(home), budgetJson(this.pending)));
      if (!this.current(home, generation)) return;
      this.resetDraft(); this.selected.set(plan); this.notice.set("Outgoing plan saved.");
      this.saving.set(false); await this.load();
    } catch (error) {
      if (!this.current(home, generation)) return;
      const uncertain = !(error instanceof HttpErrorResponse) || error.status === 0 || error.status >= 500;
      if (!uncertain) { this.pending = null; this.locked.set(false); }
      this.writeError.set(message(error) + (uncertain ? " The save may have succeeded. Retry the unchanged request safely, or reload to check saved plans." : ""));
    } finally { if (this.current(home, generation)) this.saving.set(false); }
  }
  beginCorrection(entry: BudgetEntry) {
    if (!this.canWrite() || !this.selected()?.entries.some(row => row.id === entry.id) || !this.discardAllowed()) return;
    this.resetDraft(); this.correcting.set(entry);
    this.correction.set({ amount: entry.plannedAmount ?? "", confirmedOn: localToday(), notes: entry.notes ?? "" });
  }
  correctionValid() { const value = this.correction(); return value.amount === "" || (this.amountValid(value.amount) && validDate(value.confirmedOn)); }
  changeCorrection(field: "amount" | "confirmedOn" | "notes", value: string) {
    if (!this.canWrite() || this.correctionUncertain()) return;
    this.correction.update(draft => ({ ...draft, [field]: value }));
  }
  async saveCorrection(form?: NgForm) {
    const home = this.home, generation = this.generation, plan = this.selected(), entry = this.correcting(), draft = this.correction();
    if (!home || !plan || !entry || !this.canWrite() || this.correctionUncertain() || !this.correctionValid() || form?.invalid) return;
    this.saving.set(true); this.writeError.set(""); this.notice.set("");
    try {
      const updated = parseBudget<BudgetPlan>(await this.api.sendText("PUT", this.path(home) + `/${plan.id}/entries/${entry.id}/amount`, budgetJson({
        expectedRevision: plan.revision, plannedAmount: draft.amount === "" ? null : planAmount(draft.amount),
        confirmedOn: draft.amount === "" ? null : draft.confirmedOn, notes: draft.notes.trim() || null,
      })));
      if (!this.current(home, generation)) return;
      this.correcting.set(null); this.selected.set(updated); this.notice.set("Saved amount corrected.");
      this.saving.set(false); await this.load();
    } catch (error) {
      if (!this.current(home, generation)) return;
      const uncertain = !(error instanceof HttpErrorResponse) || error.status === 0 || error.status >= 500 || error.status === 409;
      this.correctionUncertain.set(uncertain);
      this.revisionNeedsReload.set(uncertain);
      this.writeError.set(message(error) + (uncertain ? " Reload the saved plan before editing again. Your entered correction is shown below." : ""));
    } finally { if (this.current(home, generation)) this.saving.set(false); }
  }
  hasUnsavedChanges() { return this.locked() || this.draftDirty() || !!this.correcting(); }
  private discardAllowed() {
    return !this.hasUnsavedChanges() || window.confirm(this.locked() ? "The save may already exist. Discard this retry and reload saved plans to check?" : "Discard unsaved outgoing plan changes?");
  }
  canLeave() { return !this.saving() && this.discardAllowed(); }
  @HostListener("window:beforeunload", ["$event"])
  beforeUnload(event: BeforeUnloadEvent) { if (this.saving() || this.hasUnsavedChanges()) { event.preventDefault(); event.returnValue = ""; } }
}
