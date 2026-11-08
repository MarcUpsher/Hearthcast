import { Component, signal } from "@angular/core";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { provideRouter, Router } from "@angular/router";
import { RouterTestingHarness } from "@angular/router/testing";
import { describe, expect, it, vi } from "vitest";
import { Api, Workspace } from "../core/api";
import { Modelling } from "./modelling";
import { ModellingSnapshot, ProjectionRun, ProjectionPreset } from "./models";
import { routes } from "../app.routes";

@Component({ template: "History destination" })
class NavigationDestination {}

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
const payoffReport = (events: { id: string; month: number | null }[]) => {
  const result = report();
  for (const row of result.months!)
    row.financedAssets = events.map((event) => ({
      financedAssetId: event.id,
      currencyCode: "ZAR",
      payoffDate:
        event.month !== null && row.month >= event.month
          ? result.months![event.month].date
          : null,
    }));
  return result;
};
const pause = () => new Promise((resolve) => setTimeout(resolve, 340));
const deferred = <T>() => {
  let resolve!: (value: T) => void;
  const promise = new Promise<T>((r) => {
    resolve = r;
  });
  return { resolve, promise };
};
const preset = (id = "baseline", isDefault = false): ProjectionPreset => ({
  id,
  name: id,
  householdId: "home",
  isDefault,
  inputs: {
    assumptions: {
      currencyCode: "GBP",
      durationYears: 20,
      annualInflationPercent: 4,
      showInflationAdjustedValues: true,
      currencies: [
        {
          currencyCode: "GBP",
          monthlyInvestmentContribution: 500,
          annualInvestmentReturnPercent: 7,
          monthlySavingsContribution: 100,
          annualSavingsInterestPercent: 3,
        },
      ],
    },
    groups: [{ groupId: "group", bucket: "Investment" }],
    financedAssets: [
      {
        financedAssetId: "asset",
        monthlyLoanPaymentInAssetCurrency: 2000,
        extraMonthlyLoanPaymentInAssetCurrency: 200,
        annualLoanInterestPercent: 9,
        annualAssetAppreciationPercent: 2,
        investmentCurrencyAfterPayoff: "GBP",
      },
    ],
  },
});
async function setup(
  initialSnapshot = snapshot(),
  savedPresets: ProjectionPreset[] | Error = [],
  routed = false,
) {
  const current = signal({ id: "home", name: "Household" });
  const get = vi.fn(async (path: string): Promise<unknown> => {
    if (path.endsWith("/projection-presets")) {
      if (savedPresets instanceof Error) throw savedPresets;
      return structuredClone(savedPresets);
    }
    return path.endsWith("/snapshots")
      ? [
          { id: "latest", snapshotDate: "2026-01-01" },
          { id: "older", snapshotDate: "2020-01-01" },
        ]
      : path.endsWith("/latest")
        ? initialSnapshot
        : snapshot(path.split("/").at(-1));
  });
  const send = vi.fn(async (): Promise<unknown> => report());
  TestBed.configureTestingModule({
    providers: [
      provideRouter(
        routed
          ? [
              ...routes.filter((route) => route.path === "modelling"),
              { path: "history", component: NavigationDestination },
            ]
          : [],
      ),
      { provide: Api, useValue: { get, send } },
      { provide: Workspace, useValue: { current } },
    ],
  });
  let fixture: ComponentFixture<unknown>;
  let page: Modelling;
  if (routed) {
    const harness = await RouterTestingHarness.create();
    page = await harness.navigateByUrl("/modelling", Modelling);
    fixture = harness.fixture;
  } else {
    const componentFixture = TestBed.createComponent(Modelling);
    fixture = componentFixture;
    page = componentFixture.componentInstance;
  }
  fixture.detectChanges();
  await fixture.whenStable();
  await new Promise((resolve) => setTimeout(resolve, 0));
  fixture.detectChanges();
  await fixture.whenStable();
  return { fixture, page, get, send, current };
}

describe("Modelling", () => {
  it("keeps snapshots usable if presets fail to load and retries without replacing the draft", async () => {
    const { page, get } = await setup(
      snapshot(),
      new Error("Preset service unavailable"),
    );
    expect(page.snapshotId).toBe("latest");
    expect(page.snapshot()).not.toBeNull();
    expect(page.presetError()).toContain("unavailable");
    page.presetName = "Working draft";
    get.mockResolvedValueOnce([preset("later", true)]);
    await page.reloadPresets();
    expect(page.presetError()).toBe("");
    expect(page.presetsLoaded()).toBe(true);
    expect(page.presetName).toBe("Working draft");
    expect(page.presetId).toBe("");
  });
  it("does not replace saved defaults with an older preset-list response", async () => {
    const { page, get, send } = await setup(snapshot(), [
      preset("first", true),
    ]);
    const response = deferred<ProjectionPreset[]>();
    get.mockImplementationOnce(() => response.promise);
    const reload = page.reloadPresets();
    page.presetName = "New scenario";
    const saved = { ...preset("new", true), name: "New scenario" };
    send.mockResolvedValueOnce(saved);
    await page.savePreset(true);
    response.resolve([preset("first", true)]);
    await reload;
    expect(
      page
        .presets()
        .filter((p) => p.isDefault)
        .map((p) => p.id),
    ).toEqual(["new"]);
  });
  it("loads the default preset against the latest snapshot without mutating the saved copy", async () => {
    const saved = preset("usual", true);
    const { page } = await setup(snapshot(), [saved, preset("other")]);
    expect(page.presetId).toBe("usual");
    expect(page.snapshotId).toBe("latest");
    expect(page.assumptions.durationYears).toBe(20);
    expect(page.adjusted()).toBe(true);
    expect(page.investmentGroups()).toEqual(["group"]);
    expect(page.loans["asset"].extraMonthlyLoanPaymentInAssetCurrency).toBe(
      200,
    );
    expect(page.presetDirty()).toBe(false);
    page.assumptions.currencies[0].monthlyInvestmentContribution = 999;
    expect(page.presetDirty()).toBe(true);
    expect(
      page.presets()[0].inputs.assumptions.currencies[0]
        .monthlyInvestmentContribution,
    ).toBe(500);
    expect(
      saved.inputs.assumptions.currencies[0].monthlyInvestmentContribution,
    ).toBe(500);
  });
  it("only loads a preset automatically when it is marked default", async () => {
    const { page } = await setup(snapshot(), [preset()]);
    expect(page.presetId).toBe("");
    expect(page.investmentGroups()).toEqual([]);
    expect(page.presetDirty()).toBe(false);
  });
  it("retains the selected snapshot while loading another preset and can cancel discarding edits", async () => {
    const { page, send } = await setup(snapshot(), [
      preset("first", true),
      preset("second"),
    ]);
    await page.selectSnapshot("older");
    page.assumptions.durationYears = 15;
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    page.loadPreset("second");
    expect(page.presetId).toBe("first");
    expect(page.assumptions.durationYears).toBe(15);
    confirm.mockReturnValue(true);
    page.loadPreset("second");
    expect(page.snapshotId).toBe("older");
    expect(page.presetId).toBe("second");
    expect(page.assumptions.durationYears).toBe(20);
    expect(page.presetDirty()).toBe(false);
    expect(send).not.toHaveBeenCalled();
    confirm.mockRestore();
  });
  it("saves a new default explicitly and clears the previous default in the selector", async () => {
    const { page, send } = await setup(snapshot(), [preset("first", true)]);
    page.presetName = " New scenario ";
    page.presetDefault = true;
    const saved = { ...preset("new", true), name: "New scenario" };
    send.mockResolvedValueOnce(saved);
    await page.savePreset(true);
    expect(send).toHaveBeenCalledWith(
      "POST",
      "households/home/projection-presets",
      {
        name: "New scenario",
        isDefault: true,
        inputs: saved.inputs,
      },
    );
    expect(
      page
        .presets()
        .filter((p) => p.isDefault)
        .map((p) => p.id),
    ).toEqual(["new"]);
    expect(page.presetId).toBe("new");
    expect(page.presetName).toBe("New scenario");
    expect(page.presetDirty()).toBe(false);
    expect(page.snapshotId).toBe("latest");
  });
  it("updates a preset including default and inflation-display changes without re-running the graph", async () => {
    const { page, send } = await setup(snapshot(), [preset("first", true)]);
    page.presetDefault = false;
    page.adjusted.set(false);
    expect(send).not.toHaveBeenCalled();
    expect(page.presetDirty()).toBe(true);
    send.mockResolvedValueOnce(preset("first", false));
    await page.savePreset();
    const args = send.mock.calls[0] as unknown as [
      string,
      string,
      {
        isDefault: boolean;
        inputs: { assumptions: { showInflationAdjustedValues: boolean } };
      },
    ];
    expect(args[0]).toBe("PUT");
    expect(args[1]).toBe("households/home/projection-presets/first");
    expect(args[2].isDefault).toBe(false);
    expect(args[2].inputs.assumptions.showInflationAdjustedValues).toBe(false);
    expect(page.presets().some((p) => p.isDefault)).toBe(false);
  });
  it("preserves the draft and existing defaults when saving fails", async () => {
    const { page, send } = await setup(snapshot(), [preset("first", true)]);
    page.presetName = "Duplicate";
    page.presetDefault = false;
    send.mockRejectedValueOnce(new Error("Name already exists"));
    await page.savePreset(true);
    expect(page.presetError()).toContain("Name already exists");
    expect(page.presetId).toBe("first");
    expect(page.presetName).toBe("Duplicate");
    expect(page.presetDirty()).toBe(true);
    expect(page.presets()[0].isDefault).toBe(true);
    expect(page.savingPreset()).toBe(false);
  });
  it("deletes a preset without changing current assumptions or the snapshot", async () => {
    const { page, send } = await setup(snapshot(), [preset("first", true)]);
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    await page.deletePreset();
    expect(send).not.toHaveBeenCalled();
    confirm.mockReturnValue(true);
    send.mockResolvedValueOnce(null);
    await page.deletePreset();
    expect(send).toHaveBeenCalledWith(
      "DELETE",
      "households/home/projection-presets/first",
      null,
    );
    expect(page.presets()).toEqual([]);
    expect(page.presetId).toBe("");
    expect(page.presetDefault).toBe(false);
    expect(page.assumptions.durationYears).toBe(20);
    expect(page.investmentGroups()).toEqual(["group"]);
    expect(page.snapshotId).toBe("latest");
    expect(page.presetDirty()).toBe(true);
    confirm.mockRestore();
  });
  it.each(["second", ""])(
    "protects the retained deleted draft when switching to %s",
    async (target) => {
      const { fixture, page, send } = await setup(snapshot(), [
        preset("first", true),
        preset("second"),
      ]);
      const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
      try {
        send.mockResolvedValueOnce(null);
        await page.deletePreset();
        fixture.detectChanges();
        expect(fixture.nativeElement.textContent).toContain("Unsaved changes");
        page.assumptions.durationYears = 35;
        page.assumptions.currencies[0].monthlyInvestmentContribution = 999;
        confirm.mockReturnValue(false);
        page.loadPreset(target);
        expect(page.presetId).toBe("");
        expect(page.assumptions.durationYears).toBe(35);
        expect(
          page.assumptions.currencies[0].monthlyInvestmentContribution,
        ).toBe(999);
        expect(page.presetDirty()).toBe(true);
        confirm.mockReturnValue(true);
        page.loadPreset(target);
        expect(page.presetId).toBe(target);
        expect(page.presetDirty()).toBe(false);
      } finally {
        confirm.mockRestore();
      }
    },
  );
  it("clears the retained unsaved state after saving the deleted preset's draft again", async () => {
    const { page, send } = await setup(snapshot(), [preset("first", true)]);
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    try {
      send.mockResolvedValueOnce(null);
      await page.deletePreset();
      send.mockRejectedValueOnce(new Error("Save failed"));
      await page.savePreset();
      expect(page.presetDirty()).toBe(true);
      send.mockResolvedValueOnce(preset("restored"));
      await page.savePreset();
      expect(page.presetDirty()).toBe(false);
      confirm.mockClear();
      expect(page.canLeave()).toBe(true);
      expect(confirm).not.toHaveBeenCalled();
    } finally {
      confirm.mockRestore();
    }
  });
  it("guards actual route navigation and keeps the edited preset if navigation is cancelled", async () => {
    const { page } = await setup(snapshot(), [preset("first", true)], true);
    const router = TestBed.inject(Router);
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    try {
      page.assumptions.durationYears = 35;
      expect(await router.navigateByUrl("/history")).toBe(false);
      expect(router.url).toBe("/modelling");
      expect(page.assumptions.durationYears).toBe(35);
      expect(confirm).toHaveBeenCalledWith("Discard unsaved preset changes?");
      confirm.mockReturnValue(true);
      expect(await router.navigateByUrl("/history")).toBe(true);
      expect(router.url).toBe("/history");
    } finally {
      confirm.mockRestore();
    }
  });
  it("allows a clean preset to leave the route without confirmation", async () => {
    await setup(snapshot(), [preset("first", true)], true);
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    try {
      expect(await TestBed.inject(Router).navigateByUrl("/history")).toBe(true);
      expect(confirm).not.toHaveBeenCalled();
    } finally {
      confirm.mockRestore();
    }
  });
  it("only warns on unload for unsaved drafts or in-flight preset writes", async () => {
    const { page } = await setup(snapshot(), [preset("first", true)]);
    const unload = () => {
      const event = new Event("beforeunload", { cancelable: true });
      window.dispatchEvent(event);
      return event.defaultPrevented;
    };
    expect(unload()).toBe(false);
    page.presetDefault = false;
    expect(unload()).toBe(true);
    page.presetDefault = true;
    expect(unload()).toBe(false);
    page.savingPreset.set(true);
    expect(unload()).toBe(true);
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    try {
      expect(page.canLeave()).toBe(false);
      expect(confirm).not.toHaveBeenCalled();
    } finally {
      confirm.mockRestore();
    }
  });
  it("retains dirty assumptions while changing snapshots without a discard prompt", async () => {
    const { page } = await setup(snapshot(), [preset("first", true)]);
    page.assumptions.durationYears = 35;
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    try {
      await page.selectSnapshot("older");
      expect(confirm).not.toHaveBeenCalled();
      expect(page.snapshotId).toBe("older");
      expect(page.assumptions.durationYears).toBe(35);
      expect(page.presetDirty()).toBe(true);
    } finally {
      confirm.mockRestore();
    }
  });
  it("ignores a save response after changing households", async () => {
    const { fixture, page, send, current } = await setup();
    page.presetName = "Old household";
    const response = deferred<ProjectionPreset>();
    send.mockImplementationOnce(() => response.promise);
    const saving = page.savePreset();
    expect(page.savingPreset()).toBe(true);
    current.set({ id: "new-home", name: "Other" });
    fixture.detectChanges();
    await fixture.whenStable();
    response.resolve(preset("old-default", true));
    await saving;
    expect(page.presets()).toEqual([]);
    expect(page.presetId).toBe("");
    expect(page.savingPreset()).toBe(false);
  });
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
  it("groups loan payoffs by month and includes the final month without marking unpaid loans", async () => {
    const { page, fixture } = await setup();
    page.result.set(
      payoffReport([
        { id: "asset", month: 3 },
        { id: "second", month: 3 },
        { id: "last", month: 12 },
        { id: "unpaid", month: null },
      ]),
    );
    fixture.detectChanges();
    expect(page.payoffEvents()).toEqual([
      { index: 3, date: "2026-04-01", assetIds: ["asset", "second"] },
      { index: 12, date: "2027-01-01", assetIds: ["last"] },
    ]);
    expect(
      fixture.nativeElement.querySelectorAll(".payoff-marker"),
    ).toHaveLength(2);
    expect(
      fixture.nativeElement.querySelectorAll(".projection-line"),
    ).toHaveLength(4);
    expect(fixture.nativeElement.textContent).toContain(
      "Equity remains in the model after payoff",
    );
  });
  it("inspects payoff markers by hover, keyboard focus and activation", async () => {
    const s = snapshot();
    s.financedAssetSnapshots.push({
      ...s.financedAssetSnapshots[0],
      financedAssetId: "second",
      assetName: "Car",
    });
    const { page, fixture } = await setup(s);
    page.result.set(
      payoffReport([
        { id: "asset", month: 3 },
        { id: "second", month: 3 },
      ]),
    );
    fixture.detectChanges();
    const marker = fixture.nativeElement.querySelector(".payoff-marker");
    const svg = fixture.nativeElement.querySelector("svg");
    svg.getBoundingClientRect = () => ({ width: 1000, left: 0 });
    marker.dispatchEvent(
      new MouseEvent("mousemove", { bubbles: true, clientX: 920 }),
    );
    fixture.detectChanges();
    expect(page.hovered()).toBe(3); // The parent chart must not select a different month.
    expect(
      fixture.nativeElement.querySelector('[role="tooltip"]').textContent,
    ).toContain("Home, Car · Loan paid off · 2026-04-01");
    expect(marker.getAttribute("aria-label")).toContain("Home, Car");
    page.hovered.set(null);
    marker.dispatchEvent(new Event("focus"));
    expect(page.reading()?.date).toBe("2026-04-01");
    page.hovered.set(null);
    const enter = new KeyboardEvent("keydown", {
      key: "Enter",
      bubbles: true,
      cancelable: true,
    });
    marker.dispatchEvent(enter);
    expect(page.hovered()).toBe(3);
    expect(enter.defaultPrevented).toBe(true);
  });
  it("repositions payoff markers for inflation-adjusted equity without changing dates", async () => {
    const { page, fixture, send } = await setup();
    page.result.set(payoffReport([{ id: "asset", month: 6 }]));
    const nominal = page.chart()!.payoffs[0];
    page.adjusted.set(true);
    fixture.detectChanges();
    const adjusted = page.chart()!.payoffs[0];
    expect(adjusted.x).toBe(nominal.x);
    expect(adjusted.date).toBe(nominal.date);
    expect(adjusted.y).not.toBe(nominal.y);
    expect(send).not.toHaveBeenCalled();
    page.hovered.set(6);
    expect(page.reading()?.values.equity).toBe(250);
  });
  it("marks an already-paid loan at the start and removes markers when results no longer contain payoffs", async () => {
    const { page, fixture } = await setup();
    page.result.set(payoffReport([{ id: "asset", month: 0 }]));
    fixture.detectChanges();
    expect(page.chart()!.payoffs[0].x).toBe(88);
    const marker = fixture.nativeElement.querySelector(".payoff-marker");
    expect(marker.getAttribute("aria-label")).toContain(
      "Already paid off at start",
    );
    page.result.set(payoffReport([{ id: "asset", month: null }]));
    fixture.detectChanges();
    expect(
      fixture.nativeElement.querySelectorAll(".payoff-marker"),
    ).toHaveLength(0);
    page.result.set({ ...report(), canCalculate: false, months: null });
    expect(page.payoffEvents()).toEqual([]);
    expect(page.chart()).toBeNull();
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
