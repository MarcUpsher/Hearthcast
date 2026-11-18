export type BudgetFrequency = "Week" | "Month" | "Year";

// Reusable defaults only. Amounts belong to dated budget plan entries.
export interface BudgetItem {
  id: string; householdId: string; code: string; name: string;
  expenseCategoryId: string; merchantId: string | null;
  currencyCode: string; frequency: BudgetFrequency; intervalCount: number;
  notes: string | null; isRetired: boolean;
}
export interface BudgetItemDraft {
  code: string; name: string; expenseCategoryId: string; merchantId: string;
  currencyCode: string; frequency: BudgetFrequency; intervalCount: string; notes: string;
}
export interface BudgetItemDetails {
  name: string; expenseCategoryId: string; merchantId: string | null;
  currencyCode: string; frequency: BudgetFrequency; intervalCount: number; notes: string | null;
}
export function budgetRecurrence(frequency: BudgetFrequency, count: number) {
  const period = frequency.toLowerCase();
  return count === 1 ? `Every ${period}` : `Every ${count} ${period}s`;
}
