import { ComponentFixture, TestBed } from "@angular/core/testing";
import { HttpErrorResponse } from "@angular/common/http";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { MatButtonHarness } from "@angular/material/button/testing";
import { MatSelectHarness } from "@angular/material/select/testing";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Api } from "../core/api";
import { budgetItem } from "../../testing/budgets";
import { deferred, spendingEntry } from "../../testing/spending";
import { BudgetItem } from "./models";
import { AllowanceSetup } from "./allowance-setup";

const allowance = (extra: Partial<BudgetItem> = {}) => budgetItem("first", { kind: "Allowance", merchantId: null, name: "Food", code: "ALW_test", ...extra });
describe("Allowance definition controls", () => {
  let fixture: ComponentFixture<AllowanceSetup>, c: AllowanceSetup;
  let get: ReturnType<typeof vi.fn>, send: ReturnType<typeof vi.fn>;
  let changed = vi.fn<(item: BudgetItem) => void>(), selected = vi.fn<(item: BudgetItem) => void>();
  beforeEach(async () => {
    get = vi.fn(async () => []); send = vi.fn(async () => allowance()); changed = vi.fn(); selected = vi.fn();
    TestBed.configureTestingModule({ providers: [{ provide: Api, useValue: { get, send } }] });
    fixture = TestBed.createComponent(AllowanceSetup); c = fixture.componentInstance;
    fixture.componentRef.setInput("household", "first"); fixture.componentRef.setInput("items", []);
    fixture.componentRef.setInput("categories", [spendingEntry("food")]); fixture.componentRef.setInput("currencies", [{ code: "GBP", minorUnits: 2 }, { code: "EUR", minorUnits: 2 }]);
    c.changed.subscribe(changed); c.selected.subscribe(selected); await settle();
  });
  afterEach(() => { fixture.destroy(); vi.restoreAllMocks(); });
  async function settle() { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
  const draft = () => { c.change("category", "first-food"); };
  it("creates through rendered category selection with generated identity and GBP/month defaults", async () => {
    c.show(); await settle(); const loader = TestbedHarnessEnvironment.loader(fixture);
    await (await loader.getHarness(MatSelectHarness.with({ selector: '[name="allowanceCategory"]' }))).clickOptions({ text: "Food" });
    await (await loader.getHarness(MatButtonHarness.with({ text: "Add / select allowance" }))).click();
    expect(send).toHaveBeenCalledExactlyOnceWith("POST", "households/first/budgets/items", { kind: "Allowance", expenseCategoryId: "first-food", currencyCode: "GBP", frequency: "Month", intervalCount: 1, notes: null });
    expect(changed).toHaveBeenCalledWith(allowance()); expect(selected).toHaveBeenCalledWith(allowance()); expect(c.hasUnsavedChanges()).toBe(false);
    await settle(); expect(fixture.nativeElement.querySelector("form").classList.contains("ng-submitted")).toBe(false);
    expect(fixture.nativeElement.querySelectorAll(".mat-form-field-invalid")).toHaveLength(0);
    expect(fixture.nativeElement.querySelector('[name="code"]')).toBeNull(); expect(fixture.nativeElement.querySelector('[name="merchantId"]')).toBeNull();
  });
  it("selects an existing category/currency allowance without posting another definition", async () => {
    fixture.componentRef.setInput("items", [allowance({ frequency: "Year" })]); await settle(); draft(); await c.save();
    expect(send).not.toHaveBeenCalled(); expect(selected).toHaveBeenCalledWith(allowance({ frequency: "Year" })); expect(c.notice()).toContain("Existing allowance");
  });
  it.each([0, 409, 503])("requires reconciliation after uncertain/conflicting create (%s)", async status => {
    draft(); c.change("notes", "Keep input"); send.mockRejectedValueOnce(new HttpErrorResponse({ status })); await c.save();
    expect(c.needsReload()).toBe(true); expect(c.draft().notes).toBe("Keep input"); await c.save(); expect(send).toHaveBeenCalledTimes(1);
    get.mockResolvedValueOnce([allowance()]); await c.reload(); expect(c.match()).toEqual(allowance()); expect(selected).not.toHaveBeenCalled();
    c.useMatch(); expect(selected).toHaveBeenCalledWith(allowance()); expect(send).toHaveBeenCalledTimes(1);
  });
  it("allows an explicit retry after reconciliation finds no matching definition", async () => {
    draft(); send.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 })); await c.save(); await c.reload();
    expect(c.needsReload()).toBe(false); expect(c.draft().category).toBe("first-food"); await c.save(); expect(send).toHaveBeenCalledTimes(2);
  });
  it("retains editable input on definite validation rejection", async () => {
    draft(); send.mockRejectedValueOnce(new HttpErrorResponse({ status: 400, error: { detail: "Rejected" } })); await c.save();
    expect(c.error()).toBe("Rejected"); expect(c.needsReload()).toBe(false); expect(c.draft().category).toBe("first-food");
  });
  it("edits defaults without kind conversion and retains a historical currency on its own identity", async () => {
    const item = allowance({ currencyCode: "XTS" }); c.edit(item); expect(c.valid()).toBe(true); c.change("frequency", "Week"); c.change("interval", "2");
    c.change("notes", "Fortnightly"); await c.save(); expect(send).toHaveBeenCalledWith("PUT", "households/first/budgets/items/internet/details", {
      name: "Food", merchantId: null, paymentAnchorDate: null, expenseCategoryId: "first-food", currencyCode: "XTS", frequency: "Week", intervalCount: 2, notes: "Fortnightly",
    });
  });
  it.each(["0", "1.5", "2147483648", "1e3"])("rejects invalid recurrence %s", async interval => {
    draft(); c.change("interval", interval); await c.save(); expect(c.valid()).toBe(false); expect(send).not.toHaveBeenCalled();
  });
  it("retirement uses only the definition flag and conflicts require reloading", async () => {
    send.mockResolvedValueOnce(allowance({ isRetired: true })); await c.retire(allowance());
    expect(send).toHaveBeenLastCalledWith("PUT", "households/first/budgets/items/internet/retirement", { isRetired: true });
    send.mockRejectedValueOnce(new HttpErrorResponse({ status: 409 })); await c.retire(allowance({ isRetired: true }));
    expect(c.needsReload()).toBe(true); expect(c.notice()).toBe("");
  });
  it("protects typed defaults when switching edits or clearing", () => {
    draft(); const confirm = vi.spyOn(window, "confirm").mockReturnValue(false); c.edit(allowance()); expect(c.editing()).toBeNull(); c.cancel();
    expect(c.draft().category).toBe("first-food"); expect(confirm).toHaveBeenCalledTimes(2);
  });
  it.each([false, true])("ignores a late mutation from a previous household (failure=%s)", async fail => {
    draft(); const pending = deferred<BudgetItem>(); send.mockReturnValueOnce(pending.promise); const saving = c.save();
    fixture.componentRef.setInput("household", "second"); await settle();
    if (fail) pending.reject(new Error("Old error")); else pending.resolve(allowance()); await saving;
    expect(changed).not.toHaveBeenCalled(); expect(selected).not.toHaveBeenCalled(); expect(c.error()).toBe(""); expect(c.saving()).toBe(false); expect(c.hasUnsavedChanges()).toBe(false);
  });
});
