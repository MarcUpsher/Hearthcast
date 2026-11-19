import { signal } from "@angular/core";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { provideRouter } from "@angular/router";
import { HttpErrorResponse } from "@angular/common/http";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { MatButtonHarness } from "@angular/material/button/testing";
import { MatDialogHarness } from "@angular/material/dialog/testing";
import { MatSelectHarness } from "@angular/material/select/testing";
import { MatFormFieldHarness } from "@angular/material/form-field/testing";
import { MatInputHarness } from "@angular/material/input/testing";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Api } from "../core/api";
import { Workspace } from "../core/workspace";
import { Household } from "../setup/models";
import { RegularOutgoings } from "./outgoings";
import { BudgetItem } from "./models";
import { budgetDraft, budgetItem } from "../../testing/budgets";
import { deferred, spendingEntry } from "../../testing/spending";

describe("Regular outgoing setup", () => {
  let fixture: ComponentFixture<RegularOutgoings>;
  let current: ReturnType<typeof signal<Household | null>>;
  let get: ReturnType<typeof vi.fn>, send: ReturnType<typeof vi.fn>;
  const home = (id: string): Household => ({ id, name: id, people: [] });
  beforeEach(() => {
    current = signal<Household | null>(home("first"));
    get = vi.fn(async (path: string) => {
      const id = path.split("/")[1];
      if (path.includes("/categories?")) return [spendingEntry("food", id)];
      if (path.includes("/merchants?")) return [spendingEntry("shop", id)];
      if (path.endsWith("/currencies")) return [{ code: "GBP", minorUnits: 2 }, { code: "EUR", minorUnits: 2 }];
      return [budgetItem(id), budgetItem(id, { id: "old", code: "OLD", name: "Old outgoing", isRetired: true })];
    }); send = vi.fn();
    TestBed.configureTestingModule({ providers: [provideRouter([]),
      { provide: Workspace, useValue: { current } }, { provide: Api, useValue: { get, send } }] });
  });
  afterEach(() => { fixture?.destroy(); vi.restoreAllMocks(); });
  async function settle() { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
  async function page() {
    fixture = TestBed.createComponent(RegularOutgoings); await settle();
    await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false)); await settle(); return fixture.componentInstance;
  }
  const root = () => TestbedHarnessEnvironment.documentRootLoader(fixture);
  const button = (text: string) => root().getHarness(MatButtonHarness.with({ text }));

  it("reads the household's setup and shared lookups without loading plans or writing amounts", async () => {
    const c = await page();
    expect(get.mock.calls.map(([path]) => path).sort()).toEqual([
      "households/first/budgets/items?includeRetired=true", "households/first/spending/categories?includeRetired=true",
      "households/first/spending/currencies", "households/first/spending/merchants?includeRetired=true",
    ]);
    expect(c.visibleRows().map(item => item.name)).toEqual(["Internet"]); expect(send).not.toHaveBeenCalled();
    expect(c.createDraft()).toMatchObject({ currencyCode: "GBP", frequency: "Month", intervalCount: "1" });
    expect(c.hasUnsavedChanges()).toBe(false);
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false); expect(c.canLeave()).toBe(true); expect(confirm).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain("Every month");
    expect(fixture.nativeElement.querySelector('[name="amount"]')).toBeNull();
  });
  it("keeps allowance definitions out of the regular commitment setup screen", async () => {
    const original = get.getMockImplementation()! as (path: string) => Promise<unknown>; get.mockImplementation(async path => path.includes("/items?") ? [budgetItem(), budgetItem("first", { id: "allowance", kind: "Allowance" })] : original(path));
    const c = await page(); expect(c.rows().map(item => item.id)).toEqual(["internet"]);
  });
  it("creates an outgoing with named recurrence and integer interval, then resets validation and defaults", async () => {
    const c = await page(); c.createDraft.set(budgetDraft());
    const saved = budgetItem("first", { id: "energy", code: "ENERGY", name: "Electricity", intervalCount: 3, notes: "Variable usage" });
    send.mockResolvedValueOnce(saved); await settle(); await (await button("Add outgoing")).click();
    expect(send).toHaveBeenCalledExactlyOnceWith("POST", "households/first/budgets/items", { ...budgetDraft(), intervalCount: 3, paymentAnchorDate: null });
    expect(c.rows()).toContainEqual(saved); expect(c.hasUnsavedChanges()).toBe(false);
    expect(c.createDraft()).toMatchObject({ name: "", currencyCode: "GBP", frequency: "Month", intervalCount: "1" });
    await settle(); const form = fixture.nativeElement.querySelector('form[aria-label="Add outgoing"]');
    expect(form.classList.contains("ng-submitted")).toBe(false); expect(form.querySelectorAll(".mat-form-field-invalid")).toHaveLength(0);
    expect(fixture.nativeElement.textContent).toContain("Every 3 months");
  });
  it("connects rendered category/currency/recurrence controls to a valid creation request", async () => {
    const c = await page(), loader = TestbedHarnessEnvironment.loader(fixture);
    await (await loader.getHarness(MatInputHarness.with({ selector: '[name="name"]' }))).setValue("School club");
    await (await loader.getHarness(MatInputHarness.with({ selector: '[name="code"]' }))).setValue("CLUB");
    await (await loader.getHarness(MatSelectHarness.with({ selector: '[name="expenseCategoryId"]' }))).clickOptions({ text: "Food" });
    await (await loader.getHarness(MatSelectHarness.with({ selector: '[name="currencyCode"]' }))).clickOptions({ text: "EUR" });
    await (await loader.getHarness(MatInputHarness.with({ selector: '[name="intervalCount"]' }))).setValue("2");
    await (await loader.getHarness(MatSelectHarness.with({ selector: '[name="frequency"]' }))).clickOptions({ text: "Weeks" });
    send.mockResolvedValueOnce(budgetItem("first", { name: "School club", code: "CLUB", currencyCode: "EUR", frequency: "Week", intervalCount: 2, merchantId: null }));
    await (await button("Add outgoing")).click();
    expect(send.mock.calls[0][2]).toMatchObject({ name: "School club", code: "CLUB", currencyCode: "EUR", frequency: "Week", intervalCount: 2, merchantId: null, notes: null });
    expect(c.notice()).toBe("Outgoing added.");
  });
  it.each([400, 409, 503])("retains a rejected creation after HTTP %s", async status => {
    const c = await page(); c.createDraft.set(budgetDraft());
    send.mockRejectedValueOnce(new HttpErrorResponse({ status, error: { detail: "Unable to save outgoing" } })); await c.submit(false);
    expect(c.createDraft()).toEqual(budgetDraft()); expect(c.createError()).toBe("Unable to save outgoing");
    expect(c.rows()).toHaveLength(2); expect(c.saving()).toBe(false); expect(c.canWrite()).toBe(true);
  });
  it.each(["", "0", "-1", "1.5", "2147483648", "NaN", "1e3"])("rejects invalid recurrence interval %s without writing", async value => {
    const c = await page(); c.createDraft.set({ ...budgetDraft(), intervalCount: value }); await c.submit(false);
    expect(c.valid(false)).toBe(false); expect(send).not.toHaveBeenCalled();
  });
  it("accepts the recurrence integer boundary without truncation", async () => {
    const c = await page(); c.createDraft.set({ ...budgetDraft(), intervalCount: "2147483647", frequency: "Year" });
    send.mockResolvedValueOnce(budgetItem()); await c.submit(false);
    expect(send.mock.calls[0][2]).toMatchObject({ frequency: "Year", intervalCount: 2147483647 });
  });
  it.each(["name", "code", "expenseCategoryId", "currencyCode"] as const)("requires %s before creation", async field => {
    const c = await page(); c.createDraft.set({ ...budgetDraft(), [field]: "" }); await c.submit(false); expect(send).not.toHaveBeenCalled();
  });
  it("rejects unsupported new currencies and foreign setup choices", async () => {
    const c = await page(); c.createDraft.set({ ...budgetDraft(), currencyCode: "ZZZ" }); expect(c.valid(false)).toBe(false);
    c.createDraft.set({ ...budgetDraft(), merchantId: "foreign-shop" }); expect(c.valid(false)).toBe(false);
    c.createDraft.set({ ...budgetDraft(), expenseCategoryId: "foreign-category" }); expect(c.valid(false)).toBe(false);
    await c.submit(false); expect(send).not.toHaveBeenCalled();
  });
  it("edits mutable defaults in a focused sidebar, retaining code and explicitly clearing merchant and notes", async () => {
    const c = await page(); c.beginEdit(budgetItem("first", { notes: "Old notes" })); await settle();
    const form = document.querySelector('form[aria-label="Edit outgoing"]')!;
    expect(form.querySelector<HTMLInputElement>('[name="code"]')!.readOnly).toBe(true);
    await vi.waitFor(() => expect(document.activeElement?.getAttribute("name")).toBe("name"));
    c.change(true, "name", "New internet"); c.change(true, "merchantId", ""); c.change(true, "notes", ""); c.change(true, "intervalCount", "12");
    send.mockResolvedValueOnce(budgetItem("first", { name: "New internet", merchantId: null, notes: null, intervalCount: 12 }));
    await (await button("Save outgoing")).click();
    expect(send).toHaveBeenCalledExactlyOnceWith("PUT", "households/first/budgets/items/internet/details", {
      expenseCategoryId: "first-food", name: "New internet", merchantId: null, currencyCode: "GBP", frequency: "Month", intervalCount: 12, notes: null, paymentAnchorDate: null,
    });
    expect(c.rows()[1].code).toBe("INTERNET"); expect(c.editing()).toBeNull();
    await vi.waitFor(async () => expect(await root().getAllHarnesses(MatDialogHarness)).toHaveLength(0));
  });
  it("retains rejected edits and Cancel discards only the sidebar draft", async () => {
    const c = await page(); c.createDraft.set(budgetDraft()); c.beginEdit(budgetItem()); await settle(); c.change(true, "name", "Rejected change");
    send.mockRejectedValueOnce(new Error("Edit failed")); await c.submit(true);
    expect(c.editDraft().name).toBe("Rejected change"); expect(c.editError()).toBe("Edit failed");
    c.cancelEdit(); expect(c.editing()).toBeNull(); expect(c.createDraft()).toEqual(budgetDraft()); expect(c.rows()[0].name).toBe("Internet");
  });
  it("retains historical currency on its own item while limiting newly selected currencies", async () => {
    const c = await page(); c.beginEdit(budgetItem("first", { currencyCode: "XTS" })); await settle();
    const picker = await root().getHarness(MatSelectHarness.with({ selector: 'form[aria-label="Edit outgoing"] [name="currencyCode"]' }));
    expect(await picker.getValueText()).toBe("XTS (saved currency)"); expect(c.valid(true)).toBe(true);
    c.change(true, "currencyCode", "ZZZ"); expect(c.valid(true)).toBe(false);
    c.change(true, "currencyCode", "GBP"); expect(c.valid(true)).toBe(true);
    c.change(true, "currencyCode", "XTS"); c.change(true, "name", "Historical outgoing");
    send.mockResolvedValueOnce(budgetItem("first", { name: "Historical outgoing", currencyCode: "XTS" })); await c.submit(true);
    expect(send.mock.calls[0][2].currencyCode).toBe("XTS");
    c.createDraft.set({ ...budgetDraft(), currencyCode: "XTS" }); expect(c.valid(false)).toBe(false);
  });
  it("retires/restores using only the item's flag and still permits retired category/merchant references", async () => {
    const c = await page(); c.categories.set([{ ...spendingEntry("food"), isRetired: true }]); c.merchants.set([{ ...spendingEntry("shop"), isRetired: true }]);
    expect(c.visibleRows()).toContainEqual(budgetItem());
    c.createDraft.set(budgetDraft()); expect(c.valid(false)).toBe(true);
    send.mockResolvedValueOnce(budgetItem("first", { isRetired: true })); await c.setRetirement(budgetItem());
    expect(send).toHaveBeenLastCalledWith("PUT", "households/first/budgets/items/internet/retirement", { isRetired: true }); expect(c.visibleRows()).toEqual([]);
    c.status.set("retired"); send.mockResolvedValueOnce(budgetItem()); await c.setRetirement(budgetItem("first", { isRetired: true }));
    expect(send).toHaveBeenLastCalledWith("PUT", "households/first/budgets/items/internet/retirement", { isRetired: false });
  });
  it("keeps retirement failures separate from an entered creation draft", async () => {
    const c = await page(); c.createDraft.set(budgetDraft()); send.mockRejectedValueOnce(new Error("Retirement failed")); await c.setRetirement(budgetItem());
    expect(c.actionError()).toBe("Retirement failed"); expect(c.createDraft()).toEqual(budgetDraft()); expect(c.rows()[0].isRetired).toBe(false);
  });
  it("combines search, category and retirement filters including merchant and recurrence text", async () => {
    const c = await page(); c.search.set("Shop"); expect(c.visibleRows()).toHaveLength(1);
    c.search.set("Every month"); expect(c.visibleRows()).toHaveLength(1);
    c.category.set("elsewhere"); expect(c.visibleRows()).toEqual([]);
    c.category.set(""); c.status.set("all"); c.search.set("old"); expect(c.visibleRows().map(row => row.id)).toEqual(["old"]);
    c.rows.set([budgetItem("first", { notes: "Annual school renewal" })]); c.status.set("active"); c.search.set("school"); expect(c.visibleRows()).toHaveLength(1);
  });
  it("preserves drafts through failed loads and ignores an older read after a newer read", async () => {
    const c = await page(); c.createDraft.set(budgetDraft()); get.mockRejectedValueOnce(new Error("Unavailable")); await c.load();
    expect(c.error()).toBe("Unavailable"); expect(c.canWrite()).toBe(false); expect(c.createDraft()).toEqual(budgetDraft());
    const older = deferred<BudgetItem[]>(); get.mockReturnValueOnce(older.promise); const reading = c.load(); await c.load();
    older.resolve([budgetItem("first", { name: "Stale" })]); await reading;
    expect(c.rows()[0].name).toBe("Internet"); expect(c.error()).toBe(""); expect(c.createDraft()).toEqual(budgetDraft());
  });
  it("shows empty-category guidance and makes no calls without a selected household", async () => {
    current.set(null); const c = await page(); expect(get).not.toHaveBeenCalled(); expect(c.canWrite()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain("Select or create a household");
    get.mockResolvedValue([]); current.set(home("first")); await settle(); await vi.waitFor(() => expect(c.loading()).toBe(false)); await settle();
    expect(fixture.nativeElement.textContent).toContain("Add a spending category"); expect(fixture.nativeElement.textContent).toContain("No regular outgoings yet");
  });
  it.each([false, true])("ignores late creation completion after a forced household switch (failure=%s)", async fail => {
    const c = await page(); c.createDraft.set(budgetDraft()); const pending = deferred<BudgetItem>(); send.mockReturnValueOnce(pending.promise); const saving = c.submit(false);
    current.set(home("second")); await settle(); c.createDraft.set(budgetDraft("second"));
    if (fail) pending.reject(new Error("Old error")); else pending.resolve(budgetItem()); await saving;
    expect(c.rows().every(row => row.householdId === "second")).toBe(true); expect(c.createDraft()).toEqual(budgetDraft("second"));
    expect(c.createError()).toBe(""); expect(c.notice()).toBe(""); expect(c.saving()).toBe(false);
  });
  it("ignores a late read after switching away and back to the same household", async () => {
    const c = await page(); const pending = deferred<BudgetItem[]>(); get.mockReturnValueOnce(pending.promise); const reading = c.load();
    current.set(home("second")); await settle(); current.set(home("first")); await settle();
    pending.resolve([budgetItem("first", { name: "Stale" })]); await reading; expect(c.rows()[0].name).toBe("Internet");
  });
  it("blocks duplicate writes and guards cancellation/route/unload while retaining separate drafts", async () => {
    const c = await page(); c.createDraft.set(budgetDraft()); c.beginEdit(budgetItem()); await settle(); c.change(true, "name", "Edited");
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    await (await root().getHarness(MatDialogHarness)).close(); expect(c.editing()).not.toBeNull(); expect(c.canLeave()).toBe(false);
    const event = new Event("beforeunload", { cancelable: true }); window.dispatchEvent(event); expect(event.defaultPrevented).toBe(true);
    const pending = deferred<BudgetItem>(); send.mockReturnValueOnce(pending.promise); const writing = c.submit(true); confirm.mockClear();
    await c.submit(false); await c.setRetirement(budgetItem()); c.cancelEdit(); c.clearCreate();
    expect(c.canLeave()).toBe(false); expect(confirm).not.toHaveBeenCalled(); expect(send).toHaveBeenCalledTimes(1);
    pending.resolve(budgetItem("first", { name: "Edited" })); await writing; expect(c.createDraft()).toEqual(budgetDraft());
  });
  it("resets submitted creation validation when moving to another household", async () => {
    const c = await page(); c.createDraft.set(budgetDraft()); send.mockRejectedValueOnce(new Error("Failed")); await settle();
    await (await button("Add outgoing")).click(); current.set(home("second")); await settle(); await vi.waitFor(() => expect(c.loading()).toBe(false)); await settle();
    const form = fixture.nativeElement.querySelector('form[aria-label="Add outgoing"]');
    expect(form.classList.contains("ng-submitted")).toBe(false); expect(form.querySelectorAll(".mat-form-field-invalid")).toHaveLength(0);
  });
  it.each([["edit", false], ["edit", true], ["retire", false], ["retire", true]] as const)("ignores a previous household's late %s response (failure=%s)", async (operation, fail) => {
    const c = await page(), pending = deferred<BudgetItem>(); send.mockReturnValueOnce(pending.promise);
    if (operation === "edit") { c.beginEdit(budgetItem()); await settle(); c.change(true, "name", "Old edit"); }
    const writing = operation === "edit" ? c.submit(true) : c.setRetirement(budgetItem());
    current.set(home("second")); await settle(); await vi.waitFor(() => expect(c.loading()).toBe(false));
    if (fail) pending.reject(new Error("Old failure")); else pending.resolve(budgetItem("first", { name: "Old edit", isRetired: operation === "retire" }));
    await writing;
    expect(c.editing()).toBeNull(); expect(c.rows().every(row => row.householdId === "second")).toBe(true);
    expect(c.notice()).toBe(""); expect(c.editError()).toBe(""); expect(c.actionError()).toBe(""); expect(c.saving()).toBe(false);
  });

  it("explains invalid codes and recurrence values on the touched field", async () => {
    const c = await page(); c.createDraft.set(budgetDraft()); await settle(); const loader = TestbedHarnessEnvironment.loader(fixture);
    const code = await loader.getHarness(MatInputHarness.with({ selector: '[name="code"]' }));
    await code.setValue(".RENT"); await code.blur();
    const codeField = await loader.getHarness(MatFormFieldHarness.with({ floatingLabelText: "Outgoing code" }));
    expect((await codeField.getTextErrors())[0]).toContain("Start with a letter or digit");
    const count = await loader.getHarness(MatInputHarness.with({ selector: '[name="intervalCount"]' }));
    await count.setValue("0"); await count.blur();
    const countField = await loader.getHarness(MatFormFieldHarness.with({ floatingLabelText: "Every" }));
    expect((await countField.getTextErrors())[0]).toContain("positive whole number");
    await count.setValue("2147483648"); await count.blur();
    expect((await countField.getTextErrors())[0]).toContain("2147483647"); expect(c.valid(false)).toBe(false); expect(send).not.toHaveBeenCalled();
    await code.setValue("RENT"); await count.setValue("1"); expect(c.valid(false)).toBe(true);
  });
  it("clears an obsolete retirement error when starting another save", async () => {
    const c = await page(); send.mockRejectedValueOnce(new Error("Retirement failed")); await c.setRetirement(budgetItem());
    c.createDraft.set(budgetDraft()); send.mockResolvedValueOnce(budgetItem("first", { id: "energy", code: "ENERGY" }));
    await c.submit(false); expect(c.actionError()).toBe(""); expect(c.notice()).toBe("Outgoing added.");
  });

  it("saves a chosen reference date through the rendered form and resets it after success", async () => {
    const c = await page(); c.createDraft.set(budgetDraft()); c.asOf.set("2026-11-19"); await settle();
    const input = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatInputHarness.with({ selector: '[name="paymentAnchorDate"]' }));
    await input.setValue("2026-11-25"); expect(fixture.nativeElement.textContent).toContain("Next expected: 25 Nov 2026");
    send.mockResolvedValueOnce(budgetItem("first", { paymentAnchorDate: "2026-11-25" }));
    await (await button("Add outgoing")).click(); expect(send.mock.calls[0][2].paymentAnchorDate).toBe("2026-11-25");
    expect(c.createDraft().paymentAnchorDate).toBe(""); expect(c.hasUnsavedChanges()).toBe(false);
  });
  it("edits and explicitly clears a saved reference date while retaining rejected drafts", async () => {
    const c = await page(); c.beginEdit(budgetItem("first", { paymentAnchorDate: "2026-11-25" })); await settle();
    expect(c.editDraft().paymentAnchorDate).toBe("2026-11-25");
    c.change(true, "paymentAnchorDate", "2026-11-19"); send.mockRejectedValueOnce(new Error("Failed")); await c.submit(true);
    expect(c.editDraft().paymentAnchorDate).toBe("2026-11-19");
    c.change(true, "paymentAnchorDate", ""); send.mockResolvedValueOnce(budgetItem()); await c.submit(true);
    expect(send.mock.calls[1][2].paymentAnchorDate).toBeNull(); expect(c.rows().find(item => item.id === "internet")?.paymentAnchorDate).toBeNull();
  });
  it.each(["0001-01-01", "9999-12-31", "2026-02-30", "not a date"])("rejects invalid payment reference %s", async value => {
    const c = await page(); c.createDraft.set({ ...budgetDraft(), paymentAnchorDate: value }); await c.submit(false); expect(send).not.toHaveBeenCalled();
  });
  it("shows calendar timing and next dates for active outgoings without forecasting retired payments", async () => {
    const c = await page(); c.asOf.set("2026-11-19"); c.status.set("all");
    c.rows.set([budgetItem("first", { paymentAnchorDate: "2026-01-31" }), budgetItem("first", { id: "retired", code: "RETIRED", name: "Old", isRetired: true, paymentAnchorDate: "2026-01-25" })]); await settle();
    const rows = fixture.nativeElement.querySelectorAll(".outgoings-list li");
    expect(rows[0].textContent).toContain("On the 31st"); expect(rows[0].textContent).toContain("Next expected: 30 Nov 2026");
    expect(rows[1].textContent).not.toContain("Next expected:"); expect(rows[1].textContent).toContain("Payment reference: 25 Jan 2026");
    c.search.set("31st"); expect(c.visibleRows()).toHaveLength(1);
    c.search.set(""); c.status.set("retired"); await settle();
    await (await button("Edit")).click();
    const editor = document.querySelector('form[aria-label="Edit outgoing"]')!;
    expect(editor.querySelector<HTMLInputElement>('[name="paymentAnchorDate"]')!.value).toBe("2026-01-25");
    expect(editor.textContent).not.toContain("Next expected:");
  });

});
