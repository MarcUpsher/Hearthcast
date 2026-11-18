export type SpendingSetupKind = "categories" | "merchants";
export type SpendingSetupStatus = "active" | "retired" | "all";

// Spending setup has its own endpoints and identities, separate from asset/debt classification.
export interface SpendingSetupEntry {
  id: string;
  householdId: string;
  name: string;
  isRetired: boolean;
}

export interface SpendingItem extends SpendingSetupEntry {
  expenseCategoryId: string;
  code: string;
  unitKey: string;
  unitLabel: string;
  notes: string | null;
}

export interface SpendingUsualPrice {
  id: string;
  householdId: string;
  spendingItemId: string;
  merchantId: string;
  currencyCode: string;
  unitKey: string;
  unitLabel: string;
  usualUnitPrice: number;
  updatedAtUtc: string;
  revision: string;
}

export interface SpendingItemDraft {
  code: string;
  name: string;
  expenseCategoryId: string;
  unitKey: string;
  unitLabel: string;
  notes: string;
}
