import { describe, expect, it } from "vitest";
import { budgetJson, parseBudget, planAmount, planMoney, planTotals } from "./plan-data";
describe("Outgoing plan decimal values", () => {
  it("keeps saved decimals exact without altering numeric-looking labels", () => {
    const parsed = parseBudget<{ plannedAmount: string; itemName: string; intervalCount: number }>(' {"plannedAmount":999999999999.123456,"itemName":"123 \\"quoted\\"","intervalCount":3}');
    expect(parsed).toEqual({ plannedAmount: "999999999999.123456", itemName: '123 "quoted"', intervalCount: 3 });
  });
  it.each(["-1", "1e2", "1.1234567", "1000000000000", "NaN", ""])("rejects invalid entered amount %s", value => expect(planAmount(value)).toBeNull());
  it.each(["0", "20", "0.123456", "999999999999", "999999999999.123456"])("encodes valid amount %s", value => expect(planAmount(value)).toBe(value));
  it("writes exact decimal numeric tokens while escaping ordinary strings", () => {
    const value = { entries: [{ plannedAmount: "999999999999.123456", confirmedOn: "2026-11-19" }, { plannedAmount: "000.000100" }, { plannedAmount: null }],
      notes: 'Quotes "plannedAmount":"123" and \\ path', id: "123" };
    const json = budgetJson(value);
    expect(json).toContain('"plannedAmount":999999999999.123456');
    expect(json).toContain('"plannedAmount":0.0001'); expect(json).toContain('"plannedAmount":null');
    expect(parseBudget<typeof value>(json).notes).toBe(value.notes); expect(parseBudget<typeof value>(json).id).toBe("123");
    expect(() => budgetJson({ plannedAmount: '1,"other":2' })).toThrow("Invalid planned amount");
  });
  it("sums decimals exactly, separates currencies/recurrences and counts unknowns", () => {
    const row = { currencyCode: "GBP", frequency: "Month" as const, intervalCount: 1 };
    const totals = planTotals([{ ...row, plannedAmount: "999999999999.123456" }, { ...row, plannedAmount: "0.876544" }, { ...row, plannedAmount: null },
      { ...row, plannedAmount: "0", intervalCount: 3 }, { ...row, plannedAmount: "2", currencyCode: "EUR" }, { ...row, plannedAmount: "5", frequency: "Week" }]);
    expect(totals).toContainEqual({ currency: "GBP", frequency: "Month", interval: 1, amount: "1000000000000", unknown: 1 });
    expect(totals).toContainEqual({ currency: "GBP", frequency: "Month", interval: 3, amount: "0", unknown: 0 });
    expect(totals).toHaveLength(4); expect(planMoney("0.123456", "GBP")).toBe("GBP 0.123456");
  });
});
