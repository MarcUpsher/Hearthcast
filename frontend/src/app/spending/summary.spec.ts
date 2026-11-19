import { signal } from "@angular/core";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { provideRouter } from "@angular/router";
import { HttpErrorResponse } from "@angular/common/http";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { MatButtonHarness } from "@angular/material/button/testing";
import { MatInputHarness } from "@angular/material/input/testing";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Api } from "../core/api";
import { Workspace } from "../core/workspace";
import { Household } from "../setup/models";
import { SpendingSummary } from "./summary";
import { SpendingCategorySummary } from "./models";
import { deferred } from "../../testing/spending";
import { localToday } from "./amounts";
import { monthRange } from "./ranges";

function report(home = "first", extra: Partial<SpendingCategorySummary> = {}): SpendingCategorySummary {
  return { householdId: home, from: "2026-11-01", to: "2026-11-30",
    categories: [{ expenseCategoryId: "food", categoryName: "Saved food", currencyCode: "GBP", currencyMinorUnits: 2, netAmount: "12.5" }],
    reconciliationRecordCounts: { notChecked: 1, reconciled: 0, unreconciled: 0 }, warnings: [], ...extra };
}

describe("Recorded spending category report", () => {
  let fixture: ComponentFixture<SpendingSummary>;
  let current: ReturnType<typeof signal<Household | null>>;
  let get: ReturnType<typeof vi.fn<(path: string) => Promise<SpendingCategorySummary>>>;
  let send: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    current = signal<Household | null>({ id: "first", name: "Home", people: [] });
    get = vi.fn(async (path: string) => {
      const url = new URL(path, "http://test/");
      return report(path.split("/")[1], { from: url.searchParams.get("from")!, to: url.searchParams.get("to")! });
    }); send = vi.fn();
    TestBed.configureTestingModule({ providers: [provideRouter([]),
      { provide: Workspace, useValue: { current } }, { provide: Api, useValue: { get, send, getText: async (path: string) => JSON.stringify(await get(path)) } }] });
  });
  afterEach(() => { fixture?.destroy(); vi.restoreAllMocks(); });
  async function settle() { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
  async function page() {
    fixture = TestBed.createComponent(SpendingSummary); await settle();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false));
    await settle(); return fixture.componentInstance;
  }
  const button = (text: string) => TestbedHarnessEnvironment.loader(fixture).getHarness(MatButtonHarness.with({ text }));
  it("reads the full current month from summary without setup/history requests or writes", async () => {
    const c = await page(), range = monthRange(localToday().slice(0, 7))!;
    expect(get).toHaveBeenCalledExactlyOnceWith(`households/first/spending/summary?from=${range.from}&to=${range.to}`);
    expect(c.report()?.from).toBe(range.from); expect(send).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain("Saved food");
    expect(fixture.nativeElement.textContent).toContain("GBP 12.50");
    expect(fixture.nativeElement.textContent).toContain("only the spending you have recorded");
  });
  it("submits any inclusive range through rendered date controls", async () => {
    const c = await page(), loader = TestbedHarnessEnvironment.loader(fixture);
    await (await loader.getHarness(MatInputHarness.with({ selector: '[name="from"]' }))).setValue("2026-12-28");
    await (await loader.getHarness(MatInputHarness.with({ selector: '[name="to"]' }))).setValue("2027-01-10");
    expect(c.changed()).toBe(true); await (await button("Show recorded spending / refresh")).click();
    expect(get).toHaveBeenLastCalledWith("households/first/spending/summary?from=2026-12-28&to=2027-01-10");
    expect(c.changed()).toBe(false); expect(fixture.nativeElement.textContent).toContain("28 Dec 2026 – 10 Jan 2027");
  });
  it("chooses an arbitrary month and connects all calendar shortcut buttons", async () => {
    const c = await page(), loader = TestbedHarnessEnvironment.loader(fixture);
    await (await loader.getHarness(MatInputHarness.with({ selector: '[name="month"]' }))).setValue("2024-02");
    await (await button("Show month")).click();
    expect(get).toHaveBeenLastCalledWith("households/first/spending/summary?from=2024-02-01&to=2024-02-29");
    for (const text of ["This month", "Last month", "This week", "Last week"]) {
      const count = get.mock.calls.length; await (await button(text)).click(); expect(get.mock.calls.length).toBe(count + 1);
      const { from, to } = c.range(); expect(get).toHaveBeenLastCalledWith(`households/first/spending/summary?from=${from}&to=${to}`);
    }
    expect(send).not.toHaveBeenCalled();
  });
  it("keeps currencies and category identities separate with zero, negative and historical precision", async () => {
    const c = await page(); get.mockResolvedValueOnce(report("first", { categories: [
      { expenseCategoryId: "food", categoryName: "Food", currencyCode: "GBP", currencyMinorUnits: 2, netAmount: "-3" },
      { expenseCategoryId: "food", categoryName: "Food EUR", currencyCode: "EUR", currencyMinorUnits: 3, netAmount: "1.125" },
      { expenseCategoryId: "other", categoryName: "Food", currencyCode: "GBP", currencyMinorUnits: 3, netAmount: "0" },
    ] })); await c.load(); await settle();
    expect(fixture.nativeElement.querySelectorAll("table")).toHaveLength(2);
    expect(fixture.nativeElement.querySelectorAll("tbody tr")).toHaveLength(3);
    const text = fixture.nativeElement.textContent;
    expect(text).toContain("GBP -3.00"); expect(text).toContain("GBP 0.000"); expect(text).toContain("EUR 1.125");
    expect(c.groups().map(group => group.currency)).toEqual(["EUR", "GBP"]);
  });
  it("shows record counts and signed receipt discrepancies without changing category amounts", async () => {
    const c = await page(); get.mockResolvedValueOnce(report("first", { reconciliationRecordCounts: { notChecked: 2, reconciled: 3, unreconciled: 1 },
      warnings: [{ spendingRecordId: "refund", spendingDate: "2026-06-01", merchantName: "Saved shop", currencyCode: "GBP", receiptTotal: "9", lineTotal: "10", difference: "-1" }] }));
    await c.load(); await settle();
    expect([...fixture.nativeElement.querySelectorAll("dd")].map((e: HTMLElement) => e.textContent)).toEqual(["2", "3", "1"]);
    const warning = fixture.nativeElement.querySelector(".warnings").textContent;
    expect(warning).toContain("Saved shop · 1 Jun 2026"); expect(warning).toContain("Receipt: GBP 9.00 · Recorded total: GBP 10.00");
    expect(warning).toContain("Receipt minus recorded total: GBP -1.00");
    expect(fixture.nativeElement.querySelector("tbody").textContent).toContain("GBP 12.50");
    expect(fixture.nativeElement.querySelectorAll(".warnings li")).toHaveLength(1);
  });
  it("retains warning decimals rather than rounding to conventional currency precision", async () => {
    const c = await page(); get.mockResolvedValueOnce(report("first", { warnings: [{ spendingRecordId: "precise", spendingDate: "2026-11-01", merchantName: "Shop", currencyCode: "GBP", receiptTotal: "1.000001", lineTotal: "1", difference: "0.000001" }] }));
    await c.load(); await settle(); expect(fixture.nativeElement.querySelector(".warnings").textContent).toContain("GBP 0.000001");
  });
  it.each([["", "2026-12-01"], ["2026-12-01", ""], ["2026-02-30", "2026-12-01"], ["2026-12-02", "2026-12-01"], ["0001-01-01", "2026-12-01"]])("rejects invalid range %s through %s without requesting or discarding the last report", async (from, to) => {
    const c = await page(), previous = c.report(); get.mockClear(); c.change("from", from); c.change("to", to); await c.load();
    expect(c.error()).toContain("valid From and To"); expect(get).not.toHaveBeenCalled(); expect(c.report()).toBe(previous);
  });
  it("validates missing month and accepts single-day reports", async () => {
    const c = await page(); get.mockClear(); c.month.set(""); await c.showMonth();
    expect(c.error()).toContain("month and year"); expect(get).not.toHaveBeenCalled();
    c.change("from", "2026-12-01"); c.change("to", "2026-12-01"); await c.load();
    expect(get).toHaveBeenCalledExactlyOnceWith("households/first/spending/summary?from=2026-12-01&to=2026-12-01");
  });
  it("shows empty reports with zero counts and cautious coverage wording", async () => {
    const c = await page(); get.mockResolvedValueOnce(report("first", { categories: [], reconciliationRecordCounts: { notChecked: 0, reconciled: 0, unreconciled: 0 } }));
    await c.load(); await settle(); expect(fixture.nativeElement.textContent).toContain("No recorded spending in this range");
    expect(fixture.nativeElement.textContent).toContain("does not establish that nothing was spent");
    expect(fixture.nativeElement.querySelectorAll("table")).toHaveLength(0);
  });
  it("retains the last report under its own dates on failure and retries the requested range", async () => {
    const c = await page(), previous = c.report(); c.change("from", "2026-12-01"); c.change("to", "2026-12-31");
    get.mockRejectedValueOnce(new Error("Report unavailable")); await c.load(); await settle();
    expect(c.error()).toBe("Report unavailable"); expect(c.report()).toBe(previous); expect(c.changed()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain("Apply the range to update the report below");
    await c.load(); expect(c.report()?.from).toBe("2026-12-01"); expect(c.error()).toBe("");
  });
  it("does not present a missing household as a successful empty report", async () => {
    get.mockRejectedValue(new HttpErrorResponse({ status: 404, error: { detail: "Household not found" } })); const c = await page();
    expect(c.report()).toBeNull(); expect(c.error()).toBe("Household not found");
    expect(fixture.nativeElement.textContent).not.toContain("No recorded spending in this range");
  });
  it("ignores an old range response after a newer report has completed", async () => {
    const c = await page(), older = deferred<SpendingCategorySummary>(); get.mockReturnValueOnce(older.promise);
    c.change("from", "2026-12-01"); c.change("to", "2026-12-31"); const reading = c.load();
    c.change("from", "2027-01-01"); c.change("to", "2027-01-31"); await c.load();
    older.resolve(report("first", { from: "2026-12-01", to: "2026-12-31" })); await reading;
    expect(c.report()?.from).toBe("2027-01-01"); expect(c.loading()).toBe(false);
  });
  it("clears data on household changes and rejects a late response even after switching back", async () => {
    const c = await page(), pending = deferred<SpendingCategorySummary>(); get.mockReturnValueOnce(pending.promise); const reading = c.load();
    current.set({ id: "second", name: "Other", people: [] }); await settle(); expect(c.report()?.householdId).toBe("second");
    current.set({ id: "first", name: "Home", people: [] }); await settle();
    pending.resolve(report("first", { from: "2000-01-01" })); await reading; expect(c.report()?.from).not.toBe("2000-01-01");
    current.set(null); await settle(); expect(c.report()).toBeNull(); expect(c.loading()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain("Select or create a household");
  });
  it("ignores a late failure after destruction", async () => {
    const c = await page(), pending = deferred<SpendingCategorySummary>(); get.mockReturnValueOnce(pending.promise); const reading = c.load();
    fixture.destroy(); pending.reject(new Error("Old error")); await reading; expect(c.error()).toBe("");
  });
});
