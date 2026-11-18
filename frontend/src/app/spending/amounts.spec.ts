import { describe, expect, it } from "vitest";
import { decimalAmount, minorAmount, safeAmount, suggestedAmount, validSpendingDate } from "./amounts";

describe("Spending decimal entry", () => {
  it.each([
    ["1", "1.005", 2, "1.01"], ["0.3", "0.1", 2, "0.03"], ["1", "1.2345", 3, "1.235"],
    ["1", "2.5", 0, "3"], ["2", "0", 2, "0.00"], ["0.5", "0.000001", 6, "0.000001"],
  ] as const)("rounds %s × %s to %s places exactly", (quantity, price, places, expected) => {
    expect(suggestedAmount(quantity, price, places)).toBe(expected);
  });
  it("adds and subtracts exact minor units without binary rounding", () => {
    expect(decimalAmount(minorAmount("0.10", 2)! + minorAmount("0.20", 2)!, 2)).toBe("0.30");
    expect(decimalAmount(minorAmount("0", 2)! - minorAmount("1.25", 2)!, 2)).toBe("-1.25");
    expect(minorAmount("1.234", 2)).toBeNull();
  });
  it("rejects unsafe JSON numeric conversions rather than changing entered digits", () => {
    expect(safeAmount("999999999999.123456", 6, 1e12)).toBeNull();
    expect(safeAmount("1234.123456", 6, 1e12)).toBe(1234.123456);
    expect(safeAmount("200000000000.00003", 6, 1e12)).toBe(200000000000.00003);
    expect(safeAmount("0", 0, 1e12)).toBe(0);
    expect(safeAmount("1000000000000", 2, 1e12)).toBeNull();
    expect(safeAmount("1e3", 2, 1e12)).toBeNull();
  });
  it.each([["2024-02-29", true], ["2026-02-29", false], ["0001-01-01", false], ["2026-13-01", false], ["2026-11-18", true]] as const)(
    "validates date %s", (date, valid) => expect(validSpendingDate(date)).toBe(valid));
});
