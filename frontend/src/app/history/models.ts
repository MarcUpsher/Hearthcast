import { FinancialTypeCode } from "../shared/finance-types";

export interface HistoryAmount {
  currencyCode: string;
  knownSubtotal: number | null;
  missingValueCount: number;
  missingRateCount: number;
  carriedEntryCount: number;
  isComplete: boolean;
}

export interface HistoryPoint {
  snapshotId: string;
  snapshotDate: string;
  revision: string;
  netPosition: HistoryAmount;
  financedEquity: HistoryAmount;
  categories: {
    categoryId: string;
    categoryName: string;
    financialType: FinancialTypeCode;
    amount: HistoryAmount;
  }[];
  people: {
    personId: string;
    displayName: string;
    categories: {
      categoryId: string;
      categoryName: string;
      financialType: FinancialTypeCode;
      amount: HistoryAmount;
    }[];
  }[];
}

export interface HistorySeries {
  from: string;
  to: string;
  intervalMonths: number;
  currencyCode: string;
  availableCurrencies: string[];
  points: HistoryPoint[];
}
