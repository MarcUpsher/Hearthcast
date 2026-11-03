import { signal } from "@angular/core";
import { TestBed } from "@angular/core/testing";
import { describe, it, expect, beforeEach, vi } from "vitest";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { MatSelectHarness } from "@angular/material/select/testing";
import { MatButtonHarness } from "@angular/material/button/testing";
import { MatDialog, MatDialogState } from "@angular/material/dialog";
import { MatDialogHarness } from "@angular/material/dialog/testing";
import { Api, Household, Item, Asset, Workspace } from "../core/api";
import { Setup } from "./setup";

describe("Setup edit sidebars", () => {
  const home: Household = {
    id: "home",
    name: "Example",
    people: [
      { id: "a", displayName: "Alex", participatesInHouseholdFinances: true },
      { id: "b", displayName: "Morgan", participatesInHouseholdFinances: true },
    ],
  };
  let current: ReturnType<typeof signal<Household | null>>;
  let send: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    current = signal<Household | null>(home);
    send = vi.fn().mockResolvedValue({});
    TestBed.configureTestingModule({
      providers: [
        {
          provide: Workspace,
          useValue: {
            current,
            path: (p = "") => "households/home/" + p,
            load: vi.fn().mockResolvedValue(undefined),
          },
        },
        { provide: Api, useValue: { send } },
      ],
    });
  });
  async function page() {
    const fixture = TestBed.createComponent(Setup);
    fixture.componentInstance.load = vi.fn().mockResolvedValue(undefined);
    fixture.detectChanges();
    await fixture.whenStable();
    const root = TestbedHarnessEnvironment.documentRootLoader(fixture);
    async function setInput(name: string, value: string) {
      const element = document.querySelector<HTMLInputElement>(
        `input[name="${name}"], input[aria-label="${name}"]`,
      )!;
      element.value = value;
      element.dispatchEvent(new Event("input", { bubbles: true }));
      fixture.detectChanges();
      await fixture.whenStable();
    }
    async function open(
      kind: Parameters<Setup["begin"]>[0],
      row: Parameters<Setup["begin"]>[1],
    ) {
      fixture.componentInstance.begin(kind, row);
      fixture.detectChanges();
      await fixture.whenStable();
    }
    const save = async () =>
      (await root.getHarness(MatButtonHarness.with({ text: "Save" }))).click();
    return { fixture, root, setInput, open, save };
  }

  it.each([
    [
      "household",
      { id: "home", name: "Example" },
      "name",
      "households/home",
      { name: "Updated" },
    ],
    [
      "person",
      { id: "a", displayName: "Alex", participatesInHouseholdFinances: true },
      "displayName",
      "households/home/people/a",
      { displayName: "Updated", participatesInHouseholdFinances: true },
    ],
    [
      "category",
      { id: "cat", name: "Investments" },
      "name",
      "households/home/categories/cat",
      { name: "Updated" },
    ],
    [
      "group",
      { id: "group", categoryId: "cat", name: "Pensions" },
      "name",
      "households/home/categories/cat/groups/group",
      { name: "Updated" },
    ],
  ] as const)(
    "edits %s in a sidebar through its existing endpoint",
    async (kind, row, name, path, body) => {
      const p = await page();
      await p.open(kind, row);
      expect(
        await (await p.root.getHarness(MatDialogHarness)).getTitleText(),
      ).toMatch(/^Edit /);
      await p.setInput(name, "Updated");
      await p.save();
      expect(send).toHaveBeenCalledExactlyOnceWith("PUT", path, body);
      await vi.waitFor(async () =>
        expect(await p.root.getAllHarnesses(MatDialogHarness)).toHaveLength(0),
      );
    },
  );

  it("saves tracked-item details and changed household shares in one request", async () => {
    const p = await page();
    const row = {
      id: "item",
      name: "Original",
      institutionName: "Platform",
      fundName: "Fund",
      includeInHousehold: true,
      allocations: [
        { personId: "a", shareBasisPoints: 5000 },
        { personId: "b", shareBasisPoints: 5000 },
      ],
    } as Item;
    await p.open("item", row);
    await p.setInput("name", "Updated");
    await p.setInput("fundName", "");
    await p.setInput("Alex household percentage", "70");
    await p.setInput("Morgan household percentage", "30");
    await p.save();
    expect(send).toHaveBeenCalledExactlyOnceWith(
      "PUT",
      "households/home/tracked-items/item/details",
      {
        name: "Updated",
        institutionName: "Platform",
        fundName: null,
        includeInHousehold: true,
        allocations: [
          { personId: "a", shareBasisPoints: 7000 },
          { personId: "b", shareBasisPoints: 3000 },
        ],
      },
    );
    expect(row.name).toBe("Original");
    expect(row.allocations[0].shareBasisPoints).toBe(5000);
  });

  it("saves financed-asset name, owners, borrowers and household shares together", async () => {
    const p = await page();
    const row = {
      id: "asset",
      name: "Home",
      includeInHousehold: true,
      owners: [
        { personId: "a", shareBasisPoints: 5000 },
        { personId: "b", shareBasisPoints: 5000 },
      ],
      loanHolderPersonIds: ["b"],
      householdAllocations: [{ personId: "a", shareBasisPoints: 10000 }],
    } as Asset;
    await p.open("asset", row);
    await p.setInput("name", "Updated");
    await p.setInput("Alex ownership percentage", "60");
    await p.setInput("Morgan ownership percentage", "40");
    await p.save();
    expect(send).toHaveBeenCalledExactlyOnceWith(
      "PUT",
      "households/home/financed-assets/asset/details",
      {
        name: "Updated",
        includeInHousehold: true,
        owners: [
          { personId: "a", shareBasisPoints: 6000 },
          { personId: "b", shareBasisPoints: 4000 },
        ],
        loanHolderPersonIds: ["b"],
        householdAllocations: [{ personId: "a", shareBasisPoints: 10000 }],
      },
    );
    expect(row.owners[0].shareBasisPoints).toBe(5000);
  });

  it("keeps failed edits open, displays the error in the sidebar and supports retry", async () => {
    const p = await page();
    await p.open("category", { id: "cat", name: "Original" });
    await p.setInput("name", "Updated");
    send.mockRejectedValueOnce(new Error("Save failed."));
    await p.save();
    expect(await p.root.getAllHarnesses(MatDialogHarness)).toHaveLength(1);
    expect(
      document.querySelector('[role="dialog"] [role="alert"]')?.textContent,
    ).toContain("Save failed.");
    expect(
      document.querySelector<HTMLInputElement>('[name="name"]')?.value,
    ).toBe("Updated");
    expect(TestBed.inject(MatDialog).openDialogs[0].disableClose).toBe(false);
    await p.save();
    expect(send).toHaveBeenCalledTimes(2);
  });

  it("disables required blank fields and prevents duplicate submissions while saving", async () => {
    const p = await page();
    await p.open("category", { id: "cat", name: "Original" });
    await p.setInput("name", "");
    expect(
      await (
        await p.root.getHarness(MatButtonHarness.with({ text: "Save" }))
      ).isDisabled(),
    ).toBe(true);
    await p.setInput("name", "Updated");
    let resolve!: (value: unknown) => void;
    send.mockReturnValue(new Promise((r) => (resolve = r)));
    const pending = p.fixture.componentInstance.save();
    await p.fixture.componentInstance.save();
    expect(send).toHaveBeenCalledTimes(1);
    const dialog = TestBed.inject(MatDialog).openDialogs[0];
    expect(dialog.disableClose).toBe(true);
    expect(dialog.getState()).toBe(MatDialogState.OPEN);
    resolve({});
    await pending;
  });

  it("closes and discards an edit when the household changes", async () => {
    const p = await page();
    await p.open("category", { id: "cat", name: "Original" });
    await p.setInput("name", "Unsaved");
    current.set({ id: "other", name: "Other", people: [] });
    p.fixture.detectChanges();
    await p.fixture.whenStable();
    await vi.waitFor(async () =>
      expect(await p.root.getAllHarnesses(MatDialogHarness)).toHaveLength(0),
    );
    expect(send).not.toHaveBeenCalled();
  });

  it("retains required-field validation for inline creation", async () => {
    const p = await page();
    p.fixture.componentInstance.begin("person");
    p.fixture.detectChanges();
    await p.fixture.whenStable();
    const save = await TestbedHarnessEnvironment.loader(p.fixture).getHarness(
      MatButtonHarness.with({ text: "Save" }),
    );
    expect(await save.isDisabled()).toBe(true);
    await p.setInput("displayName", "New person");
    expect(await save.isDisabled()).toBe(false);
    expect(await p.root.getAllHarnesses(MatDialogHarness)).toHaveLength(0);
  });
  it("preserves a refresh error after the saved sidebar finishes closing", async () => {
    const p = await page();
    await p.open("category", { id: "cat", name: "Original" });
    p.fixture.componentInstance.load = vi.fn(async () => {
      p.fixture.componentInstance.error.set("Refresh failed");
    });
    await p.save();
    await vi.waitFor(async () =>
      expect(await p.root.getAllHarnesses(MatDialogHarness)).toHaveLength(0),
    );
    expect(p.fixture.componentInstance.error()).toBe("Refresh failed");
    expect(p.fixture.componentInstance.formError()).toBe("");
  });
  it.each(["item", "asset"] as const)(
    "creates %s with country defaults and an independently selected currency",
    async (kind) => {
      const p = await page();
      const setup = p.fixture.componentInstance;
      setup.categories.set([
        { id: "cat", name: "Assets", financialType: "Asset" },
      ]);
      setup.groups.set([{ id: "group", categoryId: "cat", name: "Cash" }]);
      setup.begin(kind);
      Object.assign(setup.value, {
        name: "Holding",
        groupId: "group",
        holderPersonId: "a",
        originalLoanAmount: 1000,
        owner_a: 100,
      });
      p.fixture.detectChanges();
      await p.fixture.whenStable();
      const country = await p.root.getHarness(
        MatSelectHarness.with({ selector: '[aria-label="Country"]' }),
      );
      const currency = await p.root.getHarness(
        MatSelectHarness.with({ selector: '[aria-label="Currency"]' }),
      );
      expect(await country.getValueText()).toContain("United Kingdom");
      expect(await currency.getValueText()).toContain("GBP");
      await country.open();
      await country.clickOptions({ text: "South Africa (ZA)" });
      expect(await currency.getValueText()).toContain("ZAR");
      await currency.open();
      await currency.clickOptions({ text: /^USD / });
      expect(await country.getValueText()).toContain("South Africa");
      expect(setup.value["currencyCode"]).toBe("USD");
      // A change unrelated to country must preserve the denomination override.
      await p.setInput("name", "Updated holding");
      await p.save();
      expect(send).toHaveBeenCalledWith(
        "POST",
        "households/home/" +
          (kind === "item" ? "tracked-items" : "financed-assets"),
        expect.objectContaining({
          countryCode: "ZA",
          currencyCode: "USD",
          name: "Updated holding",
        }),
      );
    },
  );
  it("clears the currency when a country has no usual currency, requiring an explicit choice", async () => {
    const p = await page();
    const setup = p.fixture.componentInstance;
    setup.begin("item");
    p.fixture.detectChanges();
    await p.fixture.whenStable();
    const country = await p.root.getHarness(
      MatSelectHarness.with({ selector: '[aria-label="Country"]' }),
    );
    await country.open();
    await country.clickOptions({ text: "Antarctica (AQ)" });
    expect(setup.value["currencyCode"]).toBe("");
    const currency = await p.root.getHarness(
      MatSelectHarness.with({ selector: '[aria-label="Currency"]' }),
    );
    expect(await currency.isEmpty()).toBe(true);
  });
});
