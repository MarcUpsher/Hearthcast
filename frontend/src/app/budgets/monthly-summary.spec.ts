import { ComponentFixture, TestBed } from "@angular/core/testing";
import { afterEach, describe, expect, it } from "vitest";
import { MonthlyBudgetSummary } from "./monthly-summary";
import { BudgetEntry } from "./models";

const entry = (extra: Partial<BudgetEntry> = {}): BudgetEntry => ({ id: "entry", budgetItemId: "item", kind: "Commitment", itemCode: "CODE", itemName: "Name",
  expenseCategoryId: "category", expenseCategoryName: "Food", merchantId: null, merchantName: null, plannedAmount: "100", currencyCode: "GBP", frequency: "Month",
  intervalCount: 1, notes: null, valueOrigin: "Confirmed", lastConfirmedDate: "2026-01-01", carriedFromBudgetEntryId: null, ...extra });
describe("Monthly summary display", () => {
  let fixture: ComponentFixture<MonthlyBudgetSummary>;
  afterEach(() => fixture?.destroy());
  async function show(entries: BudgetEntry[]) {
    fixture = TestBed.createComponent(MonthlyBudgetSummary); fixture.componentRef.setInput("entries", entries);
    fixture.componentRef.setInput("asOf", "2026-02-15"); fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges();
  }
  it("explains independently rounded subtotals when they differ from the exact combined total", async () => {
    await show([entry({ plannedAmount: "0.005" }), entry({ id: "allowance", kind: "Allowance", plannedAmount: "0.005" })]);
    fixture.componentRef.setInput("currencies", [{ code: "GBP", minorUnits: 2 }]); fixture.detectChanges();
    const cells = [...fixture.nativeElement.querySelectorAll("tbody td")].map((cell: any) => cell.textContent);
    expect(cells).toHaveLength(3); expect(cells.every(cell => cell.includes("GBP 0.01"))).toBe(true);
    expect(fixture.nativeElement.textContent).toContain("Displayed subtotals differ slightly because of rounding");
  });
  it("renders currency-specific precision and explains an unavailable historical currency", async () => {
    await show([entry({ currencyCode: "JPY", plannedAmount: "100", frequency: "Week" }), entry({ currencyCode: "XTS", plannedAmount: "1", frequency: "Year" })]);
    fixture.componentRef.setInput("currencies", [{ code: "JPY", minorUnits: 0 }]); fixture.detectChanges();
    const body = fixture.nativeElement.textContent; expect(body).toContain("JPY 433"); expect(body).not.toContain("JPY 433.00");
    expect(body).toContain("XTS 0.083333"); expect(body).toContain("XTS display precision is unavailable");
  });
  it("treats an absent purpose as zero within a known currency but unknown entries remain unknown", async () => {
    await show([entry({ plannedAmount: null })]);
    const cells = fixture.nativeElement.querySelectorAll("tbody td"); expect(cells[0].textContent).toContain("Unknown");
    expect(cells[1].textContent).toContain("GBP 0.000000"); expect(cells[2].textContent).toContain("Unknown");
  });
});
