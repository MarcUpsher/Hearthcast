export type BudgetItemKind = "Commitment" | "Allowance";
export type BudgetFrequency = "Week" | "Month" | "Year";

// Reusable defaults only. Amounts belong to dated budget plan entries.
export interface BudgetItem {
  id: string; householdId: string; code: string; name: string; kind: BudgetItemKind;
  expenseCategoryId: string; merchantId: string | null;
  currencyCode: string; frequency: BudgetFrequency; intervalCount: number;
  notes: string | null; isRetired: boolean; paymentAnchorDate: string | null;
}
export interface BudgetItemDraft {
  code: string; name: string; expenseCategoryId: string; merchantId: string;
  currencyCode: string; frequency: BudgetFrequency; intervalCount: string; notes: string; paymentAnchorDate: string;
}
export interface BudgetItemDetails {
  name: string; expenseCategoryId: string; merchantId: string | null;
  currencyCode: string; frequency: BudgetFrequency; intervalCount: number; notes: string | null; paymentAnchorDate: string | null;
}
export function budgetRecurrence(frequency: BudgetFrequency, count: number) {
  const period = frequency.toLowerCase();
  return count === 1 ? `Every ${period}` : `Every ${count} ${period}s`;
}

export interface BudgetEntry {
  id: string; budgetItemId: string; kind: BudgetItemKind; itemCode: string; itemName: string;
  expenseCategoryId: string; expenseCategoryName: string; merchantId: string | null; merchantName: string | null;
  plannedAmount: string | null; currencyCode: string; frequency: BudgetFrequency; intervalCount: number;
  notes: string | null; valueOrigin: "Unrecorded" | "Confirmed" | "CarriedForward";
  lastConfirmedDate: string | null; carriedFromBudgetEntryId: string | null;
}
export interface BudgetPlan {
  id: string; householdId: string; effectiveFrom: string; notes: string | null;
  createdAtUtc: string; updatedAtUtc: string; revision: string; copiedFromBudgetSnapshotId: string | null;
  isComplete: boolean; entries: BudgetEntry[];
}
export interface BudgetHistory { records: BudgetPlan[]; nextBefore: string | null; pageSize: number; }
export interface BudgetEffective { asOf: string; snapshot: BudgetPlan; effectiveUntilExclusive: string | null; }
export interface CurrentOutgoing { item: BudgetItem; entry: BudgetEntry | null; }
export interface CurrentOutgoings {
  asOf: string; snapshotId: string | null; effectiveFrom: string | null;
  effectiveUntilExclusive: string | null; isComplete: boolean | null; items: CurrentOutgoing[];
}
export type BudgetMode = "Carry" | "Confirm" | "Unrecorded";
export interface PlanRow {
  item: BudgetItem; context: BudgetEntry; source: BudgetEntry | null;
  included: boolean; mode: BudgetMode; amount: string; confirmedOn: string;
  refreshFromSetup: boolean;
}
