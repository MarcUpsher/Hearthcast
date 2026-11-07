import { signal } from "@angular/core";
import { TestBed } from "@angular/core/testing";
import { provideRouter } from "@angular/router";
import { describe, expect, it, vi } from "vitest";
import { Api, Workspace } from "../core/api";
import { Modelling } from "./modelling";
import { ModellingSnapshot, ProjectionRun } from "./models";

const snapshot = (id = "latest"): ModellingSnapshot => ({
  id,
  revision: "r1",
  snapshotDate: id === "latest" ? "2026-01-01" : "2020-01-01",
  datePrecision: 1,
  people: [],
  itemSnapshots: [
    {
      trackedItemId: "item",
      itemName: "Fund",
      groupId: "group",
      groupName: "Pension",
      categoryId: "category",
      categoryName: "Investments",
      financialType: 1,
      currencyCode: "GBP",
      amount: 100,
      valueOrigin: 1,
      lastConfirmedDate: null,
      holderSnapshotPersonId: "person",
      includeInHousehold: true,
    },
  ],
  financedAssetSnapshots: [
    {
      financedAssetId: "asset",
      assetName: "Home",
      groupId: "property",
      groupName: "Homes",
      categoryId: "assets",
      categoryName: "Property",
      currencyCode: "ZAR",
      owners: [],
      currentValuation: 1000,
      outstandingLoanBalance: 500,
      availableToWithdraw: null,
      hasWithdrawalFacility: false,
      includeInHousehold: true,
    },
  ],
});
const report = (value = 100): ProjectionRun => ({
  snapshotId: "latest",
  snapshotRevision: "r1",
  currencyCode: "GBP",
  canCalculate: true,
  months: Array.from({ length: 13 }, (_, i) => ({
    month: i,
    date: `${2026 + Math.floor(i / 12)}-${String((i % 12) + 1).padStart(2, "0")}-01`,
    nominal: {
      investments: value + i,
      savings: 10,
      financedAssetValue: 1000,
      loanBalance: 500 - i,
      equity: 500 + i,
      netPosition: value + 510 + i * 2,
    },
    inflationAdjusted: {
      investments: value / 2 + i,
      savings: 5,
      financedAssetValue: 500,
      loanBalance: 250,
      equity: 250,
      netPosition: value / 2 + 255 + i,
    },
    redirectedToInvestments: 0,
    financedAssets: [],
  })),
  issues: [],
  warnings: [],
  coverage: [],
});
const pause = () => new Promise((resolve) => setTimeout(resolve, 340));
const deferred = <T>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => {
    resolve = r;
  });
  return { resolve, promise };
};
async function setup(initialSnapshot = snapshot()) {
  const current = signal({ id: "home", name: "Household" });
  const get = vi.fn(async (path: string): Promise<unknown> =>
    path.endsWith("/snapshots")
      ? [
          { id: "latest", snapshotDate: "2026-01-01" },
          { id: "older", snapshotDate: "2020-01-01" },
        ]
      : path.endsWith("/latest")
        ? initialSnapshot
        : snapshot(path.split("/").at(-1)),
  );
  const send = vi.fn(async (): Promise<ProjectionRun> => report());
  TestBed.configureTestingModule({
    providers: [
      provideRouter([]),
      { provide: Api, useValue: { get, send } },
      { provide: Workspace, useValue: { current } },
    ],
  });
  const fixture = TestBed.createComponent(Modelling);
  fixture.detectChanges();
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  fixture.detectChanges();
  await fixture.whenStable();
  return { fixture, page: fixture.componentInstance, get, send, current };
}

describe("Modelling", () => {
  it("defaults to the latest snapshot and asks for explicit group selections", async () => {
    const { fixture, page, send } = await setup();
    expect(page.snapshotId).toBe("latest");
    expect(page.assumptions.durationYears).toBe(10);
    expect(page.investmentGroups()).toEqual([]);
    expect(send).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain(
      "Choose your starting balances",
    );
  });
  it("debounces edits and sends native loan assumptions and explicit buckets", async () => {
    const { page, send } = await setup();
    page.setGroups("Investment", ["group"]);
    page.assumptions.currencies[0].monthlyInvestmentContribution = 25;
    page.setAssets(["asset"]);
    page.loans["asset"].monthlyLoanPaymentInAssetCurrency = 2000;
    page.changed();
    await pause();
    expect(send).toHaveBeenCalledTimes(1);
    const args = send.mock.calls[0] as unknown as [
      string,
      string,
      {
        inputs: {
          groups: unknown[];
          financedAssets: { monthlyLoanPaymentInAssetCurrency: number }[];
        };
      },
    ];
    expect(args[1]).toBe("households/home/snapshots/latest/projection");
    expect(args[2].inputs.groups).toEqual([
      { groupId: "group", bucket: "Investment" },
    ]);
    expect(
      args[2].inputs.financedAssets[0].monthlyLoanPaymentInAssetCurrency,
    ).toBe(2000);
    expect(page.result()?.canCalculate).toBe(true);
  });
  it("ignores an old response when input becomes invalid", async () => {
    const { page, send } = await setup();
    const response = deferred<ProjectionRun>();
    send.mockImplementation(() => response.promise);
    page.setGroups("Investment", ["group"]);
    await pause();
    page.assumptions.durationYears = null;
    page.changed();
    response.resolve(report());
    await Promise.resolve();
    expect(page.validation()).toContain("whole duration");
    expect(page.result()).toBeNull();
    expect(page.pending()).toBe(false);
  });
  it("keeps the newest result when requests complete out of order", async () => {
    const { page, send } = await setup();
    const first = deferred<ProjectionRun>(),
      second = deferred<ProjectionRun>();
    send
      .mockImplementationOnce(() => first.promise)
      .mockImplementationOnce(() => second.promise);
    page.setGroups("Investment", ["group"]);
    await pause();
    page.assumptions.durationYears = 15;
    page.changed();
    await pause();
    second.resolve(report(200));
    await Promise.resolve();
    first.resolve(report(100));
    await Promise.resolve();
    expect(page.result()?.months?.[0].nominal.investments).toBe(200);
  });
  it("switches inflation display locally and exposes smooth solid graph readings", async () => {
    const { page, fixture, send } = await setup();
    page.result.set(report());
    fixture.detectChanges();
    const nominal = page.chart()!.lines[0].path;
    page.adjusted.set(true);
    fixture.detectChanges();
    expect(page.chart()!.lines[0].path).not.toBe(nominal);
    expect(send).not.toHaveBeenCalled();
    expect(
      fixture.nativeElement.querySelectorAll(".projection-line"),
    ).toHaveLength(4);
    expect(page.chart()!.lines.every((l) => l.path.includes("C"))).toBe(true);
    page.hovered.set(0);
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelector('[role="tooltip"]').textContent,
    ).toContain("£ 50.00");
  });
  it("retains missing selections and the chosen currency when moving into history", async () => {
    const { page, get } = await setup();
    page.setGroups("Investment", ["group"]);
    page.assumptions.currencyCode = "GBP";
    get.mockImplementation(async () => ({
      ...snapshot("older"),
      itemSnapshots: [],
      financedAssetSnapshots: [],
    }));
    await page.selectSnapshot("older");
    expect(page.investmentGroups()).toEqual(["group"]);
    expect(page.groups()[0].options[0].missing).toBe(true);
    expect(page.assumptions.currencyCode).toBe("GBP");
    expect(page.currencies()).toContain("GBP");
    expect(page.potCodes()).toEqual(["GBP"]); // Missing holdings are not inferred from cached metadata.
  });
  it("rejects double counting and blank loan amounts before calling the API", async () => {
    const { page, send } = await setup();
    page.investmentGroups.set(["group"]);
    page.savingsGroups.set(["group"]);
    page.changed();
    expect(page.validation()).toContain("one bucket");
    page.savingsGroups.set([]);
    page.setAssets(["asset"]);
    page.loans["asset"].monthlyLoanPaymentInAssetCurrency = null;
    page.changed();
    await pause();
    expect(page.validation()).toContain("loan");
    expect(send).not.toHaveBeenCalled();
  });
  it("clears household state and ignores outstanding responses after a switch", async () => {
    const { page, fixture, current, send } = await setup();
    const response = deferred<ProjectionRun>();
    send.mockImplementation(() => response.promise);
    page.setGroups("Investment", ["group"]);
    await pause();
    current.set({ id: "other", name: "Other" });
    fixture.detectChanges();
    await fixture.whenStable();
    response.resolve(report());
    await Promise.resolve();
    expect(page.investmentGroups()).toEqual([]);
    expect(page.selectedAssets()).toEqual([]);
    expect(page.result()).toBeNull();
  });
  it("shows blocking diagnostics without plotting partial results", async () => {
    const { page, fixture, send } = await setup();
    send.mockResolvedValue({
      ...report(),
      canCalculate: false,
      months: null,
      issues: [
        {
          kind: "MissingExchangeRate",
          severity: "Error",
          sourceId: null,
          message: "No saved conversion is available.",
        },
      ],
    });
    page.setGroups("Investment", ["group"]);
    await pause();
    fixture.detectChanges();
    expect(page.chart()).toBeNull();
    expect(fixture.nativeElement.textContent).toContain("No saved conversion");
  });
  it("ignores a slow snapshot load after another snapshot is selected", async () => {
    const { page, get } = await setup();
    const older = deferred<ModellingSnapshot>();
    get.mockImplementation((path: string) =>
      path.endsWith("older")
        ? older.promise
        : Promise.resolve(snapshot("latest")),
    );
    const pending = page.selectSnapshot("older");
    await page.selectSnapshot("latest");
    older.resolve(snapshot("older"));
    await pending;
    expect(page.snapshot()?.id).toBe("latest");
    expect(page.loading()).toBe(false);
  });
  it("clears results and offers retry after a failed projection", async () => {
    const { page, send } = await setup();
    page.result.set(report());
    send.mockRejectedValue(new Error("Offline"));
    page.setGroups("Investment", ["group"]);
    await pause();
    expect(page.error()).toBe("Offline");
    expect(page.result()).toBeNull();
    expect(page.pending()).toBe(false);
  });
  it("lets a fresh empty snapshot model contributions in an explicitly chosen currency", async () => {
    const { page, fixture, send } = await setup({
      ...snapshot(),
      itemSnapshots: [],
      financedAssetSnapshots: [],
      exchangeRates: [],
    });
    expect(page.assumptions.currencyCode).toBe("");
    expect(page.currencies()).toEqual([]);
    expect(send).not.toHaveBeenCalled();
    const fields = [
      ...fixture.nativeElement.querySelectorAll("mat-form-field"),
    ] as HTMLElement[];
    const currencyField = fields.find(
      (f) => f.querySelector("mat-label")?.textContent === "Viewing currency",
    )!;
    (
      currencyField.querySelector(".mat-mdc-select-trigger") as HTMLElement
    ).click();
    fixture.detectChanges();
    await fixture.whenStable();
    const usd = [...document.querySelectorAll<HTMLElement>("mat-option")].find(
      (o) => o.textContent?.trim().startsWith("USD —"),
    )!;
    expect(usd).toBeTruthy();
    usd.click();
    fixture.detectChanges();
    await fixture.whenStable();
    expect(page.assumptions.currencyCode).toBe("USD");
    const contributionField = (
      [
        ...fixture.nativeElement.querySelectorAll("mat-form-field"),
      ] as HTMLElement[]
    ).find(
      (f) => f.querySelector("mat-label")?.textContent === "Invest monthly",
    )!;
    const input = contributionField.querySelector("input")!;
    input.value = "100";
    input.dispatchEvent(new Event("input", { bubbles: true }));
    fixture.detectChanges();
    await pause();
    expect(page.validation()).toBe("");
    expect(send).toHaveBeenCalledTimes(1);
    const request = send.mock.calls[0] as unknown as [
      string,
      string,
      {
        inputs: {
          assumptions: {
            currencyCode: string;
            currencies: { monthlyInvestmentContribution: number }[];
          };
          groups: unknown[];
          financedAssets: unknown[];
        };
      },
    ];
    expect(request[2].inputs.assumptions.currencyCode).toBe("USD");
    expect(
      request[2].inputs.assumptions.currencies[0].monthlyInvestmentContribution,
    ).toBe(100);
    expect(request[2].inputs.groups).toEqual([]);
    expect(request[2].inputs.financedAssets).toEqual([]);
    expect(page.result()?.canCalculate).toBe(true);
  });
  it("does not assign units to entered contributions when leaving an empty snapshot", async () => {
    const { page, send } = await setup({
      ...snapshot(),
      itemSnapshots: [],
      financedAssetSnapshots: [],
      exchangeRates: [],
    });
    page.addCurrency("USD");
    page.assumptions.currencies[0].monthlyInvestmentContribution = 100;
    page.changed();
    await page.selectSnapshot("older");
    await pause();
    expect(page.currencies()).toContain("GBP");
    expect(page.assumptions.currencyCode).toBe("");
    expect(page.validation()).toContain("Select a projection currency");
    expect(send).not.toHaveBeenCalled();
  });
  it("resets a hover made on the old chart while a shorter projection is pending", async () => {
    const { page, send } = await setup();
    const response = deferred<ProjectionRun>();
    send.mockImplementation(() => response.promise);
    page.setGroups("Investment", ["group"]);
    const long = report();
    long.months = Array.from({ length: 121 }, (_, i) => ({
      ...report().months![0],
      month: i,
      date: new Date(Date.UTC(2026, i, 1)).toISOString().slice(0, 10),
    }));
    page.result.set(long);
    page.assumptions.durationYears = 1;
    page.changed();
    await pause();
    page.hover({
      clientX: 920,
      currentTarget: {
        getBoundingClientRect: () => ({ left: 0, width: 1000 }),
      },
    } as unknown as MouseEvent);
    expect(page.hovered()).toBe(120);
    response.resolve(report());
    await Promise.resolve();
    expect(page.result()?.months).toHaveLength(13);
    expect(page.hovered()).toBeNull();
    expect(page.chart()?.hoverX).toBeNull();
    expect(page.reading()).toBeNull();
  });
  it("keeps GBP and ZAR contributions and the loan destination unchanged when switching viewing currency", async () => {
    const { page, send } = await setup();
    page.addCurrency("ZAR");
    page.assumptions.currencies.find(
      (c) => c.currencyCode === "GBP",
    )!.monthlyInvestmentContribution = 1000;
    page.assumptions.currencies.find(
      (c) => c.currencyCode === "ZAR",
    )!.monthlyInvestmentContribution = 500;
    page.setAssets(["asset"]);
    page.loans["asset"].monthlyLoanPaymentInAssetCurrency = 20_000;
    page.loans["asset"].extraMonthlyLoanPaymentInAssetCurrency = 2000;
    page.assumptions.currencyCode = "ZAR";
    page.viewCurrencyChanged();
    await pause();
    const args = send.mock.calls[0] as unknown as [
      string,
      string,
      {
        inputs: {
          assumptions: {
            currencies: {
              currencyCode: string;
              monthlyInvestmentContribution: number;
            }[];
          };
          financedAssets: {
            investmentCurrencyAfterPayoff: string;
            extraMonthlyLoanPaymentInAssetCurrency: number;
          }[];
        };
      },
    ];
    expect(
      args[2].inputs.assumptions.currencies.find(
        (c) => c.currencyCode === "GBP",
      )!.monthlyInvestmentContribution,
    ).toBe(1000);
    expect(
      args[2].inputs.assumptions.currencies.find(
        (c) => c.currencyCode === "ZAR",
      )!.monthlyInvestmentContribution,
    ).toBe(500);
    expect(args[2].inputs.financedAssets[0].investmentCurrencyAfterPayoff).toBe(
      "GBP",
    );
    expect(
      args[2].inputs.financedAssets[0].extraMonthlyLoanPaymentInAssetCurrency,
    ).toBe(2000);
    page.removeCurrency("GBP");
    expect(page.potCodes()).toContain("GBP"); // A destination cannot be removed until its loan is redirected elsewhere.
  });
  it("adds native settings for a newly selected group's currency without changing existing contributions", async () => {
    const { page } = await setup();
    page.assumptions.currencies[0].monthlyInvestmentContribution = 1000;
    page.snapshot.set({
      ...snapshot(),
      itemSnapshots: snapshot().itemSnapshots.map((i) => ({
        ...i,
        currencyCode: "ZAR",
      })),
    });
    page.setGroups("Investment", ["group"]);
    expect(page.potCodes()).toEqual(["GBP", "ZAR"]);
    expect(page.assumptions.currencies[0].monthlyInvestmentContribution).toBe(
      1000,
    );
    expect(page.assumptions.currencies[1].monthlyInvestmentContribution).toBe(
      0,
    );
  });
  it("reconciles a retained group's historical currencies without replacing configured contributions or returns", async () => {
    const { page, get } = await setup();
    page.setGroups("Investment", ["group"]);
    const gbp = page.assumptions.currencies.find(
      (c) => c.currencyCode === "GBP",
    )!;
    gbp.monthlyInvestmentContribution = 1000;
    gbp.monthlySavingsContribution = 50;
    gbp.annualInvestmentReturnPercent = 7.25;
    gbp.annualSavingsInterestPercent = 1.5;
    const before = { ...gbp };
    const historical = snapshot("older");
    historical.itemSnapshots.push(
      {
        ...historical.itemSnapshots[0],
        trackedItemId: "old-zar",
        currencyCode: "ZAR",
      },
      {
        ...historical.itemSnapshots[0],
        trackedItemId: "excluded-usd",
        currencyCode: "USD",
        includeInHousehold: false,
      },
      {
        ...historical.itemSnapshots[0],
        trackedItemId: "other-eur",
        currencyCode: "EUR",
        groupId: "unselected",
      },
    );
    get.mockImplementation(async () => historical);
    await page.selectSnapshot("older");
    expect(page.investmentGroups()).toEqual(["group"]);
    expect(page.potCodes()).toEqual(["GBP", "ZAR"]);
    expect(page.assumptions.currencies[0]).toBe(gbp);
    expect(gbp).toEqual(before);
    expect(page.assumptions.currencies[1]).toEqual({
      currencyCode: "ZAR",
      monthlyInvestmentContribution: 0,
      annualInvestmentReturnPercent: 5,
      monthlySavingsContribution: 0,
      annualSavingsInterestPercent: 2,
    });
  });
  it("cancels the debounce timer when leaving the page", async () => {
    const { page, fixture, send } = await setup();
    page.setGroups("Investment", ["group"]);
    fixture.destroy();
    await pause();
    expect(send).not.toHaveBeenCalled();
  });
});
