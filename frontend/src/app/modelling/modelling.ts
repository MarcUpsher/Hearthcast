import {
  Component,
  DestroyRef,
  HostListener,
  computed,
  effect,
  inject,
  signal,
} from "@angular/core";
import { FormsModule } from "@angular/forms";
import { RouterLink } from "@angular/router";
import { MatButtonModule } from "@angular/material/button";
import { MatFormFieldModule } from "@angular/material/form-field";
import { MatInputModule } from "@angular/material/input";
import { MatSelectModule } from "@angular/material/select";
import { MatSlideToggleModule } from "@angular/material/slide-toggle";
import { MatCheckboxModule } from "@angular/material/checkbox";
import { Api, Inventory, Workspace, message } from "../core/api";
import { money, currencySymbol, MoneyInput } from "../core/money";
import { currencyOptions } from "../core/location-options";
import { smoothPath } from "../history/smooth-path";
import {
  Assumptions,
  CurrencyPot,
  LoanSettings,
  ModellingSnapshot,
  ProjectionRun,
  ProjectionInputs,
  ProjectionPreset,
} from "./models";

const defaults = (): Assumptions => ({
  currencyCode: "",
  durationYears: 10,
  currencies: [],
  annualInflationPercent: 3,
  showInflationAdjustedValues: false,
});
const newPot = (currencyCode: string): CurrencyPot => ({
  currencyCode,
  monthlyInvestmentContribution: 0,
  annualInvestmentReturnPercent: 5,
  monthlySavingsContribution: 0,
  annualSavingsInterestPercent: 2,
});
const newLoan = (financedAssetId: string): LoanSettings => ({
  financedAssetId,
  monthlyLoanPaymentInAssetCurrency: 0,
  extraMonthlyLoanPaymentInAssetCurrency: 0,
  investmentCurrencyAfterPayoff: "",
  annualLoanInterestPercent: 0,
  annualAssetAppreciationPercent: 0,
});
const series = [
  { key: "investments", label: "Investments", color: "#42c9ff" },
  { key: "savings", label: "Savings", color: "#b9e769" },
  { key: "equity", label: "Financed equity", color: "#ffc35a" },
  { key: "netPosition", label: "Modelled total", color: "#ec8cda" },
] as const;
type GroupOption = {
  id: string;
  name: string;
  category: string;
  missing?: boolean;
};
type AssetOption = {
  id: string;
  name: string;
  currency: string;
  value: number | null;
  debt: number | null;
  missing?: boolean;
};
type PayoffEvent = {
  index: number;
  date: string;
  assetIds: string[];
};

@Component({
  imports: [
    FormsModule,
    MoneyInput,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
    MatSlideToggleModule,
    MatCheckboxModule,
  ],
  templateUrl: "modelling.html",
  styleUrl: "modelling.scss",
})
export class Modelling {
  private api = inject(Api);
  w = inject(Workspace);
  inventory = signal<Inventory[]>([]);
  snapshot = signal<ModellingSnapshot | null>(null);
  result = signal<ProjectionRun | null>(null);
  loading = signal(false);
  pending = signal(false);
  error = signal("");
  validation = signal("");
  adjusted = signal(false);
  investmentGroups = signal<string[]>([]);
  savingsGroups = signal<string[]>([]);
  selectedAssets = signal<string[]>([]);
  hovered = signal<number | null>(null);
  presets = signal<ProjectionPreset[]>([]);
  savingPreset = signal(false);
  presetError = signal("");
  presetStatus = signal("");
  presetsLoaded = signal(false);
  presetId = "";
  presetName = "";
  presetDefault = false;
  private savedDraft = "";
  private retainedUnsavedDraft = false;
  private householdVersion = 0;
  private presetListVersion = 0;
  assumptions = defaults();
  snapshotId = "";
  loans: Record<string, LoanSettings> = {};
  private groupNames = new Map<string, GroupOption>();
  private assetNames = new Map<string, AssetOption>();
  private requestVersion = 0;
  private loadVersion = 0;
  private timer: ReturnType<typeof setTimeout> | undefined;
  private destroyed = false;
  money = money;
  symbol = currencySymbol;
  currencyChoices = currencyOptions;
  lines = series;

  currencies = computed(() => {
    const s = this.snapshot();
    return [
      ...new Set([
        ...(s?.itemSnapshots.map((i) => i.currencyCode) ?? []),
        ...(s?.financedAssetSnapshots.map((a) => a.currencyCode) ?? []),
        ...(s?.exchangeRates?.flatMap((r) => [
          r.baseCurrency,
          r.quoteCurrency,
        ]) ?? []),
        ...(this.assumptions.currencyCode
          ? [this.assumptions.currencyCode]
          : []),
      ]),
    ].sort();
  });
  otherCurrencies = computed(() =>
    currencyOptions.filter((c) => !this.currencies().includes(c.id)),
  );
  groups = computed(() => {
    const options = new Map<string, GroupOption>();
    for (const item of this.snapshot()?.itemSnapshots ?? [])
      if (item.includeInHousehold && item.financialType === 1)
        options.set(item.groupId, {
          id: item.groupId,
          name: item.groupName,
          category: item.categoryName,
        });
    for (const id of [...this.investmentGroups(), ...this.savingsGroups()])
      if (!options.has(id))
        options.set(id, {
          ...(this.groupNames.get(id) ?? {
            id,
            name: id,
            category: "Missing selections",
          }),
          missing: true,
        });
    const rows = [...options.values()].sort(
      (a, b) =>
        a.category.localeCompare(b.category) || a.name.localeCompare(b.name),
    );
    return [...new Set(rows.map((r) => r.category))].map((category) => ({
      category,
      options: rows.filter((r) => r.category === category),
    }));
  });
  assets = computed(() => {
    const options = new Map<string, AssetOption>();
    for (const asset of this.snapshot()?.financedAssetSnapshots ?? [])
      if (asset.includeInHousehold)
        options.set(asset.financedAssetId, {
          id: asset.financedAssetId,
          name: asset.assetName,
          currency: asset.currencyCode,
          value: asset.currentValuation,
          debt: asset.outstandingLoanBalance,
        });
    for (const id of this.selectedAssets())
      if (!options.has(id))
        options.set(id, {
          ...(this.assetNames.get(id) ?? {
            id,
            name: id,
            currency: "",
            value: null,
            debt: null,
          }),
          missing: true,
        });
    return [...options.values()].sort((a, b) => a.name.localeCompare(b.name));
  });
  chosenAssets = computed(() =>
    this.assets().filter((a) => this.selectedAssets().includes(a.id)),
  );
  balances = computed(() => {
    const rows = this.result()?.months;
    const key = this.adjusted() ? "inflationAdjusted" : "nominal";
    return rows?.length
      ? { starting: rows[0][key], final: rows.at(-1)![key] }
      : null;
  });
  omitted = computed(
    () =>
      this.result()?.coverage.filter((c) =>
        ["NotSelected", "ExcludedFromHousehold"].includes(c.disposition),
      ) ?? [],
  );
  payoffEvents = computed(() => {
    const months = this.result()?.months;
    if (!months?.length) return [];
    const events = new Map<number, PayoffEvent>();
    for (const asset of months.at(-1)!.financedAssets) {
      if (!asset.payoffDate) continue;
      const index = months.findIndex((m) => m.date === asset.payoffDate);
      if (index < 0) continue;
      const event = events.get(index) ?? {
        index,
        date: months[index].date,
        assetIds: [],
      };
      event.assetIds.push(asset.financedAssetId);
      events.set(index, event);
    }
    return [...events.values()].sort((a, b) => a.index - b.index);
  });
  chart = computed(() => {
    const months = this.result()?.months;
    if (!months?.length) return null;
    const key = this.adjusted() ? "inflationAdjusted" : "nominal";
    const values = months.flatMap((m) => series.map((s) => m[key][s.key]));
    const min = Math.min(0, ...values),
      maxObserved = Math.max(0, ...values);
    const max =
      maxObserved === min ? min + 1 : maxObserved + (maxObserved - min) * 0.1;
    const x = (i: number) => 88 + (i / Math.max(1, months.length - 1)) * 832;
    const y = (value: number) => 25 + ((max - value) / (max - min)) * 330;
    return {
      lines: series.map((s) => ({
        ...s,
        path: smoothPath(
          months.map((m, i) => ({ x: x(i), y: y(m[key][s.key]) })),
        ),
      })),
      payoffs: this.payoffEvents().map((event) => ({
        ...event,
        x: x(event.index),
        y: y(months[event.index][key].equity),
      })),
      ticks: Array.from({ length: 5 }, (_, i) => {
        const v = min + ((max - min) * i) / 4;
        return {
          y: y(v),
          label: new Intl.NumberFormat("en-GB", {
            notation: "compact",
            maximumFractionDigits: 1,
          }).format(v),
        };
      }),
      dates: Array.from({ length: 6 }, (_, i) => {
        const index = Math.round((i * (months.length - 1)) / 5);
        return { x: x(index), label: months[index].date.slice(0, 7) };
      }),
      hoverX: this.hovered() === null ? null : x(this.hovered()!),
    };
  });
  reading = computed(() => {
    const i = this.hovered();
    const month = i === null ? null : this.result()?.months?.[i];
    return month
      ? {
          date: month.date,
          values: this.adjusted() ? month.inflationAdjusted : month.nominal,
          payoff: this.payoffEvents().find((event) => event.index === i),
        }
      : null;
  });
  payoffs = computed(() => this.result()?.months?.at(-1)?.financedAssets ?? []);

  constructor() {
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.householdVersion++;
      this.loadVersion++;
      this.invalidate();
    });
    effect(() => {
      const home = this.w.current()?.id;
      this.householdVersion++;
      this.presets.set([]);
      this.presetsLoaded.set(false);
      this.savingPreset.set(false);
      this.presetError.set("");
      this.presetStatus.set("");
      this.presetId = "";
      this.presetName = "";
      this.presetDefault = false;
      this.savedDraft = "";
      this.retainedUnsavedDraft = false;
      this.loadVersion++;
      this.invalidate();
      this.snapshot.set(null);
      this.result.set(null);
      this.inventory.set([]);
      this.investmentGroups.set([]);
      this.savingsGroups.set([]);
      this.selectedAssets.set([]);
      this.groupNames.clear();
      this.assetNames.clear();
      this.loans = {};
      this.assumptions = defaults();
      this.adjusted.set(false);
      this.snapshotId = "";
      this.error.set("");
      this.validation.set("");
      this.loading.set(false);
      if (home) void this.initialize(home);
    });
  }
  private invalidate() {
    this.requestVersion++;
    clearTimeout(this.timer);
    this.pending.set(false);
    this.hovered.set(null);
  }
  async initialize(home: string) {
    const version = ++this.loadVersion;
    const householdVersion = this.householdVersion;
    this.loading.set(true);
    try {
      const [snapshots, presets] = await Promise.allSettled([
        this.api.get<Inventory[]>(`households/${home}/snapshots`),
        this.api.get<ProjectionPreset[]>(
          `households/${home}/projection-presets`,
        ),
      ]);
      if (version !== this.loadVersion || this.destroyed) return;
      if (presets.status === "fulfilled") {
        this.presets.set(presets.value);
        this.presetsLoaded.set(true);
        const preferred = presets.value.find((p) => p.isDefault);
        if (preferred) this.applyPreset(preferred);
      } else this.presetError.set(message(presets.reason));
      if (snapshots.status === "rejected") throw snapshots.reason;
      const rows = snapshots.value;
      this.inventory.set(rows);
      if (rows.length) await this.selectSnapshot(rows[0].id);
      if (
        householdVersion === this.householdVersion &&
        !this.destroyed &&
        !this.presetId
      )
        this.savedDraft = this.draftKey();
    } catch (e) {
      if (version === this.loadVersion) this.error.set(message(e));
    } finally {
      if (version === this.loadVersion) this.loading.set(false);
    }
  }
  private inputs(): ProjectionInputs {
    return {
      assumptions: {
        ...this.assumptions,
        currencies: this.assumptions.currencies.map((c) => ({ ...c })),
        showInflationAdjustedValues: this.adjusted(),
      },
      groups: [
        ...this.investmentGroups().map((groupId) => ({
          groupId,
          bucket: "Investment" as const,
        })),
        ...this.savingsGroups().map((groupId) => ({
          groupId,
          bucket: "Savings" as const,
        })),
      ],
      financedAssets: this.selectedAssets().map((id) => ({
        ...this.loans[id],
      })),
    };
  }
  private draftKey() {
    const inputs = this.inputs();
    // Ordering selections has no effect on a scenario's meaning.
    inputs.groups.sort((a, b) => a.groupId.localeCompare(b.groupId));
    inputs.financedAssets.sort((a, b) =>
      a.financedAssetId.localeCompare(b.financedAssetId),
    );
    inputs.assumptions.currencies.sort((a, b) =>
      a.currencyCode.localeCompare(b.currencyCode),
    );
    return JSON.stringify({
      name: this.presetName.trim(),
      isDefault: this.presetDefault,
      inputs,
    });
  }
  presetDirty() {
    return (
      this.retainedUnsavedDraft ||
      (!!this.savedDraft && this.draftKey() !== this.savedDraft)
    );
  }
  canLeave() {
    return (
      !this.savingPreset() &&
      (!this.presetDirty() || window.confirm("Discard unsaved preset changes?"))
    );
  }
  @HostListener("window:beforeunload", ["$event"])
  beforeUnload(event: BeforeUnloadEvent) {
    if (this.presetDirty() || this.savingPreset()) {
      event.preventDefault();
      event.returnValue = "";
    }
  }
  private applyPreset(preset: ProjectionPreset) {
    this.invalidate();
    this.presetId = preset.id;
    this.presetName = preset.name;
    this.presetDefault = preset.isDefault;
    const inputs = structuredClone(preset.inputs);
    this.assumptions = inputs.assumptions;
    this.adjusted.set(inputs.assumptions.showInflationAdjustedValues);
    this.investmentGroups.set(
      inputs.groups
        .filter((g) => g.bucket === "Investment")
        .map((g) => g.groupId),
    );
    this.savingsGroups.set(
      inputs.groups.filter((g) => g.bucket === "Savings").map((g) => g.groupId),
    );
    this.selectedAssets.set(
      inputs.financedAssets.map((a) => a.financedAssetId),
    );
    this.resetLoans();
    for (const loan of inputs.financedAssets)
      this.loans[loan.financedAssetId] = loan;
    this.savedDraft = this.draftKey();
    this.retainedUnsavedDraft = false;
  }
  loadPreset(id: string) {
    if (!this.canLeave()) return;
    this.presetError.set("");
    this.presetStatus.set("");
    const preset = this.presets().find((p) => p.id === id);
    if (id && !preset) return;
    if (preset) this.applyPreset(preset);
    else {
      this.presetId = "";
      this.presetName = "";
      this.presetDefault = false;
      const currencyCode = this.currencies()[0] ?? "";
      this.assumptions = {
        ...defaults(),
        currencyCode,
        currencies: currencyCode ? [newPot(currencyCode)] : [],
      };
      this.adjusted.set(false);
      this.investmentGroups.set([]);
      this.savingsGroups.set([]);
      this.selectedAssets.set([]);
      this.resetLoans();
      this.savedDraft = this.draftKey();
      this.retainedUnsavedDraft = false;
    }
    this.reconcileSelectedGroupCurrencies();
    this.changed();
  }
  private resetLoans() {
    this.loans = Object.fromEntries(
      (this.snapshot()?.financedAssetSnapshots ?? []).map((a) => [
        a.financedAssetId,
        newLoan(a.financedAssetId),
      ]),
    );
  }
  canSavePreset() {
    return (
      !!this.presetName.trim() &&
      !this.numericError() &&
      !this.savingPreset() &&
      !this.loading() &&
      !!this.snapshot()
    );
  }
  async savePreset(asNew = false) {
    if (!this.canSavePreset()) return;
    const home = this.w.current()?.id;
    if (!home) return;
    const version = this.householdVersion;
    const id = asNew ? "" : this.presetId;
    const body = {
      name: this.presetName.trim(),
      isDefault: this.presetDefault,
      inputs: this.inputs(),
    };
    this.presetListVersion++;
    this.savingPreset.set(true);
    this.presetError.set("");
    this.presetStatus.set("");
    try {
      const saved = await this.api.send<ProjectionPreset>(
        id ? "PUT" : "POST",
        `households/${home}/projection-presets${id ? `/${id}` : ""}`,
        body,
      );
      if (version !== this.householdVersion || this.destroyed) return;
      this.presets.update((rows) =>
        [
          ...rows
            .filter((p) => p.id !== saved.id)
            .map((p) => (saved.isDefault ? { ...p, isDefault: false } : p)),
          saved,
        ].sort((a, b) => a.name.localeCompare(b.name)),
      );
      this.presetId = saved.id;
      this.presetName = saved.name;
      this.savedDraft = this.draftKey();
      this.retainedUnsavedDraft = false;
      this.presetStatus.set("Preset saved.");
    } catch (e) {
      if (version === this.householdVersion && !this.destroyed)
        this.presetError.set(message(e));
    } finally {
      if (version === this.householdVersion && !this.destroyed)
        this.savingPreset.set(false);
    }
  }
  async deletePreset() {
    const id = this.presetId;
    const home = this.w.current()?.id;
    if (
      !id ||
      !home ||
      this.savingPreset() ||
      !window.confirm(
        "Delete this saved preset? Your current assumptions will remain on screen.",
      )
    )
      return;
    const version = this.householdVersion;
    this.presetListVersion++;
    this.savingPreset.set(true);
    this.presetError.set("");
    this.presetStatus.set("");
    try {
      await this.api.send(
        "DELETE",
        `households/${home}/projection-presets/${id}`,
        null,
      );
      if (version !== this.householdVersion || this.destroyed) return;
      this.presets.update((rows) => rows.filter((p) => p.id !== id));
      this.presetId = "";
      this.presetDefault = false;
      this.savedDraft = "";
      this.retainedUnsavedDraft = true;
      this.presetStatus.set(
        "Preset deleted. Current assumptions have been kept.",
      );
    } catch (e) {
      if (version === this.householdVersion && !this.destroyed)
        this.presetError.set(message(e));
    } finally {
      if (version === this.householdVersion && !this.destroyed)
        this.savingPreset.set(false);
    }
  }
  async reloadPresets() {
    const home = this.w.current()?.id;
    if (!home || this.savingPreset()) return;
    const version = this.householdVersion;
    const listVersion = ++this.presetListVersion;
    try {
      const presets = await this.api.get<ProjectionPreset[]>(
        `households/${home}/projection-presets`,
      );
      if (
        version !== this.householdVersion ||
        listVersion !== this.presetListVersion ||
        this.destroyed
      )
        return;
      this.presets.set(presets);
      this.presetsLoaded.set(true);
      this.presetError.set("");
    } catch (e) {
      if (
        version === this.householdVersion &&
        listVersion === this.presetListVersion &&
        !this.destroyed
      )
        this.presetError.set(message(e));
    }
  }
  async selectSnapshot(id: string) {
    const home = this.w.current()?.id;
    if (!home) return;
    const version = ++this.loadVersion;
    this.invalidate();
    this.snapshotId = id;
    this.snapshot.set(null);
    this.result.set(null);
    this.loading.set(true);
    this.error.set("");
    this.validation.set("");
    try {
      const snapshot = await this.api.get<ModellingSnapshot>(
        `households/${home}/snapshots/${id}`,
      );
      if (version !== this.loadVersion || this.destroyed) return;
      for (const item of snapshot.itemSnapshots)
        this.groupNames.set(item.groupId, {
          id: item.groupId,
          name: item.groupName,
          category: item.categoryName,
        });
      for (const a of snapshot.financedAssetSnapshots) {
        this.assetNames.set(a.financedAssetId, {
          id: a.financedAssetId,
          name: a.assetName,
          currency: a.currencyCode,
          value: a.currentValuation,
          debt: a.outstandingLoanBalance,
        });
        this.loans[a.financedAssetId] ??= newLoan(a.financedAssetId);
      }
      this.snapshot.set(snapshot);
      if (
        !this.assumptions.currencyCode &&
        !this.hasContributions() &&
        !this.selectedAssets().length
      )
        this.assumptions.currencyCode = this.currencies()[0] ?? "";
      if (!this.assumptions.currencies.length && this.assumptions.currencyCode)
        this.assumptions.currencies.push(newPot(this.assumptions.currencyCode));
      this.reconcileSelectedGroupCurrencies();
      this.loading.set(false);
      this.changed();
    } catch (e) {
      if (version === this.loadVersion) this.error.set(message(e));
    } finally {
      if (version === this.loadVersion) this.loading.set(false);
    }
  }
  hasContributions() {
    return this.assumptions.currencies.some(
      (c) => c.monthlyInvestmentContribution || c.monthlySavingsContribution,
    );
  }
  potCodes() {
    return this.assumptions.currencies.map((c) => c.currencyCode);
  }
  viewCurrencyChanged() {
    if (!this.assumptions.currencies.length && this.assumptions.currencyCode)
      this.assumptions.currencies.push(newPot(this.assumptions.currencyCode));
    this.changed();
  }
  addCurrency(code: string) {
    if (!this.potCodes().includes(code))
      this.assumptions.currencies.push(newPot(code));
    this.changed();
  }
  isDestination(code: string) {
    return this.selectedAssets().some(
      (id) => this.loans[id]?.investmentCurrencyAfterPayoff === code,
    );
  }
  removeCurrency(code: string) {
    if (!this.isDestination(code))
      this.assumptions.currencies = this.assumptions.currencies.filter(
        (c) => c.currencyCode !== code,
      );
    this.changed();
  }

  retry() {
    if (this.snapshot()) this.changed();
    else if (this.snapshotId) void this.selectSnapshot(this.snapshotId);
    else if (this.w.current()) void this.initialize(this.w.current()!.id);
  }

  setGroups(bucket: "Investment" | "Savings", ids: string[]) {
    (bucket === "Investment" ? this.investmentGroups : this.savingsGroups).set(
      ids,
    );
    this.reconcileSelectedGroupCurrencies();
    this.changed();
  }
  private reconcileSelectedGroupCurrencies() {
    for (const item of this.snapshot()?.itemSnapshots ?? [])
      if (
        [...this.investmentGroups(), ...this.savingsGroups()].includes(
          item.groupId,
        ) &&
        item.includeInHousehold &&
        item.financialType === 1 &&
        !this.potCodes().includes(item.currencyCode)
      )
        this.assumptions.currencies.push(newPot(item.currencyCode));
  }
  setAssets(ids: string[]) {
    this.selectedAssets.set(ids);
    for (const id of ids)
      if (this.loans[id] && !this.loans[id].investmentCurrencyAfterPayoff)
        this.loans[id].investmentCurrencyAfterPayoff = this.potCodes()[0] ?? "";
    this.changed();
  }
  private numericError() {
    const a = this.assumptions;
    const rate = (v: number | null, negative = true) =>
      v !== null &&
      Number.isFinite(v) &&
      v <= 100 &&
      (negative ? v > -100 : v >= 0);
    const amount = (v: number | null) =>
      v !== null && Number.isFinite(v) && v >= 0;
    if (
      !Number.isInteger(a.durationYears) ||
      a.durationYears! < 1 ||
      a.durationYears! > 100
    )
      return "Enter a whole duration from 1 to 100 years.";
    if (!a.currencies.length) return "Add at least one currency pot.";
    for (const pot of a.currencies) {
      if (
        !amount(pot.monthlyInvestmentContribution) ||
        !amount(pot.monthlySavingsContribution)
      )
        return "Monthly contributions must be zero or a positive amount.";
      if (
        !rate(pot.annualInvestmentReturnPercent) ||
        !rate(pot.annualSavingsInterestPercent)
      )
        return "Annual returns must be greater than −100% and at most 100%.";
    }
    if (!rate(a.annualInflationPercent))
      return "Inflation must be greater than −100% and at most 100%.";
    if (!a.currencyCode) return "Select a projection currency.";
    if (this.investmentGroups().some((id) => this.savingsGroups().includes(id)))
      return "A group can only be included in one bucket.";
    for (const id of this.selectedAssets()) {
      const loan = this.loans[id];
      if (
        !loan ||
        !amount(loan.monthlyLoanPaymentInAssetCurrency) ||
        !amount(loan.extraMonthlyLoanPaymentInAssetCurrency) ||
        !this.potCodes().includes(loan.investmentCurrencyAfterPayoff) ||
        !rate(loan.annualLoanInterestPercent, false) ||
        !rate(loan.annualAssetAppreciationPercent)
      )
        return "Check each loan's payment, interest (0–100%) and asset growth (greater than −100% to 100%).";
    }
    return "";
  }
  changed() {
    this.invalidate();
    this.error.set("");
    const error = this.numericError();
    this.validation.set(error);
    if (error || !this.snapshot() || this.loading()) {
      this.result.set(null);
      return;
    }
    if (
      !this.investmentGroups().length &&
      !this.savingsGroups().length &&
      !this.selectedAssets().length &&
      !this.hasContributions()
    ) {
      this.result.set(null);
      return;
    }
    const version = this.requestVersion;
    const home = this.w.current()!.id,
      snapshot = this.snapshotId;
    const inputs = this.inputs();
    this.pending.set(true);
    this.timer = setTimeout(async () => {
      try {
        const result = await this.api.send<ProjectionRun>(
          "POST",
          `households/${home}/snapshots/${snapshot}/projection`,
          { inputs },
        );
        if (version === this.requestVersion && !this.destroyed) {
          this.hovered.set(null);
          this.result.set(result);
        }
      } catch (e) {
        if (version === this.requestVersion && !this.destroyed) {
          this.result.set(null);
          this.error.set(message(e));
        }
      } finally {
        if (version === this.requestVersion && !this.destroyed)
          this.pending.set(false);
      }
    }, 300);
  }
  hover(event: MouseEvent) {
    const months = this.result()?.months;
    if (!months?.length) return;
    const bounds = (
      event.currentTarget as SVGSVGElement
    ).getBoundingClientRect();
    if (!bounds.width) return;
    const x = ((event.clientX - bounds.left) / bounds.width) * 1000;
    this.hovered.set(
      Math.max(
        0,
        Math.min(
          months.length - 1,
          Math.round(((x - 88) / 832) * (months.length - 1)),
        ),
      ),
    );
  }
  moveReading(delta: number, event: Event) {
    event.preventDefault();
    const length = this.result()?.months?.length ?? 0;
    if (length)
      this.hovered.set(
        Math.max(0, Math.min(length - 1, (this.hovered() ?? 0) + delta)),
      );
  }
  inspectPayoff(index: number, event: Event) {
    event.preventDefault();
    event.stopPropagation();
    this.hovered.set(index);
  }
  payoffLabel(event: PayoffEvent) {
    const names = event.assetIds.map((id) => this.assetName(id)).join(", ");
    return `${names} · ${event.index === 0 ? "Already paid off at start" : "Loan paid off"} · ${event.date}`;
  }
  assetName(id: string) {
    return this.assetNames.get(id)?.name ?? id;
  }
}
