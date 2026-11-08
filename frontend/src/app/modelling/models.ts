import { SavedAsset, SavedItem, Snapshot } from "../core/api";

export interface ModellingSnapshot extends Omit<
  Snapshot,
  "itemSnapshots" | "financedAssetSnapshots"
> {
  itemSnapshots: (SavedItem & { financialType: number })[];
  financedAssetSnapshots: (SavedAsset & { includeInHousehold: boolean })[];
}
export interface Assumptions {
  currencyCode: string;
  durationYears: number | null;
  currencies: CurrencyPot[];
  annualInflationPercent: number | null;
  showInflationAdjustedValues: boolean;
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
  months: ProjectionMonth[] | null;
  issues: {
    kind: string;
    severity: string;
    sourceId: string | null;
    message: string;
  }[];
  warnings: {
    financedAssetId: string;
    kind: string;
    firstOccurrence: string;
  }[];
  coverage: {
    sourceId: string;
    name: string;
    groupName: string;
    currencyCode: string;
    disposition: string;
    value: number | null;
    loanBalance: number | null;
  }[];
}
