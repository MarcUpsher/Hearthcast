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
  financialType: "Asset" | "Debt";
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
