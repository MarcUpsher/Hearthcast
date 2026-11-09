import { Component, computed, input } from "@angular/core";
import { money } from "../../core/money";
import { ProjectionRun } from "../models";
const growthComponents = [
  { key: "investmentReturns", label: "Investment returns" },
  { key: "savingsInterest", label: "Savings interest" },
  { key: "financedAssetValueChange", label: "Financed asset value change" },
  { key: "investmentContributions", label: "Investment contributions" },
  { key: "savingsContributions", label: "Savings contributions" },
  { key: "loanBalanceReduction", label: "Loan balance reduction" },
  { key: "reinvestedLoanPayments", label: "Reinvested loan payments" },
  { key: "inflationEffect", label: "Inflation effect" },
] as const;

@Component({
  selector: "app-growth-breakdown",
  templateUrl: "growth-breakdown.html",
  styleUrl: "growth-breakdown.scss",
})
export class GrowthBreakdown {
  result = input.required<ProjectionRun>();
  adjusted = input(false);
  money = money;
  growth = computed(() => {
    const report = this.result();
    if (
      !report?.canCalculate ||
      !report.months?.length ||
      !report.growthBreakdown
    )
      return null;
    const real = this.adjusted();
    const breakdown = real
      ? report.growthBreakdown.inflationAdjusted
      : report.growthBreakdown.nominal;
    return {
      starting: breakdown.startingNetPosition,
      final: breakdown.finalNetPosition,
      startDate: report.months[0].date,
      endDate: report.months.at(-1)!.date,
      rows: growthComponents
        .filter((row) => real || row.key !== "inflationEffect")
        .map((row) => ({ ...row, value: breakdown[row.key] })),
    };
  });
  signedMoney(value: number, currency: string) {
    if (Math.abs(value) < 0.005) return money(0, currency);
    return (value > 0 ? "+" : "") + money(value, currency);
  }
}
