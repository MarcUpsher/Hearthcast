import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from "@angular/core";
import { HttpErrorResponse } from "@angular/common/http";
import { FormsModule } from "@angular/forms";
import { MatButtonModule } from "@angular/material/button";
import { MatAutocompleteModule } from "@angular/material/autocomplete";
import { MatDialog, MatDialogModule, MatDialogRef } from "@angular/material/dialog";
import { MatInputModule } from "@angular/material/input";
import { MatSelect, MatSelectModule } from "@angular/material/select";
import { Api } from "../core/api";
import { message } from "../core/api-errors";
import { MoneyInput } from "../core/money";
import { Workspace } from "../core/workspace";
import { SpendingCaptureRequest, SpendingCurrency, SpendingItem, SpendingPromotion, SpendingRecord, SpendingReplacementRequest, SpendingLastPrice, SpendingSetupEntry, SpendingUsualPrice } from "./models";
import { decimalAmount, localToday, minorAmount, safeAmount, suggestedAmount, spendingDate, validSpendingDate } from "./amounts";
import { SpendingItemCreate, SpendingItemCreateData } from "./item-create";

export interface CaptureLineDraft {
  kind: "item" | "category" | "cost";
  key: string; itemId: string; categoryId: string; quantity: string; unitPrice: string; amount: string;
  description: string; reference: string; promotion: SpendingPromotion;
  autoAmount: boolean; priceTouched: boolean;
  receiptDescription: string; notes: string; unitKey: string; unitLabel: string;
}
const newLine = (kind: CaptureLineDraft["kind"] = "item"): CaptureLineDraft => ({ kind, key: crypto.randomUUID(), itemId: "", categoryId: "", quantity: "", unitPrice: "", amount: "",
  description: "", reference: "", promotion: "Unknown", autoAmount: true, priceTouched: false,
  receiptDescription: "", notes: "", unitKey: "", unitLabel: "" });
export interface CaptureDiscountDraft { key: string; categoryId: string; amount: string; description: string; }
const blankDraft = () => ({ date: localToday(), merchantId: "", currencyCode: "GBP", receiptTotal: "", reference: "", notes: "", discounts: [] as CaptureDiscountDraft[], lines: [newLine()] });
type CaptureDraft = ReturnType<typeof blankDraft>;

@Component({
  selector: "app-spending-capture",
  imports: [FormsModule, MoneyInput, MatButtonModule, MatAutocompleteModule, MatDialogModule, MatInputModule, MatSelectModule],
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
  readonly itemCreated = output<SpendingItem>();
  private readonly api = inject(Api);
  private readonly dialogs = inject(MatDialog);
  private readonly w = inject(Workspace);
  private home: string | null = null;
  private inputRecord: SpendingRecord | null | undefined;
  private generation = 0;
  private destroyed = false;
  private id: string | null = null;
  private baseline = "";
  private originalDraft: CaptureDraft | null = null;
  private priceVersions = new Map<string, number>();
  private changedSelections = new Set<string>();
  readonly draft = signal(blankDraft());
  readonly formVersion = signal(0);
  readonly saving = signal(false);
  readonly checking = signal(false);
  readonly frozen = signal<SpendingCaptureRequest | null>(null);
  readonly error = signal("");
  readonly lastPrices = signal<Record<string, SpendingLastPrice | null>>({});
  readonly lastPriceStates = signal<Record<string, string>>({});
  readonly date = spendingDate;
  readonly priceStates = signal<Record<string, string>>({});
  readonly editing = signal<SpendingRecord | null>(null);
  readonly editNeedsReload = signal(false);
  readonly itemEditor = signal<MatDialogRef<SpendingItemCreate, SpendingItem> | null>(null);
  readonly itemSearches = signal<Record<string, string>>({});
  readonly displayItem = (value: SpendingItem | string | null) => typeof value === "string" ? value : value?.name ?? "";

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
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.generation++; this.itemEditor()?.close(); });
    this.baseline = this.persisted(this.draft());
  }
  private current(generation: number, home: string) { return !this.destroyed && generation === this.generation && this.w.current()?.id === home; }
  locked() { return this.saving() || this.checking() || !!this.frozen() || this.editNeedsReload() || !!this.itemEditor() || this.disabled(); }
  busy() { return this.saving() || this.checking() || (this.itemEditor()?.componentInstance?.saving() ?? false); }
  minorUnits() { return this.editing()?.currencyMinorUnits ?? this.currencies().find(row => row.code === this.draft().currencyCode)?.minorUnits ?? null; }
  set(field: Exclude<keyof CaptureDraft, "lines" | "discounts">, value: string) {
    if (this.locked()) return;
    this.draft.update(draft => ({ ...draft, [field]: value })); this.error.set("");
    if (field === "merchantId" || field === "date") this.refreshPrices();
  }
  setCurrency(value: string, picker?: MatSelect) {
    if (this.locked() || this.editing()) { if (picker) picker.value = this.draft().currencyCode; return; }
    if (value === this.draft().currencyCode) return;
    if (this.draft().currencyCode && (!!this.draft().receiptTotal || this.draft().discounts.some(discount => !!discount.amount) || this.draft().lines.some(line => !!line.amount || !!line.unitPrice)) &&
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
  itemText(line: CaptureLineDraft) {
    const saved = this.savedLine(line.key);
    return this.itemSearches()[line.key] ?? (saved?.spendingItemId === line.itemId ? saved.itemName : this.items().find(item => item.id === line.itemId)?.name) ?? "";
  }
  itemContext(line: CaptureLineDraft) {
    const saved = this.savedLine(line.key), item = this.items().find(item => item.id === line.itemId);
    return saved?.spendingItemId === line.itemId && line.unitKey ? `${saved.itemCode} · ${saved.unitLabel}` : item ? `${item.code} · ${item.unitLabel}` : "";
  }
  matchingItems(line: CaptureLineDraft) {
    const search = this.itemText(line).trim().toLocaleLowerCase();
    return this.items().filter(item => (!line.categoryId || item.expenseCategoryId === line.categoryId) &&
      [item.name, item.code, item.unitLabel].some(value => value.toLocaleLowerCase().includes(search)))
      .sort((a, b) => a.name.localeCompare(b.name) || a.code.localeCompare(b.code));
  }
  searchItem(key: string, value: string | SpendingItem | null) {
    if (this.locked() || (value !== null && typeof value !== "string")) return;
    const line = this.line(key);
    if (!line || line.kind !== "item") return;
    this.itemSearches.update(searches => ({ ...searches, [key]: value ?? "" }));
    if (line.itemId) {
      this.changedSelections.add(key);
      this.priceVersions.set(key, (this.priceVersions.get(key) ?? 0) + 1);
      this.updateLine(key, old => ({ ...old, itemId: "", categoryId: "" }));
      this.priceState(key, "");
    }
  }
  changeItem(key: string) { this.searchItem(key, ""); }
  categoryHint(line: CaptureLineDraft) {
    const saved = this.savedLine(line.key), item = this.items().find(item => item.id === line.itemId);
    return saved?.spendingItemId === line.itemId && line.categoryId === saved.expenseCategoryId && item && item.expenseCategoryId !== line.categoryId ?
      "The recorded category is kept for this saved line." : "Category comes from the selected item. Change the item to choose another.";
  }
  setLineKind(key: string, kind: CaptureLineDraft["kind"]) {
    if (this.locked()) return;
    const line = this.line(key);
    if (!line || line.kind === kind) return;
    if ((line.itemId || line.quantity || line.unitPrice || line.unitKey || this.itemSearches()[key]?.trim()) && !window.confirm(
      "Change line type? This clears the item/search and quantity/unit-price details, keeping the actual amount and receipt text.")) return;
    this.changedSelections.add(key);
    this.priceVersions.set(key, (this.priceVersions.get(key) ?? 0) + 1);
    this.itemSearches.update(searches => ({ ...searches, [key]: "" })); this.priceState(key, "");
    this.updateLine(key, old => ({ ...old, kind, itemId: "", quantity: "", unitPrice: "", unitKey: "", unitLabel: "", autoAmount: !old.amount }));
  }
  changeLine(key: string, field: "categoryId" | "quantity" | "unitPrice" | "amount" | "description" | "reference" | "promotion" | "receiptDescription" | "notes", value: string) {
    if (this.locked()) return;
    if (field === "categoryId" && this.line(key)?.kind === "item" && this.line(key)?.itemId) return;
    this.updateLine(key, line => {
      const updated = { ...line, [field]: value, autoAmount: field === "amount" ? false : line.autoAmount,
        priceTouched: field === "unitPrice" ? true : line.priceTouched };
      return this.recalculate(updated);
    });
    this.error.set("");
  }
  createItem(key: string) {
    const home = this.home, generation = this.generation, line = this.line(key);
    if (!home || !line || line.kind !== "item" || !this.current(generation, home) || this.locked() || !this.categories().length) return;
    const editor = this.dialogs.open<SpendingItemCreate, SpendingItemCreateData, SpendingItem>(SpendingItemCreate, {
      data: { householdId: home, categories: this.categories(), expenseCategoryId: line.categoryId },
      width: "720px", maxWidth: "calc(100vw - 32px)", maxHeight: "calc(100dvh - 32px)",
      autoFocus: 'input[name="name"]', disableClose: true, closeOnNavigation: false,
    });
    this.itemEditor.set(editor);
    editor.afterClosed().subscribe(saved => {
      if (this.itemEditor() === editor) this.itemEditor.set(null);
      if (!saved || !this.current(generation, home) || saved.householdId !== home || !this.line(key)) return;
      this.itemCreated.emit(saved);
      this.applyItem(key, saved.id, saved);
    });
  }
  selectItem(key: string, itemId: string) {
    if (this.locked()) return;
    this.applyItem(key, itemId);
  }
  private applyItem(key: string, itemId: string, createdItem?: SpendingItem) {
    const item = createdItem ?? this.items().find(row => row.id === itemId);
    if (!item || this.line(key)?.kind !== "item") return;
    this.changedSelections.add(key);
    this.itemSearches.update(searches => ({ ...searches, [key]: item.name }));
    this.priceVersions.set(key, (this.priceVersions.get(key) ?? 0) + 1);
    this.updateLine(key, line => ({ ...line, itemId, categoryId: item.expenseCategoryId,
      quantity: "1", unitPrice: "", unitKey: "", unitLabel: "", priceTouched: false, amount: line.autoAmount ? "" : line.amount }));
    void this.lookupPrice(key);
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
  addLine(kind: CaptureLineDraft["kind"] = "item") { if (!this.locked()) this.draft.update(draft => ({ ...draft, lines: [...draft.lines, newLine(kind)] })); }
  purchaseLines() { return this.draft().lines.filter(line => line.kind !== "cost"); }
  costLines() { return this.draft().lines.filter(line => line.kind === "cost"); }
  addCost() {
    if (this.locked()) return;
    const categories = [...new Set(this.purchaseLines().map(line => line.categoryId).filter(Boolean))];
    this.draft.update(draft => ({ ...draft, lines: [...draft.lines, { ...newLine("cost"), categoryId: categories.length === 1 ? categories[0] : "" }] }));
  }
  removeLine(key: string) {
    if (this.locked() || this.draft().lines.length === 1) return;
    this.draft.update(draft => ({ ...draft, lines: draft.lines.filter(line => line.key !== key) }));
    this.priceVersions.delete(key);
    this.changedSelections.delete(key);
    this.itemSearches.update(searches => { const next = { ...searches }; delete next[key]; return next; });
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
    this.lastPrices.update(prices => ({ ...prices, [key]: null }));
    this.lastPriceStates.update(states => ({ ...states, [key]: "" }));
    if (!home || !line?.itemId || !draft.merchantId || !draft.currencyCode) { this.priceState(key, "Choose merchant and currency for a usual-price suggestion."); return; }
    const current = () => this.current(generation, home) && this.priceVersions.get(key) === version &&
      this.line(key)?.itemId === line.itemId && this.draft().merchantId === draft.merchantId && this.draft().currencyCode === draft.currencyCode && this.draft().date === draft.date;
    this.priceState(key, "Loading usual price…");
    try {
      const query = new URLSearchParams({ merchantId: draft.merchantId, currencyCode: draft.currencyCode });
      const pendingUsual = this.api.get<SpendingUsualPrice | null>(`households/${home}/spending/items/${line.itemId}/usual-price?${query}`);
      void this.lookupLastPrice(key, home, line.itemId, draft, current);
      const price = await pendingUsual;
      if (!current()) return;
      if (!price) { this.priceState(key, "No usual price. Enter the actual unit price."); return; }
      this.priceState(key, `Usual price: ${price.currencyCode} ${price.usualUnitPrice} per ${price.unitLabel}.`);
      if (!this.locked() && !this.line(key)?.priceTouched) this.updateLine(key, old => this.recalculate({ ...old, unitPrice: String(price.usualUnitPrice) }));
    } catch (error) {
      if (current()) this.priceState(key, "Usual price unavailable. Enter an actual price or retry the lookup.");
    }
  }
  private async lookupLastPrice(key: string, home: string, itemId: string, draft: CaptureDraft, current: () => boolean) {
    if (!validSpendingDate(draft.date)) return;
    this.lastPriceStates.update(states => ({ ...states, [key]: "Loading last recorded price…" }));
    try {
      const query = new URLSearchParams({ merchantId: draft.merchantId, currencyCode: draft.currencyCode, onOrBefore: draft.date });
      if (this.editing()) query.set("excludeRecordId", this.editing()!.id);
      const price = await this.api.get<SpendingLastPrice | null>(`households/${home}/spending/items/${itemId}/last-paid-price?${query}`);
      if (!current()) return;
      const valid = price && typeof price.unitPrice === "number" && typeof price.spendingDate === "string" && price.currencyCode === draft.currencyCode;
      this.lastPrices.update(prices => ({ ...prices, [key]: valid ? price : null }));
      this.lastPriceStates.update(states => ({ ...states, [key]: valid ? "" : "No earlier recorded price for this merchant and currency." }));
    } catch { if (current()) this.lastPriceStates.update(states => ({ ...states, [key]: "Last recorded price unavailable; the usual-price lookup is separate." })); }
  }
  useLastPrice(key: string) {
    const price = this.lastPrices()[key];
    if (price && !this.locked()) this.changeLine(key, "unitPrice", String(price.unitPrice));
  }
  addDiscount() {
    if (this.locked()) return;
    const categories = [...new Set(this.draft().lines.map(line => line.categoryId).filter(Boolean))];
    this.draft.update(draft => ({ ...draft, discounts: [...draft.discounts, { key: crypto.randomUUID(), categoryId: categories.length === 1 ? categories[0] : "", amount: "", description: "" }] }));
  }
  changeDiscount(key: string, field: "categoryId" | "amount" | "description", value: string) {
    if (!this.locked()) { this.draft.update(draft => ({ ...draft, discounts: draft.discounts.map(discount => discount.key === key ? { ...discount, [field]: value } : discount) })); this.error.set(""); }
  }
  removeDiscount(key: string) { if (!this.locked()) this.draft.update(draft => ({ ...draft, discounts: draft.discounts.filter(discount => discount.key !== key) })); }
  purchaseTotal() { return this.lineTotal(this.purchaseLines()); }
  costTotal() { return this.lineTotal(this.costLines()); }
  gross() { return this.lineTotal(this.draft().lines); }
  private lineTotal(lines: CaptureLineDraft[]) {
    const places = this.minorUnits(); if (places === null) return "";
    let total = 0n;
    for (const line of lines) { const value = minorAmount(line.amount, places); if (value === null) return ""; total += value; }
    return decimalAmount(total, places);
  }
  discountTotal() {
    const places = this.minorUnits(); if (places === null) return "";
    let total = 0n;
    for (const discount of this.draft().discounts) { const value = minorAmount(discount.amount, places); if (value === null) return ""; total += value; }
    return decimalAmount(total, places);
  }
  total() {
    const places = this.minorUnits(), gross = this.gross(), discounts = this.discountTotal();
    if (places === null || !gross || !discounts) return "";
    const net = minorAmount(gross, places)! - minorAmount(discounts, places)!;
    return net < 0n ? "" : decimalAmount(net, places);
  }
  discountError() { return this.draft().discounts.length && this.draft().discounts.every(discount => discount.categoryId && discount.amount) && !this.discountsValid()
    ? "Use positive discounts in the receipt currency. Discounts cannot exceed the entered lines in their assigned category." : ""; }
  discountsValid() {
    const places = this.minorUnits(); if (places === null) return false;
    const remaining = new Map<string, bigint>();
    for (const line of this.draft().lines) remaining.set(line.categoryId, (remaining.get(line.categoryId) ?? 0n) + (minorAmount(line.amount, places) ?? 0n));
    for (const discount of this.draft().discounts) {
      const value = safeAmount(discount.amount, places, 1e12), amount = minorAmount(discount.amount, places);
      if (value === null || value <= 0 || amount === null || !discount.categoryId || !remaining.has(discount.categoryId)) return false;
      remaining.set(discount.categoryId, remaining.get(discount.categoryId)! - amount);
    }
    return [...remaining.values()].every(value => value >= 0n);
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
    return this.discountsValid() && draft.lines.every(line => (line.kind !== "cost" || (!!line.description.trim() && (safeAmount(line.amount, places, 1e12) ?? 0) > 0)) && (line.kind !== "item" || !!line.itemId) && !!line.categoryId && safeAmount(line.amount, places, 1e12) !== null &&
      (!(line.quantity || line.unitPrice || (line.kind === "item" && !this.historicalAmountOnly(line))) ||
        (Number(line.quantity) > 0 && safeAmount(line.quantity, 6, 1e6) !== null && safeAmount(line.unitPrice, 6, 1e9) !== null)));
  }
  historicalAmountOnly(line: CaptureLineDraft) {
    const saved = this.savedLine(line.key);
    return !this.changedSelections.has(line.key) && !!saved && saved.spendingItemId === line.itemId && saved.quantity === null && saved.unitPrice === null && !line.quantity && !line.unitPrice;
  }
  private request(): SpendingCaptureRequest {
    const draft = this.draft(), places = this.minorUnits()!;
    this.id ??= crypto.randomUUID();
    return { id: this.id, merchantId: draft.merchantId, spendingDate: draft.date, currencyCode: draft.currencyCode, kind: "Payment",
      receiptTotal: draft.receiptTotal ? safeAmount(draft.receiptTotal, places, 1e12)! : null,
      externalReference: draft.reference.trim() || null, notes: draft.notes.trim() || null,
      ...(draft.discounts.length ? { discounts: draft.discounts.map(discount => ({ expenseCategoryId: discount.categoryId,
        amount: safeAmount(discount.amount, places, 1e12)!, description: discount.description.trim() || null })) } : {}),
      lines: draft.lines.map(line => ({ ...(line.kind === "cost" ? { isAdditionalCost: true } : {}), expenseCategoryId: line.categoryId, netAmount: safeAmount(line.amount, places, 1e12)!,
        ...(line.itemId ? { spendingItemId: line.itemId } : {}),
        ...(line.quantity || line.unitPrice ? { quantity: safeAmount(line.quantity, 6, 1e6)!, unitPrice: safeAmount(line.unitPrice, 6, 1e9)! } : {}),
        ...(this.editing() ? { receiptDescription: line.receiptDescription.trim() || null, notes: line.notes.trim() || null,
          ...(!line.itemId ? { unitKey: line.unitKey || null, unitLabel: line.unitLabel || null } : {}) } : {}),
        description: line.description.trim() || null, merchantItemReference: line.reference.trim() || null, promotionStatus: line.promotion })) };
  }
  // Use a single atomic write. Narrow corrections preserve copied receipt labels;
  // wider edits deliberately use the replacement contract and refresh those labels.
  private persistedLine(line: CaptureLineDraft) {
    const { autoAmount: _auto, priceTouched: _price, ...values } = line;
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
      lines: draft.lines.map(line => this.persistedLine(line)),
      discounts: draft.discounts.map(discount => ({ categoryId: discount.categoryId, amount: this.canonical(discount.amount), description: discount.description.trim() })) };
  }
  private persisted(draft: CaptureDraft) { return JSON.stringify(this.persistedValues(draft)); }
  editOperation(): { kind: "header" | "line" | "discounts" | "replace"; line?: CaptureLineDraft } {
    const original = this.originalDraft, draft = this.draft();
    if (!original) return { kind: "replace" };
    const oldValues = this.persistedValues(original), newValues = this.persistedValues(draft);
    const { discounts: oldDiscounts, ...oldRest } = oldValues, { discounts: newDiscounts, ...newRest } = newValues;
    const sameDiscounts = JSON.stringify(oldDiscounts) === JSON.stringify(newDiscounts);
    if (!sameDiscounts && JSON.stringify(oldRest) === JSON.stringify(newRest)) return { kind: "discounts" };
    if (sameDiscounts && draft.merchantId === original.merchantId && JSON.stringify(newValues.lines) === JSON.stringify(oldValues.lines)) return { kind: "header" };
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
    if (operation.kind === "discounts") { path += "/discounts"; body = { expectedRevision: record.revision, discounts: request.discounts ?? [] }; }
    else if (operation.kind === "header") { path += "/header"; body = header; }
    else if (operation.kind === "line") {
      const line = operation.line!;
      path += `/lines/${line.key}`;
      body = { expectedRevision: record.revision, netAmount: safeAmount(line.amount, this.minorUnits()!, 1e12)!,
        quantity: line.quantity ? safeAmount(line.quantity, 6, 1e6)! : null, unitPrice: line.unitPrice ? safeAmount(line.unitPrice, 6, 1e9)! : null,
        promotionStatus: line.promotion, notes: line.notes.trim() || null };
    } else {
      const sameDiscounts = JSON.stringify(this.persistedValues(this.originalDraft!).discounts) === JSON.stringify(this.persistedValues(this.draft()).discounts);
      const replacement: SpendingReplacementRequest = { ...header, merchantId: request.merchantId, lines: request.lines,
        ...(!sameDiscounts ? { discounts: request.discounts ?? [] } : {}) };
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
    if (this.busy() || this.itemEditor() || this.disabled() || (!this.frozen() && !this.valid())) return;
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
    this.generation++; this.id = null; this.priceVersions.clear(); this.changedSelections.clear();
    this.itemEditor()?.close(); this.itemEditor.set(null);
    const draft: CaptureDraft = record ? { date: record.spendingDate, merchantId: record.merchantId, currencyCode: record.currencyCode,
      receiptTotal: record.receiptTotal === null ? "" : String(record.receiptTotal), reference: record.externalReference ?? "", notes: record.notes ?? "",
      discounts: (record.discounts ?? []).map(discount => ({ key: discount.id, categoryId: discount.expenseCategoryId, amount: String(discount.amount), description: discount.description ?? "" })),
      lines: record.lines.map(line => ({ ...newLine(line.isAdditionalCost ? "cost" : line.spendingItemId ? "item" : "category"), key: line.id, itemId: line.spendingItemId ?? "", categoryId: line.expenseCategoryId,
        quantity: line.quantity === null ? "" : String(line.quantity), unitPrice: line.unitPrice === null ? "" : String(line.unitPrice), amount: String(line.netAmount),
        description: line.description ?? "", reference: line.merchantItemReference ?? "", promotion: line.promotionStatus,
        receiptDescription: line.receiptDescription ?? "", notes: line.notes ?? "", unitKey: line.unitKey ?? "", unitLabel: line.unitLabel ?? "",
        autoAmount: false, priceTouched: true })) } : blankDraft();
    this.editing.set(record); this.editNeedsReload.set(false); this.originalDraft = record ? structuredClone(draft) : null;
    this.draft.set(draft); this.baseline = this.persisted(draft);
    this.frozen.set(null); this.error.set(""); this.saving.set(false); this.checking.set(false); this.priceStates.set({}); this.lastPrices.set({}); this.lastPriceStates.set({}); this.itemSearches.set({});
    this.formVersion.update(version => version + 1);
    if (record) for (const line of draft.lines) if (line.itemId) void this.lookupPrice(line.key);
  }
  hasUnsavedChanges() { return !!this.frozen() || this.editNeedsReload() || (this.itemEditor()?.componentInstance?.dirty() ?? false) ||
    this.draft().lines.some(line => !line.itemId && !!this.itemSearches()[line.key]?.trim()) || this.persisted(this.draft()) !== this.baseline; }
  canLeave() {
    return !this.busy() && (!this.hasUnsavedChanges() || window.confirm(this.frozen() ?
      "A save may have succeeded. Leave this draft and check history before entering the purchase again?" : "Discard this unsaved purchase draft?"));
  }
}
