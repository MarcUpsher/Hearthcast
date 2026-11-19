import { decimalAmount, minorAmount } from "../spending/amounts";
import { reportMoney } from "../spending/summary-data";
import { BudgetEntry, BudgetFrequency } from "./models";

// Preserve decimal amounts before JSON.parse converts them to binary floats.
export function parseBudget<T>(json: string): T {
  const quoted = json.replace(/"(?:[^"\\]|\\.)*"|(-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)/g,
    (token: string, number: string | undefined) => number === undefined ? token : JSON.stringify(number));
  return JSON.parse(quoted, (key, value) => key === "intervalCount" || key === "pageSize" ? Number(value) : value);
}
const trimDecimal = (value: string) => value.replace(/\.?0+$/, "") || "0";
export function planAmount(value: string): string | null {
  const scaled = minorAmount(value, 6);
  return scaled === null || scaled >= 1_000_000_000_000_000_000n ? null : trimDecimal(decimalAmount(scaled, 6));
}
// JSON requires numeric money tokens, but Number cannot represent all valid
// six-place .NET decimals. Encode that field from validated, canonical text;
// all ordinary strings still go through JSON.stringify (including notes/IDs).
export function budgetJson(value: unknown, field = ""): string {
  if (field === "plannedAmount" && value !== null) {
    const amount = typeof value === "string" ? planAmount(value) : null;
    if (amount === null) throw new Error("Invalid planned amount.");
    return amount;
  }
  if (Array.isArray(value)) return "[" + value.map(entry => budgetJson(entry)).join(",") + "]";
  if (value !== null && typeof value === "object") return "{" + Object.entries(value)
    .filter(([, item]) => item !== undefined).map(([key, item]) => JSON.stringify(key) + ":" + budgetJson(item, key)).join(",") + "}";
  const json = JSON.stringify(value);
  if (json === undefined) throw new Error("Invalid budget request value.");
  return json;
}
export const planMoney = (value: string, currency: string) => reportMoney(value, currency, 2);
export interface PlanTotal { currency: string; frequency: BudgetFrequency; interval: number; amount: string; unknown: number; }
export function planTotals(entries: Pick<BudgetEntry, "plannedAmount" | "currencyCode" | "frequency" | "intervalCount">[]): PlanTotal[] {
  const groups = new Map<string, { total: bigint; row: PlanTotal }>();
  for (const entry of entries) {
    const key = `${entry.currencyCode}/${entry.frequency}/${entry.intervalCount}`;
    let group = groups.get(key);
    if (!group) {
      group = { total: 0n, row: { currency: entry.currencyCode, frequency: entry.frequency, interval: entry.intervalCount, amount: "0", unknown: 0 } };
      groups.set(key, group);
    }
    if (entry.plannedAmount === null) group.row.unknown++;
    else group.total += minorAmount(entry.plannedAmount, 6)!;
  }
  return [...groups.values()].sort((a, b) => a.row.currency.localeCompare(b.row.currency) || a.row.frequency.localeCompare(b.row.frequency) || a.row.interval - b.row.interval)
    .map(group => ({ ...group.row, amount: trimDecimal(decimalAmount(group.total, 6)) }));
}
