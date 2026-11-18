import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from "@angular/core";
import { HttpErrorResponse } from "@angular/common/http";
import { FormsModule } from "@angular/forms";
import { MatButtonModule } from "@angular/material/button";
import { MatInputModule } from "@angular/material/input";
import { MatSelect, MatSelectModule } from "@angular/material/select";
import { Api } from "../core/api";
import { message } from "../core/api-errors";
import { MoneyInput } from "../core/money";
import { Workspace } from "../core/workspace";
import { SpendingCaptureRequest, SpendingCurrency, SpendingItem, SpendingPromotion, SpendingRecord, SpendingReplacementRequest, SpendingSetupEntry, SpendingUsualPrice } from "./models";
import { decimalAmount, localToday, minorAmount, safeAmount, suggestedAmount, validSpendingDate } from "./amounts";

export interface CaptureLineDraft {
  key: string; itemId: string; categoryId: string; quantity: string; unitPrice: string; amount: string;
  description: string; reference: string; promotion: SpendingPromotion;
  autoAmount: boolean; priceTouched: boolean; categoryTouched: boolean;
  receiptDescription: string; notes: string; unitKey: string; unitLabel: string;
}
const newLine = (): CaptureLineDraft => ({ key: crypto.randomUUID(), itemId: "", categoryId: "", quantity: "", unitPrice: "", amount: "",
  description: "", reference: "", promotion: "Unknown", autoAmount: true, priceTouched: false, categoryTouched: false,
  receiptDescription: "", notes: "", unitKey: "", unitLabel: "" });
const blankDraft = () => ({ date: localToday(), merchantId: "", currencyCode: "", receiptTotal: "", reference: "", notes: "", lines: [newLine()] });
type CaptureDraft = ReturnType<typeof blankDraft>;

@Component({
  selector: "app-spending-capture",
  imports: [FormsModule, MoneyInput, MatButtonModule, MatInputModule, MatSelectModule],
  templateUrl: "capture.html", styleUrl: "capture.scss",
})
export class SpendingCapture {
  readonly categories = input<SpendingSetupEntry[]>([]);
  readonly merchants = input<SpendingSetupEntry[]>([]);
  readonly items = input<SpendingItem[]>([]);
  readonly currencies = input<SpendingCurrency[]>([]);
  readonly disabled = input(false);
  readonly record = input<SpendingRecord | null>(null);
  readonly saved = output<SpendingRecord>();
  readonly reloaded = output<SpendingRecord>();
  readonly cancelled = output<void>();
  private readonly api = inject(Api);
  private readonly w = inject(Workspace);
  private home: string | null = null;
  private inputRecord: SpendingRecord | null | undefined;
  private generation = 0;
  private destroyed = false;
  private id: string | null = null;
  private baseline = "";
  private originalDraft: CaptureDraft | null = null;
  private priceVersions = new Map<string, number>();
  readonly draft = signal(blankDraft());
  readonly formVersion = signal(0);
  readonly saving = signal(false);
  readonly checking = signal(false);
  readonly frozen = signal<SpendingCaptureRequest | null>(null);
  readonly error = signal("");
  readonly priceStates = signal<Record<string, string>>({});
  readonly editing = signal<SpendingRecord | null>(null);
  readonly editNeedsReload = signal(false);

  constructor() {
    effect(() => {
      const home = this.w.current()?.id ?? null;
      const record = this.record();
      untracked(() => {
        if (home === this.home && record === this.inputRecord) return;
        this.home = home;
        this.inputRecord = record;
        this.reset(record?.householdId === home ? record : null);
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.generation++; });
    this.baseline = this.persisted(this.draft());
  }
  private current(generation: number, home: string) { return !this.destroyed && generation === this.generation && this.w.current()?.id === home; }
  locked() { return this.saving() || this.checking() || !!this.frozen() || this.editNeedsReload() || this.disabled(); }
  busy() { return this.saving() || this.checking(); }
  minorUnits() { return this.editing()?.currencyMinorUnits ?? this.currencies().find(row => row.code === this.draft().currencyCode)?.minorUnits ?? null; }
  set(field: Exclude<keyof CaptureDraft, "lines">, value: string) {
    if (this.locked()) return;
    this.draft.update(draft => ({ ...draft, [field]: value })); this.error.set("");
    if (field === "merchantId") this.refreshPrices();
  }
  setCurrency(value: string, picker?: MatSelect) {
    if (this.locked() || this.editing()) { if (picker) picker.value = this.draft().currencyCode; return; }
    if (value === this.draft().currencyCode) return;
    if (this.draft().currencyCode && (!!this.draft().receiptTotal || this.draft().lines.some(line => !!line.amount || !!line.unitPrice)) &&
      !window.confirm("Changing currency keeps entered numbers without converting them. Continue?")) {
      if (picker) picker.value = this.draft().currencyCode;
      return;
    }
    this.draft.update(draft => ({ ...draft, currencyCode: value }));
    this.refreshPrices();
  }
  private updateLine(key: string, update: (line: CaptureLineDraft) => CaptureLineDraft) {
    this.draft.update(draft => ({ ...draft, lines: draft.lines.map(line => line.key === key ? update(line) : line) }));
  }
  line(key: string) { return this.draft().lines.find(line => line.key === key); }
  savedLine(key: string) { return this.editing()?.lines.find(line => line.id === key); }
  unitLabel(itemId: string) { return this.items().find(item => item.id === itemId)?.unitLabel ?? "units"; }
  changeLine(key: string, field: "categoryId" | "quantity" | "unitPrice" | "amount" | "description" | "reference" | "promotion" | "receiptDescription" | "notes", value: string) {
    if (this.locked()) return;
    this.updateLine(key, line => {
      const updated = { ...line, [field]: value, autoAmount: field === "amount" ? false : line.autoAmount,
        priceTouched: field === "unitPrice" ? true : line.priceTouched, categoryTouched: field === "categoryId" ? true : line.categoryTouched };
      return this.recalculate(updated);
    });
    this.error.set("");
  }
  selectItem(key: string, itemId: string) {
    if (this.locked()) return;
    const item = this.items().find(row => row.id === itemId);
    this.priceVersions.set(key, (this.priceVersions.get(key) ?? 0) + 1);
    this.updateLine(key, line => ({ ...line, itemId, categoryId: line.categoryTouched ? line.categoryId : item?.expenseCategoryId ?? line.categoryId,
      quantity: item ? "1" : "", unitPrice: "", unitKey: "", unitLabel: "", priceTouched: false, amount: line.autoAmount ? "" : line.amount }));
    if (item) void this.lookupPrice(key); else this.priceState(key, "");
  }
  private recalculate(line: CaptureLineDraft) {
    const places = this.minorUnits();
    return line.itemId && line.autoAmount ? { ...line, amount: places === null ? "" : suggestedAmount(line.quantity, line.unitPrice, places) } : line;
  }
  suggestion(line: CaptureLineDraft) {
    const places = this.minorUnits();
    return places === null ? "" : suggestedAmount(line.quantity, line.unitPrice, places);
  }
  useSuggestion(key: string) {
    if (!this.locked()) this.updateLine(key, line => this.recalculate({ ...line, autoAmount: true }));
  }
  addLine() { if (!this.locked()) this.draft.update(draft => ({ ...draft, lines: [...draft.lines, newLine()] })); }
  removeLine(key: string) {
    if (this.locked() || this.draft().lines.length === 1) return;
    this.draft.update(draft => ({ ...draft, lines: draft.lines.filter(line => line.key !== key) }));
    this.priceVersions.delete(key);
  }
  private priceState(key: string, text: string) { this.priceStates.update(states => ({ ...states, [key]: text })); }
  private refreshPrices() {
    for (const line of this.draft().lines) {
      if (!line.itemId) continue;
      if (!line.priceTouched) this.updateLine(line.key, old => this.recalculate({ ...old, unitPrice: "" }));
      else this.updateLine(line.key, old => this.recalculate(old));
      void this.lookupPrice(line.key);
    }
  }
  async lookupPrice(key: string) {
    const home = this.home, generation = this.generation, line = this.line(key), draft = this.draft();
    const version = (this.priceVersions.get(key) ?? 0) + 1;
    this.priceVersions.set(key, version);
    if (!home || !line?.itemId || !draft.merchantId || !draft.currencyCode) { this.priceState(key, "Choose merchant and currency for a usual-price suggestion."); return; }
    const current = () => this.current(generation, home) && this.priceVersions.get(key) === version &&
      this.line(key)?.itemId === line.itemId && this.draft().merchantId === draft.merchantId && this.draft().currencyCode === draft.currencyCode;
    this.priceState(key, "Loading usual price…");
    try {
      const query = new URLSearchParams({ merchantId: draft.merchantId, currencyCode: draft.currencyCode });
      const price = await this.api.get<SpendingUsualPrice | null>(`households/${home}/spending/items/${line.itemId}/usual-price?${query}`);
      if (!current()) return;
      if (!price) { this.priceState(key, "No usual price. Enter the actual unit price."); return; }
      this.priceState(key, `Usual price: ${price.currencyCode} ${price.usualUnitPrice} per ${price.unitLabel}.`);
      if (!this.locked() && !this.line(key)?.priceTouched) this.updateLine(key, old => this.recalculate({ ...old, unitPrice: String(price.usualUnitPrice) }));
    } catch (error) {
      if (current()) this.priceState(key, "Usual price unavailable. Enter an actual price or retry the lookup.");
    }
  }
  total() {
    const places = this.minorUnits();
    if (places === null) return "";
    let total = 0n;
    for (const line of this.draft().lines) { const value = minorAmount(line.amount, places); if (value === null) return ""; total += value; }
    return decimalAmount(total, places);
  }
  difference() {
    const places = this.minorUnits(), total = this.total();
    if (places === null || !total || !this.draft().receiptTotal) return null;
    const receipt = minorAmount(this.draft().receiptTotal, places);
    return receipt === null ? null : decimalAmount(receipt - minorAmount(total, places)!, places);
  }
  hasDifference() { const value = this.difference(); return value !== null && Number(value) !== 0; }
  valid() {
    const draft = this.draft(), places = this.minorUnits();
    if (!this.home || this.w.current()?.id !== this.home || places === null || !draft.merchantId || !validSpendingDate(draft.date) || !draft.lines.length) return false;
    if (draft.receiptTotal && safeAmount(draft.receiptTotal, places, 1e12) === null) return false;
    return draft.lines.every(line => !!line.categoryId && safeAmount(line.amount, places, 1e12) !== null &&
      (!(line.quantity || line.unitPrice || (line.itemId && !this.editing())) ||
        (Number(line.quantity) > 0 && safeAmount(line.quantity, 6, 1e6) !== null && safeAmount(line.unitPrice, 6, 1e9) !== null)));
  }
  private request(): SpendingCaptureRequest {
    const draft = this.draft(), places = this.minorUnits()!;
    this.id ??= crypto.randomUUID();
    return { id: this.id, merchantId: draft.merchantId, spendingDate: draft.date, currencyCode: draft.currencyCode, kind: "Payment",
      receiptTotal: draft.receiptTotal ? safeAmount(draft.receiptTotal, places, 1e12)! : null,
      externalReference: draft.reference.trim() || null, notes: draft.notes.trim() || null,
      lines: draft.lines.map(line => ({ expenseCategoryId: line.categoryId, netAmount: safeAmount(line.amount, places, 1e12)!,
        ...(line.itemId ? { spendingItemId: line.itemId } : {}),
        ...(line.quantity || line.unitPrice ? { quantity: safeAmount(line.quantity, 6, 1e6)!, unitPrice: safeAmount(line.unitPrice, 6, 1e9)! } : {}),
        ...(this.editing() ? { receiptDescription: line.receiptDescription.trim() || null, notes: line.notes.trim() || null,
          ...(!line.itemId ? { unitKey: line.unitKey || null, unitLabel: line.unitLabel || null } : {}) } : {}),
        description: line.description.trim() || null, merchantItemReference: line.reference.trim() || null, promotionStatus: line.promotion })) };
  }
  // Use a single atomic write. Narrow corrections preserve copied receipt labels;
  // wider edits deliberately use the replacement contract and refresh those labels.
  private persistedLine(line: CaptureLineDraft) {
    const { autoAmount: _auto, priceTouched: _price, categoryTouched: _category, ...values } = line;
    return { ...values, quantity: this.canonical(line.quantity), unitPrice: this.canonical(line.unitPrice), amount: this.canonical(line.amount),
      description: line.description.trim(), reference: line.reference.trim(), receiptDescription: line.receiptDescription.trim(), notes: line.notes.trim() };
  }
  private canonical(value: string) {
    if (!/^\d+(?:\.\d+)?$/.test(value)) return value;
    const [whole, fraction = ""] = value.split(".");
    const digits = fraction.replace(/0+$/, "");
    return String(BigInt(whole)) + (digits ? "." + digits : "");
  }
  private persistedValues(draft: CaptureDraft) {
    return { ...draft, receiptTotal: this.canonical(draft.receiptTotal), reference: draft.reference.trim(), notes: draft.notes.trim(),
      lines: draft.lines.map(line => this.persistedLine(line)) };
  }
  private persisted(draft: CaptureDraft) { return JSON.stringify(this.persistedValues(draft)); }
  editOperation(): { kind: "header" | "line" | "replace"; line?: CaptureLineDraft } {
    const original = this.originalDraft, draft = this.draft();
    if (!original) return { kind: "replace" };
    const oldValues = this.persistedValues(original), newValues = this.persistedValues(draft);
    if (draft.merchantId === original.merchantId && JSON.stringify(newValues.lines) === JSON.stringify(oldValues.lines)) return { kind: "header" };
    const { lines: _old, ...oldHeader } = oldValues, { lines: _new, ...newHeader } = newValues;
    if (JSON.stringify(oldHeader) === JSON.stringify(newHeader) && draft.lines.length === original.lines.length) {
      const changed = draft.lines.filter((line, index) => JSON.stringify(this.persistedLine(line)) !== JSON.stringify(this.persistedLine(original.lines[index])));
      if (changed.length === 1) {
        const line = changed[0], old = original.lines.find(row => row.key === line.key);
        if (old && JSON.stringify(this.persistedLine({ ...line, amount: old.amount, quantity: old.quantity, unitPrice: old.unitPrice,
          promotion: old.promotion, notes: old.notes })) === JSON.stringify(this.persistedLine(old))) return { kind: "line", line };
      }
    }
    return { kind: "replace" };
  }
  async saveEdit() {
    const record = this.editing(), home = this.home, generation = this.generation;
    if (!record || !home || !this.current(generation, home) || this.locked() || !this.valid() || !this.hasUnsavedChanges()) return;
    const request = this.request(), operation = this.editOperation();
    const header = { expectedRevision: record.revision, spendingDate: request.spendingDate, receiptTotal: request.receiptTotal,
      externalReference: request.externalReference, notes: request.notes };
    let path = `households/${home}/spending/records/${record.id}`;
    let body: unknown;
    if (operation.kind === "header") { path += "/header"; body = header; }
    else if (operation.kind === "line") {
      const line = operation.line!;
      path += `/lines/${line.key}`;
      body = { expectedRevision: record.revision, netAmount: safeAmount(line.amount, this.minorUnits()!, 1e12)!,
        quantity: line.quantity ? safeAmount(line.quantity, 6, 1e6)! : null, unitPrice: line.unitPrice ? safeAmount(line.unitPrice, 6, 1e9)! : null,
        promotionStatus: line.promotion, notes: line.notes.trim() || null };
    } else {
      const replacement: SpendingReplacementRequest = { ...header, merchantId: request.merchantId, lines: request.lines };
      body = replacement;
    }
    this.saving.set(true); this.error.set("");
    try {
      const saved = await this.api.send<SpendingRecord>("PUT", path, body);
      if (this.current(generation, home)) { this.reset(); this.saved.emit(saved); }
    } catch (error) {
      if (this.current(generation, home)) {
        const rejected = error instanceof HttpErrorResponse && error.status === 400;
        this.editNeedsReload.set(!rejected);
        this.error.set(message(error) + (rejected ? "" : " The record may have changed or the save may have succeeded. Your draft is retained. Reload the saved version before another save."));
      }
    } finally { if (this.current(generation, home)) this.saving.set(false); }
  }
  async reloadEdit() {
    const record = this.editing(), home = this.home, generation = this.generation;
    if (!record || !home || !this.current(generation, home) || this.busy()) return;
    if (this.hasUnsavedChanges() && !window.confirm("Reload the saved version and discard these local edits? The previous save may have succeeded.")) return;
    this.checking.set(true); this.error.set("");
    try {
      const latest = await this.api.get<SpendingRecord>(`households/${home}/spending/records/${record.id}`);
      if (!this.current(generation, home)) return;
      this.reloaded.emit(latest);
      if (latest.isVoided) { this.editNeedsReload.set(true); this.error.set("This record has been voided. Cancel editing to return to history."); }
      else this.reset(latest);
    } catch (error) { if (this.current(generation, home)) this.error.set(message(error)); }
    finally { if (this.current(generation, home)) this.checking.set(false); }
  }
  cancelEdit() { if (this.canLeave()) this.cancelled.emit(); }
  async save() {
    if (this.editing()) { await this.saveEdit(); return; }
    if (this.busy() || this.disabled() || (!this.frozen() && !this.valid())) return;
    const home = this.home!, generation = this.generation, request = this.frozen() ?? this.request();
    if (!home || !this.current(generation, home)) return;
    this.frozen.set(request);
    this.saving.set(true); this.error.set("");
    try {
      const saved = await this.api.send<SpendingRecord>("POST", `households/${home}/spending/records`, request);
      if (!this.current(generation, home)) return;
      this.reset(); this.saved.emit(saved);
    } catch (error) {
      if (!this.current(generation, home)) return;
      if (error instanceof HttpErrorResponse && (error.status === 400 || error.status === 404)) this.frozen.set(null);
      this.error.set(message(error));
    } finally { if (this.current(generation, home)) this.saving.set(false); }
  }
  async checkSaved() {
    const request = this.frozen(), home = this.home, generation = this.generation;
    if (!request || !home || !this.current(generation, home) || this.busy()) return;
    this.checking.set(true);
    try {
      const saved = await this.api.get<SpendingRecord>(`households/${home}/spending/records/${request.id}`);
      if (this.current(generation, home)) { this.reset(); this.saved.emit(saved); }
    } catch (error) {
      if (this.current(generation, home)) this.error.set(error instanceof HttpErrorResponse && error.status === 404 ?
        "No saved record found yet. Retry the same save to avoid creating a duplicate." : message(error));
    } finally { if (this.current(generation, home)) this.checking.set(false); }
  }
  clear() {
    if (this.busy()) return;
    if ((this.frozen() || this.hasUnsavedChanges()) && !window.confirm(this.frozen() ?
      "The previous save may have succeeded. Check the saved record first. Clear this local draft anyway?" : "Clear this purchase draft?")) return;
    this.reset();
  }
  private reset(record: SpendingRecord | null = null) {
    this.generation++; this.id = null; this.priceVersions.clear();
    const draft: CaptureDraft = record ? { date: record.spendingDate, merchantId: record.merchantId, currencyCode: record.currencyCode,
      receiptTotal: record.receiptTotal === null ? "" : String(record.receiptTotal), reference: record.externalReference ?? "", notes: record.notes ?? "",
      lines: record.lines.map(line => ({ ...newLine(), key: line.id, itemId: line.spendingItemId ?? "", categoryId: line.expenseCategoryId,
        quantity: line.quantity === null ? "" : String(line.quantity), unitPrice: line.unitPrice === null ? "" : String(line.unitPrice), amount: String(line.netAmount),
        description: line.description ?? "", reference: line.merchantItemReference ?? "", promotion: line.promotionStatus,
        receiptDescription: line.receiptDescription ?? "", notes: line.notes ?? "", unitKey: line.unitKey ?? "", unitLabel: line.unitLabel ?? "",
        autoAmount: false, priceTouched: true, categoryTouched: true })) } : blankDraft();
    this.editing.set(record); this.editNeedsReload.set(false); this.originalDraft = record ? structuredClone(draft) : null;
    this.draft.set(draft); this.baseline = this.persisted(draft);
    this.frozen.set(null); this.error.set(""); this.saving.set(false); this.checking.set(false); this.priceStates.set({});
    this.formVersion.update(version => version + 1);
  }
  hasUnsavedChanges() { return !!this.frozen() || this.editNeedsReload() || this.persisted(this.draft()) !== this.baseline; }
  canLeave() {
    return !this.busy() && (!this.hasUnsavedChanges() || window.confirm(this.frozen() ?
      "A save may have succeeded. Leave this draft and check history before entering the purchase again?" : "Discard this unsaved purchase draft?"));
  }
}
