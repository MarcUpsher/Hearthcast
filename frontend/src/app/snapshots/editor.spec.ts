import { TestBed } from "@angular/core/testing";
import { ActivatedRoute, Router, convertToParamMap } from "@angular/router";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { Api, Workspace } from "../core/api";
import { SnapshotEditor } from "./editor";

describe("Snapshot entry requests", () => {
  let editor: SnapshotEditor;
  let send: ReturnType<typeof vi.fn>;
  let get: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    send = vi.fn().mockResolvedValue({ id: "saved" });
    get = vi.fn();
    TestBed.configureTestingModule({
      providers: [
        { provide: Api, useValue: { send, get } },
        {
          provide: Workspace,
          useValue: {
            current: () => null,
            path: (p: string) => "households/example/" + p,
          },
        },
        {
          provide: Router,
          useValue: { navigate: vi.fn().mockResolvedValue(true) },
        },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { queryParamMap: convertToParamMap({}) } },
        },
      ],
    });
    editor = TestBed.runInInjectionContext(() => new SnapshotEditor());
    editor.ready.set(true);
  });
  it("keeps historical and current classification labels separate when adding an item", () => {
    editor.mode = "correct";
    const row = {
      id: "old",
      name: "Existing",
      currency: "GBP",
      mode: "keep" as const,
      amount: "20",
      previous: 20,
      confirmed: null,
      personKey: "holder",
      personName: "Alex",
      categoryId: "category",
      categoryName: "Old category",
      groupId: "group",
      groupName: "Old group",
    };
    editor.rows.set([
      row,
      { ...row, id: "renamed-group", groupName: "New group", added: true },
      {
        ...row,
        id: "renamed-category",
        categoryName: "New category",
        added: true,
      },
    ]);
    const categories = editor.groupedRows()[0].categories;
    expect(categories.map((c) => c.name)).toEqual([
      "New category",
      "Old category",
    ]);
    expect(categories[1].groups.map((g) => g.name)).toEqual([
      "New group",
      "Old group",
    ]);
    expect(new Set(categories.map((c) => c.key)).size).toBe(2);
    expect(new Set(categories[1].groups.map((g) => g.key)).size).toBe(2);
  });
  it("omits carried readings and sends unknown values as explicit nulls", async () => {
    editor.mode = "next";
    editor.sourceId = "previous";
    editor.date = "2026-10-25";
    editor.precision = 2;
    editor.rows.set([
      {
        id: "carry",
        name: "Carry",
        currency: "GBP",
        mode: "keep",
        amount: "12",
        previous: 12,
        confirmed: null,
      },
      {
        id: "unknown",
        name: "Unknown",
        currency: "GBP",
        mode: "clear",
        amount: "",
        previous: 4,
        confirmed: null,
      },
      {
        id: "zero",
        name: "Zero",
        currency: "GBP",
        mode: "record",
        amount: "0",
        previous: null,
        confirmed: null,
      },
    ]);
    await editor.save();
    expect(send).toHaveBeenCalledWith(
      "POST",
      "households/example/snapshots/next",
      expect.objectContaining({
        snapshotDate: "2026-10-01",
        sourceSnapshotId: "previous",
        values: [
          { trackedItemId: "unknown", amount: null },
          { trackedItemId: "zero", amount: 0 },
        ],
      }),
    );
  });
  it("uses the saved revision and sends only explicit corrections", async () => {
    editor.mode = "correct";
    editor.sourceId = "old";
    editor.source.set({ revision: "revision" } as never);
    editor.rows.set([
      {
        id: "loan",
        kind: 2,
        name: "Loan",
        currency: "GBP",
        mode: "record",
        amount: "125.50",
        previous: 140,
        confirmed: null,
      },
    ]);
    await editor.save();
    expect(send).toHaveBeenCalledWith(
      "PATCH",
      "households/example/snapshots/old/values",
      {
        expectedRevision: "revision",
        values: [],
        financedAssetValues: [
          { financedAssetId: "loan", balance: 2, amount: 125.5 },
        ],
      },
    );
  });
  it("keeps edits and shows a conflict returned by the API", async () => {
    send.mockRejectedValue(new Error("Reload the saved snapshot."));
    editor.rows.set([
      {
        id: "a",
        name: "Value",
        currency: "GBP",
        mode: "record",
        amount: "123.00",
        previous: null,
        confirmed: null,
      },
    ]);
    await editor.save();
    expect(editor.error()).toContain("Reload");
    expect(editor.rows()[0].amount).toBe("123.00");
  });
  it("rejects amounts that exceed supported entry precision before sending", async () => {
    editor.rows.set([
      {
        id: "a",
        name: "Value",
        currency: "GBP",
        mode: "record",
        amount: "123.456",
        previous: null,
        confirmed: null,
      },
    ]);
    await editor.save();
    expect(send).not.toHaveBeenCalled();
    expect(editor.error()).toContain("two decimal");
  });
  it("groups active rows by actual holder, category and item name, with joint assets listed once", async () => {
    TestBed.tick();
    const people = [
      { id: "a", displayName: "Alex", participatesInHouseholdFinances: true },
      { id: "b", displayName: "Morgan", participatesInHouseholdFinances: true },
    ];
    vi.spyOn(editor.w, "current").mockReturnValue({
      id: "example",
      name: "Example",
      people,
    });
    const items = [
      {
        id: "z",
        name: "Zulu savings",
        groupId: "s",
        holderPersonId: "a",
        currencyCode: "GBP",
        isRetired: false,
      },
      {
        id: "beta",
        name: "beta fund",
        groupId: "i",
        holderPersonId: "a",
        currencyCode: "GBP",
        isRetired: false,
        allocations: [{ personId: "b", shareBasisPoints: 10000 }],
      },
      {
        id: "alpha",
        name: "Alpha fund",
        institutionName: "Current platform",
        fundName: "Current fund",
        groupId: "i",
        holderPersonId: "a",
        currencyCode: "GBP",
        isRetired: false,
      },
      {
        id: "m",
        name: "Alpha fund",
        groupId: "i",
        holderPersonId: "b",
        currencyCode: "GBP",
        isRetired: false,
      },
      {
        id: "old",
        name: "Old",
        groupId: "i",
        holderPersonId: "a",
        currencyCode: "GBP",
        isRetired: true,
      },
      {
        id: "tfsa",
        name: "Zulu ETF",
        groupId: "t",
        holderPersonId: "a",
        currencyCode: "GBP",
        isRetired: false,
      },
    ];
    const asset = {
      id: "joint",
      name: "Home",
      groupId: "p",
      currencyCode: "GBP",
      isRetired: false,
      hasWithdrawalFacility: true,
      owners: [
        { personId: "b", shareBasisPoints: 5000 },
        { personId: "a", shareBasisPoints: 5000 },
      ],
    };
    const responses: Record<string, unknown> = {
      snapshots: [],
      "tracked-items": items,
      "financed-assets": [asset],
      categories: [
        { id: "invest", name: "Investments" },
        { id: "save", name: "Savings" },
        { id: "property", name: "Property" },
      ],
      "categories/invest/groups": [
        { id: "t", categoryId: "invest", name: "TFSA" },
        { id: "i", categoryId: "invest", name: "Pension" },
      ],
      "categories/save/groups": [{ id: "s", categoryId: "save", name: "Cash" }],
      "categories/property/groups": [
        { id: "p", categoryId: "property", name: "Homes" },
      ],
    };
    get.mockImplementation((path: string) =>
      Promise.resolve(responses[path.replace("households/example/", "")]),
    );
    await editor.load();
    expect(editor.error()).toBe("");
    const groups = editor.groupedRows();
    expect(groups.map((p) => p.name)).toEqual([
      "Alex",
      "Alex & Morgan (joint)",
      "Morgan",
    ]);
    expect(groups[0].categories.map((c) => c.name)).toEqual([
      "Investments",
      "Savings",
    ]);
    expect(groups[0].categories[0].groups.map((g) => g.name)).toEqual([
      "Pension",
      "TFSA",
    ]);
    expect(groups[0].categories[0].groups[0].rows.map((r) => r.name)).toEqual([
      "Alpha fund",
      "beta fund",
    ]);
    expect(groups[1].categories[0].groups[0].rows.map((r) => r.kind)).toEqual([
      1, 2, 3,
    ]);
    expect(editor.rows().filter((r) => r.id === "joint")).toHaveLength(3);
    expect(editor.rows().some((r) => r.id === "old")).toBe(false);
    const editable = groups[0].categories[0].groups[0].rows[0];
    expect(editable.institutionName).toBe("Current platform");
    editable.mode = "record";
    editable.amount = "42.00";
    await editor.save();
    expect(send).toHaveBeenCalledWith(
      "POST",
      "households/example/snapshots/next",
      expect.objectContaining({
        values: [{ trackedItemId: "alpha", amount: 42 }],
      }),
    );
  });
  it("uses historical holders, categories and owners when correcting a saved snapshot", async () => {
    TestBed.tick();
    editor.mode = "correct";
    editor.sourceId = "saved";
    const source = {
      id: "saved",
      revision: "r",
      snapshotDate: "2020-01-01",
      datePrecision: 2,
      people: [
        { id: "sa", personId: "a", displayName: "Alex then" },
        { id: "sb", personId: "b", displayName: "Morgan then" },
      ],
      itemSnapshots: [
        {
          trackedItemId: "historic",
          itemName: "Historic fund",
          institutionName: "Historical platform",
          fundName: "Historical fund",
          currencyCode: "GBP",
          holderSnapshotPersonId: "sb",
          categoryId: "oldcat",
          categoryName: "Old investments",
          groupId: "oldtfsa",
          groupName: "Historical TFSA",
          amount: 12,
          lastConfirmedDate: "2020-01-01",
        },
      ],
      financedAssetSnapshots: [
        {
          financedAssetId: "oldhome",
          assetName: "Former home",
          categoryId: "oldproperty",
          categoryName: "Old property",
          groupId: "oldhomes",
          groupName: "Historical homes",
          currencyCode: "GBP",
          hasWithdrawalFacility: false,
          owners: [{ snapshotPersonId: "sa", shareBasisPoints: 10000 }],
          currentValuation: 100,
          outstandingLoanBalance: 50,
        },
      ],
    };
    const responses: Record<string, unknown> = {
      snapshots: [
        {
          id: "saved",
          snapshotDate: "2020-01-01",
          datePrecision: 2,
          revision: "r",
        },
      ],
      "snapshots/saved": source,
      "tracked-items": [],
      "financed-assets": [],
      categories: [],
    };
    get.mockImplementation((path: string) =>
      Promise.resolve(responses[path.replace("households/example/", "")]),
    );
    await editor.load();
    expect(editor.error()).toBe("");
    expect(editor.groupedRows().map((p) => p.name)).toEqual([
      "Alex then",
      "Morgan then",
    ]);
    expect(editor.groupedRows()[1].categories[0].name).toBe("Old investments");
    expect(editor.groupedRows()[1].categories[0].groups[0].name).toBe(
      "Historical TFSA",
    );
    expect(
      editor.groupedRows()[1].categories[0].groups[0].rows[0].fundName,
    ).toBe("Historical fund");
    expect(editor.groupedRows()[0].categories[0].groups[0].rows).toHaveLength(
      2,
    );
    expect(get.mock.calls.some(([path]) => path.endsWith("/categories"))).toBe(
      true,
    );
  });
  it("invalidates stale rows after a failed source reload and refuses to save", async () => {
    editor.rows.set([
      {
        id: "old",
        name: "Old",
        currency: "GBP",
        mode: "keep",
        amount: "100",
        previous: 100,
        confirmed: null,
      },
    ]);
    editor.sourceId = "different";
    get.mockRejectedValue(new Error("Source unavailable"));
    await editor.load();
    expect(editor.ready()).toBe(false);
    expect(editor.rows()).toEqual([]);
    expect(editor.source()).toBeNull();
    await editor.save();
    expect(send).not.toHaveBeenCalled();
    expect(editor.error()).toBe("Source unavailable");
  });
  it("restores the capture date and precision when leaving historical correction", async () => {
    const source = {
      id: "old",
      snapshotDate: "2020-01-01",
      datePrecision: 2,
      people: [],
      itemSnapshots: [],
      financedAssetSnapshots: [],
    };
    get.mockImplementation((path: string) =>
      Promise.resolve(
        path.endsWith("snapshots/old")
          ? source
          : path.endsWith("/snapshots")
            ? [{ id: "old" }]
            : [],
      ),
    );
    editor.date = "2026-11-02";
    editor.precision = 1;
    editor.mode = "correct";
    await editor.load();
    expect(editor.date).toBe("2020-01-01");
    editor.mode = "next";
    await editor.load();
    expect(editor.date).toBe("2026-11-02");
    expect(editor.precision).toBe(1);
  });
  it("allows cancellation of navigation and source/mode changes with unsaved work", async () => {
    get.mockResolvedValue([]);
    editor.mode = "new";
    await editor.load();
    editor.date = "2000-01-01";
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    try {
      expect(editor.canLeave()).toBe(false);
      const modeSelect = { value: "next" };
      const sourceSelect = { value: "other" };
      editor.changeMode("next", modeSelect);
      editor.changeSource("other", sourceSelect);
      expect(modeSelect.value).toBe("new");
      expect(sourceSelect.value).toBe("");
      expect(editor.mode).toBe("new");
      expect(editor.sourceId).toBe("");
      const event = new Event("beforeunload", { cancelable: true });
      editor.beforeUnload(event as BeforeUnloadEvent);
      expect(event.defaultPrevented).toBe(true);
    } finally {
      confirm.mockRestore();
    }
  });
  it("offers only active missing items, supports cancelling additions and saves them explicitly", async () => {
    vi.spyOn(editor.w, "current").mockReturnValue({
      id: "example",
      name: "Example",
      people: [
        {
          id: "child",
          displayName: "Child",
          participatesInHouseholdFinances: false,
        },
      ],
    });
    editor.mode = "correct";
    editor.sourceId = "saved";
    editor.source.set({ revision: "revision" } as never);
    editor.items = [
      { id: "present", name: "Existing", isRetired: false },
      { id: "retired", name: "Retired", isRetired: true },
      {
        id: "missing",
        name: "Savings",
        isRetired: false,
        holderPersonId: "child",
        groupId: "cash",
        currencyCode: "GBP",
        includeInHousehold: false,
        allocations: [],
      },
    ].map((item) => ({
      holderPersonId: "child",
      groupId: "cash",
      currencyCode: "GBP",
      countryCode: "GB",
      includeInHousehold: false,
      allocations: [],
      ...item,
    }));
    editor.categories = [
      { id: "savings", name: "Savings", financialType: "Asset" },
    ];
    editor.groups = [{ id: "cash", categoryId: "savings", name: "Cash" }];
    editor.rows.set([
      {
        id: "present",
        name: "Existing",
        currency: "GBP",
        mode: "keep",
        amount: "20",
        previous: 20,
        confirmed: null,
      },
    ]);
    expect(editor.availableItems().map((i) => i.id)).toEqual(["missing"]);
    editor.itemToAdd = "retired";
    editor.addItem();
    expect(editor.rows()).toHaveLength(1);
    editor.itemToAdd = "missing";
    editor.addItem();
    expect(editor.availableItems()).toEqual([]);
    expect(editor.rows()[1].personName).toBe("Child");
    expect(editor.rows()[1].groupName).toBe("Cash");
    editor.cancelAddition("missing");
    expect(editor.availableItems()).toHaveLength(1);
    editor.itemToAdd = "missing";
    editor.addItem();
    editor.rows()[1].amount = "12028";
    await editor.save();
    expect(send).toHaveBeenCalledWith(
      "PATCH",
      "households/example/snapshots/saved/values",
      {
        expectedRevision: "revision",
        values: [],
        financedAssetValues: [],
        addedItems: [{ trackedItemId: "missing", amount: 12028 }],
      },
    );
    expect(editor.rows()[0].amount).toBe("20");
  });
});
