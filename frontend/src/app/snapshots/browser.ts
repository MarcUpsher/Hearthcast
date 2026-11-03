import { money } from "../core/money";
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
  SavedItem,
  SavedAsset,
  Report,
  ReportCurrency,
  ReportGroup,
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
  styleUrl: "browser.scss",
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
  money = money;
  groupEntries(group: ReportGroup, financialType?: number) {
    const entries = (group.items ?? []).map((entry) => {
      const item = "item" in entry ? entry.item : entry;
      const amount =
        "item" in entry ? entry.contributingAmount : item.balance.amount;
      const share =
        "item" in entry && entry.allocationShareBasisPoints !== 10000
          ? ` · ${entry.allocationShareBasisPoints / 100}% household share`
          : "";
      return {
        key: "item:" + item.itemSnapshotId,
        name: item.itemName,
        description:
          "Held by " +
          this.holder(item.holderSnapshotPersonId) +
          share +
          (financialType === 2 ? " · Debt" : ""),
        institutionName: item.institutionName,
        fundName: item.fundName,
        amount: amount == null ? null : financialType === 2 ? -amount : amount,
        status: this.status(item.balance.valueOrigin),
      };
    });
    for (const entry of group.financedAssets ?? []) {
      const asset = "asset" in entry ? entry.asset : entry;
      const amount =
        "asset" in entry ? entry.attributedEquity : asset.currentEquity;
      const share =
        "asset" in entry
          ? (entry.allocationShareBasisPoints ??
            entry.ownershipShareBasisPoints)
          : undefined;
      entries.push({
        key: "asset:" + asset.financedAssetSnapshotId,
        name: asset.assetName,
        description:
          "Equity" +
          (share !== undefined && share !== 10000
            ? ` · ${share / 100}% ${"allocationShareBasisPoints" in entry ? "household share" : "ownership"}`
            : ""),
        institutionName: undefined,
        fundName: undefined,
        amount,
        status:
          amount == null
            ? "Missing balances"
            : this.status(
                asset.currentValuation.valueOrigin === 3 ||
                  asset.outstandingLoanBalance.valueOrigin === 3
                  ? 3
                  : 2,
              ),
      });
    }
    return entries.sort(
      (a, b) =>
        a.name.localeCompare(b.name, "en", {
          numeric: true,
          sensitivity: "base",
        }) || a.key.localeCompare(b.key),
    );
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
  groupedEntries() {
    type Group = {
      key: string;
      name: string;
      items: SavedItem[];
      assets: SavedAsset[];
    };
    type Category = { key: string; name: string; groups: Map<string, Group> };
    const people = new Map<
      string,
      { key: string; name: string; categories: Map<string, Category> }
    >();
    const groupFor = (
      personKey: string,
      personName: string,
      entry: SavedItem | SavedAsset,
    ) => {
      let person = people.get(personKey);
      if (!person) {
        person = { key: personKey, name: personName, categories: new Map() };
        people.set(personKey, person);
      }
      const categoryKey = JSON.stringify([
        entry.categoryId,
        entry.categoryName,
      ]);
      let category = person.categories.get(categoryKey);
      if (!category) {
        category = {
          key: categoryKey,
          name: entry.categoryName,
          groups: new Map(),
        };
        person.categories.set(categoryKey, category);
      }
      const groupKey = JSON.stringify([entry.groupId, entry.groupName]);
      let group = category.groups.get(groupKey);
      if (!group) {
        group = { key: groupKey, name: entry.groupName, items: [], assets: [] };
        category.groups.set(groupKey, group);
      }
      return group;
    };
    for (const item of this.items())
      groupFor(
        item.holderSnapshotPersonId,
        this.holder(item.holderSnapshotPersonId),
        item,
      ).items.push(item);
    const collator = new Intl.Collator("en", {
      numeric: true,
      sensitivity: "base",
    });
    for (const asset of this.saved()?.financedAssetSnapshots ?? []) {
      if (
        (this.currency && asset.currencyCode !== this.currency) ||
        !`${asset.assetName} ${asset.categoryName} ${asset.groupName}`
          .toLowerCase()
          .includes(this.search.toLowerCase())
      )
        continue;
      const owners = asset.owners
        .map((owner) => ({
          id: owner.snapshotPersonId,
          name: this.holder(owner.snapshotPersonId),
        }))
        .sort(
          (a, b) =>
            collator.compare(a.name, b.name) || a.id.localeCompare(b.id),
        );
      const key =
        owners.length === 1
          ? owners[0].id
          : "owners:" +
            owners
              .map((owner) => owner.id)
              .sort()
              .join("|");
      const name =
        owners.map((owner) => owner.name).join(" & ") +
        (owners.length > 1 ? " (joint)" : "");
      groupFor(key, name || "Unknown owner", asset).assets.push(asset);
    }
    const compare = (
      a: { name: string; key: string },
      b: { name: string; key: string },
    ) => collator.compare(a.name, b.name) || a.key.localeCompare(b.key);
    return [...people.values()].sort(compare).map((person) => ({
      ...person,
      categories: [...person.categories.values()]
        .sort(compare)
        .map((category) => ({
          ...category,
          groups: [...category.groups.values()].sort(compare).map((group) => ({
            ...group,
            items: group.items.sort(
              (a, b) =>
                collator.compare(a.itemName, b.itemName) ||
                a.trackedItemId.localeCompare(b.trackedItemId),
            ),
            assets: group.assets.sort(
              (a, b) =>
                collator.compare(a.assetName, b.assetName) ||
                a.financedAssetId.localeCompare(b.financedAssetId),
            ),
          })),
        })),
    }));
  }
  holder(id: string) {
    return (
      this.saved()?.people.find((p) => p.id === id)?.displayName ?? "Unknown"
    );
  }
}
