import { signal } from "@angular/core";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { MatButtonHarness } from "@angular/material/button/testing";
import { MatInputHarness } from "@angular/material/input/testing";
import { MatDialogHarness } from "@angular/material/dialog/testing";
import { MatSelectHarness } from "@angular/material/select/testing";
import { HttpErrorResponse } from "@angular/common/http";
import { provideRouter } from "@angular/router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Api } from "../core/api";
import { Workspace } from "../core/workspace";
import { Household } from "../setup/models";
import { SpendingRecords } from "./records";
import { SpendingHistoryPage, SpendingRecord, SpendingItem } from "./models";
import { deferred, spendingDraft, spendingEntry, spendingItem, spendingRecord } from "../../testing/spending";

describe("Spending history and records", () => {
  let fixture: ComponentFixture<SpendingRecords>;
  let current: ReturnType<typeof signal<Household | null>>;
  let get: ReturnType<typeof vi.fn>, send: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    current = signal<Household | null>({ id: "first", name: "Home", people: [] });
    get = vi.fn(async (path: string) => {
      const home = path.split("/")[1];
      if (path.includes("/categories?")) return [spendingEntry("food", home)];
      if (path.includes("/merchants?")) return [spendingEntry("shop", home)];
      if (path.includes("/usual-price?")) return null;
      if (path.includes("/items?")) return [spendingItem(home)];
      if (path.endsWith("/currencies")) return [{ code: "GBP", minorUnits: 2 }];
      if (path.includes("/records?")) return { records: [spendingRecord(home)], nextCursor: null, pageSize: 25 };
      return spendingRecord(home);
    });
    send = vi.fn();
    TestBed.configureTestingModule({ providers: [provideRouter([]),
      { provide: Workspace, useValue: { current } }, { provide: Api, useValue: { get, send } }] });
  });
  afterEach(() => { fixture?.destroy(); vi.restoreAllMocks(); });
  async function settle() { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
  async function page() {
    fixture = TestBed.createComponent(SpendingRecords); await settle();
    const c = fixture.componentInstance;
    await vi.waitFor(() => { expect(c.lookupLoading()).toBe(false); expect(c.historyLoading()).toBe(false); });
    await settle(); return c;
  }
  it("reads setup and bounded history without writing and displays saved rather than current names", async () => {
    const c = await page();
    expect(get).toHaveBeenCalledTimes(5);
    const path = get.mock.calls.find(([path]) => path.includes("/records?"))![0];
    expect(new URL(path, "http://test/").searchParams.get("pageSize")).toBe("25");
    expect(new URL(path, "http://test/").searchParams.get("includeVoided")).toBe("false");
    expect(send).not.toHaveBeenCalled();
    c.view(spendingRecord()); await settle();
    expect(fixture.nativeElement.textContent).toContain("Saved shop");
    expect(fixture.nativeElement.textContent).toContain("Saved food");
    expect(fixture.nativeElement.textContent).toContain("Receipt total was not supplied");
  });
  it("binds both directions of pagination to the applied inclusive range and void selection", async () => {
    const c = await page();
    c.changeFilter("from", "2026-12-28"); c.changeFilter("to", "2027-01-10"); c.changeFilter("includeVoided", true);
    get.mockResolvedValueOnce({ records: [spendingRecord()], nextCursor: "cursor A", pageSize: 25 });
    await c.loadHistory();
    get.mockResolvedValueOnce({ records: [spendingRecord("first", { id: "next" })], nextCursor: null, pageSize: 25 });
    await c.loadHistory("next");
    let query = new URL(get.mock.calls.at(-1)![0], "http://test/").searchParams;
    expect(Object.fromEntries(query)).toEqual({ from: "2026-12-28", to: "2027-01-10", includeVoided: "true", pageSize: "25", cursor: "cursor A" });
    expect(c.previous()).toEqual([null]);
    get.mockResolvedValueOnce({ records: [spendingRecord()], nextCursor: "cursor A", pageSize: 25 });
    await c.loadHistory("previous");
    query = new URL(get.mock.calls.at(-1)![0], "http://test/").searchParams;
    expect(query.get("cursor")).toBeNull(); expect(query.get("from")).toBe("2026-12-28");
    c.changeFilter("from", "2027-01-01");
    expect(c.filtersChanged()).toBe(true);
    get.mockResolvedValueOnce({ records: [], nextCursor: null, pageSize: 25 });
    await c.loadHistory();
    expect(c.previous()).toEqual([]); expect(c.filtersChanged()).toBe(false);
  });
  it("retains the current page and continuation after a failed next-page read", async () => {
    const c = await page();
    c.page.set({ records: [spendingRecord()], nextCursor: "next", pageSize: 25 });
    get.mockRejectedValueOnce(new Error("History unavailable")); await c.loadHistory("next");
    expect(c.historyError()).toBe("History unavailable");
    expect(c.page().records[0].id).toBe("record"); expect(c.previous()).toEqual([]);
    get.mockResolvedValueOnce({ records: [], nextCursor: null, pageSize: 25 }); await c.loadHistory("next");
    expect(new URL(get.mock.calls.at(-1)![0], "http://test/").searchParams.get("cursor")).toBe("next");
  });
  it("validates reversed and malformed ranges without querying", async () => {
    const c = await page(); get.mockClear();
    c.changeFilter("from", "2026-12-31"); c.changeFilter("to", "2026-12-01"); await c.loadHistory();
    expect(c.historyError()).toContain("valid dates"); expect(get).not.toHaveBeenCalled();
    c.changeFilter("from", "2026-02-30"); await c.loadHistory(); expect(get).not.toHaveBeenCalled();
  });
  it("keeps successful capture separate from a failed history refresh", async () => {
    const c = await page(); get.mockRejectedValueOnce(new Error("History failed"));
    c.onSaved(spendingRecord());
    await settle(); await vi.waitFor(() => expect(c.historyLoading()).toBe(false));
    expect(c.notice()).toBe("Purchase saved."); expect(c.selected()?.id).toBe("record");
    expect(c.historyError()).toBe("History failed"); expect(send).not.toHaveBeenCalled();
  });
  it("shows mismatch, refund signs and void retention without changing saved totals", async () => {
    const c = await page();
    const record = spendingRecord("first", { kind: "Refund", countedAmount: -10, receiptTotal: 12, receiptDifference: 2, reconciliationStatus: "Unreconciled" });
    c.page.set({ records: [record], nextCursor: null, pageSize: 25 }); c.view(record); await settle();
    expect(fixture.nativeElement.textContent).toContain("GBP -10.00");
    expect(fixture.nativeElement.textContent).toContain("Receipt mismatch: GBP 2.00");
    c.view({ ...record, isVoided: true, countedAmount: 0 }); await settle();
    expect(fixture.nativeElement.textContent).toContain("retained but count as zero");
    expect(c.selected()?.totalAmount).toBe(10);
  });

  it.each([["2026-06-01", "1 Jun 2026"], ["2026-01-01", "1 Jan 2026"]])("preserves calendar date %s in list and detail across summer/winter time", async (date, expected) => {
    const c = await page();
    const record = spendingRecord("first", { spendingDate: date });
    c.page.set({ records: [record], nextCursor: null, pageSize: 25 }); c.view(record);
    await settle();
    const text = fixture.nativeElement.textContent as string;
    expect(text.split(expected).length - 1).toBe(2);
    expect(text).not.toContain("31 May 2026");
  });
  it("requires confirmation and the selected revision to void, retains detail and refreshes history", async () => {
    const c = await page(); c.view(spendingRecord());
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    await c.voidRecord(); expect(send).not.toHaveBeenCalled();
    confirm.mockReturnValue(true);
    send.mockResolvedValueOnce(spendingRecord("first", { isVoided: true, countedAmount: 0, revision: "r2" }));
    await c.voidRecord();
    expect(send).toHaveBeenCalledExactlyOnceWith("POST", "households/first/spending/records/record/void", { expectedRevision: "record-r1" });
    expect(c.selected()?.isVoided).toBe(true); expect(c.selected()?.totalAmount).toBe(10);
    await c.voidRecord(); expect(send).toHaveBeenCalledTimes(1);
  });
  it("requires a fresh record read after a conflicting void without automatically retrying it", async () => {
    const c = await page(); c.view(spendingRecord());
    vi.spyOn(window, "confirm").mockReturnValue(true);
    send.mockRejectedValueOnce(new HttpErrorResponse({ status: 409 })); await c.voidRecord();
    expect(c.needsReload().has("record")).toBe(true);
    await c.voidRecord(); expect(send).toHaveBeenCalledTimes(1);
    get.mockResolvedValueOnce(spendingRecord("first", { revision: "latest" })); await c.reloadDetail();
    expect(c.needsReload().has("record")).toBe(false); expect(send).toHaveBeenCalledTimes(1);
    send.mockResolvedValueOnce(spendingRecord("first", { isVoided: true, countedAmount: 0 })); await c.voidRecord();
    expect(send).toHaveBeenLastCalledWith("POST", "households/first/spending/records/record/void", { expectedRevision: "latest" });
  });
  it("ignores a previous detail's late reload when another record is selected", async () => {
    const c = await page(); c.view(spendingRecord());
    const pending = deferred<SpendingRecord>(); get.mockReturnValueOnce(pending.promise);
    const reload = c.reloadDetail(); c.view(spendingRecord("first", { id: "other" }));
    pending.resolve(spendingRecord()); await reload;
    expect(c.selected()?.id).toBe("other"); expect(c.detailLoading()).toBe(false);
  });
  it("ignores an old household's late history and void completions", async () => {
    const c = await page(); c.view(spendingRecord());
    const history = deferred<SpendingHistoryPage>(); get.mockReturnValueOnce(history.promise);
    const reading = c.loadHistory();
    const voided = deferred<SpendingRecord>(); send.mockReturnValueOnce(voided.promise);
    vi.spyOn(window, "confirm").mockReturnValue(true); const writing = c.voidRecord();
    current.set({ id: "second", name: "Second", people: [] }); await settle();
    await vi.waitFor(() => expect(c.historyLoading()).toBe(false));
    history.resolve({ records: [spendingRecord()], nextCursor: null, pageSize: 25 });
    voided.resolve(spendingRecord("first", { isVoided: true })); await reading; await writing;
    expect(c.page().records[0].householdId).toBe("second"); expect(c.selected()).toBeNull();
    expect(c.notice()).toBe(""); expect(c.voiding()).toBe(false);
  });
  it("protects a capture draft, preserves it during history changes and warns on unload", async () => {
    const c = await page();
    c.capture()!.set("notes", "Keep my receipt draft");
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    expect(c.canLeave()).toBe(false); expect(confirm).toHaveBeenCalledOnce();
    await c.loadHistory(); expect(c.capture()!.draft().notes).toBe("Keep my receipt draft");
    const event = new Event("beforeunload", { cancelable: true }); window.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
  });
  it("opens a fresh record for editing and updates details/history after saving", async () => {
    const c = await page(); c.view(spendingRecord());
    get.mockResolvedValueOnce(spendingRecord("first", { revision: "fresh", notes: "Saved note" }));
    await c.beginEdit(); await settle();
    expect(c.editing()?.revision).toBe("fresh"); expect(c.capture()?.draft().notes).toBe("Saved note");
    c.capture()!.set("notes", "Correction");
    send.mockResolvedValueOnce(spendingRecord("first", { notes: "Correction", revision: "new" }));
    await c.capture()!.save(); await settle();
    expect(c.editing()).toBeNull(); expect(c.selected()?.notes).toBe("Correction");
    expect(c.notice()).toBe("Purchase changes saved.");
    expect(c.capture()?.hasUnsavedChanges()).toBe(false);
  });
  it("protects a new purchase draft before entering edit mode and retains it on failed read", async () => {
    const c = await page(); c.view(spendingRecord()); c.capture()!.set("notes", "Keep receipt");
    get.mockClear(); const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    await c.beginEdit(); expect(get).not.toHaveBeenCalled(); expect(c.editing()).toBeNull();
    confirm.mockReturnValue(true); get.mockRejectedValueOnce(new Error("Offline")); await c.beginEdit();
    expect(c.editing()).toBeNull(); expect(c.capture()?.draft().notes).toBe("Keep receipt");
    expect(c.detailError()).toBe("Offline");
  });
  it("blocks detail switching/voiding while editing, guards navigation and cancellation", async () => {
    const c = await page(); c.view(spendingRecord()); await c.beginEdit(); await settle();
    c.capture()!.set("notes", "Edit draft");
    c.view(spendingRecord("first", { id: "other" })); await c.voidRecord();
    expect(c.selected()?.id).toBe("record"); expect(send).not.toHaveBeenCalled();
    vi.spyOn(window, "confirm").mockReturnValue(false);
    expect(c.canLeave()).toBe(false); c.capture()!.cancelEdit(); expect(c.editing()).not.toBeNull();
    vi.mocked(window.confirm).mockReturnValue(true); c.capture()!.cancelEdit(); await settle();
    expect(c.editing()).toBeNull(); expect(c.capture()?.hasUnsavedChanges()).toBe(false);
  });
  it("does not open an editor for refunds, voided records or stale edit reads", async () => {
    const c = await page(); c.view(spendingRecord("first", { kind: "Refund" })); get.mockClear();
    await c.beginEdit(); expect(get).not.toHaveBeenCalled();
    c.view(spendingRecord("first", { isVoided: true })); await c.beginEdit(); expect(get).not.toHaveBeenCalled();
    c.view(spendingRecord()); const pending = deferred<SpendingRecord>(); get.mockReturnValueOnce(pending.promise);
    const opening = c.beginEdit(); c.view(spendingRecord("first", { id: "other" }));
    pending.resolve(spendingRecord()); await opening; expect(c.editing()).toBeNull();
    expect(c.selected()?.id).toBe("other");
    get.mockResolvedValueOnce(spendingRecord("first", { id: "other", isVoided: true })); await c.beginEdit();
    expect(c.editing()).toBeNull(); expect(c.detailError()).toContain("voided");
  });

  it.each([false, true])("updates selected details on editor reload, including voided=%s, before cancelling", async voided => {
    const c = await page(); c.view(spendingRecord()); await c.beginEdit(); await settle();
    const latest = spendingRecord("first", { revision: "latest", notes: "Other changes", isVoided: voided });
    get.mockResolvedValueOnce(latest); await c.capture()!.reloadEdit(); await settle();
    expect(c.selected()?.revision).toBe("latest"); expect(c.selected()?.isVoided).toBe(voided);
    vi.spyOn(window, "confirm").mockReturnValue(true); c.capture()!.cancelEdit(); await settle();
    expect(c.editing()).toBeNull(); expect(c.selected()?.notes).toBe("Other changes");
    await c.voidRecord();
    if (voided) expect(send).not.toHaveBeenCalled();
    else expect(send.mock.calls[0][2]).toEqual({ expectedRevision: "latest" });
  });
  it("allows leaving during a stalled detail read and ignores its late household response", async () => {
    const c = await page(); c.view(spendingRecord());
    const pending = deferred<SpendingRecord>(); get.mockReturnValueOnce(pending.promise);
    const reading = c.reloadDetail(); expect(c.canLeave()).toBe(true);
    current.set({ id: "second", name: "Second", people: [] }); await settle();
    pending.resolve(spendingRecord()); await reading;
    expect(c.selected()).toBeNull(); expect(c.detailLoading()).toBe(false);
  });

  const root = () => TestbedHarnessEnvironment.documentRootLoader(fixture);
  async function itemClosed(c: SpendingRecords) {
    await vi.waitFor(() => expect(c.capture()?.itemEditor() === null).toBe(true)); await settle();
  }
  async function createDialog(c: SpendingRecords, key = c.capture()!.draft().lines[0].key) {
    c.capture()!.createItem(key); await settle();
    return c.capture()!.itemEditor()!.componentInstance;
  }
  it("creates an item through the rendered purchase form and selects it on the initiating line without saving the purchase", async () => {
    const c = await page(), capture = c.capture()!;
    capture.set("merchantId", "first-shop"); capture.setCurrency("GBP"); capture.set("notes", "Keep this visit");
    const first = capture.draft().lines[0].key;
    capture.changeLine(first, "categoryId", "first-food"); capture.changeLine(first, "amount", "4");
    capture.addLine(); const key = capture.draft().lines[1].key; capture.changeLine(key, "categoryId", "first-food");
    await settle();
    await (await root().getHarness(MatButtonHarness.with({ selector: '[aria-label="Create item for line 2"]' }))).click();
    const dialog = await root().getHarness(MatDialogHarness);
    await vi.waitFor(() => expect(document.activeElement?.getAttribute("name")).toBe("name"));
    const fields = { code: "MILK-1L", name: "Milk", unitKey: "litre", unitLabel: "Litre", notes: "Keep chilled" };
    for (const [name, value] of Object.entries(fields)) {
      await (await dialog.getHarness(MatInputHarness.with({ selector: `[name="${name}"]` }))).setValue(value);
    }
    const created = spendingItem("first", { id: "milk", ...fields }); send.mockResolvedValueOnce(created);
    await (await dialog.getHarness(MatButtonHarness.with({ text: "Create & use item" }))).click();
    await settle();
    await itemClosed(c);
    expect(send).toHaveBeenCalledExactlyOnceWith("POST", "households/first/spending/items", { ...fields, expenseCategoryId: "first-food" });
    expect(c.items()).toContainEqual(created);
    expect(capture.line(key)).toMatchObject({ itemId: "milk", categoryId: "first-food", quantity: "1", unitPrice: "" });
    expect(capture.line(first)?.amount).toBe("4"); expect(capture.draft().notes).toBe("Keep this visit");
    const picker = await root().getHarness(MatSelectHarness.with({ selector: '[aria-label="Catalogue item for line 2"]' }));
    expect(await picker.getValueText()).toContain("MILK-1L · Milk");
    expect(await root().getAllHarnesses(MatDialogHarness)).toHaveLength(0);
    capture.changeLine(key, "unitPrice", "2");
    send.mockResolvedValueOnce(spendingRecord()); await capture.save();
    expect(send.mock.calls[1][2].lines[1]).toMatchObject({ spendingItemId: "milk", quantity: 1, unitPrice: 2, netAmount: 2 });
  });
  it("retains rejected item fields, permits correction and opens the next dialog without invalid fields", async () => {
    const c = await page(), editor = await createDialog(c);
    editor.draft.set(spendingDraft()); send.mockRejectedValueOnce(new HttpErrorResponse({ status: 409, error: { detail: "Code already exists" } }));
    await (await root().getHarness(MatButtonHarness.with({ text: "Create & use item" }))).click(); await settle();
    expect(editor.draft()).toEqual(spendingDraft()); expect(editor.error()).toBe("Code already exists");
    expect(c.items().some(item => item.id === "milk")).toBe(false);
    editor.change("code", "MILK-2L"); send.mockResolvedValueOnce(spendingItem("first", { id: "milk", code: "MILK-2L" }));
    await editor.save(); await itemClosed(c);
    await createDialog(c); await settle();
    const form = document.querySelector('form[aria-label="Create spending item"]')!;
    expect(form.classList.contains("ng-submitted")).toBe(false);
    expect(form.querySelectorAll(".mat-form-field-invalid")).toHaveLength(0);
    expect(form.querySelector<HTMLInputElement>('[name="name"]')?.value).toBe("");
  });
  it("protects an item draft on cancel, Escape and navigation while preserving the purchase", async () => {
    const c = await page(), capture = c.capture()!;
    capture.set("notes", "Keep purchase"); const before = structuredClone(capture.draft());
    const editor = await createDialog(c); editor.change("name", "Unsaved milk");
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    editor.dismiss(); expect(capture.itemEditor()).not.toBeNull();
    await (await root().getHarness(MatDialogHarness)).close(); expect(capture.itemEditor()).not.toBeNull();
    expect(c.canLeave()).toBe(false);
    const event = new Event("beforeunload", { cancelable: true }); window.dispatchEvent(event); expect(event.defaultPrevented).toBe(true);
    confirm.mockReturnValue(true); editor.dismiss(); await itemClosed(c);
    expect(capture.itemEditor()).toBeNull(); expect(capture.draft()).toEqual(before); expect(send).not.toHaveBeenCalled();
  });
  it("includes item-only drafts in guards and blocks duplicate submissions, cancellation and navigation during creation", async () => {
    const c = await page(), editor = await createDialog(c);
    expect(c.hasUnsavedChanges()).toBe(false); editor.draft.set(spendingDraft()); expect(c.hasUnsavedChanges()).toBe(true);
    const pending = deferred<SpendingItem>(); send.mockReturnValueOnce(pending.promise);
    const saving = editor.save(); await editor.save();
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    editor.dismiss(); expect(c.canLeave()).toBe(false); expect(confirm).not.toHaveBeenCalled();
    expect(c.capture()?.itemEditor()).not.toBeNull(); expect(send).toHaveBeenCalledTimes(1);
    await c.capture()!.save(); expect(send).toHaveBeenCalledTimes(1);
    pending.resolve(spendingItem("first", { id: "milk" })); await saving; await itemClosed(c);
    expect(c.busy()).toBe(false);
  });
  it.each([false, true])("ignores a pending creation after a forced household switch (failure=%s)", async fail => {
    const c = await page(), editor = await createDialog(c); editor.draft.set(spendingDraft());
    const pending = deferred<SpendingItem>(); send.mockReturnValueOnce(pending.promise); const saving = editor.save();
    current.set({ id: "second", name: "Second", people: [] }); await settle();
    await vi.waitFor(() => expect(c.lookupLoading()).toBe(false)); await settle();
    c.capture()!.set("notes", "New household draft");
    if (fail) pending.reject(new Error("Old error")); else pending.resolve(spendingItem("first", { id: "milk" }));
    await saving; await settle();
    expect(c.items().every(item => item.householdId === "second")).toBe(true);
    expect(c.capture()?.draft().notes).toBe("New household draft"); expect(c.capture()?.itemEditor()).toBeNull();
    expect(c.capture()?.draft().lines[0].itemId).toBe(""); expect(c.busy()).toBe(false);
  });
  it("does not allow frozen/disabled purchase forms to open item creation", async () => {
    const c = await page(), capture = c.capture()!, key = capture.draft().lines[0].key;
    c.lookupLoading.set(true); await settle(); capture.createItem(key); expect(capture.itemEditor()).toBeNull();
    c.lookupLoading.set(false); await settle();
    capture.frozen.set({ id: "id", merchantId: "shop", spendingDate: "2026-11-18", currencyCode: "GBP", kind: "Payment", receiptTotal: null, externalReference: null, notes: null, lines: [] });
    capture.createItem(key); expect(capture.itemEditor()).toBeNull();
  });
  it("prevents a stale lookup from dropping an item created while correcting a saved purchase", async () => {
    const c = await page(); c.view(spendingRecord()); await c.beginEdit(); await settle();
    const capture = c.capture()!, editor = await createDialog(c); editor.draft.set(spendingDraft());
    const older = deferred<SpendingItem[]>();
    get.mockImplementationOnce(async () => [spendingEntry("food")]).mockImplementationOnce(async () => [spendingEntry("shop")]).mockReturnValueOnce(older.promise);
    const reading = c.loadLookups(); const created = spendingItem("first", { id: "milk" }); send.mockResolvedValueOnce(created);
    await editor.save(); await itemClosed(c);
    older.resolve([spendingItem()]); await reading; await settle();
    expect(c.items()).toContainEqual(created); expect(capture.draft().lines[0].itemId).toBe("milk");
    expect(c.editing()?.id).toBe("record"); expect(capture.editOperation().kind).toBe("replace");
  });

  it.each([false, true])("returns keyboard focus to the initiating receipt line after closing creation (saved=%s)", async saved => {
    const c = await page(), capture = c.capture()!; capture.addLine(); await settle();
    const trigger = fixture.nativeElement.querySelector('button[aria-label="Create item for line 2"]') as HTMLButtonElement;
    trigger.focus(); const editor = await createDialog(c, capture.draft().lines[1].key);
    expect(trigger.disabled).toBe(false);
    await vi.waitFor(() => expect(document.activeElement?.getAttribute("name")).toBe("name"));
    if (saved) { editor.draft.set(spendingDraft()); send.mockResolvedValueOnce(spendingItem("first", { id: "milk" })); await editor.save(); }
    else editor.dismiss();
    await itemClosed(c); expect(document.activeElement).toBe(trigger);
  });

});
