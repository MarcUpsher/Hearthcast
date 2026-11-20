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

export interface SpendingCurrency { code: string; minorUnits: number; }
export type SpendingPromotion = "Unknown" | "Yes" | "No";
export interface SpendingDiscountInput { expenseCategoryId: string; amount: number; description?: string | null; }
export interface SpendingRecordedDiscount extends SpendingDiscountInput { id: string; position: number; expenseCategoryName: string; }
export interface SpendingLastPrice {
  spendingRecordId: string; spendingLineId: string; spendingDate: string; currencyCode: string; unitKey: string; unitLabel: string;
  quantity: number; unitPrice: number; netAmount: number; effectiveUnitPrice: number; promotionStatus: SpendingPromotion; hasReceiptDiscount: boolean;
}
export interface SpendingCaptureLine {
  isAdditionalCost?: boolean;
  expenseCategoryId: string;
  netAmount: number;
  spendingItemId?: string;
  quantity?: number;
  unitPrice?: number;
  description?: string | null;
  merchantItemReference?: string | null;
  promotionStatus: SpendingPromotion;
  unitKey?: string | null;
  unitLabel?: string | null;
  receiptDescription?: string | null;
  notes?: string | null;
}
export interface SpendingCaptureRequest {
  id: string;
  merchantId: string;
  spendingDate: string;
  currencyCode: string;
  kind: "Payment";
  lines: SpendingCaptureLine[];
  discounts?: SpendingDiscountInput[];
  receiptTotal: number | null;
  externalReference: string | null;
  notes: string | null;
}
export interface SpendingReplacementRequest {
  expectedRevision: string;
  merchantId: string;
  spendingDate: string;
  lines: SpendingCaptureLine[];
  discounts?: SpendingDiscountInput[];
  receiptTotal: number | null;
  externalReference: string | null;
  notes: string | null;
}
export interface SpendingRecordedLine {
  isAdditionalCost?: boolean;
  id: string; position: number; expenseCategoryId: string; expenseCategoryName: string;
  spendingItemId: string | null; itemCode: string | null; itemName: string | null;
  unitKey: string | null; unitLabel: string | null; quantity: number | null; unitPrice: number | null;
  netAmount: number; suggestedAmount: number | null; effectiveUnitPrice: number | null;
  description: string | null; merchantItemReference: string | null; receiptDescription: string | null;
  promotionStatus: SpendingPromotion; notes: string | null;
}
export interface SpendingRecord {
  id: string; householdId: string; kind: "Payment" | "Refund"; spendingDate: string;
  merchantId: string; merchantName: string; currencyCode: string; currencyMinorUnits: number;
  receiptTotal: number | null; externalReference: string | null; notes: string | null;
  revision: string; isVoided: boolean; voidedAtUtc: string | null;
  totalAmount: number; countedAmount: number; receiptDifference: number | null;
  reconciliationStatus: "NotChecked" | "Reconciled" | "Unreconciled";
  lines: SpendingRecordedLine[];
  discounts?: SpendingRecordedDiscount[]; grossAmount?: number; discountAmount?: number;
  purchaseAmount?: number; additionalCostAmount?: number;
}
export interface SpendingHistoryPage { records: SpendingRecord[]; nextCursor: string | null; pageSize: number; }

export interface SpendingCategoryTotal {
  expenseCategoryId: string; categoryName: string; currencyCode: string; currencyMinorUnits: number; netAmount: string;
}
export interface SpendingReceiptWarning {
  spendingRecordId: string; spendingDate: string; merchantName: string; currencyCode: string;
  receiptTotal: string; lineTotal: string; difference: string;
}
export interface SpendingCategorySummary {
  householdId: string; from: string; to: string; categories: SpendingCategoryTotal[];
  reconciliationRecordCounts: { notChecked: number; reconciled: number; unreconciled: number };
  warnings: SpendingReceiptWarning[];
}
