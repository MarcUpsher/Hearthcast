import { describe, expect, it } from "vitest";
import { monthRange, presetRange } from "./ranges";

describe("Spending report calendar ranges", () => {
  it.each([
    ["2026-12", "2026-12-01", "2026-12-31"], ["2024-02", "2024-02-01", "2024-02-29"],
    ["2026-02", "2026-02-01", "2026-02-28"], ["2026-04", "2026-04-01", "2026-04-30"],
    ["0099-02", "0099-02-01", "0099-02-28"], ["9999-12", "9999-12-01", "9999-12-31"],
  ])("turns month %s into inclusive calendar dates", (month, from, to) => {
    expect(monthRange(month)).toEqual({ from, to });
  });
  it.each(["", "2026-2", "2026-13", "2026-00", "0000-01", "0001-01", "2026-02-01", "text"])("rejects unsupported month %s", month => {
    expect(monthRange(month)).toBeNull();
  });
  it.each([
    ["thisMonth", "2026-12-12", "2026-12-01", "2026-12-31"], ["lastMonth", "2026-01-12", "2025-12-01", "2025-12-31"],
    ["thisWeek", "2026-12-30", "2026-12-28", "2027-01-03"], ["thisWeek", "2026-12-28", "2026-12-28", "2027-01-03"],
    ["thisWeek", "2027-01-03", "2026-12-28", "2027-01-03"], ["lastWeek", "2027-01-04", "2026-12-28", "2027-01-03"],
    ["thisWeek", "2026-03-29", "2026-03-23", "2026-03-29"], ["thisWeek", "2026-10-25", "2026-10-19", "2026-10-25"],
  ] as const)("resolves %s for local calendar date %s", (preset, today, from, to) => {
    expect(presetRange(preset, today)).toEqual({ from, to });
  });
  it("rejects invalid dates and weeks outside supported DateOnly years", () => {
    expect(presetRange("thisMonth", "2026-02-30")).toBeNull();
    expect(presetRange("thisWeek", "9999-12-31")).toBeNull();
  });
});
