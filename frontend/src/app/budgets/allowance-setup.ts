import { Component, DestroyRef, ElementRef, effect, inject, input, output, signal, untracked, viewChild } from "@angular/core";
import { HttpErrorResponse } from "@angular/common/http";
import { FormsModule, NgForm } from "@angular/forms";
import { MatButtonModule } from "@angular/material/button";
import { MatInputModule } from "@angular/material/input";
import { MatSelect, MatSelectModule } from "@angular/material/select";
import { Api } from "../core/api";
import { message } from "../core/api-errors";
import { SpendingCurrency, SpendingSetupEntry } from "../spending/models";
import { BudgetFrequency, BudgetItem, budgetRecurrence } from "./models";
import { BudgetInterval, budgetInterval } from "./interval";

interface Draft { category: string; currency: string; frequency: BudgetFrequency; interval: string; notes: string; }
const empty = (): Draft => ({ category: "", currency: "GBP", frequency: "Month", interval: "1", notes: "" });
const fromItem = (item: BudgetItem): Draft => ({ category: item.expenseCategoryId, currency: item.currencyCode,
  frequency: item.frequency, interval: String(item.intervalCount), notes: item.notes ?? "" });
const formValues = (draft: Draft) => ({ allowanceCategory: draft.category, allowanceCurrency: draft.currency,
  allowanceFrequency: draft.frequency, allowanceInterval: draft.interval, allowanceNotes: draft.notes });

@Component({ selector: "app-allowance-setup", imports: [FormsModule, MatButtonModule, MatInputModule, MatSelectModule, BudgetInterval],
  templateUrl: "allowance-setup.html", styles: `
    .fields { display: grid; grid-template-columns: repeat(auto-fit, minmax(210px, 1fr)); gap: 24px 16px; align-items: start; }
    details { margin: 16px 0; } summary { cursor: pointer; margin-bottom: 16px; }
    ul { list-style: none; padding: 0; } li { padding: 12px 0; border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .actions { margin-top: 16px; } textarea { resize: vertical; }
  ` })
export class AllowanceSetup {
  private readonly api = inject(Api);
  private readonly form = viewChild(NgForm);
  private readonly categoryControl = viewChild(MatSelect);
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  readonly household = input.required<string>();
  readonly items = input.required<BudgetItem[]>();
  readonly categories = input.required<SpendingSetupEntry[]>();
  readonly currencies = input.required<SpendingCurrency[]>();
  readonly disabled = input(false);
  readonly inDraft = input(false);
  readonly changed = output<BudgetItem>();
  readonly selected = output<BudgetItem>();
  readonly inventory = output<BudgetItem[]>();
  readonly draft = signal(empty());
  readonly editing = signal<BudgetItem | null>(null);
  readonly saving = signal(false);
  readonly error = signal("");
  readonly notice = signal("");
  readonly needsReload = signal(false);
  readonly match = signal<BudgetItem | null>(null);
  readonly expanded = signal(false);
  show() {
    if (!this.canWrite()) return;
    const home = this.household(), generation = this.generation;
    this.expanded.set(true); this.host.nativeElement.scrollIntoView?.({ block: "center", behavior: "smooth" });
    setTimeout(() => { if (this.current(home, generation)) this.categoryControl()?.focus(); }, 0);
  }
  readonly recurrence = budgetRecurrence;
  private home = "";
  private generation = 0;
  private destroyed = false;
  constructor() {
    effect(() => { const home = this.household(); untracked(() => { if (this.home !== home) {
      this.home = home; this.generation++; this.saving.set(false); this.reset();
    } }); });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.generation++; });
  }
  private current(home: string, generation: number) { return !this.destroyed && this.household() === home && this.generation === generation; }
  rows() { return this.items().filter(item => item.kind === "Allowance").sort((a, b) => this.categoryName(a.expenseCategoryId).localeCompare(this.categoryName(b.expenseCategoryId)) || a.currencyCode.localeCompare(b.currencyCode) || a.id.localeCompare(b.id)); }
  categoryName(id: string) { return this.categories().find(category => category.id === id)?.name ?? "Category unavailable"; }
  canWrite() { return !this.disabled() && !this.saving() && !this.needsReload(); }
  valid() {
    const value = this.draft(); return this.categories().some(category => category.id === value.category) &&
      (this.currencies().some(currency => currency.code === value.currency) || this.editing()?.currencyCode === value.currency) &&
      ["Week", "Month", "Year"].includes(value.frequency) && budgetInterval(value.interval) !== null;
  }
  historicalCurrency() { const item = this.editing(); return item && !this.currencies().some(currency => currency.code === item.currencyCode) ? item.currencyCode : null; }
  change(field: keyof Draft, value: string) { if (this.canWrite()) { this.draft.update(draft => ({ ...draft, [field]: value })); this.error.set(""); this.match.set(null); } }
  reset(form?: NgForm) {
    if (this.saving()) return;
    this.draft.set(empty()); this.editing.set(null); this.error.set(""); this.notice.set(""); this.needsReload.set(false); this.match.set(null); (form ?? this.form())?.resetForm(formValues(empty()));
  }
  hasUnsavedChanges() { return this.needsReload() || JSON.stringify(this.draft()) !== JSON.stringify(this.editing() ? fromItem(this.editing()!) : empty()); }
  edit(item: BudgetItem, form?: NgForm) {
    if (!this.canWrite() || (this.hasUnsavedChanges() && !window.confirm("Discard these unsaved allowance defaults?"))) return;
    this.editing.set(item); this.draft.set(fromItem(item)); this.error.set(""); this.notice.set(""); form?.resetForm(formValues(this.draft()));
  }
  cancel(form?: NgForm) { if (!this.saving() && (!this.hasUnsavedChanges() || window.confirm("Discard these unsaved allowance defaults?"))) this.reset(form); }
  async save(form?: NgForm) {
    if (!this.canWrite() || !this.valid() || form?.invalid) return;
    const home = this.household(), generation = this.generation, value = this.draft(), editing = this.editing();
    const existing = !editing ? this.items().find(item => item.kind === "Allowance" && !item.isRetired && item.expenseCategoryId === value.category && item.currencyCode === value.currency) : null;
    if (existing) { this.selected.emit(existing); this.reset(form); this.notice.set("Existing allowance selected. Edit its defaults below if needed."); return; }
    this.saving.set(true); this.error.set(""); this.notice.set("");
    try {
      const item = await this.api.send<BudgetItem>(editing ? "PUT" : "POST", `households/${home}/budgets/items${editing ? "/" + editing.id + "/details" : ""}`, {
        ...(editing ? { name: editing.name, merchantId: null, paymentAnchorDate: null } : { kind: "Allowance" }),
        expenseCategoryId: value.category, currencyCode: value.currency, frequency: value.frequency, intervalCount: Number(value.interval), notes: value.notes.trim() || null,
      });
      if (!this.current(home, generation)) return;
      this.changed.emit(item); if (!editing) this.selected.emit(item);
      this.saving.set(false); this.reset(form); this.notice.set(editing ? "Allowance defaults updated. Saved plan amounts are unchanged." : "Allowance definition added. Enter its amount in a budget plan.");
    } catch (error) { if (this.current(home, generation)) {
      this.error.set(message(error)); this.needsReload.set(!(error instanceof HttpErrorResponse) || error.status === 0 || error.status >= 500 || error.status === 409);
    } } finally { if (this.current(home, generation)) this.saving.set(false); }
  }
  async retire(item: BudgetItem) {
    if (!this.canWrite()) return;
    const home = this.household(), generation = this.generation; this.saving.set(true); this.error.set(""); this.notice.set("");
    try {
      const saved = await this.api.send<BudgetItem>("PUT", `households/${home}/budgets/items/${item.id}/retirement`, { isRetired: !item.isRetired });
      if (this.current(home, generation)) { this.changed.emit(saved); this.notice.set("Allowance definition updated. Saved plans are unchanged."); }
    } catch (error) { if (this.current(home, generation)) {
      this.error.set(message(error)); this.needsReload.set(!(error instanceof HttpErrorResponse) || error.status === 0 || error.status >= 500 || error.status === 409);
    } } finally { if (this.current(home, generation)) this.saving.set(false); }
  }
  async reload() {
    if (this.saving() || this.disabled()) return;
    const home = this.household(), generation = this.generation; this.saving.set(true); this.error.set("");
    try {
      const items = await this.api.get<BudgetItem[]>(`households/${home}/budgets/items?includeRetired=true`);
      if (!this.current(home, generation)) return;
      this.inventory.emit(items); const value = this.draft();
      this.match.set(!this.editing() ? items.find(item => item.kind === "Allowance" && !item.isRetired && item.expenseCategoryId === value.category && item.currencyCode === value.currency) ?? null : null);
      if (this.editing()) this.editing.set(items.find(item => item.id === this.editing()!.id) ?? this.editing());
      this.needsReload.set(false); this.notice.set(this.match() ? "An active allowance exists. Use it or edit its defaults below." : "Definitions reloaded. Your entered defaults have been retained; check them before saving.");
    } catch (error) { if (this.current(home, generation)) this.error.set(message(error)); }
    finally { if (this.current(home, generation)) this.saving.set(false); }
  }
  useMatch(form?: NgForm) { const item = this.match(); if (item && this.canWrite()) { this.selected.emit(item); this.reset(form); } }
}
