export type SpendingSetupKind = "categories" | "merchants";
export type SpendingSetupStatus = "active" | "retired" | "all";

// Spending setup has its own endpoints and identities, separate from asset/debt classification.
export interface SpendingSetupEntry {
  id: string;
  householdId: string;
  name: string;
  isRetired: boolean;
}
