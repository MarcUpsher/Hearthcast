import { describe, expect, it } from "vitest";
import { monthlyAverages } from "./monthly-average";

type Entry = Parameters<typeof monthlyAverages>[0][number];
const entry = (extra: Partial<Entry> = {}): Entry => ({ kind: "Commitment", currencyCode: "GBP", frequency: "Month", intervalCount: 1, plannedAmount: "100", ...extra });
const currencies = [{ code: "GBP", minorUnits: 2 }, { code: "EUR", minorUnits: 2 }, { code: "JPY", minorUnits: 0 }, { code: "KWD", minorUnits: 3 }, { code: "CLF", minorUnits: 4 }];
describe("Monthly planning averages", () => {
  it.each([
    ["Week", 1, "100", "433.33"], ["Week", 2, "100", "216.67"], ["Week", 52, "1200", "100.00"],
    ["Month", 1, "400", "400.00"], ["Month", 3, "1200", "400.00"], ["Month", 6, "600", "100.00"],
    ["Year", 1, "600", "50.00"], ["Year", 2, "600", "25.00"], ["Month", 2147483647, "100", "0.00"],
  ] as const)("averages %s × %s for amount %s", (frequency, intervalCount, plannedAmount, expected) => {
    const [result] = monthlyAverages([entry({ frequency, intervalCount, plannedAmount })], currencies);
    expect(result.combined).toEqual({ amount: expected, known: 1, unknown: 0 });
  });
  it("sums exact repeating fractions before rounding", () => {
    const [result] = monthlyAverages(Array.from({ length: 12 }, () => entry({ frequency: "Year", plannedAmount: "1", currencyCode: "XTS" })), currencies);
    expect(result.combined.amount).toBe("1.000000"); expect(result.precisionFallback).toBe(true);
    const [small] = monthlyAverages(Array.from({ length: 12 }, () => entry({ frequency: "Year", plannedAmount: "0.06" })), currencies);
    expect(small.combined.amount).toBe("0.06");
  });
  it("keeps additive purposes distinct and rounds their exact combined sum independently", () => {
    const [result] = monthlyAverages([entry({ plannedAmount: "0.005" }), entry({ kind: "Allowance", plannedAmount: "0.005" })], currencies);
    expect(result.commitments.amount).toBe("0.01"); expect(result.allowances.amount).toBe("0.01"); expect(result.combined.amount).toBe("0.01");
    expect(result.roundingDifference).toBe(true);
    const [food] = monthlyAverages([entry({ plannedAmount: "50" }), entry({ kind: "Allowance", plannedAmount: "400" })], currencies);
    expect(food.combined.amount).toBe("450.00");
  });
  it("separates currencies without conversion and uses their display precision", () => {
    const results = monthlyAverages([entry({ plannedAmount: "1.234567" }), entry({ currencyCode: "EUR", plannedAmount: "1.234567" }),
      entry({ currencyCode: "JPY", plannedAmount: "1.5" }), entry({ currencyCode: "KWD", plannedAmount: "1.2345" }), entry({ currencyCode: "CLF", plannedAmount: "1.23455" })], currencies);
    expect(results.map(row => [row.currency, row.combined.amount, row.places])).toEqual([
      ["CLF", "1.2346", 4], ["EUR", "1.23", 2], ["GBP", "1.23", 2], ["JPY", "2", 0], ["KWD", "1.235", 3],
    ]);
  });
  it("retains six-decimal large saved amounts without Number precision loss", () => {
    const [result] = monthlyAverages([entry({ currencyCode: "XTS", plannedAmount: "999999999999.123456" }), entry({ currencyCode: "XTS", plannedAmount: "0.876544" })], currencies);
    expect(result.combined.amount).toBe("1000000000000.000000");
  });
  it("distinguishes known zero, unknown-only purpose and partial known subtotals", () => {
    const [result] = monthlyAverages([entry({ plannedAmount: "0" }), entry({ plannedAmount: null }), entry({ kind: "Allowance", plannedAmount: null })], currencies);
    expect(result.commitments).toEqual({ amount: "0.00", known: 1, unknown: 1 });
    expect(result.allowances).toEqual({ amount: null, known: 0, unknown: 1 });
    expect(result.combined).toEqual({ amount: "0.00", known: 1, unknown: 2 });
    expect(monthlyAverages([entry({ plannedAmount: null })], currencies)[0].combined.amount).toBeNull();
    expect(monthlyAverages([], currencies)).toEqual([]);
  });
  it.each([null, [], [{ code: "GBP", minorUnits: 7 }], [{ code: "GBP", minorUnits: -1 }], [{ code: "GBP", minorUnits: 1.5 }]])("uses an explicit six-place fallback for unavailable precision %j", policy => {
    const [result] = monthlyAverages([entry({ plannedAmount: "1", frequency: "Year" })], policy);
    expect(result.precisionFallback).toBe(true); expect(result.combined.amount).toBe("0.083333");
  });
  it.each([0, -1, 1.5, 2147483648])("rejects unsupported recurrence interval %s", intervalCount => {
    expect(() => monthlyAverages([entry({ intervalCount })], currencies)).toThrow("unsupported recurrence");
  });
  it("does not mutate saved entries or their classification", () => {
    const values = [entry({ frequency: "Week" }), entry({ kind: "Allowance", plannedAmount: "400" })], before = structuredClone(values);
    monthlyAverages(values, currencies); expect(values).toEqual(before);
  });
});
