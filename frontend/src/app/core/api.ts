import { inject, Injectable, signal } from "@angular/core";
import { HttpClient, HttpErrorResponse } from "@angular/common/http";
import { firstValueFrom } from "rxjs";
export interface Person {
  id: string;
  displayName: string;
  participatesInHouseholdFinances: boolean;
}
export interface Household {
  id: string;
  name: string;
  people: Person[];
}
export interface Share {
  personId: string;
  shareBasisPoints: number;
}
export interface Category {
  id: string;
  name: string;
  financialType: string;
}
export interface Group {
  id: string;
  categoryId: string;
  name: string;
}
export interface Item {
  id: string;
  name: string;
  institutionName?: string | null;
  fundName?: string | null;
  groupId: string;
  holderPersonId: string;
  currencyCode: string;
  countryCode: string;
  includeInHousehold: boolean;
  isRetired: boolean;
  allocations: Share[];
}
export interface Asset {
  id: string;
  name: string;
  groupId: string;
  currencyCode: string;
  countryCode: string;
  isRetired: boolean;
  includeInHousehold: boolean;
  owners: Share[];
  householdAllocations: Share[];
  loanHolderPersonIds: string[];
  originalLoanAmount: number;
  hasWithdrawalFacility: boolean;
  purchaseDate: string | null;
  purchasePrice: number | null;
}
export interface Inventory {
  id: string;
  snapshotDate: string;
  datePrecision: number;
  revision: string;
}
export interface ItemActivity {
  trackedItemId: string;
  lastIncludedDate: string;
  lastConfirmedDate: string | null;
  lastConfirmedPrecision: number | null;
  presentInLatestSnapshot: boolean;
  latestValueOrigin: number | null;
}
export interface AssetActivity {
  financedAssetId: string;
  lastIncludedDate: string;
  presentInLatestSnapshot: boolean;
  hasCarriedValuesInLatestSnapshot: boolean;
  balances: {
    balance: number;
    lastConfirmedDate: string | null;
    lastConfirmedPrecision: number | null;
  }[];
}
export interface SnapshotActivity {
  latestSnapshot: Inventory | null;
  items: ItemActivity[];
  financedAssets: AssetActivity[];
}
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
  balance: { amount: number | null; valueOrigin: number };
}
export interface ReportAsset {
  financedAssetSnapshotId: string;
  assetName: string;
  currentEquity: number | null;
  currentValuation: { valueOrigin: number };
  outstandingLoanBalance: { valueOrigin: number };
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
    financialType?: number;
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
export interface SavedItem {
  trackedItemId: string;
  itemName: string;
  institutionName?: string | null;
  fundName?: string | null;
  groupName: string;
  groupId: string;
  categoryName: string;
  categoryId: string;
  currencyCode: string;
  amount: number | null;
  valueOrigin: number;
  lastConfirmedDate: string | null;
  holderSnapshotPersonId: string;
  includeInHousehold: boolean;
}
export interface SavedAsset {
  financedAssetId: string;
  assetName: string;
  categoryId: string;
  categoryName: string;
  groupId: string;
  groupName: string;
  owners: { snapshotPersonId: string; shareBasisPoints: number }[];
  currencyCode: string;
  currentValuation: number | null;
  outstandingLoanBalance: number | null;
  availableToWithdraw: number | null;
  hasWithdrawalFacility: boolean;
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
    financialType: number;
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
export interface Snapshot {
  exchangeRateLookupDate?: string | null;
  exchangeRateBaseCurrency?: string | null;
  exchangeRateStatus?: "NotCaptured" | "NotRequired" | "Pending" | "Ready";
  exchangeRates?: {
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
  datePrecision: number;
  people: { id: string; personId: string; displayName: string }[];
  itemSnapshots: SavedItem[];
  financedAssetSnapshots: SavedAsset[];
}
@Injectable({ providedIn: "root" })
export class Api {
  private http = inject(HttpClient);
  get<T>(path: string) {
    return firstValueFrom(this.http.get<T>("/api/" + path));
  }
  send<T>(method: string, path: string, body: unknown) {
    return firstValueFrom(
      this.http.request<T>(method, "/api/" + path, { body }),
    );
  }
}
@Injectable({ providedIn: "root" })
export class Workspace {
  api = inject(Api);
  households = signal<Household[]>([]);
  current = signal<Household | null>(null);
  error = signal("");
  async load(id?: string) {
    try {
      const rows = await this.api.get<Household[]>("households");
      this.households.set(rows);
      this.current.set(
        rows.find(
          (h) =>
            h.id ===
            (id ?? this.current()?.id ?? localStorage.getItem("household")),
        ) ??
          rows[0] ??
          null,
      );
      this.error.set("");
    } catch (e) {
      this.error.set(message(e));
    }
  }
  select(id: string) {
    this.current.set(this.households().find((h) => h.id === id) ?? null);
    localStorage.setItem("household", id);
  }
  path(suffix = "") {
    return "households/" + this.current()!.id + (suffix ? "/" + suffix : "");
  }
}
export function message(e: unknown) {
  if (e instanceof HttpErrorResponse) {
    if (e.status === 0)
      return "Cannot reach the API. Check that Hearthcast is running, then retry.";
    return (
      [e.error?.detail, ...Object.values(e.error?.errors ?? {}).flat()]
        .filter(Boolean)
        .join("\n") ||
      e.error?.title ||
      "The request failed."
    );
  }
  return e instanceof Error ? e.message : "The request failed.";
}
