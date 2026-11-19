import { BudgetItem, BudgetItemDraft } from "../app/budgets/models";
export function budgetItem(home = "first", extra: Partial<BudgetItem> = {}): BudgetItem {
  return { id: "internet", householdId: home, expenseCategoryId: `${home}-food`, merchantId: `${home}-shop`,
    kind: "Commitment", code: "INTERNET", name: "Internet", currencyCode: "GBP", frequency: "Month", intervalCount: 1, notes: null, isRetired: false, paymentAnchorDate: null, ...extra };
}
export function budgetDraft(home = "first"): BudgetItemDraft {
  return { code: "ENERGY", name: "Electricity", expenseCategoryId: `${home}-food`, merchantId: `${home}-shop`,
    currencyCode: "GBP", frequency: "Month", intervalCount: "3", notes: "Variable usage", paymentAnchorDate: "" };
}
