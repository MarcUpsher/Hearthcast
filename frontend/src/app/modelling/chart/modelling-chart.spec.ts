import { describe, expect, it } from "vitest";
import {
  report,
  payoffReport,
  expenseReport,
} from "../../../testing/projection-fixtures";
import {
  buildModellingChart,
  chartReading,
  expenseMilestone,
  payoffEvents,
} from "./modelling-chart";

describe("Pure modelling chart geometry", () => {
  it("has no plot or reading for absent, empty or blocked results", () => {
    for (const result of [
      null,
      { ...report(), months: [] },
      { ...report(), canCalculate: false },
    ]) {
      expect(buildModellingChart(result, false)).toBeNull();
      expect(chartReading(result, false, 0)).toBeNull();
      expect(payoffEvents(result)).toEqual([]);
    }
  });

  it("anchors the full range and keeps headroom above every series", () => {
    const plot = buildModellingChart(report(), false)!;
    expect(plot.dates[0]).toEqual({ x: 88, label: "2026-01" });
    expect(plot.dates.at(-1)).toEqual({ x: 920, label: "2027-01" });
    expect(plot.lines).toHaveLength(4);
    for (const line of plot.lines) {
      expect(line.path).toMatch(/^M88\.000 /);
      expect(line.path).toContain("C");
      const coordinates = line.path.match(/-?\d+\.\d+/g)!.map(Number);
      const ys = coordinates.filter((_, i) => i % 2 === 1);
      expect(Math.min(...ys)).toBeGreaterThan(25);
      expect(Math.max(...ys)).toBeLessThanOrEqual(355);
    }
  });

  it("handles zero, negative and single-month balances without invalid coordinates", () => {
    const result = report();
    result.months = [result.months![0]];
    for (const value of [0, -100]) {
      result.months[0].nominal = {
        investments: 0,
        savings: 0,
        financedAssetValue: 0,
        loanBalance: -value,
        equity: 0,
        netPosition: value,
      };
      const plot = buildModellingChart(result, false)!;
      expect(plot.dates).toHaveLength(1);
      expect(plot.lines.every((line) => !/NaN|Infinity/.test(line.path))).toBe(
        true,
      );
      expect(plot.ticks.every((tick) => Number.isFinite(tick.y))).toBe(true);
    }
  });

  it("groups independent loan payoffs and places starting/final events on the endpoints", () => {
    const result = payoffReport([
      { id: "first", month: 0 },
      { id: "second", month: 12 },
      { id: "third", month: 12 },
      { id: "unpaid", month: null },
    ]);
    const plot = buildModellingChart(result, false)!;
    expect(plot.payoffs).toHaveLength(2);
    expect(plot.payoffs[0].x).toBe(88);
    expect(plot.payoffs[1].x).toBe(920);
    expect(plot.payoffs[1].assetIds).toEqual(["second", "third"]);
    expect(
      buildModellingChart(result, true)!.payoffs.map((event) => event.date),
    ).toEqual(plot.payoffs.map((event) => event.date));
  });

  it("uses returned expense amounts and keeps the milestone at the same month in real values", () => {
    const result = expenseReport();
    const nominal = buildModellingChart(result, false)!;
    const real = buildModellingChart(result, true)!;
    expect(nominal.expenseTarget!.marker).not.toBeNull();
    expect(real.expenseTarget!.marker!.index).toBe(
      nominal.expenseTarget!.marker!.index,
    );
    expect(real.expenseTarget!.marker!.x).toBe(
      nominal.expenseTarget!.marker!.x,
    );
    expect(real.expenseTarget!.path).not.toBe(nominal.expenseTarget!.path);
    const reading = chartReading(
      result,
      true,
      real.expenseTarget!.marker!.index,
    )!;
    expect(reading.expenseTarget!.required).toBe(
      result.months![0].expenseTarget!.inflationAdjustedRequiredCapital,
    );
    expect(reading.expenseTarget!.isCovered).toBe(true);
  });

  it("only adds the eligible savings curve when requested and keeps an unreached guide without a marker", () => {
    expect(
      buildModellingChart(expenseReport(106, true), false)!.lines,
    ).toHaveLength(5);
    const plot = buildModellingChart(expenseReport(200), false)!;
    expect(plot.lines).toHaveLength(4);
    expect(plot.expenseTarget!.path).not.toBe("");
    expect(plot.expenseTarget!.marker).toBeNull();
  });

  it("ignores incomplete expense metadata and out-of-range hover positions", () => {
    const result = expenseReport();
    result.months![0].expenseTarget = null;
    expect(expenseMilestone(result)).toBeNull();
    expect(buildModellingChart(result, false)!.expenseTarget).toBeNull();
    for (const index of [-1, 1000, 0.5]) {
      expect(buildModellingChart(result, false, index)!.hoverX).toBeNull();
      expect(chartReading(result, false, index)).toBeNull();
    }
    expect(buildModellingChart(result, false, 0)!.hoverX).toBe(88);
  });

  it("does not mutate an immutable response while building views or readings", () => {
    const result = expenseReport(106, true);
    const before = JSON.stringify(result);
    const freeze = (value: unknown): void => {
      if (value && typeof value === "object") {
        Object.freeze(value);
        for (const child of Object.values(value)) freeze(child);
      }
    };
    freeze(result);
    buildModellingChart(result, false, 0);
    buildModellingChart(result, true, 12);
    chartReading(result, true, 12);
    expenseMilestone(result);
    expect(JSON.stringify(result)).toBe(before);
  });
});
