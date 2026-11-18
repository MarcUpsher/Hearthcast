// All draft arithmetic uses decimal strings and scaled integers, including midpoint rounding.
function parts(value: string) {
  if (!/^\d+(?:\.\d+)?$/.test(value)) return null;
  const [whole, fraction = ""] = value.split(".");
  return { integer: BigInt(whole + fraction), scale: fraction.length };
}
export function minorAmount(value: string, places: number): bigint | null {
  const parsed = parts(value);
  if (!parsed || parsed.scale > places) return null;
  return parsed.integer * 10n ** BigInt(places - parsed.scale);
}
export function decimalAmount(value: bigint, places: number): string {
  const sign = value < 0n ? "-" : "";
  const digits = String(value < 0n ? -value : value).padStart(places + 1, "0");
  return sign + (places ? digits.slice(0, -places) + "." + digits.slice(-places) : digits);
}
export function suggestedAmount(quantity: string, price: string, places: number): string {
  const q = parts(quantity), p = parts(price);
  if (!q || !p || q.scale > 6 || p.scale > 6) return "";
  const integer = q.integer * p.integer, scale = q.scale + p.scale;
  if (scale <= places) return decimalAmount(integer * 10n ** BigInt(places - scale), places);
  const divisor = 10n ** BigInt(scale - places);
  return decimalAmount((integer + divisor / 2n) / divisor, places);
}
// Prevent silently losing significant decimal digits when encoding the API's numeric money fields.
export function safeAmount(value: string, places: number, limit: number): number | null {
  const minor = minorAmount(value, places), numeric = Number(value);
  if (minor === null || !Number.isFinite(numeric) || numeric >= limit) return null;
  const encoded = minorAmount(String(numeric), places);
  return encoded === minor ? numeric : null;
}
export function validSpendingDate(value: string) {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(value) || value <= "0001-01-01") return false;
  const date = new Date(value + "T00:00:00Z");
  return Number.isFinite(date.getTime()) && date.toISOString().slice(0, 10) === value;
}
export function localToday() {
  const date = new Date();
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}
export function recordedMoney(value: number, currency: string, places: number) {
  return `${currency} ${new Intl.NumberFormat("en-GB", { minimumFractionDigits: places, maximumFractionDigits: places }).format(value).replaceAll(",", " ")}`;
}
const calendarDateFormat = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", year: "numeric", timeZone: "UTC" });
export function spendingDate(value: string) {
  return validSpendingDate(value) ? calendarDateFormat.format(new Date(value + "T00:00:00Z")) : value;
}
