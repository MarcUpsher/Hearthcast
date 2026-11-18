import { localToday, validSpendingDate } from "./amounts";

export interface SpendingRange { from: string; to: string; }
export type SpendingRangePreset = "thisMonth" | "lastMonth" | "thisWeek" | "lastWeek";
const calendar = (value: string) => new Date(value + "T00:00:00Z");
const iso = (value: Date) => value.toISOString().slice(0, 10);
const validRange = (range: SpendingRange) => validSpendingDate(range.from) && validSpendingDate(range.to) ? range : null;

export function monthRange(month: string): SpendingRange | null {
  if (!/^\d{4}-(0[1-9]|1[0-2])$/.test(month) || !validSpendingDate(month + "-01")) return null;
  const end = calendar(month + "-01");
  end.setUTCMonth(end.getUTCMonth() + 1); end.setUTCDate(0);
  return validRange({ from: month + "-01", to: iso(end) });
}

// Start with the user's local calendar date, then use UTC calendar arithmetic so
// month lengths and Monday–Sunday weeks do not shift at daylight-saving changes.
export function presetRange(preset: SpendingRangePreset, today = localToday()): SpendingRange | null {
  if (!validSpendingDate(today)) return null;
  const start = calendar(today);
  if (preset === "thisMonth" || preset === "lastMonth") {
    start.setUTCDate(1);
    if (preset === "lastMonth") start.setUTCMonth(start.getUTCMonth() - 1);
    return monthRange(iso(start).slice(0, 7));
  }
  start.setUTCDate(start.getUTCDate() - (start.getUTCDay() + 6) % 7 - (preset === "lastWeek" ? 7 : 0));
  const end = new Date(start); end.setUTCDate(end.getUTCDate() + 6);
  return validRange({ from: iso(start), to: iso(end) });
}
