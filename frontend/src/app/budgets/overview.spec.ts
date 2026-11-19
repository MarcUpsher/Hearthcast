import { signal } from "@angular/core";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { provideRouter } from "@angular/router";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { MatInputHarness } from "@angular/material/input/testing";
import { MatButtonHarness } from "@angular/material/button/testing";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Api } from "../core/api";
import { Workspace } from "../core/workspace";
import { Household } from "../setup/models";
import { localToday } from "../spending/amounts";
import { budgetItem } from "../../testing/budgets";
import { deferred, spendingEntry } from "../../testing/spending";
import { BudgetEntry, CurrentOutgoing, CurrentOutgoings } from "./models";
import { OutgoingOverview } from "./overview";
import { By } from "@angular/platform-browser";
import { MonthlyBudgetSummary } from "./monthly-summary";

const saved = (extra: Partial<BudgetEntry> = {}): BudgetEntry => ({ id: "entry", budgetItemId: "internet", kind: "Commitment", itemCode: "OLD", itemName: "Saved Internet",
  expenseCategoryId: "saved-category", expenseCategoryName: "Saved category", merchantId: null, merchantName: null, plannedAmount: "30.123456",
  currencyCode: "GBP", frequency: "Month", intervalCount: 1, notes: "Saved notes", valueOrigin: "CarriedForward", lastConfirmedDate: "2026-01-01", carriedFromBudgetEntryId: "earlier-entry", ...extra });
const row = (entry: BudgetEntry | null = saved()): CurrentOutgoing => ({ item: budgetItem("first", { paymentAnchorDate: "2026-01-31" }), entry });
const report = (extra: Partial<CurrentOutgoings> = {}): CurrentOutgoings => ({ asOf: localToday(), snapshotId: "plan", effectiveFrom: "2026-01-01",
  effectiveUntilExclusive: null, isComplete: true, items: [row()], ...extra });

describe("Current outgoings overview", () => {
  let fixture: ComponentFixture<OutgoingOverview>;
  let current: ReturnType<typeof signal<Household | null>>;
  let get: ReturnType<typeof vi.fn>, text: ReturnType<typeof vi.fn>, send: ReturnType<typeof vi.fn>;
  let response: CurrentOutgoings;
  const home = (id: string): Household => ({ id, name: id, people: [] });
  beforeEach(() => {
    response = report(); current = signal<Household | null>(home("first"));
    get = vi.fn(async (path: string) => path.endsWith("/currencies") ? [{ code: "GBP", minorUnits: 2 }, { code: "EUR", minorUnits: 2 }] : [spendingEntry(path.includes("/categories?") ? "food" : "shop", path.split("/")[1])]);
    text = vi.fn(async () => JSON.stringify(response)); send = vi.fn();
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: Workspace, useValue: { current } }, { provide: Api, useValue: { get, getText: text, send } }] });
  });
  afterEach(() => { fixture?.destroy(); vi.restoreAllMocks(); });
  async function settle() { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
  async function page() { fixture = TestBed.createComponent(OutgoingOverview); await settle(); await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false)); await settle(); return fixture.componentInstance; }

  it("uses explicit local dates, includes retired definitions and never writes or reads history", async () => {
    await page(); expect(text).toHaveBeenCalledExactlyOnceWith(`households/first/budgets/current-outgoings?asOf=${localToday()}&includeRetired=true`);
    expect(get.mock.calls.map(([path]) => path).sort()).toEqual(["households/first/spending/categories?includeRetired=true", "households/first/spending/currencies", "households/first/spending/merchants?includeRetired=true"]);
    expect(send).not.toHaveBeenCalled();
  });
  it("displays saved amount/context/provenance instead of changed live defaults", async () => {
    response.items[0].item = budgetItem("first", { name: "Current name", currencyCode: "EUR", frequency: "Year", paymentAnchorDate: "2026-01-31" });
    await page(); const body = fixture.nativeElement.textContent;
    expect(body).toContain("GBP 30.123456"); expect(body).toContain("Saved Internet"); expect(body).toContain("Saved category"); expect(body).toContain("Carried forward");
    expect(body).toContain("Last confirmed 1 Jan 2026"); expect(body).toContain("Current definition: Current name");
    expect(body).toContain("Current recurrence differs"); expect(body).toContain("Current definition uses EUR");
    expect(fixture.nativeElement.querySelector(".outgoing-context").textContent).not.toContain("Shop");
  });
  it("distinguishes no plan, empty plan, omitted entry, unknown and explicit zero", async () => {
    response = report({ snapshotId: null, effectiveFrom: null, isComplete: null, items: [row(null)] }); const c = await page();
    expect(c.state(c.report()!.items[0])).toBe("No effective plan"); expect(c.totals()).toEqual([]);
    response = report({ items: [row(null)] }); await c.load(); await settle();
    expect(c.state(c.report()!.items[0])).toBe("Not included in plan"); expect(c.missing()).toBe(1); expect(fixture.nativeElement.textContent).toContain("This plan includes no outgoings");
    response = report({ isComplete: false, items: [row(saved({ plannedAmount: null, lastConfirmedDate: null, valueOrigin: "Unrecorded" }))] }); await c.load(); await settle();
    expect(c.unknown()).toBe(1); expect(c.totals()[0]).toMatchObject({ amount: "0", unknown: 1 }); expect(fixture.nativeElement.textContent).toContain("Unknown amount (GBP)");
    expect(fixture.nativeElement.textContent).toContain("Known subtotal:"); expect(fixture.nativeElement.textContent).toContain("Never confirmed");
    response = report({ items: [row(saved({ plannedAmount: "0" }))] }); await c.load(); await settle(); expect(c.unknown()).toBe(0);
    expect(fixture.nativeElement.textContent).toContain("GBP 0.00"); expect(c.state(c.report()!.items[0])).toBe("Amount recorded");
  });
  it("sums exact saved numeric tokens and separates currency and recurrence groups", async () => {
    response.items = [row(saved({ plannedAmount: "999999999999.123456" })), { item: budgetItem("first", { id: "two" }), entry: saved({ id: "two", plannedAmount: "0.876544", budgetItemId: "two" }) },
      { item: budgetItem("first", { id: "three" }), entry: saved({ id: "three", plannedAmount: "5", currencyCode: "EUR", budgetItemId: "three" }) },
      { item: budgetItem("first", { id: "four" }), entry: saved({ id: "four", plannedAmount: "8", frequency: "Week", budgetItemId: "four" }) },
      { item: budgetItem("first", { id: "five" }), entry: saved({ id: "five", plannedAmount: "10", intervalCount: 3, budgetItemId: "five" }) }];
    text.mockImplementation(async () => JSON.stringify(response).replace('"999999999999.123456"', '999999999999.123456'));
    const c = await page(); expect(c.totals()).toContainEqual({ currency: "GBP", frequency: "Month", interval: 1, amount: "1000000000000", unknown: 0 }); expect(c.totals()).toHaveLength(4);
    expect(fixture.nativeElement.textContent).toContain("GBP 999 999 999 999.123456");
  });
  it("filters saved categories, names and retirement while keeping all-plan totals stable", async () => {
    response.items.push({ item: budgetItem("first", { id: "old", isRetired: true }), entry: saved({ id: "old-entry", budgetItemId: "old", itemName: "Old water", plannedAmount: "10" }) });
    const c = await page(), total = c.totals(); expect(c.visible()).toHaveLength(2); c.search.set("water"); expect(c.visible()).toHaveLength(1);
    c.status.set("active"); expect(c.visible()).toHaveLength(0); c.search.set(""); expect(c.visible()).toHaveLength(1);
    c.category.set("saved-category"); expect(c.visible()).toHaveLength(1); c.category.set("first-food"); expect(c.visible()).toHaveLength(0);
    expect(c.totals()).toEqual(total); expect(c.categoryOptions()).toEqual([{ id: "saved-category", name: "Saved category" }]);
  });
  it("shows current lookup labels for rows without a saved entry", async () => {
    response.items = [row(null)]; const c = await page(); const body = fixture.nativeElement.textContent;
    expect(body).toContain("Food"); expect(body).toContain("Shop"); expect(c.categoryOptions()[0].id).toBe("first-food");
  });
  it("shows averages from the selected saved plan while leaving originals and filter scope intact", async () => {
    const commitment = row(saved({ plannedAmount: "100", frequency: "Week" })); commitment.item.frequency = "Year"; commitment.item.isRetired = true;
    response = report({ asOf: "2026-02-15", effectiveUntilExclusive: "2026-02-20", items: [commitment,
      { item: budgetItem("first", { id: "allowance", kind: "Allowance" }), entry: saved({ id: "allowance-entry", budgetItemId: "allowance", kind: "Allowance", plannedAmount: "400" }) },
      { item: budgetItem("first", { id: "missing" }), entry: null }] });
    const c = await page(), summary = fixture.debugElement.query(By.directive(MonthlyBudgetSummary)).componentInstance as MonthlyBudgetSummary;
    expect(summary.averages()[0].commitments.amount).toBe("433.33"); expect(summary.averages()[0].allowances.amount).toBe("400.00"); expect(summary.averages()[0].combined.amount).toBe("833.33");
    const body = fixture.nativeElement.textContent; expect(body).toContain("Based on the plan effective on 15 Feb 2026"); expect(body).toContain("until before 20 Feb 2026");
    expect(body).toContain("Weekly costs use 52 weeks per year"); expect(body).toContain("GBP 100.00"); expect(body).toContain("Every week");
    const before = summary.averages(); c.status.set("active"); c.search.set("nothing"); await settle(); expect(summary.averages()).toEqual(before);
    expect(c.missing()).toBe(1); expect(send).not.toHaveBeenCalled();
  });
  it("does not show a zero monthly budget before the first plan and labels an empty plan", async () => {
    response = report({ snapshotId: null, effectiveFrom: null, isComplete: null, items: [row(null)] }); const c = await page();
    expect(fixture.nativeElement.querySelector("app-monthly-budget-summary")).toBeNull();
    response = report({ items: [row(null)] }); await c.load(); await settle();
    expect(fixture.nativeElement.textContent).toContain("no commitments or allowances to average");
    expect(fixture.nativeElement.querySelector("app-monthly-budget-summary table")).toBeNull();
  });
  it("labels unknown-only averages and mixed partial totals without converting unknowns to zero", async () => {
    response = report({ isComplete: false, items: [row(saved({ plannedAmount: null })),
      { item: budgetItem("first", { id: "allowance", kind: "Allowance" }), entry: saved({ id: "allowance-entry", budgetItemId: "allowance", kind: "Allowance", plannedAmount: "400" }) }] });
    await page(); const cells = fixture.nativeElement.querySelectorAll("app-monthly-budget-summary tbody td");
    expect(cells[0].textContent).toContain("Unknown"); expect(cells[0].textContent).not.toContain("GBP 0.00");
    expect(cells[2].textContent).toContain("Known subtotal"); expect(cells[2].textContent).toContain("GBP 400.00"); expect(cells[2].textContent).toContain("1 unknown");
  });
  it("keeps plan reads available when display currency metadata fails", async () => {
    get.mockImplementation(async path => { if (path.endsWith("/currencies")) throw new Error("Policy unavailable"); return [spendingEntry("food")]; });
    const c = await page(); expect(c.error()).toBe(""); expect(c.currencies()).toBeNull();
    expect(fixture.nativeElement.textContent).toContain("GBP display precision is unavailable");
    expect(fixture.nativeElement.querySelector("app-monthly-budget-summary table")).not.toBeNull();
  });
  it("renders the saved plan while optional currency metadata is stalled, then refines precision", async () => {
    const pending = deferred<{ code: string; minorUnits: number }[]>();
    get.mockImplementation(async path => path.endsWith("/currencies") ? pending.promise : [spendingEntry("food")]);
    const c = await page(); expect(c.loading()).toBe(false); expect(c.currencies()).toBeNull();
    expect(fixture.nativeElement.querySelector("app-monthly-budget-summary table")).not.toBeNull();
    pending.resolve([{ code: "GBP", minorUnits: 2 }]);
    await vi.waitFor(() => expect(c.currencies()).toEqual([{ code: "GBP", minorUnits: 2 }])); await settle();
    expect(fixture.nativeElement.textContent).not.toContain("GBP display precision is unavailable");
  });
  it("rejects an older currency lookup after a newer request refines the same household", async () => {
    const pending = deferred<{ code: string; minorUnits: number }[]>(); let initial = true;
    get.mockImplementation(async path => {
      if (!path.endsWith("/currencies")) return [spendingEntry("food")];
      if (initial) { initial = false; return pending.promise; } return [{ code: "GBP", minorUnits: 2 }];
    });
    const c = await page(); await c.load(); pending.resolve([{ code: "GBP", minorUnits: 3 }]); await settle();
    expect(c.currencies()).toEqual([{ code: "GBP", minorUnits: 2 }]);
  });
  it.each([false, true])("ignores stale optional currency completion after a household round trip (failure=%s)", async fail => {
    const pending = deferred<{ code: string; minorUnits: number }[]>(); let initial = true;
    get.mockImplementation(async path => {
      if (!path.endsWith("/currencies")) return [spendingEntry("food", path.split("/")[1])];
      if (initial) { initial = false; return pending.promise; } return [{ code: "GBP", minorUnits: 2 }];
    });
    const c = await page(); current.set(home("second")); await settle(); current.set(home("first")); await settle();
    if (fail) pending.reject(new Error("Stale policy failure")); else pending.resolve([{ code: "GBP", minorUnits: 0 }]); await settle();
    expect(c.currencies()).toEqual([{ code: "GBP", minorUnits: 2 }]); expect(c.error()).toBe("");
  });
  it("uses the displayed report date until a new date is applied and replaces averages with the next plan", async () => {
    response = report({ asOf: "2026-02-15", items: [row(saved({ plannedAmount: "400" }))] }); const c = await page();
    c.asOf.set("2026-03-01"); await settle(); let summary = fixture.debugElement.query(By.directive(MonthlyBudgetSummary)).componentInstance as MonthlyBudgetSummary;
    expect(summary.asOf()).toBe("2026-02-15"); expect(summary.averages()[0].combined.amount).toBe("400.00");
    response = report({ asOf: "2026-03-01", snapshotId: "new-plan", effectiveFrom: "2026-03-01", items: [row(saved({ plannedAmount: "500" }))] });
    await c.load(); await settle(); summary = fixture.debugElement.query(By.directive(MonthlyBudgetSummary)).componentInstance as MonthlyBudgetSummary;
    expect(summary.asOf()).toBe("2026-03-01"); expect(summary.averages()[0].combined.amount).toBe("500.00");
  });
  it("shows allowances under their saved category without internal codes or payment schedules", async () => {
    response = report({ items: [{ item: budgetItem("first", { kind: "Allowance", code: "ALW_INTERNAL", name: "Internal allowance", merchantId: null }),
      entry: saved({ kind: "Allowance", itemCode: "ALW_INTERNAL", expenseCategoryName: "Food", itemName: "Internal allowance", plannedAmount: "400" }) }] });
    const c = await page(), list = fixture.nativeElement.querySelector(".overview-list:last-child") ?? fixture.nativeElement;
    const body = fixture.nativeElement.textContent; expect(body).toContain("Spending allowances"); expect(body).toContain("GBP 400.00"); expect(body).not.toContain("ALW_INTERNAL");
    expect(list.querySelector(".payment-schedule")).toBeNull(); expect(c.nextDate(c.visible()[0])).toBeNull();
    c.search.set("ALW_INTERNAL"); expect(c.visible()).toHaveLength(0);
  });
  it("clears a category selection absent from a newly loaded report", async () => {
    const c = await page(); c.category.set("saved-category"); expect(c.visible()).toHaveLength(1);
    response = report({ asOf: "2026-02-01", items: [row(null)] }); c.asOf.set("2026-02-01"); await c.load(); await settle();
    expect(c.category()).toBe(""); expect(c.visible()).toHaveLength(1);
  });
  it("searches the displayed note context without falling back from a null saved note", async () => {
    response.items = [{ item: budgetItem("first", { notes: "Current-only note" }), entry: saved({ notes: null }) }];
    const c = await page(); c.search.set("Current-only"); expect(c.visible()).toHaveLength(0);
    response.items[0].entry = null; await c.load(); await settle(); expect(c.visible()).toHaveLength(1);
    expect(fixture.nativeElement.querySelector(".overview-list").textContent).toContain("Current-only note");
  });
  it("shows unavailable lookup labels instead of failing", async () => {
    response.items = [row(null)]; get.mockResolvedValue([]); const c = await page();
    expect(c.categoryName(c.visible()[0])).toBe("Category unavailable"); expect(c.merchantName(c.visible()[0])).toBe("Merchant unavailable");
  });
  it("derives next dates from current anchored recurrence and warns beyond the saved interval", async () => {
    response = report({ asOf: "2026-02-01", effectiveUntilExclusive: "2026-02-28" }); const c = await page();
    expect(c.nextDate(c.visible()[0])).toBe("2026-02-28"); expect(c.afterPlan(c.visible()[0])).toBe(true);
    expect(fixture.nativeElement.textContent).toContain("Next expected: 28 Feb 2026"); expect(fixture.nativeElement.textContent).toContain("amount may differ");
  });
  it("retains retired saved amounts without forecasting retired dates", async () => {
    response.items[0].item.isRetired = true; const c = await page(); expect(c.totals()[0].amount).toBe("30.123456"); expect(c.nextDate(c.visible()[0])).toBeNull();
    const body = fixture.nativeElement.textContent; expect(body).toContain("Retired definition"); expect(body).toContain("Saved amounts remain in this plan"); expect(body).not.toContain("Next expected:");
  });
  it("handles unset and exhausted dates without dropping planned amounts", async () => {
    response.items[0].item.paymentAnchorDate = null; const c = await page(); expect(fixture.nativeElement.textContent).toContain("Payment date not set");
    response = report({ asOf: "9999-12-30", items: [{ item: budgetItem("first", { paymentAnchorDate: "9999-12-29", frequency: "Year" }), entry: saved() }] });
    await c.load(); await settle(); expect(c.nextDate(c.visible()[0])).toBeNull(); expect(fixture.nativeElement.textContent).toContain("No further date within the supported range");
  });
  it("submits the rendered date selection and clearly labels unapplied dates", async () => {
    const c = await page(), loader = TestbedHarnessEnvironment.loader(fixture);
    await (await loader.getHarness(MatInputHarness.with({ selector: '[name="asOf"]' }))).setValue("2026-03-01");
    expect(fixture.nativeElement.textContent).toContain("date selection has changed"); response = report({ asOf: "2026-03-01" });
    await (await loader.getHarness(MatButtonHarness.with({ text: "Show outgoings" }))).click();
    expect(text).toHaveBeenLastCalledWith("households/first/budgets/current-outgoings?asOf=2026-03-01&includeRetired=true"); expect(c.report()?.asOf).toBe("2026-03-01");
  });
  it.each(["", "0001-01-01", "9999-12-31", "2026-02-30", "bad"])("rejects invalid date %s without another request", async date => {
    const c = await page(); text.mockClear(); c.asOf.set(date); await c.load(); expect(text).not.toHaveBeenCalled(); expect(c.error()).toContain("supported as-of date");
  });
  it("resets Today using the local calendar date", async () => {
    const c = await page(); c.asOf.set("2026-01-01"); await c.today(); expect(c.asOf()).toBe(localToday());
    expect(text).toHaveBeenLastCalledWith(`households/first/budgets/current-outgoings?asOf=${localToday()}&includeRetired=true`);
  });
  it("hides stale results on a failed reload and allows recovery", async () => {
    const c = await page(); text.mockRejectedValueOnce(new Error("Unavailable")); await c.load(); await settle();
    expect(c.error()).toBe("Unavailable"); expect(fixture.nativeElement.querySelector(".overview-list")).toBeNull();
    await c.load(); await settle(); expect(c.error()).toBe(""); expect(fixture.nativeElement.querySelector(".overview-list")).not.toBeNull();
  });
  it("rejects older reads after a newer request", async () => {
    const c = await page(), pending = deferred<string>(); text.mockReturnValueOnce(pending.promise); const first = c.load();
    response = report({ asOf: "2026-03-01" }); c.asOf.set("2026-03-01"); await c.load(); pending.resolve(JSON.stringify(report({ asOf: "2026-01-01" }))); await first;
    expect(c.report()?.asOf).toBe("2026-03-01"); expect(c.loading()).toBe(false);
  });
  it.each([false, true])("ignores stale household reads after switching away and back (failure=%s)", async fail => {
    const c = await page(), pending = deferred<string>(); text.mockReturnValueOnce(pending.promise); const first = c.load(); c.search.set("old filter");
    current.set(home("second")); await settle(); current.set(home("first")); await settle();
    if (fail) pending.reject(new Error("Old error")); else pending.resolve(JSON.stringify(report({ asOf: "2025-01-01" }))); await first;
    expect(c.report()?.asOf).toBe(localToday()); expect(c.search()).toBe(""); expect(c.error()).toBe("");
  });
  it("shows empty and no-household states without writes", async () => {
    current.set(null); const c = await page(); expect(get).not.toHaveBeenCalled(); expect(text).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain("Select or create a household");
    response = report({ items: [] }); current.set(home("first")); await settle(); await vi.waitFor(() => expect(c.loading()).toBe(false)); await settle(); expect(fixture.nativeElement.textContent).toContain("No outgoing definitions yet"); expect(send).not.toHaveBeenCalled();
  });
});
