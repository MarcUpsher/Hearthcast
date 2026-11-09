import { smoothPath } from "../../shared/charts/smooth-path";
import { ProjectionRun } from "../models";

export const projectionSeries = [
  { key: "investments", label: "Investments", color: "#42c9ff" },
  { key: "savings", label: "Savings", color: "#b9e769" },
  { key: "equity", label: "Financed equity", color: "#ffc35a" },
  { key: "netPosition", label: "Modelled total", color: "#ec8cda" },
] as const;

export type PayoffEvent = {
  index: number;
  date: string;
  assetIds: string[];
};

export function payoffEvents(report: ProjectionRun | null) {
  const months = report?.months;
  if (!report?.canCalculate || !months?.length) return [];
  const events = new Map<number, PayoffEvent>();
  for (const asset of months.at(-1)!.financedAssets) {
    if (!asset.payoffDate) continue;
    const index = months.findIndex((m) => m.date === asset.payoffDate);
    if (index < 0) continue;
    const event = events.get(index) ?? {
      index,
      date: months[index].date,
      assetIds: [],
    };
    event.assetIds.push(asset.financedAssetId);
    events.set(index, event);
  }
  return [...events.values()].sort((a, b) => a.index - b.index);
}

export function expenseMilestone(report: ProjectionRun | null) {
  const target = report?.expenseTarget;
  const months = report?.months;
  if (
    !report?.canCalculate ||
    !target ||
    !months?.length ||
    months.some((m) => !m.expenseTarget)
  )
    return null;
  return {
    ...target,
    index: months.findIndex((m) => m.date === target.firstReachedDate),
    finalCovered: months.at(-1)!.expenseTarget!.isCovered,
    basis: target.includeSavings ? "Investments + savings" : "Investments",
  };
}

export function buildModellingChart(
  report: ProjectionRun | null,
  adjusted: boolean,
  hovered: number | null = null,
) {
  const months = report?.months;
  if (!report?.canCalculate || !months?.length) return null;
  const key = adjusted ? "inflationAdjusted" : "nominal";
  const expense = expenseMilestone(report);
  const required = (i: number) =>
    adjusted
      ? months[i].expenseTarget!.inflationAdjustedRequiredCapital
      : months[i].expenseTarget!.nominalRequiredCapital;
  const eligible = (i: number) =>
    adjusted
      ? months[i].expenseTarget!.inflationAdjustedEligibleBalance
      : months[i].expenseTarget!.nominalEligibleBalance;
  const values = months.flatMap((m, i) => [
    ...projectionSeries.map((s) => m[key][s.key]),
    ...(expense ? [required(i), eligible(i)] : []),
  ]);
  const min = Math.min(0, ...values),
    maxObserved = Math.max(0, ...values);
  const max =
    maxObserved === min ? min + 1 : maxObserved + (maxObserved - min) * 0.1;
  const x = (i: number) => 88 + (i / Math.max(1, months.length - 1)) * 832;
  const y = (value: number) => 25 + ((max - value) / (max - min)) * 330;
  return {
    lines: [
      ...projectionSeries.map((s) => ({
        ...s,
        path: smoothPath(
          months.map((m, i) => ({ x: x(i), y: y(m[key][s.key]) })),
        ),
      })),
      ...(expense?.includeSavings
        ? [
            {
              key: "eligiblePortfolio",
              label: "Investments + savings",
              color: "#57ddc8",
              path: smoothPath(
                months.map((_, i) => ({ x: x(i), y: y(eligible(i)) })),
              ),
            },
          ]
        : []),
    ],
    expenseTarget: expense
      ? {
          path: smoothPath(
            months.map((_, i) => ({ x: x(i), y: y(required(i)) })),
          ),
          marker:
            expense.index >= 0
              ? {
                  index: expense.index,
                  x: x(expense.index),
                  y: y(eligible(expense.index)),
                }
              : null,
        }
      : null,
    payoffs: payoffEvents(report).map((event) => ({
      ...event,
      x: x(event.index),
      y: y(months[event.index][key].equity),
    })),
    ticks: Array.from({ length: 5 }, (_, i) => {
      const v = min + ((max - min) * i) / 4;
      return {
        y: y(v),
        label: new Intl.NumberFormat("en-GB", {
          notation: "compact",
          maximumFractionDigits: 1,
        }).format(v),
      };
    }),
    dates: [
      ...new Set(
        Array.from({ length: 6 }, (_, i) =>
          Math.round((i * (months.length - 1)) / 5),
        ),
      ),
    ].map((index) => ({ x: x(index), label: months[index].date.slice(0, 7) })),
    hoverX:
      hovered !== null &&
      Number.isInteger(hovered) &&
      hovered >= 0 &&
      hovered < months.length
        ? x(hovered)
        : null,
  };
}

export function chartReading(
  report: ProjectionRun | null,
  adjusted: boolean,
  hovered: number | null,
) {
  const i = hovered;
  const month = i === null ? null : report?.months?.[i];
  return report?.canCalculate && month
    ? {
        date: month.date,
        values: adjusted ? month.inflationAdjusted : month.nominal,
        payoff: payoffEvents(report).find((event) => event.index === i),
        expenseTarget: month.expenseTarget
          ? {
              required: adjusted
                ? month.expenseTarget.inflationAdjustedRequiredCapital
                : month.expenseTarget.nominalRequiredCapital,
              eligible: adjusted
                ? month.expenseTarget.inflationAdjustedEligibleBalance
                : month.expenseTarget.nominalEligibleBalance,
              isCovered: month.expenseTarget.isCovered,
            }
          : null,
      }
    : null;
}
