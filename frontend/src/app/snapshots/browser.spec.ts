import { TestBed } from "@angular/core/testing";
import { provideRouter } from "@angular/router";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import {
  Api,
  Workspace,
  Report,
  ReportGroup,
  Snapshot,
  MergedView,
} from "../core/api";
import { SnapshotBrowser } from "./browser";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { MatSelectHarness } from "@angular/material/select/testing";

describe("Snapshot rendering", () => {
  beforeEach(() => vi.stubEnv("TZ", "Europe/London"));
  afterEach(() => vi.unstubAllEnvs());
  it("places the combined summary first, expands items, marks partial values and follows the selected person", async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: Api, useValue: {} },
        { provide: Workspace, useValue: { current: () => null } },
      ],
    });
    const fixture = TestBed.createComponent(SnapshotBrowser);
    fixture.detectChanges();
    await fixture.whenStable();
    const browser = fixture.componentInstance;
    const amount = (
      currencyCode: string,
      knownSubtotal: number | null,
      missing = 0,
    ) => ({
      currencyCode,
      knownSubtotal,
      missingValueCount: missing,
      missingRateCount: 0,
      carriedEntryCount: 0,
      isComplete: missing === 0,
    });
    const whole: MergedView = {
      amounts: [amount("GBP", 100), amount("ZAR", 2000)],
      categories: [
        {
          categoryId: "investments",
          categoryName: "Investments",
          financialType: 1,
          amounts: [amount("GBP", 100), amount("ZAR", 2000)],
          groups: [
            {
              groupId: "pensions",
              groupName: "Pensions",
              amounts: [amount("GBP", 100), amount("ZAR", 2000)],
              entries: [
                {
                  key: "one",
                  name: "Example pension",
                  description: "Held by Alex",
                  sourceCurrency: "GBP",
                  sourceAmount: 100,
                  status: "Confirmed",
                  institutionName: "Example institution",
                  fundName: null,
                  amounts: [amount("GBP", 100), amount("ZAR", 2000)],
                },
              ],
            },
          ],
        },
      ],
    };
    browser.saved.set({
      id: "s",
      people: [],
      itemSnapshots: [],
      financedAssetSnapshots: [],
    } as unknown as Snapshot);
    browser.mergedReport.set({
      snapshotId: "s",
      revision: "r",
      currencies: ["GBP", "ZAR"],
      household: whole,
      householdPeople: [
        {
          personId: "alex",
          view: { ...whole, amounts: [amount("GBP", 50), amount("ZAR", 1000)] },
        },
      ],
      individualPeople: [
        {
          personId: "alex",
          view: {
            amounts: [amount("GBP", null, 1), amount("ZAR", null, 1)],
            categories: [],
          },
        },
      ],
      allRecorded: whole,
    });
    fixture.detectChanges();
    let block: HTMLElement =
      fixture.nativeElement.querySelector(".merged-summary");
    expect(block.textContent).toContain("£ 100.00");
    expect(block.textContent).toContain("R 2 000.00");
    expect(block.querySelector(".merged-entry")).toBeNull();
    const button = [...block.querySelectorAll("button")].find((button) =>
      button.textContent?.includes("Pensions"),
    )!;
    button.click();
    fixture.detectChanges();
    expect(button.getAttribute("aria-expanded")).toBe("true");
    expect(block.querySelector(".merged-entry")?.textContent).toContain(
      "Example pension",
    );
    expect(block.textContent).toContain("Native: £ 100.00 GBP");
    expect(block.textContent).toContain("Example institution");
    browser.person = "alex";
    expect(browser.mergedView()?.amounts[0].knownSubtotal).toBe(50);
    fixture.changeDetectorRef.markForCheck();
    fixture.detectChanges();
    await fixture.whenStable();
    block = fixture.nativeElement.querySelector(".merged-summary");
    expect(block.querySelector(".merged-total")?.textContent).toContain(
      "£ 50.00",
    );
    browser.view = 1;
    fixture.changeDetectorRef.markForCheck();
    fixture.detectChanges();
    await fixture.whenStable();
    block = fixture.nativeElement.querySelector(".merged-summary");
    expect(block.textContent).toContain("Unknown");
    expect(block.textContent).toContain("Incomplete");
    expect(block.textContent).toContain("1 missing values");
    expect(
      block.compareDocumentPosition(
        fixture.nativeElement.querySelector(".cards"),
      ) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });
  it.each([
    "household-summary",
    "individual-holdings",
    "summary",
    "merged-summary",
  ])(
    "hides inconsistent results from %s and recovers on retry",
    async (mismatchedEndpoint) => {
      let mismatch = true;
      const saved = {
        id: "snapshot",
        revision: "current",
        snapshotDate: "2023-01-01",
        datePrecision: 2,
        people: [],
        itemSnapshots: [],
        financedAssetSnapshots: [],
      } as unknown as Snapshot;
      const emptyView = { amounts: [], categories: [] };
      const get = vi.fn(async (path: string) => {
        if (path.endsWith("/snapshots")) return [saved];
        if (path.endsWith("/snapshot")) return saved;
        const endpoint = path.split("/").at(-1);
        const revision =
          mismatch && endpoint === mismatchedEndpoint ? "old" : "current";
        return endpoint === "merged-summary"
          ? {
              snapshotId: saved.id,
              revision,
              currencies: [],
              household: emptyView,
              householdPeople: [],
              individualPeople: [],
              allRecorded: emptyView,
            }
          : { revision, currencies: [], people: [] };
      });
      TestBed.configureTestingModule({
        providers: [
          provideRouter([]),
          { provide: Api, useValue: { get } },
          {
            provide: Workspace,
            useValue: {
              current: () => null,
              path: (path: string) => "households/example/" + path,
            },
          },
        ],
      });
      const fixture = TestBed.createComponent(SnapshotBrowser);
      fixture.detectChanges();
      await fixture.whenStable();
      const browser = fixture.componentInstance;
      browser.selected = saved.id;
      await browser.open();
      fixture.detectChanges();
      expect(browser.saved()).toBeNull();
      expect(browser.mergedReport()).toBeNull();
      expect(browser.busy()).toBe(false);
      expect(
        fixture.nativeElement.querySelector('[role="alert"]').textContent,
      ).toContain("Snapshot changed while loading");
      expect(fixture.nativeElement.querySelector(".merged-summary")).toBeNull();
      expect(fixture.nativeElement.querySelector(".cards")).toBeNull();

      mismatch = false;
      const retry = [...fixture.nativeElement.querySelectorAll("button")].find(
        (button) =>
          (button as HTMLButtonElement).textContent?.trim() === "Retry",
      ) as HTMLButtonElement;
      retry.click();
      await fixture.whenStable();
      fixture.detectChanges();
      expect(browser.error()).toBe("");
      expect(browser.saved()?.revision).toBe("current");
      expect(browser.mergedReport()?.revision).toBe("current");
      expect(
        fixture.nativeElement.querySelector(".merged-summary"),
      ).not.toBeNull();
    },
  );
  it("shows pending rates and retries against the displayed snapshot revision", async () => {
    const send = vi.fn().mockResolvedValue({});
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: Api, useValue: { send } },
        {
          provide: Workspace,
          useValue: {
            current: () => null,
            path: (path: string) => "households/example/" + path,
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(SnapshotBrowser);
    fixture.detectChanges();
    await fixture.whenStable();
    const browser = fixture.componentInstance;
    browser.saved.set({
      id: "saved",
      revision: "revision",
      exchangeRateStatus: "Pending",
      people: [],
      itemSnapshots: [],
      financedAssetSnapshots: [],
    } as unknown as Snapshot);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(
      "Financial values are saved",
    );
    const open = vi.spyOn(browser, "open").mockResolvedValue();
    await browser.retryExchangeRates();
    expect(send).toHaveBeenCalledWith(
      "POST",
      "households/example/snapshots/saved/exchange-rates/retry",
      { expectedRevision: "revision" },
    );
    expect(open).toHaveBeenCalledOnce();
  });
  it("renders returned totals and names the selected individual", async () => {
    TestBed.configureTestingModule({
      imports: [SnapshotBrowser],
      providers: [
        provideRouter([]),
        { provide: Api, useValue: {} },
        { provide: Workspace, useValue: { current: () => null } },
      ],
    });
    const fixture = TestBed.createComponent(SnapshotBrowser);
    const c = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    const totals = {
      knownNetPosition: 123,
      ordinaryAssets: {
        knownSubtotal: 123,
        missingEntryCount: 0,
        carriedForwardEntryCount: 1,
      },
      ordinaryDebts: {
        knownSubtotal: 0,
        missingEntryCount: 0,
        carriedForwardEntryCount: 0,
      },
      financedAssetEquity: {
        knownSubtotal: 0,
        missingEntryCount: 0,
        carriedForwardEntryCount: 0,
      },
    };
    const currency = {
      currencyCode: "GBP",
      totals,
      holdingsTotals: totals,
      allRecordedTotals: totals,
      categories: [],
    };
    const report = {
      currencies: [currency],
      people: [
        {
          person: { personId: "a", displayName: "Alex" },
          currencies: [currency],
        },
      ],
    } as Report;
    c.reports.set([report, report, report]);
    c.saved.set({
      id: "snapshot",
      people: [],
      itemSnapshots: [
        {
          trackedItemId: "sample",
          itemName: "Sample holding",
          groupName: "Pension",
          categoryName: "Investments",
          currencyCode: "GBP",
          amount: 123,
          valueOrigin: 3,
          lastConfirmedDate: "2026-06-01",
          holderSnapshotPersonId: "holder",
          includeInHousehold: true,
        },
      ],
      financedAssetSnapshots: [],
    } as unknown as Snapshot);
    c.inventory.set([
      {
        id: "snapshot",
        snapshotDate: "2026-09-01",
        datePrecision: 2,
        revision: "v",
      },
    ]);
    c.selected = "snapshot";
    c.year = "2026";
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain("£ 123.00");
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain("September");
    expect(fixture.nativeElement.textContent.replace(/\s+/g, " ")).toContain(
      "Jun 2026",
    );
    expect(fixture.nativeElement.textContent).toContain("1 carried entries");
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const view = await loader.getHarness(
      MatSelectHarness.with({ selector: '[aria-label="Summary view"]' }),
    );
    await view.open();
    await view.clickOptions({ text: "Individual holdings" });
    expect(c.person).toBe("a");
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain("Alex");
  });
  it("groups saved holdings by historical holder and category, showing joint assets once and filtering both types", async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: Api, useValue: {} },
        { provide: Workspace, useValue: { current: () => null } },
      ],
    });
    const fixture = TestBed.createComponent(SnapshotBrowser);
    fixture.detectChanges();
    await fixture.whenStable();
    const c = fixture.componentInstance;
    c.saved.set({
      id: "snapshot",
      people: [
        { id: "a", personId: "first", displayName: "Alex historical" },
        { id: "b", personId: "second", displayName: "Morgan" },
      ],
      itemSnapshots: [
        {
          trackedItemId: "z",
          itemName: "Zulu",
          holderSnapshotPersonId: "a",
          categoryId: "investment",
          categoryName: "Investments",
          groupId: "pension",
          groupName: "Pension",
          currencyCode: "GBP",
          amount: 20,
        },
        {
          trackedItemId: "a",
          itemName: "Alpha",
          holderSnapshotPersonId: "a",
          categoryId: "investment",
          categoryName: "Investments",
          groupId: "pension",
          groupName: "Pension",
          currencyCode: "GBP",
          amount: 10,
        },
        {
          trackedItemId: "cash",
          itemName: "Cash",
          holderSnapshotPersonId: "b",
          categoryId: "savings",
          categoryName: "Savings",
          groupId: "cash",
          groupName: "Cash",
          currencyCode: "ZAR",
          amount: 30,
        },
      ],
      financedAssetSnapshots: [
        {
          financedAssetId: "home",
          assetName: "Home",
          categoryId: "property",
          categoryName: "Property",
          groupId: "homes",
          groupName: "Homes",
          currencyCode: "GBP",
          owners: [
            { snapshotPersonId: "a", shareBasisPoints: 5000 },
            { snapshotPersonId: "b", shareBasisPoints: 5000 },
          ],
          currentValuation: 300,
          outstandingLoanBalance: 100,
          hasWithdrawalFacility: false,
        },
      ],
    } as unknown as Snapshot);
    const people = c.groupedEntries();
    const alex = people.find((p) => p.name === "Alex historical")!;
    expect(alex.categories[0].name).toBe("Investments");
    expect(alex.categories[0].groups[0].items.map((i) => i.itemName)).toEqual([
      "Alpha",
      "Zulu",
    ]);
    const joint = people.find((p) => p.name.includes("(joint)"))!;
    expect(joint.name).toBe("Alex historical & Morgan (joint)");
    expect(joint.categories[0].groups[0].assets).toHaveLength(1);
    expect(
      people.flatMap((p) =>
        p.categories.flatMap((c) => c.groups.flatMap((g) => g.assets)),
      ),
    ).toHaveLength(1);
    fixture.detectChanges();
    await fixture.whenStable();
    expect(
      fixture.nativeElement.querySelector(
        'section[aria-label="Alex historical"] h3',
      ).textContent,
    ).toBe("Investments");
    c.search = "alpha";
    expect(c.groupedEntries()).toHaveLength(1);
    expect(c.groupedEntries()[0].categories[0].groups[0].items).toHaveLength(1);
    c.search = "home";
    expect(c.groupedEntries()[0].name).toContain("(joint)");
    c.currency = "ZAR";
    expect(c.groupedEntries()).toEqual([]);
    c.search = "";
    expect(c.groupedEntries().map((p) => p.name)).toEqual(["Morgan"]);
  });
  it("uses returned household contributions and owned equity, preserving unknown amounts and debt signs", async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: Api, useValue: {} },
        { provide: Workspace, useValue: { current: () => null } },
      ],
    });
    const fixture = TestBed.createComponent(SnapshotBrowser);
    fixture.detectChanges();
    await fixture.whenStable();
    const c = fixture.componentInstance;
    c.saved.set({
      people: [{ id: "holder", personId: "a", displayName: "Alex" }],
      itemSnapshots: [],
      financedAssetSnapshots: [],
    } as unknown as Snapshot);
    const item = {
      itemSnapshotId: "i",
      itemName: "Pension",
      holderSnapshotPersonId: "holder",
      balance: { amount: 100, valueOrigin: 3 },
    };
    const asset = {
      financedAssetSnapshotId: "a",
      assetName: "Home",
      currentEquity: 200,
      currentValuation: { valueOrigin: 2 },
      outstandingLoanBalance: { valueOrigin: 2 },
    };
    const group = {
      groupId: "group",
      groupName: "Pension/SIPP",
      items: [
        { item, allocationShareBasisPoints: 5000, contributingAmount: 50 },
      ],
      financedAssets: [
        { asset, allocationShareBasisPoints: 5000, attributedEquity: 100 },
      ],
    } as ReportGroup;
    expect(
      c.groupEntries(group).find((e) => e.name === "Pension")?.amount,
    ).toBe(50);
    expect(
      c.groupEntries(group).find((e) => e.name === "Pension")?.description,
    ).toContain("50% household share");
    expect(
      c.groupEntries(group).find((e) => e.name === "Pension")?.status,
    ).toBe("Carried forward");
    expect(c.groupEntries(group).find((e) => e.name === "Home")?.amount).toBe(
      100,
    );
    expect(
      c
        .groupEntries({
          ...group,
          items: [item],
          financedAssets: [
            { asset, ownershipShareBasisPoints: 2500, attributedEquity: 50 },
          ],
        })
        .find((e) => e.name === "Home")?.amount,
    ).toBe(50);
    expect(
      c.groupEntries({ ...group, items: [item], financedAssets: [] })[0].amount,
    ).toBe(100);
    expect(
      c.groupEntries({ ...group, items: [item], financedAssets: [] }, 2)[0]
        .amount,
    ).toBe(-100);
    expect(
      c.groupEntries({
        ...group,
        items: [
          { item, allocationShareBasisPoints: 5000, contributingAmount: null },
        ],
        financedAssets: [],
      })[0].amount,
    ).toBeNull();
  });
  it("starts group drill-downs collapsed and reveals only the selected person's report entries", async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: Api, useValue: {} },
        { provide: Workspace, useValue: { current: () => null } },
      ],
    });
    const fixture = TestBed.createComponent(SnapshotBrowser);
    fixture.detectChanges();
    await fixture.whenStable();
    const c = fixture.componentInstance;
    const subtotal = {
      knownSubtotal: 50,
      carriedForwardEntryCount: 0,
      missingEntryCount: 0,
    };
    const totals = {
      knownNetPosition: 50,
      ordinaryAssets: subtotal,
      ordinaryDebts: { ...subtotal, knownSubtotal: 0 },
      financedAssetEquity: { ...subtotal, knownSubtotal: 0 },
    };
    const currency = (name: string) => ({
      currencyCode: "GBP",
      totals,
      categories: [
        {
          categoryId: "investments",
          categoryName: "Investments",
          financialType: 1,
          totals,
          groups: [
            {
              groupId: "pension",
              groupName: "Pension/SIPP",
              totals,
              items: [
                {
                  item: {
                    itemSnapshotId: name,
                    itemName: name,
                    holderSnapshotPersonId: "holder",
                    balance: { amount: 100, valueOrigin: 2 },
                  },
                  allocationShareBasisPoints: 5000,
                  contributingAmount: 50,
                },
              ],
              financedAssets: [],
            },
          ],
        },
      ],
    });
    c.saved.set({
      id: "s",
      people: [{ id: "holder", personId: "a", displayName: "Alex" }],
      itemSnapshots: [],
      financedAssetSnapshots: [],
    } as unknown as Snapshot);
    c.reports.set([
      {
        currencies: [currency("Whole household")],
        people: [
          {
            person: { personId: "a", displayName: "Alex" },
            currencies: [currency("Alex pension")],
          },
          {
            person: { personId: "b", displayName: "Morgan" },
            currencies: [currency("Morgan pension")],
          },
        ],
      },
    ] as unknown as Report[]);
    c.person = "a";
    fixture.detectChanges();
    await fixture.whenStable();
    const details: HTMLDetailsElement = fixture.nativeElement.querySelector(
      "details.summary-group",
    );
    expect(details.open).toBe(false);
    details.querySelector("summary")!.click();
    expect(details.open).toBe(true);
    expect(details.textContent).toContain("Alex pension");
    expect(details.textContent).not.toContain("Morgan pension");
    expect(details.textContent).toContain("£ 50.00");
    c.person = "b";
    fixture.detectChanges();
    expect(details.textContent).toContain("Morgan pension");
    expect(details.textContent).not.toContain("Alex pension");
    // Adding current items to history can produce multiple labels for one ID.
    const renamed = currency("Historical item");
    const oldCategory = renamed.categories[0];
    oldCategory.groups.push({
      ...oldCategory.groups[0],
      groupName: "Renamed group",
    });
    renamed.categories.push({
      ...oldCategory,
      categoryName: "Renamed category",
      groups: [oldCategory.groups[0]],
    });
    const warnings = vi.spyOn(console, "warn");
    try {
      c.person = "";
      c.reports.set([
        { currencies: [renamed], people: [] },
      ] as unknown as Report[]);
      fixture.detectChanges();
      expect(
        fixture.nativeElement.querySelectorAll("details.summary-group"),
      ).toHaveLength(3);
      expect(fixture.nativeElement.textContent).toContain("Renamed category");
      expect(fixture.nativeElement.textContent).toContain("Renamed group");
      expect(warnings.mock.calls.flat().join(" ")).not.toContain("NG0955");
    } finally {
      warnings.mockRestore();
    }
  });
});
