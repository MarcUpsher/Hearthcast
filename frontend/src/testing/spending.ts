import { SpendingItem, SpendingItemDraft, SpendingRecord, SpendingSetupEntry, SpendingUsualPrice } from "../app/spending/models";

export function spendingEntry(kind: "food" | "shop", home = "first"): SpendingSetupEntry {
  return { id: `${home}-${kind}`, householdId: home, name: kind === "food" ? "Food" : "Shop", isRetired: false };
}
export function spendingItem(home = "first", extra: Partial<SpendingItem> = {}): SpendingItem {
  return { id: "eggs", householdId: home, expenseCategoryId: `${home}-food`, code: "EGGS-6", name: "Eggs",
    unitKey: "pack-6", unitLabel: "Six eggs", notes: null, isRetired: false, ...extra };
}
export function spendingPrice(home = "first", extra: Partial<SpendingUsualPrice> = {}): SpendingUsualPrice {
  return { id: "price", householdId: home, spendingItemId: "eggs", merchantId: `${home}-shop`, currencyCode: "GBP",
    unitKey: "pack-6", unitLabel: "Six eggs", usualUnitPrice: 3, updatedAtUtc: "2026-11-18T12:00:00Z", revision: "r1", ...extra };
}
export function spendingDraft(home = "first"): SpendingItemDraft {
  return { code: "MILK-1L", name: "Milk", expenseCategoryId: `${home}-food`, unitKey: "litre", unitLabel: "Litre", notes: "Keep chilled" };
}
export function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason: unknown) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}

export function spendingRecord(home = "first", extra: Partial<SpendingRecord> = {}): SpendingRecord {
  return { id: "record", householdId: home, kind: "Payment", spendingDate: "2026-11-18", merchantId: `${home}-shop`,
    merchantName: "Saved shop", currencyCode: "GBP", currencyMinorUnits: 2, receiptTotal: null, externalReference: null,
    notes: null, revision: "record-r1", isVoided: false, voidedAtUtc: null, totalAmount: 10, countedAmount: 10,
    receiptDifference: null, reconciliationStatus: "NotChecked", lines: [{ id: "saved-line", position: 1,
      expenseCategoryId: `${home}-food`, expenseCategoryName: "Saved food", spendingItemId: null, itemCode: null,
      itemName: null, unitKey: null, unitLabel: null, quantity: null, unitPrice: null, netAmount: 10,
      suggestedAmount: null, effectiveUnitPrice: null, description: null, merchantItemReference: null,
      receiptDescription: null, promotionStatus: "Unknown", notes: null }], ...extra };
}
