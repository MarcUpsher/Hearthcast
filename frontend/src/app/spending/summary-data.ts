import { decimalAmount } from "./amounts";
import { SpendingCategorySummary } from "./models";

// Quote numeric tokens before JSON.parse can convert decimals to binary floats.
// Quoted JSON strings (including escaped quotes and numeric-looking names/IDs)
// are matched as whole tokens and stay untouched. Only integer metadata is then
// converted back to numbers; report money remains exact decimal text.
export function parseSpendingSummary(json: string): SpendingCategorySummary {
  const quoted = json.replace(/"(?:[^"\\]|\\.)*"|(-?(?:0|[1-9]\d*)(?:\.\d+)?(?:[eE][+-]?\d+)?)/g,
    (token: string, number: string | undefined) => number === undefined ? token : JSON.stringify(number));
  const integers = new Set(["currencyMinorUnits", "notChecked", "reconciled", "unreconciled"]);
  return JSON.parse(quoted, (key, value) => integers.has(key) ? Number(value) : value);
}

function normalized(value: string) {
  const match = /^(-?)(\d+)(?:\.(\d+))?(?:[eE]([+-]?\d+))?$/.exec(value);
  if (!match) throw new Error("The report contains an invalid decimal amount.");
  const exponent = Number(match[4] ?? 0);
  // .NET decimal values fit well within this bound; reject unreasonable scales.
  if (Math.abs(exponent) > 100) throw new Error("The report contains an unsupported decimal scale.");
  const fraction = match[3] ?? "", scale = fraction.length - exponent;
  const integer = BigInt(match[1] + match[2] + fraction) * (scale < 0 ? 10n ** BigInt(-scale) : 1n);
  const decimal = decimalAmount(integer, Math.max(0, scale));
  return decimal.includes(".") ? decimal.replace(/0+$/, "").replace(/\.$/, "") : decimal;
}
export function reportDecimalPlaces(value: string) { return normalized(value).split(".")[1]?.length ?? 0; }

export function reportMoney(value: string, currency: string, places: number) {
  const [whole, fraction = ""] = normalized(value).split(".");
  const digits = fraction.padEnd(Math.max(places, fraction.length), "0");
  return currency + " " + whole.replace(/\B(?=(\d{3})+(?!\d))/g, " ") + (digits ? "." + digits : "");
}
