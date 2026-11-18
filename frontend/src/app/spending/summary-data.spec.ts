import { describe, expect, it } from "vitest";
import { parseSpendingSummary, reportMoney } from "./summary-data";

describe("Exact spending report decimals", () => {
  it("preserves raw JSON decimal tokens while decoding integer metadata and escaped strings", () => {
    const report = parseSpendingSummary(String.raw`{"householdId":"123","from":"2026-12-01","to":"2026-12-31","categories":[{"expenseCategoryId":"456","categoryName":"Food \"netAmount\": 12.5, 123","currencyCode":"CLF","currencyMinorUnits":4,"netAmount":1900000000000.0001}],"reconciliationRecordCounts":{"notChecked":0,"reconciled":2,"unreconciled":1},"warnings":[{"spendingRecordId":"789","spendingDate":"2026-12-01","merchantName":"Shop","currencyCode":"CLF","receiptTotal":950000000000.0001,"lineTotal":950000000000.0000,"difference":0.0001}]}`);
    expect(report.categories[0].netAmount).toBe("1900000000000.0001");
    expect(report.categories[0].currencyMinorUnits).toBe(4);
    expect(report.categories[0].categoryName).toBe('Food "netAmount": 12.5, 123');
    expect(report.householdId).toBe("123"); expect(report.categories[0].expenseCategoryId).toBe("456");
    expect(report.reconciliationRecordCounts).toEqual({ notChecked: 0, reconciled: 2, unreconciled: 1 });
    expect(report.warnings[0].difference).toBe("0.0001");
    expect(reportMoney(report.categories[0].netAmount, "CLF", 4)).toBe("CLF 1 900 000 000 000.0001");
  });
  it.each([
    ["-1900000000000.0001", 4, "CLF -1 900 000 000 000.0001"], ["0", 4, "CLF 0.0000"],
    ["12.5000", 2, "CLF 12.50"], ["1e-6", 2, "CLF 0.000001"], ["1.0001e12", 4, "CLF 1 000 100 000 000.0000"],
  ] as const)("formats %s exactly with saved precision %s", (value, places, expected) => {
    expect(reportMoney(value, "CLF", places)).toBe(expected);
  });
});
