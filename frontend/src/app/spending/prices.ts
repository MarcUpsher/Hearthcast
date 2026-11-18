import { Component, DestroyRef, effect, inject, input, output, signal, untracked } from "@angular/core";
import { DatePipe } from "@angular/common";
import { HttpErrorResponse } from "@angular/common/http";
import { FormsModule, NgForm } from "@angular/forms";
import { MatButtonModule } from "@angular/material/button";
import { MatInputModule } from "@angular/material/input";
import { MatSelectModule } from "@angular/material/select";
import { Api } from "../core/api";
import { message } from "../core/api-errors";
import { MoneyInput } from "../core/money";
import { Workspace } from "../core/workspace";
import { SpendingItem, SpendingSetupEntry, SpendingUsualPrice } from "./models";

interface PriceDraft { merchantId: string; currencyCode: string; amount: string; }
const emptyDraft = (): PriceDraft => ({ merchantId: "", currencyCode: "", amount: "" });
const priceFormat = new Intl.NumberFormat("en-GB", { minimumFractionDigits: 2, maximumFractionDigits: 6 });

@Component({
  selector: "app-spending-prices",
  imports: [DatePipe, FormsModule, MoneyInput, MatButtonModule, MatInputModule, MatSelectModule],
  templateUrl: "prices.html",
  styleUrl: "prices.scss",
})
export class SpendingPrices {
  readonly item = input.required<SpendingItem>();
  readonly merchants = input<SpendingSetupEntry[]>([]);
  readonly disabled = input(false);
  readonly close = output<void>();
  private readonly api = inject(Api);
  private readonly w = inject(Workspace);
  private home: string | null = null;
  private itemId: string | null = null;
  private generation = 0;
  private requestVersion = 0;
  private destroyed = false;
  readonly rows = signal<SpendingUsualPrice[]>([]);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly loadError = signal("");
  readonly error = signal("");
  readonly notice = signal("");
  readonly conflict = signal(false);
  readonly editing = signal<SpendingUsualPrice | null>(null);
  readonly draft = signal(emptyDraft());

  constructor() {
    effect(() => {
      const item = this.item();
      const selectedHome = this.w.current()?.id ?? null;
      const home = item.householdId === selectedHome ? selectedHome : null;
      untracked(() => {
        if (home === this.home && item.id === this.itemId) return;
        this.generation++;
        this.home = home;
        this.itemId = item.id;
        this.rows.set([]);
        this.loading.set(false);
        this.saving.set(false);
        this.loadError.set("");
        this.reset();
        if (home && item.householdId === home) void this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.generation++; });
  }
  private current(generation: number, home: string, itemId: string) {
    return !this.destroyed && generation === this.generation && this.w.current()?.id === home &&
      this.item().id === itemId && this.item().householdId === home;
  }
  private upsert(price: SpendingUsualPrice) {
    this.rows.update(rows => [...rows.filter(row => row.id !== price.id), price].sort((a, b) =>
      a.currencyCode.localeCompare(b.currencyCode) || this.merchantName(a.merchantId).localeCompare(this.merchantName(b.merchantId)) || a.id.localeCompare(b.id)));
  }
  async load() {
    const home = this.home, itemId = this.itemId, generation = this.generation;
    if (!home || !itemId || !this.current(generation, home, itemId) || this.saving()) return;
    const version = ++this.requestVersion;
    const current = () => this.current(generation, home, itemId) && version === this.requestVersion;
    this.loading.set(true);
    this.loadError.set("");
    try {
      const rows = await this.api.get<SpendingUsualPrice[]>(`households/${home}/spending/items/${itemId}/usual-prices`);
      if (current()) {
        this.rows.set(rows);
        const editing = this.editing();
        const saved = editing && rows.find(row => row.id === editing.id);
        if (editing && saved && saved.revision !== editing.revision) {
          this.conflict.set(true);
          this.error.set("The saved price changed. Your amount has been kept; reload the saved price before applying it.");
        }
      }
    } catch (error) {
      if (current()) this.loadError.set(message(error));
    } finally {
      if (current()) this.loading.set(false);
    }
  }
  change(field: keyof PriceDraft, value: string) {
    this.draft.update(draft => ({ ...draft, [field]: value }));
    this.error.set("");
  }
  format(price: SpendingUsualPrice) { return `${price.currencyCode} ${priceFormat.format(price.usualUnitPrice).replaceAll(",", " ")}`; }
  merchantName(id: string) { return this.merchants().find(row => row.id === id)?.name ?? "Merchant unavailable"; }
  duplicate() {
    if (this.editing()) return undefined;
    return this.rows().find(row => row.merchantId === this.draft().merchantId &&
      row.currencyCode === this.draft().currencyCode.trim().toUpperCase());
  }
  validAmount() {
    const amount = this.draft().amount;
    return /^\d+(?:\.\d{1,6})?$/.test(amount) && Number.isFinite(Number(amount)) && Number(amount) < 1_000_000_000;
  }
  canSave() {
    const home = this.home, itemId = this.itemId;
    return !!home && !!itemId && this.current(this.generation, home, itemId) && !this.disabled() &&
      !this.saving() && !this.loading() && !this.loadError() && !this.conflict() && !this.duplicate() &&
      !!this.draft().merchantId && /^[A-Za-z]{3}$/.test(this.draft().currencyCode.trim()) && this.validAmount();
  }
  beginEdit(price: SpendingUsualPrice) {
    if (this.editing()?.id === price.id) return;
    if (this.disabled() || this.loading() || this.loadError() || !this.canLeave()) return;
    this.editing.set(price);
    this.draft.set({ merchantId: price.merchantId, currencyCode: price.currencyCode, amount: String(price.usualUnitPrice) });
    this.conflict.set(false);
    this.error.set("");
    this.notice.set("");
  }
  reset(form?: NgForm) {
    if (this.saving()) return;
    this.editing.set(null);
    this.draft.set(emptyDraft());
    this.conflict.set(false);
    this.error.set("");
    this.notice.set("");
    form?.resetForm(emptyDraft());
  }
  async save(form?: NgForm) {
    if (!this.canSave()) return;
    const home = this.home!, itemId = this.itemId!, generation = this.generation;
    const editing = this.editing(), draft = this.draft();
    const body = editing ? { expectedRevision: editing.revision, usualUnitPrice: Number(draft.amount) } : {
      spendingItemId: itemId, merchantId: draft.merchantId,
      currencyCode: draft.currencyCode.trim().toUpperCase(), usualUnitPrice: Number(draft.amount),
    };
    this.saving.set(true);
    this.error.set("");
    this.notice.set("");
    try {
      const saved = await this.api.send<SpendingUsualPrice>(editing ? "PUT" : "POST",
        `households/${home}/spending/usual-prices${editing ? "/" + editing.id : ""}`, body);
      if (!this.current(generation, home, itemId)) return;
      this.upsert(saved);
      this.saving.set(false);
      this.reset(form);
      this.notice.set("Usual price saved.");
    } catch (error) {
      if (!this.current(generation, home, itemId)) return;
      if (error instanceof HttpErrorResponse && error.status === 409) {
        this.conflict.set(!!editing);
        this.error.set(editing ? "This price changed elsewhere. Reload the saved price before deciding whether to apply your amount." :
          "A price for this merchant and currency already exists. Reload prices and edit the existing entry.");
      } else this.error.set(message(error));
    } finally {
      if (this.current(generation, home, itemId)) this.saving.set(false);
    }
  }
  async reloadCurrent() {
    const editing = this.editing(), home = this.home, itemId = this.itemId, generation = this.generation;
    if (!editing || !home || !itemId || this.saving() || this.loading() || this.disabled()) return;
    const version = ++this.requestVersion;
    const current = () => this.current(generation, home, itemId) && version === this.requestVersion;
    this.loading.set(true);
    try {
      const saved = await this.api.get<SpendingUsualPrice>(`households/${home}/spending/usual-prices/${editing.id}`);
      if (!current() || this.editing() !== editing) return;
      this.upsert(saved);
      this.editing.set(saved);
      this.conflict.set(false);
      this.error.set("");
      this.notice.set("Latest saved price loaded. Your entered amount has been kept.");
    } catch (error) {
      if (current() && this.editing() === editing) this.error.set(message(error));
    } finally {
      if (current()) this.loading.set(false);
    }
  }
  hasUnsavedChanges() {
    const editing = this.editing();
    return editing ? this.draft().amount !== String(editing.usualUnitPrice) : Object.values(this.draft()).some(value => !!value.trim());
  }
  canLeave() {
    return !this.saving() && (!this.hasUnsavedChanges() || window.confirm("Discard unsaved usual-price changes?"));
  }
  requestClose() { if (this.canLeave()) this.close.emit(); }
}
