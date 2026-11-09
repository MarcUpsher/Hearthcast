export type ProjectionIssueKind =
  | "MissingGroup"
  | "NoHouseholdItems"
  | "DebtGroupSelected"
  | "MissingFinancedAsset"
  | "ExcludedFinancedAsset"
  | "MissingValue"
  | "MissingExchangeRate"
  | "CarriedValue"
  | "MissingCurrencySettings";
export type ProjectionDisposition =
  | "Investment"
  | "Savings"
  | "FinancedAsset"
  | "NotSelected"
  | "ExcludedFromHousehold"
  | "InvalidSelection";

export interface Assumptions {
  currencyCode: string;
  durationYears: number | null;
  currencies: CurrencyPot[];
  annualInflationPercent: number | null;
  showInflationAdjustedValues: boolean;
  expenseTarget?: ExpenseTargetSettings | null;
}
export interface ExpenseTargetSettings {
  annualExpenses: number | null;
  currencyCode: string;
  annualWithdrawalRatePercent: number | null;
  includeSavings: boolean;
}
export interface ExpenseTargetMonth {
  nominalRequiredCapital: number;
  inflationAdjustedRequiredCapital: number;
  nominalEligibleBalance: number;
  inflationAdjustedEligibleBalance: number;
  isCovered: boolean;
}
export interface ExpenseTargetSummary {
  expenseCurrencyCode: string;
  annualExpenses: number;
  annualWithdrawalRatePercent: number;
  includeSavings: boolean;
  requiredCapitalInExpenseCurrency: number;
  firstReachedDate: string | null;
  alreadyReachedAtStart: boolean;
}
export interface CurrencyPot {
  currencyCode: string;
  monthlyInvestmentContribution: number | null;
  annualInvestmentReturnPercent: number | null;
  monthlySavingsContribution: number | null;
  annualSavingsInterestPercent: number | null;
}
export interface LoanSettings {
  financedAssetId: string;
  monthlyLoanPaymentInAssetCurrency: number | null;
  extraMonthlyLoanPaymentInAssetCurrency: number | null;
  investmentCurrencyAfterPayoff: string;
  annualLoanInterestPercent: number | null;
  annualAssetAppreciationPercent: number | null;
}
export interface ProjectionInputs {
  assumptions: Assumptions;
  groups: { groupId: string; bucket: "Investment" | "Savings" }[];
  financedAssets: LoanSettings[];
}
export interface ProjectionPreset {
  id: string;
  householdId: string;
  name: string;
  isDefault: boolean;
  inputs: ProjectionInputs;
}
export interface Balances {
  investments: number;
  savings: number;
  financedAssetValue: number;
  loanBalance: number;
  equity: number;
  netPosition: number;
}
export interface ProjectionMonth {
  month: number;
  date: string;
  nominal: Balances;
  inflationAdjusted: Balances;
  redirectedToInvestments: number;
  expenseTarget?: ExpenseTargetMonth | null;
  financedAssets: {
    financedAssetId: string;
    currencyCode: string;
    payoffDate: string | null;
  }[];
}
export interface ProjectionRun {
  snapshotId: string;
  snapshotRevision: string;
  currencyCode: string;
  canCalculate: boolean;
  expenseTarget?: ExpenseTargetSummary | null;
  growthBreakdown: {
    nominal: ProjectionGrowthBreakdown;
    inflationAdjusted: ProjectionGrowthBreakdown;
  } | null;
  months: ProjectionMonth[] | null;
  issues: {
    kind: ProjectionIssueKind;
    severity: "Warning" | "Error";
    sourceId: string | null;
    message: string;
  }[];
  warnings: {
    financedAssetId: string;
    kind: "PaymentDoesNotReduceLoanBalance";
    firstOccurrence: string;
  }[];
  coverage: {
    sourceId: string;
    name: string;
    groupName: string;
    currencyCode: string;
    disposition: ProjectionDisposition;
    value: number | null;
    loanBalance: number | null;
  }[];
}
export interface ProjectionGrowthBreakdown {
  startingNetPosition: number;
  investmentReturns: number;
  savingsInterest: number;
  financedAssetValueChange: number;
  investmentContributions: number;
  savingsContributions: number;
  loanBalanceReduction: number;
  reinvestedLoanPayments: number;
  inflationEffect: number;
  finalNetPosition: number;
}
