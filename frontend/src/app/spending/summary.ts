import { Component, DestroyRef, computed, effect, inject, signal, untracked } from "@angular/core";
import { FormsModule } from "@angular/forms";
import { MatButtonModule } from "@angular/material/button";
import { MatInputModule } from "@angular/material/input";
import { Api } from "../core/api";
import { message } from "../core/api-errors";
import { Workspace } from "../core/workspace";
import { localToday, spendingDate, validSpendingDate } from "./amounts";
import { SpendingCategorySummary, SpendingCategoryTotal, SpendingReceiptWarning } from "./models";
import { SpendingNavigation } from "./navigation";
import { monthRange, presetRange, SpendingRange, SpendingRangePreset } from "./ranges";
import { parseSpendingSummary, reportDecimalPlaces, reportMoney } from "./summary-data";

@Component({
  imports: [FormsModule, MatButtonModule, MatInputModule, SpendingNavigation],
  templateUrl: "summary.html", styleUrl: "summary.scss",
})
export class SpendingSummary {
  readonly w = inject(Workspace);
  private readonly api = inject(Api);
  private home: string | null = null;
  private generation = 0;
  private version = 0;
  private destroyed = false;
  readonly range = signal<SpendingRange>(presetRange("thisMonth")!);
  readonly month = signal(localToday().slice(0, 7));
  readonly report = signal<SpendingCategorySummary | null>(null);
  readonly loading = signal(false);
  readonly error = signal("");
  readonly money = reportMoney;
  readonly date = spendingDate;
  readonly groups = computed(() => {
    const groups = new Map<string, SpendingCategoryTotal[]>();
    for (const category of this.report()?.categories ?? []) {
      const rows = groups.get(category.currencyCode) ?? [];
      rows.push(category); groups.set(category.currencyCode, rows);
    }
    return [...groups].sort(([a], [b]) => a.localeCompare(b)).map(([currency, rows]) => ({ currency,
      rows: rows.sort((a, b) => a.categoryName.localeCompare(b.categoryName) || a.expenseCategoryId.localeCompare(b.expenseCategoryId)) }));
  });

  constructor() {
    effect(() => {
      const home = this.w.current()?.id ?? null;
      untracked(() => {
        if (home === this.home) return;
        this.home = home; this.generation++;
        this.report.set(null); this.loading.set(false); this.error.set("");
        this.range.set(presetRange("thisMonth")!); this.month.set(localToday().slice(0, 7));
        if (home) void this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.generation++; });
  }
  private current(home: string, generation: number, version: number) {
    return !this.destroyed && generation === this.generation && this.w.current()?.id === home && version === this.version;
  }
  change(field: keyof SpendingRange, value: string) { this.range.update(range => ({ ...range, [field]: value })); this.error.set(""); }
  changed() { const report = this.report(), range = this.range(); return !!report && (report.from !== range.from || report.to !== range.to); }
  async preset(preset: SpendingRangePreset) {
    const range = presetRange(preset);
    if (!range) return;
    this.range.set(range); this.month.set(range.from.slice(0, 7)); await this.load();
  }
  async showMonth() {
    const range = monthRange(this.month());
    if (!range) { this.error.set("Choose a valid month and year."); return; }
    this.range.set(range); await this.load();
  }
  async load() {
    const home = this.home, generation = this.generation, range = { ...this.range() };
    if (!home || this.w.current()?.id !== home || this.destroyed) return;
    if (!validSpendingDate(range.from) || !validSpendingDate(range.to) || range.from > range.to) {
      this.error.set("Supply valid From and To dates, with From on or before To."); return;
    }
    const version = ++this.version;
    this.loading.set(true); this.error.set("");
    try {
      const query = new URLSearchParams(range);
      const report = parseSpendingSummary(await this.api.getText(`households/${home}/spending/summary?${query}`));
      if (this.current(home, generation, version)) this.report.set(report);
    } catch (error) { if (this.current(home, generation, version)) this.error.set(message(error)); }
    finally { if (this.current(home, generation, version)) this.loading.set(false); }
  }
  warningMoney(warning: SpendingReceiptWarning, value: string) {
    // Warning responses omit precision. Use the currency's maximum saved report
    // precision and preserve every decimal present in the warning's amounts.
    const places = Math.max(0, ...this.report()!.categories.filter(row => row.currencyCode === warning.currencyCode).map(row => row.currencyMinorUnits),
      ...[warning.receiptTotal, warning.lineTotal, warning.difference].map(reportDecimalPlaces));
    return reportMoney(value, warning.currencyCode, places);
  }
}
