import { signal } from "@angular/core";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { HttpErrorResponse } from "@angular/common/http";
import { provideRouter } from "@angular/router";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { MatInputHarness } from "@angular/material/input/testing";
import { MatButtonHarness } from "@angular/material/button/testing";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Api } from "../core/api";
import { Workspace } from "../core/workspace";
import { Household } from "../setup/models";
import { localToday } from "../spending/amounts";
import { budgetItem } from "../../testing/budgets";
import { deferred, spendingEntry } from "../../testing/spending";
import { BudgetEntry, BudgetPlan } from "./models";
import { OutgoingPlans } from "./plans";

const entry = (extra: Partial<BudgetEntry> = {}): BudgetEntry => ({ id: "entry", budgetItemId: "internet", kind: "Commitment", itemCode: "OLD-INTERNET", itemName: "Saved Internet",
  expenseCategoryId: "first-food", expenseCategoryName: "Saved category", merchantId: null, merchantName: null, plannedAmount: "30.123456",
  currencyCode: "GBP", frequency: "Month", intervalCount: 1, notes: "Saved notes", valueOrigin: "Confirmed", lastConfirmedDate: "2026-01-01", carriedFromBudgetEntryId: null, ...extra });
const plan = (extra: Partial<BudgetPlan> = {}): BudgetPlan => ({ id: "plan", householdId: "first", effectiveFrom: "2026-01-01", notes: null,
  createdAtUtc: "2026-01-01T00:00:00Z", updatedAtUtc: "2026-01-01T00:00:00Z", revision: "revision", copiedFromBudgetSnapshotId: null,
  isComplete: true, entries: [entry()], ...extra });

describe("Outgoing plans", () => {
  let fixture: ComponentFixture<OutgoingPlans>;
  let current: ReturnType<typeof signal<Household | null>>;
  let get: ReturnType<typeof vi.fn>, text: ReturnType<typeof vi.fn>, sendText: ReturnType<typeof vi.fn>, send: ReturnType<typeof vi.fn>;
  let source: BudgetPlan | null;
  const home = (id: string): Household => ({ id, name: id, people: [] });
  beforeEach(() => {
    source = plan(); current = signal<Household | null>(home("first"));
    get = vi.fn(async (path: string) => {
      const id = path.split("/")[1];
      if (path.endsWith("/currencies")) return [{ code: "GBP", minorUnits: 2 }, { code: "EUR", minorUnits: 2 }];
      if (path.includes("/items?")) return [budgetItem(id)];
      if (path.includes("/categories?")) return [spendingEntry("food", id)];
      return [spendingEntry("shop", id)];
    });
    text = vi.fn(async (path: string) => {
      const id = path.split("/")[1];
      if (path.includes("copy-source?")) return source ? JSON.stringify({ ...source, householdId: id }) : "";
      if (path.includes("effective?")) return JSON.stringify({ asOf: new URL(path, "http://test/").searchParams.get("asOf"), snapshot: plan({ householdId: id }), effectiveUntilExclusive: null });
      if (path.includes("?pageSize=")) return JSON.stringify({ records: [plan({ householdId: id })], nextBefore: null, pageSize: 25 });
      return JSON.stringify(plan({ householdId: id }));
    });
    sendText = vi.fn(async () => JSON.stringify(plan())); send = vi.fn();
    TestBed.configureTestingModule({ providers: [provideRouter([]), { provide: Workspace, useValue: { current } }, { provide: Api, useValue: { get, getText: text, sendText, send } }] });
  });
  afterEach(() => { fixture?.destroy(); vi.restoreAllMocks(); });
  async function settle() { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
  async function page() { fixture = TestBed.createComponent(OutgoingPlans); await settle(); await vi.waitFor(() => expect(fixture.componentInstance.loading()).toBe(false)); await settle(); return fixture.componentInstance; }
  const payload = (index = 0) => JSON.parse(sendText.mock.calls[index][2]);
  const button = (label: string) => TestbedHarnessEnvironment.loader(fixture).getHarness(MatButtonHarness.with({ text: label }));
  async function initial() { source = null; const c = await page(); await c.startNew(); await settle(); return c; }

  it("reads explicit local as-of, bounded history and saved context without writing", async () => {
    const c = await page(); expect(text).toHaveBeenCalledWith(`households/first/budgets/snapshots/effective?asOf=${localToday()}`);
    expect(fixture.nativeElement.textContent).toContain("Saved category"); expect(fixture.nativeElement.textContent).toContain("GBP 30.123456");
    expect(c.hasUnsavedChanges()).toBe(false); expect(sendText).not.toHaveBeenCalled();
  });
  it("creates a first plan using entered zero, explicit unknown, notes and all active items", async () => {
    get.mockImplementation(async (path: string) => path.includes("/items?") ? [budgetItem(), budgetItem("first", { id: "water", name: "Water" }), budgetItem("first", { id: "old", isRetired: true })] : [spendingEntry("food")]);
    const c = await initial(); expect(c.rows()).toHaveLength(2); expect(c.validDraft()).toBe(false);
    c.changeRow("internet", { amount: "0" }); c.changeRow("water", { mode: "Unrecorded" }); c.notes.set("Initial costs");
    await c.savePlan();
    expect(sendText).toHaveBeenCalledExactlyOnceWith("POST", "households/first/budgets/snapshots", expect.any(String));
    expect(payload()).toEqual({
      id: expect.any(String), effectiveFrom: localToday(), notes: "Initial costs", copiedFromBudgetSnapshotId: null,
      entries: [{ budgetItemId: "internet", mode: "Confirm", refreshFromSetup: false, plannedAmount: 0, confirmedOn: localToday() }, { budgetItemId: "water", mode: "Unrecorded", refreshFromSetup: false }],
    });
    expect(c.creating()).toBe(false); expect(c.hasUnsavedChanges()).toBe(false); expect(c.notice()).toBe("Outgoing plan saved.");
  });
  it("connects rendered amount input and save button without resetting validation to red", async () => {
    const c = await initial(); const loader = TestbedHarnessEnvironment.loader(fixture);
    await (await loader.getHarness(MatInputHarness.with({ selector: '[name="amount-internet"]' }))).setValue("42.50");
    expect(c.rows()[0].amount).toBe("42.50"); await (await button("Save budget plan")).click();
    expect(payload().entries[0].plannedAmount).toBe(42.5); await settle();
    expect(fixture.nativeElement.querySelector('form[aria-label="New budget plan"]')).toBeNull();
  });
  it("submits the entire supported six-decimal amount exactly for create and correction", async () => {
    const c = await initial(); c.changeRow("internet", { amount: "999999999999.123456" }); await c.savePlan();
    expect(sendText.mock.calls[0][2]).toContain('"plannedAmount":999999999999.123456');
    c.beginCorrection(c.selected()!.entries[0]); c.changeCorrection("amount", "999999999999.123456"); await c.saveCorrection();
    expect(sendText.mock.calls[1][2]).toContain('"plannedAmount":999999999999.123456');
  });
  it("carries saved values/metadata without reconfirming, including unknown and retired entries", async () => {
    source = plan({ entries: [entry({ plannedAmount: null, valueOrigin: "Unrecorded", lastConfirmedDate: null })] });
    get.mockImplementation(async (path: string) => path.includes("/items?") ? [budgetItem("first", { isRetired: true, currencyCode: "EUR", frequency: "Year" })] : []);
    const c = await page(); await c.startNew();
    expect(c.rows()[0]).toMatchObject({ included: true, mode: "Carry", context: { currencyCode: "GBP", frequency: "Month", plannedAmount: null } });
    expect(c.draftTotals()[0].unknown).toBe(1); await c.savePlan();
    expect(payload()).toMatchObject({ copiedFromBudgetSnapshotId: "plan", entries: [{ budgetItemId: "internet", mode: "Carry", refreshFromSetup: false }] });
  });
  it("requires an explicit new amount when current setup changes the currency or recurrence", async () => {
    get.mockImplementation(async (path: string) => path.includes("/items?") ? [budgetItem("first", { currencyCode: "EUR", frequency: "Year" })] : []);
    const c = await page(); await c.startNew(); c.useSetup(c.rows()[0], true);
    expect(c.rows()[0]).toMatchObject({ refreshFromSetup: true, mode: "Confirm", amount: "", context: { currencyCode: "EUR", frequency: "Year" } });
    expect(c.canCarry(c.rows()[0])).toBe(false); expect(c.validDraft()).toBe(false);
    c.changeRow("internet", { mode: "Carry" }); expect(c.validDraft()).toBe(false);
    c.changeRow("internet", { mode: "Confirm", amount: "50" }); await c.savePlan();
    expect(payload().entries[0]).toMatchObject({ mode: "Confirm", plannedAmount: 50, refreshFromSetup: true });
  });
  it("clears an entered amount when reverting to saved currency or recurrence too", async () => {
    get.mockImplementation(async (path: string) => path.includes("/items?") ? [budgetItem("first", { currencyCode: "EUR", frequency: "Year" })] : []);
    const c = await page(); await c.startNew(); c.useSetup(c.rows()[0], true); c.changeRow("internet", { amount: "120" });
    c.useSetup(c.rows()[0], false); expect(c.rows()[0]).toMatchObject({ mode: "Confirm", amount: "", context: { currencyCode: "GBP", frequency: "Month" } });
    expect(c.validDraft()).toBe(false); await c.savePlan(); expect(sendText).not.toHaveBeenCalled();
  });
  it("refreshes definitions at capture so items added in another tab are included", async () => {
    const c = await page(); source = plan({ entries: [entry(), entry({ id: "second-entry", budgetItemId: "water", itemName: "Water" })] });
    get.mockImplementation(async path => path.includes("/items?") ? [budgetItem(), budgetItem("first", { id: "water", name: "Water" }), budgetItem("first", { id: "new", name: "School club" })] : [spendingEntry("food")]);
    await c.startNew(); expect(c.rows().map(row => row.item.id).sort()).toEqual(["internet", "new", "water"]);
    expect(c.rows().find(row => row.item.id === "water")?.mode).toBe("Carry"); expect(c.rows().find(row => row.item.id === "new")?.mode).toBe("Confirm");
  });
  it("blocks creation if a concurrently saved source item has no fetched definition", async () => {
    const c = await page(); source = plan({ entries: [entry(), entry({ id: "missing", budgetItemId: "new-in-another-tab" })] });
    await c.startNew(); expect(c.sourceDate()).toBe(""); expect(c.validDraft()).toBe(false); expect(c.writeError()).toContain("definitions changed");
    await c.savePlan(); expect(sendText).not.toHaveBeenCalled();
  });
  it.each(["-1", "1.1234567", "1000000000000"])("prevents invalid amount %s", async amount => {
    const c = await initial(); c.changeRow("internet", { amount }); await c.savePlan(); expect(sendText).not.toHaveBeenCalled(); expect(c.validDraft()).toBe(false);
  });
  it("allows excluded rows to have unfinished inputs but confirms an empty replacement", async () => {
    const c = await initial(); c.changeRow("internet", { included: false }); const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    await c.savePlan(); expect(sendText).not.toHaveBeenCalled(); expect(confirm).toHaveBeenCalledWith(expect.stringContaining("empty plan"));
    confirm.mockReturnValue(true); await c.savePlan(); expect(payload().entries).toEqual([]);
  });
  it("requires a source reload after changing effective date, retaining draft if replacement is cancelled", async () => {
    const c = await initial(); c.changeRow("internet", { amount: "25" }); c.changeDate("2026-12-01");
    expect(c.validDraft()).toBe(false); const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    await c.loadSource(); expect(c.rows()[0].amount).toBe("25"); expect(c.sourceDate()).not.toBe("2026-12-01");
    confirm.mockReturnValue(true); await c.loadSource(); expect(c.sourceDate()).toBe("2026-12-01"); expect(c.rows()[0].amount).toBe("");
  });
  it("does not permit save after a failed source load", async () => {
    const c = await initial(); c.changeDate("2026-12-01"); vi.spyOn(window, "confirm").mockReturnValue(true); text.mockRejectedValueOnce(new Error("Unavailable")); await c.loadSource();
    expect(c.sourceDate()).toBe(""); await c.savePlan(); expect(sendText).not.toHaveBeenCalled(); expect(c.writeError()).toBe("Unavailable");
  });
  it("preserves discard protection for retained notes and a chosen date after source reload", async () => {
    const c = await page(); await c.startNew(); c.notes.set("Important entered notes"); c.changeDate("2026-12-01");
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true); await c.loadSource();
    expect(c.notes()).toBe("Important entered notes"); expect(c.hasUnsavedChanges()).toBe(true);
    confirm.mockReturnValue(false); expect(c.canLeave()).toBe(false);
    c.notes.set(""); expect(c.hasUnsavedChanges()).toBe(true);
  });
  it("loads enough history to preview the nearest later plan beyond the first page", async () => {
    const c = await initial(); c.changeDate("2026-01-15");
    text.mockImplementation(async (path: string) => {
      if (path.includes("copy-source")) return JSON.stringify(plan());
      return JSON.stringify(path.includes("&before=") ? { records: [plan({ effectiveFrom: "2026-02-01" }), plan()], nextBefore: null, pageSize: 100 } :
        { records: [plan({ effectiveFrom: "2026-04-01" })], nextBefore: "2026-04-01", pageSize: 100 });
    });
    vi.spyOn(window, "confirm").mockReturnValue(true); await c.loadSource(); expect(c.draftEnd()).toBe("2026-02-01");
    await c.savePlan(); expect(window.confirm).toHaveBeenCalledWith(expect.stringContaining("ending before 1 Feb 2026"));
  });
  it.each([400, 409])("retains editable draft after definite HTTP %s rejection", async status => {
    const c = await initial(); c.changeRow("internet", { amount: "20" }); sendText.mockRejectedValueOnce(new HttpErrorResponse({ status, error: { detail: "Rejected" } }));
    await c.savePlan(); expect(c.locked()).toBe(false); expect(c.rows()[0].amount).toBe("20"); expect(c.writeError()).toBe("Rejected");
    c.changeRow("internet", { amount: "30" }); expect(c.rows()[0].amount).toBe("30");
  });
  it.each([0, 503])("retries the identical identity and payload after uncertain HTTP %s", async status => {
    const c = await initial(); c.changeRow("internet", { amount: "20" }); sendText.mockRejectedValueOnce(new HttpErrorResponse({ status }));
    await c.savePlan(); const original = structuredClone(sendText.mock.calls[0][2]); expect(c.locked()).toBe(true);
    c.changeRow("internet", { amount: "99" }); c.changeDate("2026-12-02"); expect(c.rows()[0].amount).toBe("20");
    await c.savePlan(); expect(sendText.mock.calls[1][2]).toEqual(original); expect(c.locked()).toBe(false);
  });
  it("sends amount-only corrections with saved revision and exact nullable pairs", async () => {
    const c = await page(); c.beginCorrection(c.selected()!.entries[0]); c.changeCorrection("amount", "0"); await c.saveCorrection();
    expect(sendText).toHaveBeenLastCalledWith("PUT", "households/first/budgets/snapshots/plan/entries/entry/amount", expect.any(String));
    expect(payload()).toEqual({
      expectedRevision: "revision", plannedAmount: 0, confirmedOn: localToday(), notes: "Saved notes",
    });
    c.beginCorrection(c.selected()!.entries[0]); c.changeCorrection("amount", ""); c.changeCorrection("notes", ""); await c.saveCorrection();
    expect(payload(1)).toEqual({ expectedRevision: "revision", plannedAmount: null, confirmedOn: null, notes: null });
  });
  it.each([409, 0, 503])("requires reload after a conflicting/uncertain correction (%s)", async status => {
    const c = await page(); c.beginCorrection(c.selected()!.entries[0]); c.changeCorrection("amount", "22"); sendText.mockRejectedValueOnce(new HttpErrorResponse({ status }));
    await c.saveCorrection(); expect(c.correctionUncertain()).toBe(true); expect(c.correction().amount).toBe("22"); await c.saveCorrection();
    expect(sendText).toHaveBeenCalledTimes(1); vi.spyOn(window, "confirm").mockReturnValue(true); await c.load();
    expect(c.correcting()).toBeNull(); expect(c.selected()?.revision).toBe("revision");
  });
  it("keeps the reload requirement after cancelling an uncertain correction", async () => {
    const c = await page(); c.beginCorrection(c.selected()!.entries[0]); sendText.mockRejectedValueOnce(new HttpErrorResponse({ status: 409 })); await c.saveCorrection();
    vi.spyOn(window, "confirm").mockReturnValue(true); c.cancel(); expect(c.correcting()).toBeNull(); expect(c.canWrite()).toBe(false);
    c.beginCorrection(c.selected()!.entries[0]); expect(c.correcting()).toBeNull(); await c.load(); expect(c.canWrite()).toBe(true);
  });
  it("focuses the rendered correction and submits an unknown amount as null", async () => {
    const c = await page(); await (await button("Correct amount for Saved Internet")).click(); await settle();
    expect(document.activeElement?.getAttribute("name")).toBe("correctedAmount");
    await (await TestbedHarnessEnvironment.loader(fixture).getHarness(MatInputHarness.with({ selector: '[name="correctedAmount"]' }))).setValue("");
    await (await button("Save correction")).click(); expect(payload()).toMatchObject({ plannedAmount: null, confirmedOn: null });
    expect(c.correcting()).toBeNull();
  });
  it("switches to the effective plan when the as-of date changes", async () => {
    const c = await page(); c.asOf.set("2025-06-01");
    text.mockImplementation(async path => path.includes("effective?") ? JSON.stringify({ asOf: "2025-06-01", snapshot: plan({ id: "earlier", effectiveFrom: "2025-01-01" }), effectiveUntilExclusive: "2026-01-01" }) : JSON.stringify({ records: [plan()], nextBefore: null, pageSize: 25 }));
    await c.load(); expect(c.selected()?.id).toBe("earlier"); expect(c.loadedAsOf()).toBe("2025-06-01");
  });
  it("ignores late correction failure after a household switch", async () => {
    const c = await page(); c.beginCorrection(c.selected()!.entries[0]); const pending = deferred<string>(); sendText.mockReturnValueOnce(pending.promise); const writing = c.saveCorrection();
    current.set(home("second")); await settle(); pending.reject(new HttpErrorResponse({ status: 409 })); await writing;
    expect(c.selected()?.householdId).toBe("second"); expect(c.revisionNeedsReload()).toBe(false); expect(c.writeError()).toBe("");
  });
  it("ignores a late source load after a household switch", async () => {
    const c = await page(), pending = deferred<string>(); text.mockReturnValueOnce(pending.promise); const reading = c.startNew();
    current.set(home("second")); await settle(); pending.resolve(JSON.stringify(plan())); await reading;
    expect(c.selected()?.householdId).toBe("second"); expect(c.source()).toBeNull(); expect(c.creating()).toBe(false);
  });
  it("retains editable correction after validation rejection and preserves saved metadata", async () => {
    const c = await page(); c.beginCorrection(c.selected()!.entries[0]); c.changeCorrection("amount", "22"); sendText.mockRejectedValueOnce(new HttpErrorResponse({ status: 400 }));
    await c.saveCorrection(); expect(c.correctionUncertain()).toBe(false); expect(c.correction().amount).toBe("22"); expect(c.selected()!.entries[0].itemName).toBe("Saved Internet");
  });
  it("distinguishes absent and empty effective plans", async () => {
    text.mockImplementation(async path => path.includes("effective?") ? "" : JSON.stringify({ records: [], nextBefore: null, pageSize: 25 }));
    const c = await page(); expect(c.effective()).toBeNull(); expect(fixture.nativeElement.textContent).toContain("No plan effective on");
    text.mockImplementation(async path => path.includes("effective?") ? JSON.stringify({ asOf: localToday(), snapshot: plan({ entries: [] }), effectiveUntilExclusive: null }) : JSON.stringify({ records: [], nextBefore: null, pageSize: 25 }));
    await c.load(); await settle(); expect(fixture.nativeElement.textContent).toContain("This is an empty plan");
  });
  it("guards route/unload/draft replacement and blocks duplicate writes", async () => {
    const c = await initial(); c.changeRow("internet", { amount: "22" }); const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    expect(c.canLeave()).toBe(false); c.cancel(); expect(c.creating()).toBe(true);
    const event = new Event("beforeunload", { cancelable: true }); window.dispatchEvent(event); expect(event.defaultPrevented).toBe(true);
    const pending = deferred<string>(); sendText.mockReturnValueOnce(pending.promise); const saving = c.savePlan(); confirm.mockClear();
    await c.savePlan(); c.cancel(); expect(c.canLeave()).toBe(false); expect(confirm).not.toHaveBeenCalled(); expect(sendText).toHaveBeenCalledTimes(1);
    pending.resolve(JSON.stringify(plan())); await saving;
  });
  it.each([false, true])("ignores a forced household switch's late save (failure=%s)", async fail => {
    const c = await initial(); c.changeRow("internet", { amount: "22" }); const pending = deferred<string>(); sendText.mockReturnValueOnce(pending.promise); const saving = c.savePlan();
    current.set(home("second")); await settle(); await vi.waitFor(() => expect(c.loading()).toBe(false));
    if (fail) pending.reject(new Error("Old error")); else pending.resolve(JSON.stringify(plan())); await saving;
    expect(c.selected()?.householdId).toBe("second"); expect(c.notice()).toBe(""); expect(c.writeError()).toBe(""); expect(c.creating()).toBe(false);
  });
  it("ignores an old read after switching away and back", async () => {
    const c = await page(), pending = deferred<string>(); text.mockReturnValueOnce(pending.promise); const reading = c.load();
    current.set(home("second")); await settle(); current.set(home("first")); await settle(); pending.resolve(JSON.stringify({ records: [], nextBefore: null, pageSize: 25 })); await reading;
    expect(c.history()).toHaveLength(1); expect(c.selected()?.householdId).toBe("first");
  });
  it("paginates history without losing drafts or duplicating records", async () => {
    const c = await initial(); c.changeRow("internet", { amount: "22" }); c.nextBefore.set("2026-01-01");
    text.mockResolvedValueOnce(JSON.stringify({ records: [plan(), plan({ id: "older", effectiveFrom: "2025-01-01" })], nextBefore: null, pageSize: 25 }));
    await c.more(); expect(c.history().map(row => row.id)).toEqual(["plan", "older"]); expect(c.rows()[0].amount).toBe("22");
  });
  it("makes no requests without a household", async () => {
    current.set(null); const c = await page(); expect(get).not.toHaveBeenCalled(); expect(text).not.toHaveBeenCalled(); expect(c.canWrite()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain("Select or create a household");
  });
  it("edits an allowance while carrying commitments in the same saved plan", async () => {
    const a = budgetItem("first", { id: "allowance", kind: "Allowance", name: "Internal name", code: "ALW_INTERNAL", merchantId: null });
    source = plan({ entries: [entry({ plannedAmount: "50" }), entry({ id: "allowance-entry", budgetItemId: "allowance", kind: "Allowance", itemCode: a.code, itemName: a.name, expenseCategoryName: "Food", plannedAmount: "400", lastConfirmedDate: "2026-01-01" })] });
    const normalGet = get.getMockImplementation()! as (path: string) => Promise<unknown>; get.mockImplementation(async path => path.includes("/items?") ? [budgetItem(), a] : normalGet(path));
    const c = await page(); await c.startNew(); await settle(); const allowanceRow = c.rows().find(row => row.item.id === a.id)!;
    expect(allowanceRow.mode).toBe("Carry"); expect(allowanceRow.source?.lastConfirmedDate).toBe("2026-01-01");
    const body = fixture.nativeElement.querySelector('form[aria-label="New budget plan"]').textContent;
    expect(body).toContain("Spending allowances"); expect(body).not.toContain("ALW_INTERNAL"); expect(body).toContain("GBP 450.00");
    c.enterAllowance(allowanceRow, "425.123456"); await c.savePlan();
    expect(payload().entries).toEqual([{ budgetItemId: a.id, mode: "Confirm", refreshFromSetup: false, plannedAmount: 425.123456, confirmedOn: localToday() },
      { budgetItemId: "internet", mode: "Carry", refreshFromSetup: false }]);
  });
  it("adds a new allowance definition without losing or prematurely saving the plan draft", async () => {
    const c = await page(); await c.startNew(); c.notes.set("Keep this plan");
    const setup = c.allowanceSetup()!; setup.change("category", "first-food");
    const item = budgetItem("first", { id: "allowance", kind: "Allowance", merchantId: null }); send.mockResolvedValueOnce(item); await setup.save();
    expect(c.rows()).toHaveLength(2); expect(c.notes()).toBe("Keep this plan"); expect(sendText).not.toHaveBeenCalled();
    expect(c.rows().find(row => row.item.id === item.id)).toMatchObject({ included: true, mode: "Confirm", amount: "", context: { kind: "Allowance" } });
    expect(c.validDraft()).toBe(false); c.enterAllowance(c.rows().find(row => row.item.id === item.id)!, "0"); expect(c.validDraft()).toBe(true);
  });
  it("prevents two included allowance identities from claiming the same category/currency", async () => {
    const first = budgetItem("first", { id: "old", kind: "Allowance", isRetired: true, merchantId: null });
    const next = budgetItem("first", { id: "next", kind: "Allowance", merchantId: null });
    source = plan({ entries: [entry({ id: "old-entry", budgetItemId: first.id, kind: "Allowance", expenseCategoryId: first.expenseCategoryId })] });
    get.mockImplementation(async path => path.includes("/items?") ? [first, next] : path.endsWith("/currencies") ? [{ code: "GBP", minorUnits: 2 }] : [spendingEntry("food")]);
    const c = await page(); await c.startNew(); c.enterAllowance(c.rows().find(row => row.item.id === "next")!, "400");
    expect(c.duplicateAllowances()).toBe(true); await c.savePlan(); expect(sendText).not.toHaveBeenCalled();
    c.changeRow("old", { included: false }); expect(c.validDraft()).toBe(true); await c.savePlan(); expect(payload().entries).toHaveLength(1);
  });
  it("requires fresh confirmation when an allowance changes category in either direction", async () => {
    const a = budgetItem("first", { kind: "Allowance", merchantId: null, expenseCategoryId: "pharmacy" });
    source = plan({ entries: [entry({ kind: "Allowance", expenseCategoryId: "food" })] });
    get.mockImplementation(async path => path.includes("/items?") ? [a] : path.endsWith("/currencies") ? [{ code: "GBP", minorUnits: 2 }] : [spendingEntry("food")]);
    const c = await page(); await c.startNew(); c.useSetup(c.rows()[0], true);
    expect(c.canCarry(c.rows()[0])).toBe(false); expect(c.rows()[0].amount).toBe(""); c.enterAllowance(c.rows()[0], "400"); c.useSetup(c.rows()[0], false);
    expect(c.rows()[0].amount).toBe(""); expect(c.validDraft()).toBe(false);
  });
  it("clears a new row's amount after its definition changes meaning while preserving saved-source rows", async () => {
    const c = await initial(); c.changeRow("internet", { amount: "20" }); c.definitionChanged(budgetItem("first", { currencyCode: "EUR" }));
    expect(c.rows()[0].amount).toBe(""); expect(c.rows()[0].context.currencyCode).toBe("EUR");
    source = plan(); vi.spyOn(window, "confirm").mockReturnValue(true); await c.loadSource(); c.definitionChanged(budgetItem("first", { currencyCode: "USD" }));
    expect(c.rows()[0].context.currencyCode).toBe("GBP"); expect(c.rows()[0].amount).toBe("30.123456");
  });
  it("guards unsaved allowance defaults and blocks plan actions during their write", async () => {
    const c = await page(); await c.startNew(); const setup = c.allowanceSetup()!; setup.change("category", "first-food");
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false); expect(c.canLeave()).toBe(false); await c.load(); expect(setup.draft().category).toBe("first-food");
    const pending = deferred<ReturnType<typeof budgetItem>>(); send.mockReturnValueOnce(pending.promise); const writing = setup.save(); confirm.mockClear();
    expect(c.canLeave()).toBe(false); await c.savePlan(); expect(sendText).not.toHaveBeenCalled(); expect(confirm).not.toHaveBeenCalled();
    pending.resolve(budgetItem("first", { id: "allowance", kind: "Allowance", merchantId: null })); await writing;
  });

  it("rebuilds refreshed source context and clears the amount after an allowance default changes meaning", async () => {
    const a = budgetItem("first", { kind: "Allowance", merchantId: null });
    source = plan({ entries: [entry({ kind: "Allowance", expenseCategoryId: a.expenseCategoryId })] });
    get.mockImplementation(async path => path.includes("/items?") ? [a] : path.endsWith("/currencies") ? [{ code: "GBP", minorUnits: 2 }] : [spendingEntry("food")]);
    const c = await page(); await c.startNew(); c.useSetup(c.rows()[0], true); c.enterAllowance(c.rows()[0], "400");
    const setup = c.allowanceSetup()!; setup.edit(a); setup.change("frequency", "Week"); send.mockResolvedValueOnce({ ...a, frequency: "Week" }); await setup.save();
    expect(c.rows()[0]).toMatchObject({ refreshFromSetup: true, context: { frequency: "Week" }, mode: "Confirm", amount: "" });
    await c.savePlan(); expect(sendText).not.toHaveBeenCalled(); c.enterAllowance(c.rows()[0], "100"); await c.savePlan();
    expect(payload().entries[0]).toMatchObject({ plannedAmount: 100, mode: "Confirm", refreshFromSetup: true });
  });
  it("blocks plan and correction saves until unfinished or uncertain allowance setup is resolved", async () => {
    const c = await page(); await c.startNew(); const setup = c.allowanceSetup()!; setup.change("category", "first-food");
    await c.savePlan(); expect(sendText).not.toHaveBeenCalled(); expect(setup.draft().category).toBe("first-food");
    send.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 })); await setup.save(); expect(setup.needsReload()).toBe(true);
    await c.savePlan(); expect(sendText).not.toHaveBeenCalled(); expect(setup.needsReload()).toBe(true);
    vi.spyOn(window, "confirm").mockReturnValue(true); setup.cancel(); await c.savePlan(); expect(sendText).toHaveBeenCalledTimes(1);
    c.beginCorrection(c.selected()!.entries[0]); setup.change("category", "first-food"); await c.saveCorrection(); expect(sendText).toHaveBeenCalledTimes(1);
    expect(setup.draft().category).toBe("first-food");
  });
  it.each(["400", null])("Keep previous displays the saved allowance value (%s) rather than the edited draft", async amount => {
    const a = budgetItem("first", { kind: "Allowance", merchantId: null });
    source = plan({ entries: [entry({ kind: "Allowance", plannedAmount: amount })] });
    get.mockImplementation(async path => path.includes("/items?") ? [a] : path.endsWith("/currencies") ? [{ code: "GBP", minorUnits: 2 }] : [spendingEntry("food")]);
    const c = await page(); await c.startNew(); c.enterAllowance(c.rows()[0], "450"); c.changeRow("internet", { mode: "Carry" }); await settle();
    const input = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatInputHarness.with({ selector: '[name="amount-internet"]' }));
    expect(await input.getValue()).toBe(amount ?? ""); expect(c.draftEntries()[0].plannedAmount).toBe(amount); await c.savePlan();
    expect(payload().entries[0]).toEqual({ budgetItemId: "internet", mode: "Carry", refreshFromSetup: false });
  });

});
