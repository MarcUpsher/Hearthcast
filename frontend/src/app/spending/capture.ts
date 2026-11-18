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
import { SpendingCaptureRequest, SpendingCurrency, SpendingItem, SpendingPromotion, SpendingRecord, SpendingSetupEntry, SpendingUsualPrice } from "./models";
import { decimalAmount, localToday, minorAmount, safeAmount, suggestedAmount, validSpendingDate } from "./amounts";

export interface CaptureLineDraft {
  key: string; itemId: string; categoryId: string; quantity: string; unitPrice: string; amount: string;
  description: string; reference: string; promotion: SpendingPromotion;
  autoAmount: boolean; priceTouched: boolean; categoryTouched: boolean;
}
const newLine = (): CaptureLineDraft => ({ key: crypto.randomUUID(), itemId: "", categoryId: "", quantity: "", unitPrice: "", amount: "",
  description: "", reference: "", promotion: "Unknown", autoAmount: true, priceTouched: false, categoryTouched: false });
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
  readonly saved = output<SpendingRecord>();
  private readonly api = inject(Api);
  private readonly w = inject(Workspace);
  private home: string | null = null;
  private generation = 0;
  private destroyed = false;
  private id: string | null = null;
  private baseline = "";
  private priceVersions = new Map<string, number>();
  readonly draft = signal(blankDraft());
  readonly formVersion = signal(0);
  readonly saving = signal(false);
  readonly checking = signal(false);
  readonly frozen = signal<SpendingCaptureRequest | null>(null);
  readonly error = signal("");
  readonly priceStates = signal<Record<string, string>>({});

  constructor() {
    effect(() => {
      const home = this.w.current()?.id ?? null;
      untracked(() => { if (home !== this.home) { this.home = home; this.reset(); } });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.generation++; });
    this.baseline = JSON.stringify(this.draft());
  }
  private current(generation: number, home: string) { return !this.destroyed && generation === this.generation && this.w.current()?.id === home; }
  locked() { return this.saving() || this.checking() || !!this.frozen() || this.disabled(); }
  busy() { return this.saving() || this.checking(); }
  minorUnits() { return this.currencies().find(row => row.code === this.draft().currencyCode)?.minorUnits ?? null; }
  set(field: Exclude<keyof CaptureDraft, "lines">, value: string) {
    if (this.locked()) return;
    this.draft.update(draft => ({ ...draft, [field]: value })); this.error.set("");
    if (field === "merchantId") this.refreshPrices();
  }
  setCurrency(value: string, picker?: MatSelect) {
    if (this.locked()) { if (picker) picker.value = this.draft().currencyCode; return; }
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
  unitLabel(itemId: string) { return this.items().find(item => item.id === itemId)?.unitLabel ?? "units"; }
  changeLine(key: string, field: "categoryId" | "quantity" | "unitPrice" | "amount" | "description" | "reference" | "promotion", value: string) {
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
      quantity: item ? "1" : "", unitPrice: "", priceTouched: false, amount: line.autoAmount ? "" : line.amount }));
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
      (!line.itemId || (Number(line.quantity) > 0 && safeAmount(line.quantity, 6, 1e6) !== null && safeAmount(line.unitPrice, 6, 1e9) !== null)));
  }
  private request(): SpendingCaptureRequest {
    const draft = this.draft(), places = this.minorUnits()!;
    this.id ??= crypto.randomUUID();
    return { id: this.id, merchantId: draft.merchantId, spendingDate: draft.date, currencyCode: draft.currencyCode, kind: "Payment",
      receiptTotal: draft.receiptTotal ? safeAmount(draft.receiptTotal, places, 1e12)! : null,
      externalReference: draft.reference.trim() || null, notes: draft.notes.trim() || null,
      lines: draft.lines.map(line => ({ expenseCategoryId: line.categoryId, netAmount: safeAmount(line.amount, places, 1e12)!,
        ...(line.itemId ? { spendingItemId: line.itemId, quantity: safeAmount(line.quantity, 6, 1e6)!, unitPrice: safeAmount(line.unitPrice, 6, 1e9)! } : {}),
        description: line.description.trim() || null, merchantItemReference: line.reference.trim() || null, promotionStatus: line.promotion })) };
  }
  async save() {
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
  private reset() {
    this.generation++; this.id = null; this.priceVersions.clear();
    const draft = blankDraft(); this.draft.set(draft); this.baseline = JSON.stringify(draft);
    this.frozen.set(null); this.error.set(""); this.saving.set(false); this.checking.set(false); this.priceStates.set({});
    this.formVersion.update(version => version + 1);
  }
  hasUnsavedChanges() { return !!this.frozen() || JSON.stringify(this.draft()) !== this.baseline; }
  canLeave() {
    return !this.busy() && (!this.hasUnsavedChanges() || window.confirm(this.frozen() ?
      "A save may have succeeded. Leave this draft and check history before entering the purchase again?" : "Discard this unsaved purchase draft?"));
  }
}
