import {
  Component,
  DestroyRef,
  TemplateRef,
  effect,
  inject,
  signal,
  viewChild,
} from "@angular/core";
import { FormsModule } from "@angular/forms";
import { formatDate, NgTemplateOutlet } from "@angular/common";
import { MatButtonModule } from "@angular/material/button";
import { MatInputModule } from "@angular/material/input";
import { MatSelectModule } from "@angular/material/select";
import { MatCheckboxModule } from "@angular/material/checkbox";
import { MatSortModule, Sort } from "@angular/material/sort";
import {
  MatDialog,
  MatDialogModule,
  MatDialogRef,
} from "@angular/material/dialog";
import {
  Api,
  Household,
  Workspace,
  Category,
  Group,
  Item,
  Asset,
  Person,
  Share,
  SnapshotActivity,
  message,
} from "../core/api";
type SetupKind =
  "household" | "person" | "category" | "group" | "item" | "asset";
type SetupRow = Partial<Household & Person & Category & Group & Item & Asset>;
interface Field {
  key: string;
  label: string;
  type?: string;
  optional?: boolean;
  options?: { id: string; name: string }[];
}
@Component({
  imports: [
    FormsModule,
    NgTemplateOutlet,
    MatDialogModule,
    MatButtonModule,
    MatInputModule,
    MatSelectModule,
    MatCheckboxModule,
    MatSortModule,
  ],
  templateUrl: "setup.html",
})
export class Setup {
  w = inject(Workspace);
  api = inject(Api);
  private readonly dialogs = inject(MatDialog);
  private editor?: MatDialogRef<unknown, boolean>;
  private readonly editPanel =
    viewChild.required<TemplateRef<unknown>>("editPanel");
  editing = false;
  categories = signal<Category[]>([]);
  groups = signal<Group[]>([]);
  items = signal<Item[]>([]);
  assets = signal<Asset[]>([]);
  activity = signal<SnapshotActivity | null>(null);
  activityError = signal("");
  activityLoading = signal(false);
  historyFilter = "all";
  holderFilter = "";
  itemSort: Sort = { active: "name", direction: "asc" };
  assetSort: Sort = { active: "name", direction: "asc" };
  private readonly collator = new Intl.Collator("en", {
    numeric: true,
    sensitivity: "base",
  });
  formError = signal("");
  error = signal("");
  busy = signal(false);
  tab = "people";
  search = "";
  status = "all";
  title = "";
  fields: Field[] = [];
  value: Record<string, unknown> = {};
  kind: SetupKind = "household";
  path = "";
  method: "POST" | "PUT" = "POST";
  association = false;
  financed = false;
  private generation = 0;
  constructor() {
    inject(DestroyRef).onDestroy(() => this.editor?.close());
    effect(() => {
      const id = this.w.current()?.id;
      this.editor?.close();
      this.title = "";
      this.editing = false;
      this.historyFilter = "all";
      this.holderFilter = "";
      this.activity.set(null);
      this.generation++;
      if (id) void this.load();
    });
  }
  async load() {
    const g = ++this.generation;
    const base = this.w.path();
    void this.loadActivity(base, g);
    try {
      const [categories, items, assets] = await Promise.all([
        this.api.get<Category[]>(this.w.path("categories")),
        this.api.get<Item[]>(this.w.path("tracked-items")),
        this.api.get<Asset[]>(this.w.path("financed-assets")),
      ]);
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
      this.categories.set(categories);
      this.items.set(items);
      this.assets.set(assets);
      this.groups.set(groups);
      this.error.set("");
    } catch (e) {
      if (g === this.generation) this.error.set(message(e));
    }
  }
  private async loadActivity(base: string, generation: number) {
    this.activityLoading.set(true);
    this.activityError.set("");
    try {
      const result = await this.api.get<SnapshotActivity>(
        base + "/snapshots/activity",
      );
      if (generation === this.generation) this.activity.set(result);
    } catch (e) {
      if (generation === this.generation) {
        this.activity.set(null);
        this.historyFilter = "all";
        this.activityError.set(message(e));
      }
    } finally {
      if (generation === this.generation) this.activityLoading.set(false);
    }
  }
  activityDate(
    date: string | null | undefined,
    precision: number | null | undefined,
  ) {
    return date
      ? formatDate(
          date + "T00:00:00Z",
          precision === 2 ? "MMMM yyyy" : "d MMM yyyy",
          "en",
          "UTC",
        )
      : "No confirmed reading";
  }
  itemActivity(id: string) {
    return this.activity()?.items.find((i) => i.trackedItemId === id);
  }
  assetActivity(id: string) {
    return this.activity()?.financedAssets.find(
      (a) => a.financedAssetId === id,
    );
  }
  balanceActivity(id: string, balance: number) {
    return this.assetActivity(id)?.balances.find((b) => b.balance === balance);
  }
  setSort(sort: Sort, asset = false) {
    if (asset) this.assetSort = sort;
    else this.itemSort = sort;
  }
  private sortValue(
    row: Item | Asset,
    column: string,
    asset: boolean,
  ): string | number | null {
    switch (column) {
      case "name":
        return row.name;
      case "group":
        return this.groupName(row.groupId);
      case "holder":
        return this.name((row as Item).holderPersonId);
      case "currency":
        return row.currencyCode;
      case "household":
        return row.includeInHousehold ? "Included" : "Individual only";
      case "checklist":
        return row.isRetired ? "Retired" : "Active";
      case "presence":
        return this.activity() ? this.presence(row.id, asset) : null;
      case "confirmed":
        return this.itemActivity(row.id)?.lastConfirmedDate ?? null;
      case "originalLoan":
        return (row as Asset).originalLoanAmount;
      case "valuationConfirmed":
        return this.balanceActivity(row.id, 1)?.lastConfirmedDate ?? null;
      case "loanConfirmed":
        return this.balanceActivity(row.id, 2)?.lastConfirmedDate ?? null;
      case "withdrawalConfirmed":
        return (row as Asset).hasWithdrawalFacility
          ? (this.balanceActivity(row.id, 3)?.lastConfirmedDate ?? null)
          : null;
      default:
        return null;
    }
  }
  presence(id: string, asset = false) {
    if (!this.activity())
      return this.activityLoading() ? "Loading…" : "Unavailable";
    const row = asset ? this.assetActivity(id) : this.itemActivity(id);
    if (!row) return "Never recorded";
    if (!row.presentInLatestSnapshot) return "Not present";
    if (asset)
      return this.assetActivity(id)?.hasCarriedValuesInLatestSnapshot
        ? "Present · includes carried values"
        : "Present";
    const origin = this.itemActivity(id)?.latestValueOrigin;
    return origin === 3
      ? "Present · carried forward"
      : origin === 2
        ? "Present · confirmed"
        : "Present · unrecorded";
  }
  name(id: string, type = "person") {
    if (type === "person")
      return (
        this.w.current()?.people.find((p) => p.id === id)?.displayName ??
        "Unknown"
      );
    return (
      (type === "group" ? this.groups() : this.categories()).find(
        (p) => p.id === id,
      )?.name ?? "Unknown"
    );
  }
  groupName(id: string) {
    const g = this.groups().find((g) => g.id === id);
    return g ? this.name(g.categoryId, "category") + " / " + g.name : "Unknown";
  }
  filtered<T extends Item | Asset>(rows: T[], asset = false) {
    const filtered = rows.filter(
      (r) =>
        [
          r.name,
          ...("holderPersonId" in r
            ? [r.institutionName ?? "", r.fundName ?? ""]
            : []),
        ]
          .join(" ")
          .toLowerCase()
          .includes(this.search.toLowerCase()) &&
        (!this.holderFilter ||
          (asset
            ? "owners" in r &&
              r.owners.some((owner) => owner.personId === this.holderFilter)
            : "holderPersonId" in r &&
              r.holderPersonId === this.holderFilter)) &&
        (this.status === "all" ||
          r.isRetired === (this.status === "retired")) &&
        (this.historyFilter === "all" ||
          (asset ? this.assetActivity(r.id) : this.itemActivity(r.id))
            ?.presentInLatestSnapshot === true),
    );
    const sort = asset ? this.assetSort : this.itemSort;
    if (!sort.direction) return filtered;
    return filtered.sort((a, b) => {
      const left = this.sortValue(a, sort.active, asset);
      const right = this.sortValue(b, sort.active, asset);
      // Unknown dates/values always follow known ones, in either direction.
      if (left == null)
        return right == null ? this.collator.compare(a.name, b.name) : 1;
      if (right == null) return -1;
      const compared =
        typeof left === "number" && typeof right === "number"
          ? left - right
          : this.collator.compare(String(left), String(right));
      return (
        (sort.direction === "asc" ? compared : -compared) ||
        this.collator.compare(a.name, b.name) ||
        a.id.localeCompare(b.id)
      );
    });
  }
  begin(kind: SetupKind, row?: SetupRow) {
    if (this.busy()) return;
    const previousEditor = this.editor;
    this.editor = undefined;
    previousEditor?.close();
    this.editing = !!row;
    this.formError.set("");
    this.kind = kind;
    this.method = row ? "PUT" : "POST";
    this.value = {
      name: "",
      institutionName: "",
      fundName: "",
      displayName: "",
      currencyCode: "GBP",
      countryCode: "GB",
      includeInHousehold: false,
      participatesInHouseholdFinances: true,
      hasWithdrawalFacility: false,
      ...row,
    };
    this.association = kind === "item" || kind === "asset";
    this.financed = kind === "asset";
    this.fields = [];
    const text = (
      key: string,
      label: string,
      optional = false,
      type = "text",
    ): Field => ({ key, label, optional, type });
    const select = (
      key: string,
      label: string,
      options: { id: string; name: string }[],
    ): Field => ({ key, label, options });
    if (kind === "household") {
      this.path = "households" + (row ? "/" + row.id : "");
      this.fields = [
        text("name", "Household name"),
        ...(row ? [] : [text("firstPersonDisplayName", "First person’s name")]),
      ];
    }
    if (kind === "person") {
      this.path = this.w.path("people" + (row ? "/" + row.id : ""));
      this.fields = [
        text("displayName", "Name"),
        text(
          "participatesInHouseholdFinances",
          "Participates in household finances",
          false,
          "checkbox",
        ),
      ];
    }
    if (kind === "category") {
      this.path = this.w.path("categories" + (row ? "/" + row.id : ""));
      this.fields = [
        text("name", "Category name"),
        ...(row
          ? []
          : [
              select("financialType", "Type", [
                { id: "Asset", name: "Asset" },
                { id: "Debt", name: "Debt" },
              ]),
            ]),
      ];
    }
    if (kind === "group") {
      this.path = this.w.path(
        "categories/" +
          (row?.categoryId ?? this.categories()[0]?.id) +
          "/groups" +
          (row ? "/" + row.id : ""),
      );
      this.fields = [
        ...(row ? [] : [select("categoryId", "Category", this.categories())]),
        text("name", "Group name"),
      ];
      this.value["categoryId"] = row?.categoryId ?? this.categories()[0]?.id;
    }
    if (this.association) {
      const segment = kind === "item" ? "tracked-items" : "financed-assets";
      this.path = this.w.path(segment + (row ? "/" + row.id + "/details" : ""));
      this.fields = [
        text("name", "Name"),
        ...(this.financed
          ? []
          : [
              text("institutionName", "Institution / platform", true),
              text("fundName", "Fund name", true),
            ]),
        ...(!row
          ? [
              select(
                "groupId",
                "Group",
                this.groups()
                  .filter(
                    (g) =>
                      !this.financed ||
                      this.categories().find((c) => c.id === g.categoryId)
                        ?.financialType === "Asset",
                  )
                  .map((g) => ({ id: g.id, name: this.groupName(g.id) })),
              ),
              ...(this.financed
                ? []
                : [
                    select(
                      "holderPersonId",
                      "Actual account holder",
                      (this.w.current()?.people ?? []).map((p) => ({
                        id: p.id,
                        name: p.displayName,
                      })),
                    ),
                  ]),
              text("currencyCode", "Currency code"),
              text("countryCode", "Country code"),
              ...(this.financed
                ? [
                    text(
                      "originalLoanAmount",
                      "Original loan amount",
                      false,
                      "number",
                    ),
                    text("purchaseDate", "Purchase date", true, "date"),
                    text("purchasePrice", "Purchase price", true, "number"),
                    text(
                      "hasWithdrawalFacility",
                      "Withdrawal facility",
                      false,
                      "checkbox",
                    ),
                  ]
                : []),
            ]
          : []),
      ];
      this.fields.push(
        text(
          "includeInHousehold",
          "Include in household finances",
          false,
          "checkbox",
        ),
      );
      for (const p of this.w.current()?.people ?? []) {
        this.value["owner_" + p.id] =
          ((row?.owners ?? []).find((s: Share) => s.personId === p.id)
            ?.shareBasisPoints ?? 0) / 100;
        this.value["household_" + p.id] =
          ((row?.allocations ?? row?.householdAllocations ?? []).find(
            (s: Share) => s.personId === p.id,
          )?.shareBasisPoints ?? 0) / 100;
        this.value["loan_" + p.id] =
          row?.loanHolderPersonIds?.includes(p.id) ?? false;
      }
    }
    this.title = row
      ? "Edit " + (row.displayName ?? row.name)
      : "Create " +
        ({
          item: "tracked item",
          asset: "financed asset",
          person: "person",
          group: "group",
          category: "category",
          household: "household",
        }[kind] ?? kind);
    if (row) this.openEditor();
  }
  private openEditor() {
    const editor = this.dialogs.open<unknown, unknown, boolean>(
      this.editPanel(),
      {
        position: { right: "0", top: "0" },
        width: this.financed ? "640px" : "480px",
        maxWidth: "100vw",
        height: "100dvh",
        maxHeight: "100dvh",
        panelClass: "edit-sidebar",
        autoFocus: "first-tabbable",
      },
    );
    this.editor = editor;
    editor.afterClosed().subscribe(() => {
      if (this.editor === editor) {
        this.editor = undefined;
        this.title = "";
        this.editing = false;
        this.formError.set("");
      }
    });
  }
  cancel() {
    if (this.busy()) return;
    if (this.editor) this.editor.close();
    else this.title = "";
  }
  shares(prefix: string) {
    return (this.w.current()?.people ?? [])
      .map((p) => {
        const n = Number(this.value[prefix + p.id] ?? 0) * 100;
        if (
          !Number.isFinite(n) ||
          n < 0 ||
          n > 10000 ||
          Math.abs(n - Math.round(n)) > 0.000001
        )
          throw new Error(
            "Shares must be percentages from 0 to 100, with at most two decimal places.",
          );
        return { personId: p.id, shareBasisPoints: Math.round(n) };
      })
      .filter((s) => s.shareBasisPoints > 0);
  }
  async save() {
    if (this.busy()) return;
    const editor = this.editor;
    const householdId = this.w.current()?.id;
    this.busy.set(true);
    this.formError.set("");
    if (editor) editor.disableClose = true;
    try {
      for (const f of this.fields) {
        if (
          !f.optional &&
          (f.type ?? "text") === "text" &&
          !f.options &&
          !String(this.value[f.key] ?? "").trim()
        )
          throw new Error(f.label + " is required.");
      }
      let body: Record<string, unknown> = {};
      for (const f of this.fields)
        body[f.key] =
          this.value[f.key] === "" && f.optional ? null : this.value[f.key];
      if (this.kind === "group" && this.method === "POST") {
        this.path = this.w.path(
          "categories/" + this.value["categoryId"] + "/groups",
        );
        delete body["categoryId"];
      }
      if (this.association) {
        const allocations = this.value["includeInHousehold"]
          ? this.shares("household_")
          : [];
        if (this.financed) {
          body["owners"] = this.shares("owner_");
          body["loanHolderPersonIds"] = (this.w.current()?.people ?? [])
            .filter((p) => this.value["loan_" + p.id])
            .map((p) => p.id);
          body["householdAllocations"] = allocations;
        } else body["allocations"] = allocations;
      }
      const result = await this.api.send<{ id: string }>(
        this.method,
        this.path,
        body,
      );
      editor?.close(true);
      if (householdId !== this.w.current()?.id) return;
      if (!editor) this.title = "";
      if (this.kind === "household" || this.kind === "person")
        await this.w.load(this.kind === "household" ? result.id : undefined);
      else await this.load();
    } catch (e) {
      if (householdId === this.w.current()?.id) this.formError.set(message(e));
    } finally {
      this.busy.set(false);
      if (editor) editor.disableClose = false;
    }
  }
  async retire(row: Item | Asset, asset = false) {
    this.busy.set(true);
    try {
      await this.api.send(
        "PUT",
        this.w.path(
          (asset ? "financed-assets" : "tracked-items") +
            "/" +
            row.id +
            "/retirement",
        ),
        { isRetired: !row.isRetired },
      );
      await this.load();
    } catch (e) {
      this.error.set(message(e));
    } finally {
      this.busy.set(false);
    }
  }
  async remove(p: Person) {
    if (!confirm("Remove " + p.displayName + " from this household?")) return;
    this.busy.set(true);
    try {
      await this.api.send("DELETE", this.w.path("people/" + p.id), null);
      await this.w.load();
    } catch (e) {
      this.error.set(message(e));
    } finally {
      this.busy.set(false);
    }
  }
}
