import { minorAmount, decimalAmount } from "../spending/amounts";
import { SpendingCurrency } from "../spending/models";
import { BudgetEntry } from "./models";

type Entry = Pick<BudgetEntry, "kind" | "currencyCode" | "frequency" | "intervalCount" | "plannedAmount">;
interface Fraction { numerator: bigint; denominator: bigint; }
interface Bucket { value: Fraction; known: number; unknown: number; }
export interface MonthlyAmount { amount: string | null; known: number; unknown: number; }
export interface MonthlyAverage {
  currency: string; places: number; precisionFallback: boolean;
  commitments: MonthlyAmount; allowances: MonthlyAmount; combined: MonthlyAmount; roundingDifference: boolean;
}
const bucket = (): Bucket => ({ value: { numerator: 0n, denominator: 1n }, known: 0, unknown: 0 });
function gcd(a: bigint, b: bigint): bigint { while (b) [a, b] = [b, a % b]; return a; }
function add(a: Fraction, b: Fraction): Fraction {
  const common = gcd(a.denominator, b.denominator);
  const denominator = a.denominator / common * b.denominator;
  const numerator = a.numerator * (b.denominator / common) + b.numerator * (a.denominator / common);
  const divisor = gcd(numerator, denominator);
  return { numerator: numerator / divisor, denominator: denominator / divisor };
}
function display(value: Bucket, places: number): MonthlyAmount {
  // Values are exact fractions of millionths. Round positive midpoint ties away
  // from zero once, at the requested display precision, after summing the bucket.
  const divisor = value.value.denominator * 10n ** BigInt(6 - places);
  const whole = value.value.numerator / divisor, remainder = value.value.numerator % divisor;
  const rounded = whole + (remainder * 2n >= divisor ? 1n : 0n);
  return { amount: value.unknown && !value.known ? null : decimalAmount(rounded, places), known: value.known, unknown: value.unknown };
}

// A run rate from ONE effective plan. Dates/payment anchors never enter this
// calculation: weekly uses the explicit 52-week convention, monthly/yearly use
// 12 months per year. Fractions retain repeating decimals through aggregation.
export function monthlyAverages(entries: Entry[], currencies: SpendingCurrency[] | null): MonthlyAverage[] {
  const groups = new Map<string, { commitments: Bucket; allowances: Bucket }>();
  for (const entry of entries) {
    if (!Number.isInteger(entry.intervalCount) || entry.intervalCount < 1 || entry.intervalCount > 2147483647 ||
      !["Week", "Month", "Year"].includes(entry.frequency) || !["Commitment", "Allowance"].includes(entry.kind))
      throw new Error("The plan contains an unsupported recurrence or budget kind.");
    if (!groups.has(entry.currencyCode)) groups.set(entry.currencyCode, { commitments: bucket(), allowances: bucket() });
    const group = groups.get(entry.currencyCode)!, target = entry.kind === "Allowance" ? group.allowances : group.commitments;
    if (entry.plannedAmount === null) { target.unknown++; continue; }
    const amount = minorAmount(entry.plannedAmount, 6);
    if (amount === null) throw new Error("The plan contains an invalid amount.");
    const interval = BigInt(entry.intervalCount);
    const fraction = { numerator: amount * (entry.frequency === "Week" ? 52n : 1n),
      denominator: interval * (entry.frequency === "Month" ? 1n : 12n) };
    target.value = add(target.value, fraction); target.known++;
  }
  return [...groups].sort(([a], [b]) => a.localeCompare(b)).map(([currency, group]) => {
    const precision = currencies?.find(row => row.code === currency)?.minorUnits;
    const precisionFallback = precision === undefined || !Number.isInteger(precision) || precision < 0 || precision > 6;
    const places = precisionFallback ? 6 : precision!;
    const commitments = display(group.commitments, places), allowances = display(group.allowances, places);
    const combined = display({ value: add(group.commitments.value, group.allowances.value),
      known: commitments.known + allowances.known, unknown: commitments.unknown + allowances.unknown }, places);
    const roundingDifference = minorAmount(commitments.amount ?? "0", places)! + minorAmount(allowances.amount ?? "0", places)! !== minorAmount(combined.amount ?? "0", places)!;
    return { currency, places, precisionFallback, commitments, allowances, combined, roundingDifference };
  });
}
