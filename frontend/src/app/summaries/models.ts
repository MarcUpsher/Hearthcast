import {
  FinancialTypeCode,
  SnapshotValueOrigin,
} from "../shared/finance-types";

export interface Totals {
  knownNetPosition: number;
  ordinaryAssets: {
    knownSubtotal: number;
    missingEntryCount: number;
    carriedForwardEntryCount: number;
  };
  ordinaryDebts: {
    knownSubtotal: number;
    missingEntryCount: number;
    carriedForwardEntryCount: number;
  };
  financedAssetEquity: {
    knownSubtotal: number;
    missingEntryCount: number;
    carriedForwardEntryCount: number;
  };
}

export interface ReportItem {
  itemSnapshotId: string;
  itemName: string;
  holderSnapshotPersonId: string;
  institutionName?: string | null;
  fundName?: string | null;
  balance: { amount: number | null; valueOrigin: SnapshotValueOrigin };
}

export interface ReportAsset {
  financedAssetSnapshotId: string;
  assetName: string;
  currentEquity: number | null;
  currentValuation: { valueOrigin: SnapshotValueOrigin };
  outstandingLoanBalance: { valueOrigin: SnapshotValueOrigin };
}

export interface ReportGroup {
  groupId: string;
  groupName: string;
  totals: Totals;
  holdingsTotals: Totals;
  allRecordedTotals: Totals;
  items?: (
    | ReportItem
    | {
        item: ReportItem;
        allocationShareBasisPoints: number;
        contributingAmount: number | null;
      }
  )[];
  financedAssets?: (
    | ReportAsset
    | {
        asset: ReportAsset;
        attributedEquity: number | null;
        allocationShareBasisPoints?: number;
        ownershipShareBasisPoints?: number;
      }
  )[];
}

export interface ReportCurrency {
  currencyCode: string;
  totals: Totals;
  holdingsTotals: Totals;
  allRecordedTotals: Totals;
  categories: {
    categoryId: string;
    categoryName: string;
    financialType?: FinancialTypeCode;
    totals: Totals;
    holdingsTotals: Totals;
    allRecordedTotals: Totals;
    groups: ReportGroup[];
  }[];
}

export interface Report {
  revision?: string;
  currencies: ReportCurrency[];
  people: {
    person: { personId: string; displayName: string };
    currencies: ReportCurrency[];
  }[];
}

export interface MergedAmount {
  currencyCode: string;
  knownSubtotal: number | null;
  missingValueCount: number;
  missingRateCount: number;
  carriedEntryCount: number;
  isComplete: boolean;
}

export interface MergedEntry {
  kind: 1 | 2;
  key: string;
  name: string;
  description: string;
  sourceCurrency: string;
  sourceAmount: number | null;
  status: string;
  institutionName: string | null;
  fundName: string | null;
  amounts: MergedAmount[];
}

export interface MergedView {
  amounts: MergedAmount[];
  categories: {
    categoryId: string;
    categoryName: string;
    financialType: FinancialTypeCode;
    amounts: MergedAmount[];
    groups: {
      groupId: string;
      groupName: string;
      amounts: MergedAmount[];
      entries: MergedEntry[];
    }[];
  }[];
}

export interface MergedReport {
  snapshotId: string;
  revision: string;
  currencies: string[];
  household: MergedView;
  householdPeople: { personId: string; view: MergedView }[];
  individualPeople: { personId: string; view: MergedView }[];
  allRecorded: MergedView;
}
