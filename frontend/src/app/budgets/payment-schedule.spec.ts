import { describe, expect, it } from "vitest";
import { nextPaymentDate, paymentTiming, validPaymentAnchor } from "./payment-schedule";

describe("Expected outgoing payment dates", () => {
  it.each([
    ["2026-11-19", "Month", 1, "2026-11-01", "2026-11-19"],
    ["2026-11-19", "Month", 1, "2026-11-19", "2026-11-19"],
    ["2026-11-19", "Month", 1, "2026-11-20", "2026-12-19"],
    ["2026-12-25", "Month", 1, "2027-01-01", "2027-01-25"],
    ["2026-01-31", "Month", 1, "2026-02-01", "2026-02-28"],
    ["2026-01-31", "Month", 1, "2026-03-01", "2026-03-31"],
    ["2026-01-31", "Month", 1, "2026-04-01", "2026-04-30"],
    ["2024-01-31", "Month", 1, "2024-02-01", "2024-02-29"],
    ["2026-01-31", "Month", 3, "2026-02-01", "2026-04-30"],
    ["2026-01-31", "Month", 3, "2026-05-01", "2026-07-31"],
    ["2024-02-29", "Year", 1, "2025-02-28", "2025-02-28"],
    ["2024-02-29", "Year", 1, "2025-03-01", "2026-02-28"],
    ["2024-02-29", "Year", 1, "2028-02-01", "2028-02-29"],
    ["2024-02-29", "Year", 2, "2025-03-01", "2026-02-28"],
    ["2026-11-20", "Week", 1, "2026-11-21", "2026-11-27"],
    ["2026-11-20", "Week", 2, "2026-11-28", "2026-12-04"],
    ["2026-03-23", "Week", 1, "2026-03-29", "2026-03-30"],
    ["2026-10-19", "Week", 1, "2026-10-25", "2026-10-26"],
    ["0099-01-31", "Month", 1, "0099-02-01", "0099-02-28"],
    ["1900-01-31", "Month", 1, "1900-02-01", "1900-02-28"],
    ["2000-01-31", "Month", 1, "2000-02-01", "2000-02-29"],
    ["0001-01-02", "Month", 1, "9999-12-01", "9999-12-02"],
  ] as const)("resolves %s every %s %s as of %s", (anchor, frequency, interval, asOf, expected) => {
    expect(nextPaymentDate(anchor, frequency, interval, asOf)).toBe(expected);
  });
  it.each(["Week", "Month", "Year"] as const)("returns no date rather than overflowing a large %s interval", frequency => {
    expect(nextPaymentDate("2026-01-01", frequency, 2147483647, "2026-01-02")).toBeNull();
    expect(nextPaymentDate("9999-12-30", frequency, 1, "9999-12-30")).toBe("9999-12-30");
  });
  it.each(["0001-01-01", "9999-12-31", "2026-02-30", "infinity", "2026-11-19T00:00:00Z"])("rejects invalid reference %s", anchor => {
    expect(validPaymentAnchor(anchor)).toBe(false); expect(nextPaymentDate(anchor, "Month", 1, "2026-11-19")).toBeNull();
  });
  it("distinguishes an unset date and invalid recurrence from a forecast", () => {
    expect(validPaymentAnchor("")).toBe(true);
    expect(nextPaymentDate(null, "Month", 1, "2026-11-19")).toBeNull();
    for (const count of [0, -1, 1.5, 2147483648]) expect(nextPaymentDate("2026-11-19", "Month", count, "2026-11-20")).toBeNull();
  });
  it.each([["2026-11-01", "Month", "On the 1st"], ["2026-11-02", "Month", "On the 2nd"], ["2026-11-03", "Month", "On the 3rd"], ["2026-11-11", "Month", "On the 11th"], ["2026-11-19", "Year", "19 November"], ["2026-11-20", "Week", "Friday"]] as const)("describes %s as %s timing", (anchor, frequency, expected) => {
    expect(paymentTiming(anchor, frequency)).toBe(expected);
  });
});
