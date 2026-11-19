import { Component, DestroyRef, HostListener, TemplateRef, effect, inject, signal, untracked, viewChild, viewChildren } from "@angular/core";
import { NgTemplateOutlet } from "@angular/common";
import { FormsModule, NgForm } from "@angular/forms";
import { MatButtonModule } from "@angular/material/button";
import { MatDialog, MatDialogModule, MatDialogRef } from "@angular/material/dialog";
import { MatInputModule } from "@angular/material/input";
import { MatSelectModule } from "@angular/material/select";
import { Api } from "../core/api";
import { message } from "../core/api-errors";
import { Workspace } from "../core/workspace";
import { SpendingCurrency, SpendingSetupEntry, SpendingSetupStatus } from "../spending/models";
import { SpendingNavigation } from "../spending/navigation";
import { BudgetItem, BudgetItemDetails, BudgetItemDraft, budgetRecurrence } from "./models";
import { localToday, spendingDate } from "../spending/amounts";
import { nextPaymentDate, paymentTiming, validPaymentAnchor } from "./payment-schedule";
import { BudgetPaymentDate } from "./payment-date";
import { BudgetInterval, budgetInterval } from "./interval";

const emptyDraft = (): BudgetItemDraft => ({ code: "", name: "", expenseCategoryId: "", merchantId: "",
  currencyCode: "GBP", frequency: "Month", intervalCount: "1", notes: "", paymentAnchorDate: "" });
const itemDraft = (item: BudgetItem): BudgetItemDraft => ({ code: item.code, name: item.name,
  expenseCategoryId: item.expenseCategoryId, merchantId: item.merchantId ?? "", currencyCode: item.currencyCode,
  frequency: item.frequency, intervalCount: String(item.intervalCount), notes: item.notes ?? "", paymentAnchorDate: item.paymentAnchorDate ?? "" });

@Component({
  imports: [FormsModule, NgTemplateOutlet, MatButtonModule, MatDialogModule, MatInputModule, MatSelectModule, SpendingNavigation, BudgetInterval, BudgetPaymentDate],
  templateUrl: "outgoings.html", styleUrl: "outgoings.scss",
})
export class RegularOutgoings {
  readonly w = inject(Workspace);
  private readonly api = inject(Api);
  private readonly dialogs = inject(MatDialog);
  private readonly editPanel = viewChild.required<TemplateRef<unknown>>("editPanel");
  private readonly forms = viewChildren(NgForm);
  private editor?: MatDialogRef<unknown>;
  private home: string | null = null;
  private generation = 0;
  private version = 0;
  private destroyed = false;
  readonly rows = signal<BudgetItem[]>([]);
  readonly categories = signal<SpendingSetupEntry[]>([]);
  readonly merchants = signal<SpendingSetupEntry[]>([]);
  readonly currencies = signal<SpendingCurrency[]>([]);
  readonly loading = signal(false);
  readonly error = signal("");
  readonly saving = signal(false);
  readonly notice = signal("");
  readonly actionError = signal("");
  readonly search = signal("");
  readonly status = signal<SpendingSetupStatus>("active");
  readonly category = signal("");
  readonly createDraft = signal(emptyDraft());
  readonly createError = signal("");
  readonly editing = signal<BudgetItem | null>(null);
  readonly editDraft = signal(emptyDraft());
  readonly editError = signal("");
  readonly recurrence = budgetRecurrence;
  readonly asOf = signal(localToday());
  readonly date = spendingDate;
  readonly timing = paymentTiming;
  @HostListener("window:focus")
  refreshDate() { this.asOf.set(localToday()); }
  nextExpected(item: BudgetItem) { return nextPaymentDate(item.paymentAnchorDate, item.frequency, item.intervalCount, this.asOf()); }
  paymentPreview(edit: boolean) {
    if (edit && this.editing()?.isRetired) return "";
    const draft = this.draft(edit);
    if (!draft.paymentAnchorDate || !validPaymentAnchor(draft.paymentAnchorDate) || budgetInterval(draft.intervalCount) === null) return "";
    const next = nextPaymentDate(draft.paymentAnchorDate, draft.frequency, Number(draft.intervalCount), this.asOf());
    return next ? "Next expected: " + this.date(next) : "No further occurrence within the supported date range.";
  }
  readonly codePattern = "\\s*[A-Za-z0-9][A-Za-z0-9._-]{0,63}\\s*";

  constructor() {
    effect(() => {
      const home = this.w.current()?.id ?? null;
      untracked(() => {
        if (home === this.home) return;
        this.home = home; this.generation++;
        this.closeEditor(); this.rows.set([]); this.categories.set([]); this.merchants.set([]); this.currencies.set([]);
        this.loading.set(false); this.saving.set(false); this.error.set(""); this.actionError.set(""); this.notice.set("");
        this.clearCreate();
        for (const form of this.forms()) form.resetForm(this.createDraft());
        this.search.set(""); this.status.set("active"); this.category.set("");
        if (home) void this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.generation++; this.editor?.close(); });
  }
  private current(home: string, generation: number) { return !this.destroyed && generation === this.generation && this.w.current()?.id === home; }
  canWrite() { return !!this.home && this.home === this.w.current()?.id && !this.saving() && !this.loading() && !this.error(); }
  async load() {
    const home = this.home, generation = this.generation;
    if (!home || !this.current(home, generation) || this.saving()) return;
    const version = ++this.version, current = () => this.current(home, generation) && version === this.version;
    this.refreshDate(); this.loading.set(true); this.error.set("");
    try {
      const [rows, categories, merchants, currencies] = await Promise.all([
        this.api.get<BudgetItem[]>(`households/${home}/budgets/items?includeRetired=true`),
        this.api.get<SpendingSetupEntry[]>(`households/${home}/spending/categories?includeRetired=true`),
        this.api.get<SpendingSetupEntry[]>(`households/${home}/spending/merchants?includeRetired=true`),
        this.api.get<SpendingCurrency[]>(`households/${home}/spending/currencies`),
      ]);
      if (current()) { this.rows.set(rows); this.categories.set(categories); this.merchants.set(merchants); this.currencies.set(currencies); }
    } catch (error) { if (current()) this.error.set(message(error)); }
    finally { if (current()) this.loading.set(false); }
  }
  categoryName(id: string) { return this.categories().find(row => row.id === id)?.name ?? "Category unavailable"; }
  merchantName(id: string | null) { return this.merchants().find(row => row.id === id)?.name ?? "Merchant unavailable"; }
  visibleRows() {
    const search = this.search().trim().toLocaleLowerCase();
    return this.rows().filter(row => (this.status() === "all" || row.isRetired === (this.status() === "retired")) &&
      (!this.category() || row.expenseCategoryId === this.category()) &&
      [row.name, row.code, row.currencyCode, row.notes ?? "", this.categoryName(row.expenseCategoryId),
        row.merchantId ? this.merchantName(row.merchantId) : "", this.recurrence(row.frequency, row.intervalCount), row.paymentAnchorDate ?? "", row.paymentAnchorDate ? this.timing(row.paymentAnchorDate, row.frequency) : ""]
        .some(value => value.toLocaleLowerCase().includes(search)))
      .sort((a, b) => a.name.localeCompare(b.name) || a.code.localeCompare(b.code));
  }
  draft(edit: boolean) { return edit ? this.editDraft() : this.createDraft(); }
  change(edit: boolean, field: keyof BudgetItemDraft, value: string) {
    if (this.saving()) return;
    (edit ? this.editDraft : this.createDraft).update(draft => ({ ...draft, [field]: value }));
    (edit ? this.editError : this.createError).set("");
  }
  historicalCurrency() {
    const item = this.editing();
    return item && !this.currencies().some(currency => currency.code === item.currencyCode) ? item.currencyCode : null;
  }
  valid(edit: boolean) {
    const draft = this.draft(edit);
    return !!draft.name.trim() && /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/.test(draft.code.trim()) &&
      this.categories().some(row => row.id === draft.expenseCategoryId) &&
      (!draft.merchantId || this.merchants().some(row => row.id === draft.merchantId)) &&
      (this.currencies().some(row => row.code === draft.currencyCode) || (edit && this.editing()?.currencyCode === draft.currencyCode)) &&
      ["Week", "Month", "Year"].includes(draft.frequency) && budgetInterval(draft.intervalCount) !== null && validPaymentAnchor(draft.paymentAnchorDate);
  }
  clearCreate(form?: NgForm) {
    if (this.saving()) return;
    const draft = emptyDraft(); this.createDraft.set(draft); this.createError.set(""); form?.resetForm(draft);
  }
  editDirty() { const item = this.editing(); return !!item && JSON.stringify(this.editDraft()) !== JSON.stringify(itemDraft(item)); }
  beginEdit(item: BudgetItem) {
    if (!this.canWrite() || item.householdId !== this.home || this.editor) return;
    this.editing.set(item); this.editDraft.set(itemDraft(item)); this.editError.set("");
    const editor = this.dialogs.open(this.editPanel(), { position: { right: "0", top: "0" }, width: "540px", maxWidth: "100vw", height: "100dvh", maxHeight: "100dvh",
      panelClass: "edit-sidebar", autoFocus: 'input[name="name"]', disableClose: true, closeOnNavigation: false });
    this.editor = editor;
    const dismiss = () => { if (!this.saving() && (!this.editDirty() || window.confirm("Discard this unsaved outgoing edit?"))) this.closeEditor(); };
    editor.backdropClick().subscribe(dismiss);
    editor.keydownEvents().subscribe(event => { if (event.key === "Escape") { event.preventDefault(); dismiss(); } });
    editor.afterClosed().subscribe(() => { if (this.editor === editor) this.closeEditor(); });
  }
  cancelEdit() { if (!this.saving()) this.closeEditor(); }
  private closeEditor() {
    const editor = this.editor; this.editor = undefined; editor?.close();
    this.editing.set(null); this.editDraft.set(emptyDraft()); this.editError.set("");
  }
  async submit(edit: boolean, form?: NgForm) {
    if (form?.invalid || !this.canWrite() || !this.valid(edit) || (edit && (!this.editing() || !this.editDirty()))) return;
    const home = this.home!, generation = this.generation, item = this.editing(), draft = this.draft(edit);
    const details: BudgetItemDetails = { name: draft.name, expenseCategoryId: draft.expenseCategoryId, merchantId: draft.merchantId || null,
      currencyCode: draft.currencyCode, frequency: draft.frequency, intervalCount: Number(draft.intervalCount), notes: draft.notes.trim() ? draft.notes : null, paymentAnchorDate: draft.paymentAnchorDate || null };
    this.saving.set(true); this.notice.set(""); this.actionError.set(""); (edit ? this.editError : this.createError).set("");
    try {
      const saved = await this.api.send<BudgetItem>(edit ? "PUT" : "POST", `households/${home}/budgets/items${edit ? "/" + item!.id + "/details" : ""}`,
        edit ? details : { ...details, code: draft.code });
      if (!this.current(home, generation)) return;
      this.rows.update(rows => [...rows.filter(row => row.id !== saved.id), saved]);
      if (edit) this.closeEditor();
      else { const draft = emptyDraft(); this.createDraft.set(draft); form?.resetForm(draft); }
      this.notice.set(edit ? "Outgoing updated." : "Outgoing added.");
    } catch (error) { if (this.current(home, generation)) (edit ? this.editError : this.createError).set(message(error)); }
    finally { if (this.current(home, generation)) this.saving.set(false); }
  }
  async setRetirement(item: BudgetItem) {
    if (!this.canWrite() || item.householdId !== this.home) return;
    const home = this.home!, generation = this.generation;
    this.saving.set(true); this.actionError.set(""); this.notice.set("");
    try {
      const saved = await this.api.send<BudgetItem>("PUT", `households/${home}/budgets/items/${item.id}/retirement`, { isRetired: !item.isRetired });
      if (this.current(home, generation)) { this.rows.update(rows => rows.map(row => row.id === saved.id ? saved : row)); this.notice.set(`${saved.name} ${saved.isRetired ? "retired" : "restored"}.`); }
    } catch (error) { if (this.current(home, generation)) this.actionError.set(message(error)); }
    finally { if (this.current(home, generation)) this.saving.set(false); }
  }
  hasUnsavedChanges() { return JSON.stringify(this.createDraft()) !== JSON.stringify(emptyDraft()) || this.editDirty(); }
  canLeave() { return !this.saving() && (!this.hasUnsavedChanges() || window.confirm("Discard unsaved regular outgoing changes?")); }
  @HostListener("window:beforeunload", ["$event"])
  beforeUnload(event: BeforeUnloadEvent) {
    if (this.saving() || this.hasUnsavedChanges()) { event.preventDefault(); event.returnValue = ""; }
  }
}
