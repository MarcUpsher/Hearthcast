import { Component, effect, inject, signal } from "@angular/core";
import { CommonModule } from "@angular/common";
import { FormsModule } from "@angular/forms";
import { RouterLink, ActivatedRoute, Router } from "@angular/router";
import { MatButtonModule } from "@angular/material/button";
import { MatFormFieldModule } from "@angular/material/form-field";
import { MatSelectModule } from "@angular/material/select";
import { MatInputModule } from "@angular/material/input";
import {
  Api,
  Workspace,
  Inventory,
  Snapshot,
  Report,
  ReportCurrency,
  Totals,
  message,
} from "../core/api";
@Component({
  imports: [
    CommonModule,
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatSelectModule,
    MatInputModule,
  ],
  templateUrl: "browser.html",
})
export class SnapshotBrowser {
  w = inject(Workspace);
  api = inject(Api);
  route = inject(ActivatedRoute);
  router = inject(Router);
  inventory = signal<Inventory[]>([]);
  saved = signal<Snapshot | null>(null);
  reports = signal<Report[]>([]);
  busy = signal(false);
  error = signal("");
  selected = "";
  year = "";
  view = 0;
  person = "";
  search = "";
  currency = "";
  private generation = 0;
  constructor() {
    effect(() => {
      const id = this.w.current()?.id;
      this.generation++;
      this.saved.set(null);
      this.inventory.set([]);
      if (id) void this.load();
    });
  }
  async load() {
    const g = ++this.generation;
    this.busy.set(true);
    this.error.set("");
    try {
      const rows = await this.api.get<Inventory[]>(this.w.path("snapshots"));
      if (g !== this.generation) return;
      this.inventory.set(rows);
      const query = this.route.snapshot.queryParamMap.get("snapshot");
      const choice =
        rows.find((s) => s.id === (query ?? this.selected)) ?? rows[0];
      if (choice) {
        this.selected = choice.id;
        this.year = choice.snapshotDate.slice(0, 4);
        await this.open();
      } else this.busy.set(false);
    } catch (e) {
      if (g === this.generation) {
        this.error.set(message(e));
        this.busy.set(false);
      }
    }
  }
  years() {
    return [
      ...new Set(this.inventory().map((s) => s.snapshotDate.slice(0, 4))),
    ];
  }
  months() {
    return this.inventory().filter((s) => s.snapshotDate.startsWith(this.year));
  }
  changeYear() {
    const row = this.months()[0];
    if (row) {
      this.selected = row.id;
      void this.open();
    }
  }
  async open() {
    const g = ++this.generation;
    this.busy.set(true);
    this.error.set("");
    this.saved.set(null);
    try {
      const base = this.w.path("snapshots/" + this.selected);
      const [saved, ...reports] = await Promise.all([
        this.api.get<Snapshot>(base),
        ...["household-summary", "individual-holdings", "summary"].map((s) =>
          this.api.get<Report>(base + "/" + s),
        ),
      ]);
      if (g !== this.generation) return;
      this.saved.set(saved);
      this.reports.set(reports);
      if (!this.people().some((p) => p.person.personId === this.person))
        this.changeView();
      void this.router.navigate([], {
        queryParams: { snapshot: this.selected },
        replaceUrl: true,
      });
    } catch (e) {
      if (g === this.generation) this.error.set(message(e));
    } finally {
      if (g === this.generation) this.busy.set(false);
    }
  }
  people() {
    return this.view === 2 ? [] : (this.reports()[this.view]?.people ?? []);
  }
  changeView() {
    this.person =
      this.view === 1 ? (this.people()[0]?.person.personId ?? "") : "";
  }
  currencyOptions() {
    return [
      ...new Set([
        ...(this.saved()?.itemSnapshots ?? []).map((i) => i.currencyCode),
        ...(this.saved()?.financedAssetSnapshots ?? []).map(
          (a) => a.currencyCode,
        ),
      ]),
    ].sort();
  }
  currencies(): ReportCurrency[] {
    const report = this.reports()[this.view];
    if (!report) return [];
    if (this.view === 1 || this.person) {
      const p =
        report.people.find((p) => p.person.personId === this.person) ??
        (this.view === 1 ? report.people[0] : undefined);
      return p?.currencies ?? [];
    }
    return report.currencies ?? [];
  }
  totals(
    row:
      | ReportCurrency
      | { totals: Totals; holdingsTotals: Totals; allRecordedTotals: Totals },
  ) {
    return this.view === 0
      ? row.totals
      : this.view === 1
        ? row.holdingsTotals
        : row.allRecordedTotals;
  }
  money(value: number | null | undefined, currency: string) {
    return value == null
      ? "Unknown"
      : new Intl.NumberFormat("en-GB", {
          style: "currency",
          currency,
          minimumFractionDigits: 2,
          maximumFractionDigits: 2,
        }).format(value);
  }
  status(origin: number) {
    return origin === 3
      ? "Carried forward"
      : origin === 2
        ? "Confirmed"
        : "Unrecorded";
  }
  items() {
    return (this.saved()?.itemSnapshots ?? []).filter(
      (i) =>
        (!this.currency || i.currencyCode === this.currency) &&
        `${i.itemName} ${i.groupName} ${i.categoryName} ${i.institutionName ?? ""} ${i.fundName ?? ""}`
          .toLowerCase()
          .includes(this.search.toLowerCase()),
    );
  }
  holder(id: string) {
    return (
      this.saved()?.people.find((p) => p.id === id)?.displayName ?? "Unknown"
    );
  }
}
