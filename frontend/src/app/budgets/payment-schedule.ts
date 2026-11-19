import { validSpendingDate } from "../spending/amounts";
import { BudgetFrequency } from "./models";

export function validPaymentAnchor(value: string) {
  return !value || (validSpendingDate(value) && value < "9999-12-31");
}
const calendar = (value: string) => new Date(value + "T00:00:00Z");
const maxDay = calendar("9999-12-30").getTime() / 86400000;
function monthDate(index: number, day: number): string | null {
  const year = Math.floor(index / 12), month = index % 12 + 1;
  if (year < 1 || year > 9999) return null;
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const last = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31][month - 1];
  const result = `${String(year).padStart(4, "0")}-${String(month).padStart(2, "0")}-${String(Math.min(day, last)).padStart(2, "0")}`;
  return validPaymentAnchor(result) ? result : null;
}

// Next occurrence on or after the explicit local calendar date. Every candidate
// is derived from the original anchor, so February clipping never shifts March.
export function nextPaymentDate(anchor: string | null, frequency: BudgetFrequency, interval: number, asOf: string): string | null {
  if (!anchor || !validPaymentAnchor(anchor) || !validPaymentAnchor(asOf) || !asOf || !Number.isInteger(interval) || interval < 1 || interval > 2147483647 || !["Week", "Month", "Year"].includes(frequency)) return null;
  if (anchor >= asOf) return anchor;
  const origin = calendar(anchor), today = calendar(asOf);
  if (frequency === "Week") {
    const originDay = origin.getTime() / 86400000, difference = today.getTime() / 86400000 - originDay, step = interval * 7;
    const candidate = originDay + Math.ceil(difference / step) * step;
    return candidate > maxDay ? null : new Date(candidate * 86400000).toISOString().slice(0, 10);
  }
  const month = origin.getUTCFullYear() * 12 + origin.getUTCMonth(), step = interval * (frequency === "Year" ? 12 : 1);
  const elapsed = today.getUTCFullYear() * 12 + today.getUTCMonth() - month;
  const cycles = Math.floor(elapsed / step);
  const candidate = monthDate(month + cycles * step, origin.getUTCDate());
  return candidate && candidate >= asOf ? candidate : monthDate(month + (cycles + 1) * step, origin.getUTCDate());
}

const weekdays = new Intl.DateTimeFormat("en-GB", { weekday: "long", timeZone: "UTC" });
const yearly = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "long", timeZone: "UTC" });
export function paymentTiming(anchor: string, frequency: BudgetFrequency) {
  if (!anchor || !validPaymentAnchor(anchor)) return "";
  const date = calendar(anchor), day = date.getUTCDate();
  if (frequency === "Week") return weekdays.format(date);
  const suffix = day % 100 >= 11 && day % 100 <= 13 ? "th" : ({ 1: "st", 2: "nd", 3: "rd" }[day % 10] ?? "th");
  const label = frequency === "Month" ? `On the ${day}${suffix}` : yearly.format(date);
  const clipped = frequency === "Month" ? day >= 29 : date.getUTCMonth() === 1 && day === 29;
  return clipped ? label + " (last day in shorter months)" : label;
}
