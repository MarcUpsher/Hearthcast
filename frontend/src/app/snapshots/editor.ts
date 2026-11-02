import {
  Component,
  HostListener,
  computed,
  effect,
  inject,
  signal,
} from "@angular/core";
import { FormsModule } from "@angular/forms";
import { Router, RouterLink, ActivatedRoute } from "@angular/router";
import { MatButtonModule } from "@angular/material/button";
import { MatFormFieldModule } from "@angular/material/form-field";
import { MatSelectModule } from "@angular/material/select";
import { MatInputModule } from "@angular/material/input";
import {
  Api,
  Workspace,
  Inventory,
  Item,
  Asset,
  Snapshot,
  Category,
  Group,
  message,
} from "../core/api";
type CaptureMode = "next" | "new" | "correct";
interface Entry {
  id: string;
  name: string;
  currency: string;
  kind?: number;
  mode: string;
  amount: string;
  previous: number | null;
  confirmed: string | null;
  personKey?: string;
  personName?: string;
  categoryId?: string;
  categoryName?: string;
  groupId?: string;
  groupName?: string;
  itemName?: string;
  institutionName?: string | null;
  fundName?: string | null;
}
@Component({
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatSelectModule,
    MatInputModule,
  ],
  templateUrl: "editor.html",
})
export class SnapshotEditor {
  w = inject(Workspace);
  api = inject(Api);
  router = inject(Router);
  route = inject(ActivatedRoute);
  rows = signal<Entry[]>([]);
  private readonly collator = new Intl.Collator("en", {
    numeric: true,
    sensitivity: "base",
  });
  groupedRows = computed(() => {
    const people = new Map<
      string,
      {
        key: string;
        name: string;
        categories: Map<
          string,
          {
            key: string;
            name: string;
            groups: Map<string, { key: string; name: string; rows: Entry[] }>;
          }
        >;
      }
    >();
    for (const row of this.rows()) {
      const key = row.personKey ?? "unknown";
      let person = people.get(key);
      if (!person) {
        person = {
          key,
          name: row.personName ?? "Unknown holder",
          categories: new Map(),
        };
        people.set(key, person);
      }
      const categoryKey = row.categoryId ?? "unknown";
      let category = person.categories.get(categoryKey);
      if (!category) {
        category = {
          key: categoryKey,
          name: row.categoryName ?? "Unknown category",
          groups: new Map(),
        };
        person.categories.set(categoryKey, category);
      }
      const groupKey = row.groupId ?? "unknown";
      let group = category.groups.get(groupKey);
      if (!group) {
        group = {
          key: groupKey,
          name: row.groupName ?? "Unknown group",
          rows: [],
        };
        category.groups.set(groupKey, group);
      }
      group.rows.push(row);
    }
    const compare = (
      a: { name: string; key: string },
      b: { name: string; key: string },
    ) => this.collator.compare(a.name, b.name) || a.key.localeCompare(b.key);
    return [...people.values()].sort(compare).map((person) => ({
      key: person.key,
      name: person.name,
      categories: [...person.categories.values()]
        .sort(compare)
        .map((category) => ({
          key: category.key,
          name: category.name,
          groups: [...category.groups.values()].sort(compare).map((group) => ({
            ...group,
            rows: group.rows.sort(
              (a, b) =>
                this.collator.compare(
                  a.itemName ?? a.name,
                  b.itemName ?? b.name,
                ) ||
                a.id.localeCompare(b.id) ||
                (a.kind ?? 0) - (b.kind ?? 0),
            ),
          })),
        })),
    }));
  });
  inventory = signal<Inventory[]>([]);
  source = signal<Snapshot | null>(null);
  busy = signal(false);
  error = signal("");
  saving = signal(false);
  ready = signal(false);
  private baseline: string | null = null;
  private captureDate = "";
  private capturePrecision = 2;
  private previousMode: CaptureMode = "next";
  mode: CaptureMode = this.route.snapshot.queryParamMap.has("correct")
    ? "correct"
    : "next";
  sourceId = this.route.snapshot.queryParamMap.get("correct") ?? "";
  precision = 2;
  date = new Date().toLocaleDateString("en-CA");
  private generation = 0;
  items: Item[] = [];
  assets: Asset[] = [];
  constructor() {
    effect(() => {
      if (this.w.current()) void this.load();
      else {
        this.generation++;
        this.ready.set(false);
        this.rows.set([]);
        this.source.set(null);
      }
    });
  }
  private fingerprint() {
    return JSON.stringify([
      this.date,
      this.precision,
      this.rows().map((r) => [r.id, r.kind, r.mode, r.amount]),
    ]);
  }
  hasUnsavedChanges() {
    return (
      this.ready() &&
      this.baseline !== null &&
      this.baseline !== this.fingerprint()
    );
  }
  canLeave() {
    return (
      !this.saving() &&
      (!this.hasUnsavedChanges() ||
        confirm("Discard unsaved snapshot changes?"))
    );
  }
  @HostListener("window:beforeunload", ["$event"])
  beforeUnload(event: BeforeUnloadEvent) {
    if (this.hasUnsavedChanges() || this.saving()) {
      event.preventDefault();
      event.returnValue = "";
    }
  }
  changeMode(mode: CaptureMode, select?: { value: string }) {
    if (this.busy() || !this.canLeave()) {
      if (select) select.value = this.mode;
      return;
    }
    this.mode = mode;
    void this.load();
  }
  changeSource(id: string, select?: { value: string }) {
    if (this.busy() || !this.canLeave()) {
      if (select) select.value = this.sourceId;
      return;
    }
    this.sourceId = id;
    void this.load();
  }
  async load() {
    if (this.mode === "correct" && this.previousMode !== "correct") {
      this.captureDate = this.date;
      this.capturePrecision = this.precision;
    } else if (this.mode !== "correct" && this.previousMode === "correct") {
      this.date = this.captureDate || new Date().toLocaleDateString("en-CA");
      this.precision = this.capturePrecision;
    }
    this.previousMode = this.mode;
    this.ready.set(false);
    this.baseline = null;
    this.rows.set([]);
    this.source.set(null);
    const g = ++this.generation;
    this.busy.set(true);
    this.error.set("");
    try {
      const [inventory, items, assets, categories] = await Promise.all([
        this.api.get<Inventory[]>(this.w.path("snapshots")),
        this.api.get<Item[]>(this.w.path("tracked-items")),
        this.api.get<Asset[]>(this.w.path("financed-assets")),
        this.mode === "correct"
          ? Promise.resolve([] as Category[])
          : this.api.get<Category[]>(this.w.path("categories")),
      ]);
      if (g !== this.generation) return;
      const groups = (
        await Promise.all(
          categories.map((c) =>
            this.api.get<Group[]>(
              this.w.path("categories/" + c.id + "/groups"),
            ),
          ),
        )
      ).flat();
      if (g !== this.generation) return;
      this.inventory.set(inventory);
      this.items = items;
      this.assets = assets;
      if (!inventory.some((i) => i.id === this.sourceId))
        this.sourceId = inventory[0]?.id ?? "";
      const source = this.sourceId
        ? await this.api.get<Snapshot>(
            this.w.path("snapshots/" + this.sourceId),
          )
        : null;
      if (g !== this.generation) return;
      this.source.set(source);
      if (this.mode === "correct" && source) {
        this.date = source.snapshotDate;
        this.precision = source.datePrecision;
      }
      const rows: Entry[] = [];
      const person = (id: string, saved = false) => {
        if (saved) {
          const p = source?.people.find((p) => p.id === id);
          return {
            personKey: p?.personId ?? id,
            personName: p?.displayName ?? "Unknown holder",
          };
        }
        const p = this.w.current()?.people.find((p) => p.id === id);
        return {
          personKey: id,
          personName: p?.displayName ?? "Unknown holder",
        };
      };
      const category = (groupId: string) => {
        const group = groups.find((g) => g.id === groupId);
        const found = categories.find((c) => c.id === group?.categoryId);
        return {
          categoryId: found?.id,
          categoryName: found?.name,
          groupId,
          groupName: group?.name,
        };
      };
      const selected =
        this.mode === "correct"
          ? (source?.itemSnapshots ?? []).map((i) => ({
              id: i.trackedItemId,
              name: i.itemName,
              institutionName: i.institutionName,
              fundName: i.fundName,
              currencyCode: i.currencyCode,
              ...person(i.holderSnapshotPersonId, true),
              categoryId: i.categoryId,
              categoryName: i.categoryName,
              groupId: i.groupId,
              groupName: i.groupName,
            }))
          : items
              .filter((i) => !i.isRetired)
              .map((i) => ({
                ...i,
                ...person(i.holderPersonId),
                ...category(i.groupId),
              }));
      for (const i of selected) {
        const old = source?.itemSnapshots.find((v) => v.trackedItemId === i.id);
        rows.push({
          id: i.id,
          name: i.name,
          currency: i.currencyCode,
          mode: this.mode === "new" ? "clear" : "keep",
          amount: old?.amount == null ? "" : String(old.amount),
          previous: old?.amount ?? null,
          confirmed: old?.lastConfirmedDate ?? null,
          personKey: i.personKey,
          personName: i.personName,
          categoryId: i.categoryId,
          categoryName: i.categoryName,
          groupId: i.groupId,
          groupName: i.groupName,
          itemName: i.name,
          institutionName: i.institutionName,
          fundName: i.fundName,
        });
      }
      const selectedAssets =
        this.mode === "correct"
          ? (source?.financedAssetSnapshots ?? []).map((a) => ({
              id: a.financedAssetId,
              name: a.assetName,
              currencyCode: a.currencyCode,
              hasWithdrawalFacility: a.hasWithdrawalFacility,
              ownerPeople: a.owners.map((o) =>
                person(o.snapshotPersonId, true),
              ),
              categoryId: a.categoryId,
              categoryName: a.categoryName,
              groupId: a.groupId,
              groupName: a.groupName,
            }))
          : assets
              .filter((a) => !a.isRetired)
              .map((a) => ({
                ...a,
                ownerPeople: a.owners.map((o) => person(o.personId)),
                ...category(a.groupId),
              }));
      for (const a of selectedAssets) {
        const owners = [...a.ownerPeople].sort(
          (x, y) =>
            this.collator.compare(x.personName, y.personName) ||
            x.personKey.localeCompare(y.personKey),
        );
        const personKey =
          owners.length === 1
            ? owners[0].personKey
            : "owners:" +
              owners
                .map((o) => o.personKey)
                .sort()
                .join("|");
        const personName =
          owners.map((o) => o.personName).join(" & ") +
          (owners.length > 1 ? " (joint)" : "");
        const old = source?.financedAssetSnapshots.find(
          (v) => v.financedAssetId === a.id,
        );
        for (const [kind, key, label] of [
          [1, "currentValuation", "Valuation"],
          [2, "outstandingLoanBalance", "Outstanding loan"],
          [3, "availableToWithdraw", "Available to withdraw"],
        ] as const) {
          if (kind === 3 && !a.hasWithdrawalFacility) continue;
          const value = old?.[key] ?? null;
          rows.push({
            id: a.id,
            kind,
            name: a.name + " · " + label,
            currency: a.currencyCode,
            mode: this.mode === "new" ? "clear" : "keep",
            amount: value == null ? "" : String(value),
            previous: value,
            confirmed: null,
            personKey,
            personName,
            categoryId: a.categoryId,
            categoryName: a.categoryName,
            groupId: a.groupId,
            groupName: a.groupName,
            itemName: a.name,
          });
        }
      }
      this.rows.set(rows);
      this.baseline = this.fingerprint();
      this.ready.set(true);
    } catch (e) {
      if (g === this.generation) this.error.set(message(e));
    } finally {
      if (g === this.generation) this.busy.set(false);
    }
  }
  async save() {
    if (!this.ready() || this.busy()) return;
    this.saving.set(true);
    this.busy.set(true);
    this.error.set("");
    try {
      const values: { trackedItemId: string; amount: number | null }[] = [];
      const financedAssetValues: {
        financedAssetId: string;
        balance: number;
        amount: number | null;
      }[] = [];
      for (const r of this.rows()) {
        if (r.mode === "keep") continue;
        let amount: number | null = null;
        if (r.mode === "record") {
          if (!/^\d{1,12}(\.\d{1,2})?$/.test(r.amount))
            throw new Error(
              r.name +
                ": enter a nonnegative amount with at most 12 whole digits and two decimal places.",
            );
          amount = Number(r.amount);
        }
        if (r.kind)
          financedAssetValues.push({
            financedAssetId: r.id,
            balance: r.kind,
            amount,
          });
        else values.push({ trackedItemId: r.id, amount });
      }
      let body: unknown;
      let method = "POST";
      let path = this.w.path("snapshots");
      if (this.mode === "correct") {
        if (!values.length && !financedAssetValues.length)
          throw new Error("Choose at least one value to record or clear.");
        method = "PATCH";
        path += "/" + this.sourceId + "/values";
        body = {
          expectedRevision: this.source()!.revision,
          values,
          financedAssetValues,
        };
      } else {
        body = {
          snapshotDate:
            this.precision === 2 ? this.date.slice(0, 7) + "-01" : this.date,
          datePrecision: this.precision,
          values,
          financedAssetValues,
          ...(this.mode === "next"
            ? { sourceSnapshotId: this.sourceId || null }
            : {}),
        };
        if (this.mode === "next") path += "/next";
      }
      const saved = await this.api.send<Snapshot>(method, path, body);
      this.baseline = this.fingerprint();
      this.saving.set(false);
      this.busy.set(false);
      await this.router.navigate(["/snapshots"], {
        queryParams: { snapshot: saved.id },
      });
    } catch (e) {
      this.error.set(message(e));
    } finally {
      this.saving.set(false);
      this.busy.set(false);
    }
  }
}
