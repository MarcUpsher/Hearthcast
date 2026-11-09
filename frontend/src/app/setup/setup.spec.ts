import { TestBed } from "@angular/core/testing";
import { describe, it, expect, beforeEach, vi } from "vitest";
import { Api } from "../core/api";
import { Workspace } from "../core/workspace";
import { SnapshotActivity } from "../snapshots/models";
import { Item, Asset } from "./models";
import { Setup } from "./setup";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { MatSortHeaderHarness } from "@angular/material/sort/testing";
import { MatButtonHarness } from "@angular/material/button/testing";
import { MatDialogHarness } from "@angular/material/dialog/testing";
import { MatInputHarness } from "@angular/material/input/testing";
import { MatSelectHarness } from "@angular/material/select/testing";

describe("Setup snapshot activity", () => {
  let c: Setup;
  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        {
          provide: Workspace,
          useValue: {
            current: () => null,
            path: (p = "") => "households/home/" + p,
          },
        },
        { provide: Api, useValue: { send: vi.fn() } },
      ],
    });
    c = TestBed.runInInjectionContext(() => new Setup());
    expect(c.status).toBe("active");
    c.status = "all";
    const activity: SnapshotActivity = {
      latestSnapshot: {
        id: "latest",
        snapshotDate: "2026-09-01",
        datePrecision: 2,
        revision: "v",
      },
      items: [
        {
          trackedItemId: "carried",
          lastIncludedDate: "2026-09-01",
          lastConfirmedDate: "2025-12-01",
          lastConfirmedPrecision: 2,
          presentInLatestSnapshot: true,
          latestValueOrigin: 3,
        },
        {
          trackedItemId: "old",
          lastIncludedDate: "2020-06-01",
          lastConfirmedDate: "2020-06-01",
          lastConfirmedPrecision: 2,
          presentInLatestSnapshot: false,
          latestValueOrigin: null,
        },
        {
          trackedItemId: "unknown",
          lastIncludedDate: "2026-09-01",
          lastConfirmedDate: null,
          lastConfirmedPrecision: null,
          presentInLatestSnapshot: true,
          latestValueOrigin: 1,
        },
      ],
      financedAssets: [
        {
          financedAssetId: "property",
          lastIncludedDate: "2026-09-01",
          presentInLatestSnapshot: true,
          hasCarriedValuesInLatestSnapshot: true,
          balances: [],
        },
      ],
    };
    c.activity.set(activity);
  });
  it("includes carried and unrecorded items in the latest filter independently of retirement", () => {
    c.historyFilter = "latest";
    const rows = [
      { id: "carried", name: "Cash", isRetired: true },
      { id: "old", name: "Old", isRetired: false },
      { id: "unknown", name: "Unknown", isRetired: true },
      { id: "never", name: "Never", isRetired: false },
    ] as Item[];
    expect(c.filtered(rows).map((r) => r.id)).toEqual(["carried", "unknown"]);
    c.status = "active";
    expect(c.filtered(rows)).toEqual([]);
    c.status = "retired";
    c.search = "cash";
    expect(c.filtered(rows).map((r) => r.id)).toEqual(["carried"]);
  });
  it("distinguishes presence, confirmation, and never-recorded items", () => {
    expect(c.presence("carried")).toContain("carried forward");
    expect(c.itemActivity("carried")?.lastConfirmedDate).toBe("2025-12-01");
    expect(c.presence("unknown")).toContain("unrecorded");
    expect(c.presence("old")).toBe("Not present");
    expect(c.presence("never")).toBe("Never recorded");
    expect(c.activityDate(null, null)).toBe("No confirmed reading");
  });
  it("combines the actual-holder filter with search, checklist, latest presence and sorting", () => {
    const rows = [
      {
        id: "carried",
        name: "Zulu cash",
        holderPersonId: "a",
        isRetired: true,
        allocations: [{ personId: "b", shareBasisPoints: 10000 }],
      },
      {
        id: "unknown",
        name: "Alpha cash",
        holderPersonId: "a",
        isRetired: true,
      },
      { id: "other", name: "Other cash", holderPersonId: "b", isRetired: true },
      { id: "old", name: "Old cash", holderPersonId: "a", isRetired: false },
    ] as Item[];
    c.holderFilter = "a";
    c.search = "cash";
    c.status = "retired";
    c.historyFilter = "latest";
    expect(c.filtered(rows).map((r) => r.id)).toEqual(["carried", "unknown"]);
    c.setSort({ active: "name", direction: "desc" });
    expect(c.filtered(rows).map((r) => r.id)).toEqual(["carried", "unknown"]);
    c.holderFilter = "b";
    expect(c.filtered(rows)).toEqual([]);
    c.historyFilter = "all";
    expect(c.filtered(rows).map((r) => r.id)).toEqual(["other"]);
    c.holderFilter = "";
    expect(c.filtered(rows)).toHaveLength(3);
  });
  it("includes jointly owned assets for either owner, not borrowers or household allocation recipients", () => {
    const rows = [
      {
        id: "joint",
        name: "Home",
        isRetired: false,
        owners: [
          { personId: "a", shareBasisPoints: 5000 },
          { personId: "b", shareBasisPoints: 5000 },
        ],
      },
      {
        id: "sole",
        name: "Car",
        isRetired: false,
        owners: [{ personId: "b", shareBasisPoints: 10000 }],
        loanHolderPersonIds: ["a"],
        householdAllocations: [{ personId: "a", shareBasisPoints: 10000 }],
      },
    ] as Asset[];
    c.holderFilter = "a";
    expect(c.filtered(rows, true).map((r) => r.id)).toEqual(["joint"]);
    c.holderFilter = "b";
    expect(c.filtered(rows, true).map((r) => r.id)).toEqual(["sole", "joint"]);
    c.holderFilter = "";
    expect(c.filtered(rows, true)).toHaveLength(2);
  });
  it("filters the rendered item table when a person is selected, and restores all holders", async () => {
    c.load = vi.fn().mockResolvedValue(undefined);
    vi.spyOn(c.w, "current").mockReturnValue({
      id: "home",
      name: "Example",
      people: [
        { id: "a", displayName: "Alex", participatesInHouseholdFinances: true },
        {
          id: "b",
          displayName: "Morgan",
          participatesInHouseholdFinances: false,
        },
      ],
    });
    const fixture = TestBed.createComponent(Setup);
    fixture.componentInstance.load = vi.fn().mockResolvedValue(undefined);
    fixture.componentInstance.tab = "items";
    fixture.componentInstance.status = "all";
    fixture.componentInstance.items.set([
      { id: "first", name: "First", isRetired: false, holderPersonId: "a" },
      { id: "second", name: "Second", isRetired: true, holderPersonId: "b" },
    ] as Item[]);
    fixture.detectChanges();
    await fixture.whenStable();
    const select = await TestbedHarnessEnvironment.loader(fixture).getHarness(
      MatSelectHarness.with({ selector: '[aria-label="Holder filter"]' }),
    );
    await select.open();
    await select.clickOptions({ text: "Morgan" });
    const names = () =>
      Array.from(
        fixture.nativeElement.querySelectorAll(
          'table[aria-label="Tracked items"] tbody tr',
        ),
      ).map((r: any) => r.cells[0].textContent.trim());
    expect(names()).toEqual(["Second"]);
    await select.open();
    await select.clickOptions({ text: "All holders" });
    expect(names()).toEqual(["First", "Second"]);
  });
  it("filters financed assets using their own activity", () => {
    c.historyFilter = "latest";
    expect(
      c
        .filtered(
          [
            { id: "property", name: "Home", isRetired: true },
            { id: "sold", name: "Sold", isRetired: true },
          ] as Asset[],
          true,
        )
        .map((r) => r.id),
    ).toEqual(["property"]);
    expect(c.presence("property", true)).toContain("carried values");
  });
  it("formats monthly dates without timezone shifts and exact dates with a day", () => {
    vi.stubEnv("TZ", "Europe/London");
    try {
      expect(c.activityDate("2026-09-01", 2)).toBe("September 2026");
      expect(c.activityDate("2026-09-20", 1)).toBe("20 Sep 2026");
    } finally {
      vi.unstubAllEnvs();
    }
  });
  it("opens a single Edit sidebar and discards changes on Cancel", async () => {
    vi.spyOn(c.w, "current").mockReturnValue({
      id: "home",
      name: "Example",
      people: [],
    });
    const fixture = TestBed.createComponent(Setup);
    const setup = fixture.componentInstance;
    setup.load = vi.fn().mockResolvedValue(undefined);
    setup.tab = "items";
    setup.status = "all";
    const row = {
      id: "existing",
      name: "Original",
      institutionName: "Platform",
      fundName: "Fund",
      isRetired: true,
    } as Item;
    setup.items.set([row]);
    fixture.detectChanges();
    await fixture.whenStable();
    const actions = Array.from(
      fixture.nativeElement.querySelectorAll(
        'table[aria-label="Tracked items"] tbody button',
      ),
    ) as HTMLButtonElement[];
    expect(actions.map((b) => b.textContent!.trim())).toEqual([
      "Edit",
      "Activate",
    ]);
    actions[0].click();
    fixture.detectChanges();
    await fixture.whenStable();
    const root = TestbedHarnessEnvironment.documentRootLoader(fixture);
    const dialog = await root.getHarness(MatDialogHarness);
    expect(await dialog.getTitleText()).toBe("Edit Original");
    const input = await root.getHarness(
      MatInputHarness.with({ selector: 'input[name="name"]' }),
    );
    await input.setValue("Unsaved");
    const cancel = await root.getHarness(
      MatButtonHarness.with({ text: "Cancel" }),
    );
    await cancel.click();
    await vi.waitFor(async () => {
      expect(await root.getAllHarnesses(MatDialogHarness)).toHaveLength(0);
    });
    expect(c.api.send).not.toHaveBeenCalled();
    expect(row.name).toBe("Original");
  });
  it("searches optional institution and fund metadata alongside the item name", () => {
    const rows = [
      {
        id: "a",
        name: "Retirement",
        holderPersonId: "holder",
        institutionName: "Example platform",
        fundName: "Equity",
        isRetired: false,
      },
      { id: "b", name: "Cash", holderPersonId: "holder", isRetired: false },
    ] as Item[];
    c.search = "PLATFORM";
    expect(c.filtered(rows).map((r) => r.id)).toEqual(["a"]);
    c.search = "equity";
    expect(c.filtered(rows).map((r) => r.id)).toEqual(["a"]);
  });
  it("sorts actual confirmation dates chronologically, with unknown dates last in both directions", () => {
    const rows = [
      { id: "unknown", name: "Unknown", isRetired: true },
      { id: "old", name: "Old", isRetired: true },
      { id: "carried", name: "Cash", isRetired: true },
    ] as Item[];
    c.setSort({ active: "confirmed", direction: "desc" });
    expect(c.filtered(rows).map((r) => r.id)).toEqual([
      "carried",
      "old",
      "unknown",
    ]);
    c.setSort({ active: "confirmed", direction: "asc" });
    expect(c.filtered(rows).map((r) => r.id)).toEqual([
      "old",
      "carried",
      "unknown",
    ]);
    expect(rows.map((r) => r.id)).toEqual(["unknown", "old", "carried"]);
  });
  it("sorts holder and group display names instead of their IDs", () => {
    vi.spyOn(c.w, "current").mockReturnValue({
      id: "home",
      name: "Example",
      people: [
        { id: "z", displayName: "Alex", participatesInHouseholdFinances: true },
        {
          id: "a",
          displayName: "Morgan",
          participatesInHouseholdFinances: true,
        },
      ],
    });
    c.categories.set([
      { id: "cat", name: "Investments", financialType: "Asset" },
    ]);
    c.groups.set([
      { id: "z", categoryId: "cat", name: "Pension" },
      { id: "a", categoryId: "cat", name: "Savings" },
    ]);
    const rows = [
      {
        id: "first",
        name: "First",
        isRetired: false,
        groupId: "a",
        holderPersonId: "a",
      },
      {
        id: "second",
        name: "Second",
        isRetired: false,
        groupId: "z",
        holderPersonId: "z",
      },
    ] as Item[];
    for (const active of ["holder", "group"]) {
      c.setSort({ active, direction: "asc" });
      expect(c.filtered(rows).map((r) => r.id)).toEqual(["second", "first"]);
    }
  });
  it("sorts loan amounts numerically and keeps table sort state independent", () => {
    const rows = [
      { id: "ten", name: "Ten", isRetired: true, originalLoanAmount: 10 },
      { id: "two", name: "Two", isRetired: true, originalLoanAmount: 2 },
    ] as Asset[];
    c.setSort({ active: "originalLoan", direction: "asc" }, true);
    expect(c.filtered(rows, true).map((r) => r.id)).toEqual(["two", "ten"]);
    c.setSort({ active: "confirmed", direction: "desc" });
    expect(c.filtered(rows, true).map((r) => r.id)).toEqual(["two", "ten"]);
  });
  it("sorts each financed balance independently", () => {
    c.activity.update((a) => ({
      ...a!,
      financedAssets: [
        {
          financedAssetId: "a",
          lastIncludedDate: "2026-09-01",
          presentInLatestSnapshot: true,
          hasCarriedValuesInLatestSnapshot: false,
          balances: [
            {
              balance: 1,
              lastConfirmedDate: "2026-09-01",
              lastConfirmedPrecision: 2,
            },
            {
              balance: 2,
              lastConfirmedDate: "2025-01-01",
              lastConfirmedPrecision: 2,
            },
          ],
        },
        {
          financedAssetId: "b",
          lastIncludedDate: "2026-09-01",
          presentInLatestSnapshot: true,
          hasCarriedValuesInLatestSnapshot: false,
          balances: [
            {
              balance: 1,
              lastConfirmedDate: "2025-01-01",
              lastConfirmedPrecision: 2,
            },
            {
              balance: 2,
              lastConfirmedDate: "2026-09-01",
              lastConfirmedPrecision: 2,
            },
          ],
        },
      ],
    }));
    const rows = [
      { id: "a", name: "A", isRetired: true },
      { id: "b", name: "B", isRetired: true },
    ] as Asset[];
    c.setSort({ active: "valuationConfirmed", direction: "desc" }, true);
    expect(c.filtered(rows, true).map((r) => r.id)).toEqual(["a", "b"]);
    c.setSort({ active: "loanConfirmed", direction: "desc" }, true);
    expect(c.filtered(rows, true).map((r) => r.id)).toEqual(["b", "a"]);
  });
  it("toggles the rendered item-name header between ascending and descending", async () => {
    c.load = vi.fn().mockResolvedValue(undefined);
    vi.spyOn(c.w, "current").mockReturnValue({
      id: "home",
      name: "Example",
      people: [],
    });
    const fixture = TestBed.createComponent(Setup);
    fixture.componentInstance.load = vi.fn().mockResolvedValue(undefined);
    fixture.componentInstance.tab = "items";
    fixture.componentInstance.status = "all";
    fixture.componentInstance.items.set([
      { id: "b", name: "Beta", isRetired: true, allocations: [] },
      { id: "a", name: "Alpha", isRetired: true, allocations: [] },
    ] as unknown as Item[]);
    fixture.detectChanges();
    await fixture.whenStable();
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const header = await loader.getHarness(
      MatSortHeaderHarness.with({ label: "Item" }),
    );
    const names = () =>
      Array.from(
        fixture.nativeElement.querySelectorAll(
          'table[aria-label="Tracked items"] tbody tr',
        ),
      ).map((r: any) => r.cells[0].textContent.trim());
    expect(names()).toEqual(["Alpha", "Beta"]);
    await header.click();
    expect(names()).toEqual(["Alpha", "Beta"]);
    await header.click();
    expect(names()).toEqual(["Beta", "Alpha"]);
  });
  it("defaults to person, group, newest confirmation and active status, with missing dates last", () => {
    vi.spyOn(c.w, "current").mockReturnValue({
      id: "home",
      name: "Example",
      people: [
        { id: "a", displayName: "Alex", participatesInHouseholdFinances: true },
        {
          id: "b",
          displayName: "Morgan",
          participatesInHouseholdFinances: true,
        },
      ],
    });
    c.categories.set([
      { id: "cat", name: "Investments", financialType: "Asset" },
    ]);
    c.groups.set([
      { id: "p", categoryId: "cat", name: "Pension" },
      { id: "s", categoryId: "cat", name: "Savings" },
    ]);
    const row = (
      id: string,
      holderPersonId = "a",
      groupId = "p",
      isRetired = false,
    ) => ({ id, name: id, holderPersonId, groupId, isRetired }) as Item;
    const rows = [
      row("other", "b"),
      row("savings", "a", "s"),
      row("unknown"),
      row("old"),
      row("retired", "a", "p", true),
      row("active"),
    ];
    c.activity.update((a) => ({
      ...a!,
      items: [
        { trackedItemId: "old", lastConfirmedDate: "2020-01-01" },
        { trackedItemId: "retired", lastConfirmedDate: "2026-09-01" },
        { trackedItemId: "active", lastConfirmedDate: "2026-09-01" },
      ] as SnapshotActivity["items"],
    }));
    expect(c.filtered(rows).map((r) => r.id)).toEqual([
      "active",
      "retired",
      "old",
      "unknown",
      "savings",
      "other",
    ]);
    expect(rows[0].id).toBe("other");
    c.setSort({ active: "name", direction: "asc" });
    expect(c.filtered(rows).map((r) => r.id)).toEqual([
      "active",
      "old",
      "other",
      "retired",
      "savings",
      "unknown",
    ]);
  });
});
