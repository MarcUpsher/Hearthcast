import { ProjectionRun } from "../app/modelling/models";

export const report = (value = 100): ProjectionRun => {
  const inflation = (month: number) => Math.pow(1.03, month / 12);
  const nominalGrowth = {
    startingNetPosition: value + 510,
    investmentReturns: 12,
    savingsInterest: 0,
    financedAssetValueChange: 0,
    investmentContributions: 0,
    savingsContributions: 0,
    loanBalanceReduction: 12,
    reinvestedLoanPayments: 0,
    inflationEffect: 0,
    finalNetPosition: value + 534,
  };
  const realChange = Array.from(
    { length: 12 },
    (_, i) => 1 / inflation(i + 1),
  ).reduce((sum, n) => sum + n, 0);
  return {
    snapshotId: "latest",
    snapshotRevision: "r1",
    currencyCode: "GBP",
    canCalculate: true,
    growthBreakdown: {
      nominal: nominalGrowth,
      inflationAdjusted: {
        ...nominalGrowth,
        investmentReturns: realChange,
        loanBalanceReduction: realChange,
        inflationEffect:
          (value + 534) / inflation(12) - (value + 510) - realChange * 2,
        finalNetPosition: (value + 534) / inflation(12),
      },
    },
    months: Array.from({ length: 13 }, (_, i) => ({
      month: i,
      date: `${2026 + Math.floor(i / 12)}-${String((i % 12) + 1).padStart(2, "0")}-01`,
      nominal: {
        investments: value + i,
        savings: 10,
        financedAssetValue: 1000,
        loanBalance: 500 - i,
        equity: 500 + i,
        netPosition: value + 510 + i * 2,
      },
      inflationAdjusted: {
        investments: (value + i) / inflation(i),
        savings: 10 / inflation(i),
        financedAssetValue: 1000 / inflation(i),
        loanBalance: (500 - i) / inflation(i),
        equity: (500 + i) / inflation(i),
        netPosition: (value + 510 + i * 2) / inflation(i),
      },
      redirectedToInvestments: 0,
      financedAssets: [],
    })),
    issues: [],
    warnings: [],
    coverage: [],
  };
};
export const payoffReport = (events: { id: string; month: number | null }[]) => {
  const result = report();
  for (const row of result.months!)
    row.financedAssets = events.map((event) => ({
      financedAssetId: event.id,
      currencyCode: "ZAR",
      payoffDate:
        event.month !== null && row.month >= event.month
          ? result.months![event.month].date
          : null,
    }));
  return result;
};
export const expenseReport = (
  capital = 106,
  includeSavings = false,
): ProjectionRun => {
  const result = report();
  for (const row of result.months!) {
    const inflation = Math.pow(1.03, row.month / 12);
    const eligible =
      row.nominal.investments + (includeSavings ? row.nominal.savings : 0);
    row.expenseTarget = {
      nominalRequiredCapital: capital * inflation,
      inflationAdjustedRequiredCapital: capital,
      nominalEligibleBalance: eligible,
      inflationAdjustedEligibleBalance: eligible / inflation,
      isCovered:
        Math.round(eligible * 100) >= Math.round(capital * inflation * 100),
    };
  }
  const first = result.months!.find((m) => m.expenseTarget!.isCovered);
  result.expenseTarget = {
    expenseCurrencyCode: "GBP",
    annualExpenses: capital * 0.04,
    annualWithdrawalRatePercent: 4,
    includeSavings,
    requiredCapitalInExpenseCurrency: capital,
    firstReachedDate: first?.date ?? null,
    alreadyReachedAtStart: first?.month === 0,
  };
  return result;
};
