import { countryCurrencies, currentCurrencyCodes } from "./country-currencies";
import { currencySymbol } from "./money";

const countries = new Intl.DisplayNames(["en-GB"], { type: "region" });
const currencies = new Intl.DisplayNames(["en-GB"], { type: "currency" });

export const countryOptions = Object.keys(countryCurrencies)
  .filter((code) => code !== "ZZ")
  .map((id) => ({ id, name: `${countries.of(id) ?? id} (${id})` }))
  .sort((a, b) => a.name.localeCompare(b.name, "en-GB"));

export const currencyOptions = currentCurrencyCodes.map((id) => ({
  id,
  name: `${id} — ${currencies.of(id) ?? id} (${currencySymbol(id)})`,
}));

export function defaultCurrency(country: string): string {
  return countryCurrencies[country] ?? "";
}
