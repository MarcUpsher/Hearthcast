import {
  DatePrecision,
  FinancialTypeCode,
  SnapshotValueOrigin,
  FinancedAssetBalanceKind,
} from "../shared/finance-types";

export interface Inventory {
  id: string;
  snapshotDate: string;
  datePrecision: DatePrecision;
  revision: string;
}

export interface ItemActivity {
  trackedItemId: string;
  lastIncludedDate: string;
  lastConfirmedDate: string | null;
  lastConfirmedPrecision: DatePrecision | null;
  presentInLatestSnapshot: boolean;
  latestValueOrigin: SnapshotValueOrigin | null;
}

export interface AssetActivity {
  financedAssetId: string;
  lastIncludedDate: string;
  presentInLatestSnapshot: boolean;
  hasCarriedValuesInLatestSnapshot: boolean;
  balances: {
    balance: FinancedAssetBalanceKind;
    lastConfirmedDate: string | null;
    lastConfirmedPrecision: DatePrecision | null;
  }[];
}

export interface SnapshotActivity {
  latestSnapshot: Inventory | null;
  items: ItemActivity[];
  financedAssets: AssetActivity[];
}

export interface SnapshotShare {
  snapshotPersonId: string;
  shareBasisPoints: number;
}
export interface SavedItem {
  id: string;
  householdId: string;
  snapshotId: string;
  financialType: FinancialTypeCode;
  countryCode: string;
  lastConfirmedPrecision: DatePrecision | null;
  carriedFromItemSnapshotId: string | null;
  householdAllocations: SnapshotShare[];
  trackedItemId: string;
  itemName: string;
  institutionName: string | null;
  fundName: string | null;
  groupName: string;
  groupId: string;
  categoryName: string;
  categoryId: string;
  currencyCode: string;
  amount: number | null;
  valueOrigin: SnapshotValueOrigin;
  lastConfirmedDate: string | null;
  holderSnapshotPersonId: string;
  includeInHousehold: boolean;
}

export interface SavedAsset {
  id: string;
  householdId: string;
  snapshotId: string;
  financialType: FinancialTypeCode;
  countryCode: string;
  purchaseDate: string | null;
  purchasePrice: number | null;
  originalLoanAmount: number;
  includeInHousehold: boolean;
  currentValuationValueOrigin: SnapshotValueOrigin;
  currentValuationLastConfirmedDate: string | null;
  currentValuationLastConfirmedPrecision: DatePrecision | null;
  currentValuationCarriedFromSnapshotId: string | null;
  outstandingLoanBalanceValueOrigin: SnapshotValueOrigin;
  outstandingLoanBalanceLastConfirmedDate: string | null;
  outstandingLoanBalanceLastConfirmedPrecision: DatePrecision | null;
  outstandingLoanBalanceCarriedFromSnapshotId: string | null;
  availableToWithdrawValueOrigin: SnapshotValueOrigin;
  availableToWithdrawLastConfirmedDate: string | null;
  availableToWithdrawLastConfirmedPrecision: DatePrecision | null;
  availableToWithdrawCarriedFromSnapshotId: string | null;
  loanHolderSnapshotPersonIds: string[];
  householdAllocations: SnapshotShare[];
  financedAssetId: string;
  assetName: string;
  categoryId: string;
  categoryName: string;
  groupId: string;
  groupName: string;
  owners: SnapshotShare[];
  currencyCode: string;
  currentValuation: number | null;
  outstandingLoanBalance: number | null;
  availableToWithdraw: number | null;
  hasWithdrawalFacility: boolean;
}

export interface SnapshotPerson {
  id: string;
  householdId: string;
  snapshotId: string;
  personId: string;
  displayName: string;
  participatesInHouseholdFinances: boolean;
}
export interface Snapshot {
  householdId: string;
  householdName: string;
  createdAtUtc: string;
  updatedAtUtc: string;
  exchangeRateLookupDate: string | null;
  exchangeRateBaseCurrency: string | null;
  exchangeRateStatus: "NotCaptured" | "NotRequired" | "Pending" | "Ready";
  exchangeRates: {
    baseCurrency: string;
    quoteCurrency: string;
    rate: number;
    effectiveDate: string;
    source: string;
    retrievedAtUtc: string;
  }[];
  id: string;
  revision: string;
  snapshotDate: string;
  datePrecision: DatePrecision;
  people: SnapshotPerson[];
  itemSnapshots: SavedItem[];
  financedAssetSnapshots: SavedAsset[];
}
