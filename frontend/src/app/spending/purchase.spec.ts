import { signal } from "@angular/core";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { MatButtonHarness } from "@angular/material/button/testing";
import { MatInputHarness } from "@angular/material/input/testing";
import { MatDialogHarness } from "@angular/material/dialog/testing";
import { MatAutocompleteHarness } from "@angular/material/autocomplete/testing";
import { HttpErrorResponse } from "@angular/common/http";
import { ActivatedRoute, convertToParamMap, provideRouter, Router } from "@angular/router";
import { BehaviorSubject } from "rxjs";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Api } from "../core/api";
import { Workspace } from "../core/workspace";
import { Household } from "../setup/models";
import { SpendingPurchase } from "./purchase";
import { SpendingItem, SpendingRecord } from "./models";
import { deferred, spendingDraft, spendingEntry, spendingItem, spendingRecord } from "../../testing/spending";

describe("Purchase creation, view and editing", () => {
  let fixture: ComponentFixture<SpendingPurchase>;
  let current: ReturnType<typeof signal<Household | null>>;
  let get: ReturnType<typeof vi.fn>, send: ReturnType<typeof vi.fn>;
  let params: BehaviorSubject<ReturnType<typeof convertToParamMap>>;
  beforeEach(() => {
    vi.spyOn(window, "scrollTo").mockImplementation(() => {});
    current = signal<Household | null>({ id: "first", name: "Home", people: [] });
    params = new BehaviorSubject(convertToParamMap({ recordId: "record" }));
    get = vi.fn(async (path: string) => {
      const home = path.split("/")[1];
      if (path.includes("/categories?")) return [spendingEntry("food", home)];
      if (path.includes("/merchants?")) return [spendingEntry("shop", home)];
      if (path.includes("/usual-price?") || path.includes("/last-paid-price?")) return null;
      if (path.includes("/items?")) return [spendingItem(home)];
      if (path.endsWith("/currencies")) return [{ code: "GBP", minorUnits: 2 }];
      return spendingRecord(home, { id: path.split("/").at(-1) });
    });
    send = vi.fn();
    TestBed.configureTestingModule({ providers: [provideRouter([]),
      { provide: Workspace, useValue: { current } }, { provide: Api, useValue: { get, send } }] });
  });
  afterEach(() => { fixture?.destroy(); vi.restoreAllMocks(); });
  async function settle() { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
  async function page(create = true) {
    TestBed.overrideProvider(ActivatedRoute, { useValue: { paramMap: params.asObservable(), snapshot: { paramMap: params.value, data: { create } } } });
    vi.spyOn(TestBed.inject(Router), "navigate").mockResolvedValue(true);
    fixture = TestBed.createComponent(SpendingPurchase); await settle();
    const c = fixture.componentInstance;
    await vi.waitFor(() => { expect(c.lookupLoading()).toBe(false); expect(c.detailLoading()).toBe(false); });
    await settle(); return c;
  }
  it("opens a purchase read-only from its URL without loading entry choices or history", async () => {
    const c = await page(false); expect(get).toHaveBeenCalledExactlyOnceWith("households/first/spending/records/record");
    expect(c.selected()?.id).toBe("record"); expect(c.capture()).toBeUndefined(); expect(c.editing()).toBeNull();
    expect(fixture.nativeElement.textContent).toContain("Saved shop"); expect(fixture.nativeElement.textContent).toContain("Saved food");
    expect(fixture.nativeElement.querySelector('a[href="/spending/records"]')).not.toBeNull(); expect(send).not.toHaveBeenCalled();
  });
  it("shows costs and discounts once, using saved amounts and labels", async () => {
    const record = spendingRecord("first", { purchaseAmount: 10, additionalCostAmount: 3.99, grossAmount: 13.99, discountAmount: 2, totalAmount: 11.99,
      discounts: [{ id: "discount", position: 1, expenseCategoryId: "first-food", expenseCategoryName: "Saved food", amount: 2, description: "Bundle" }] });
    record.lines.push({ ...record.lines[0], id: "cost", position: 2, isAdditionalCost: true, netAmount: 3.99, description: "Express delivery" });
    get.mockResolvedValueOnce(record); await page(false);
    const section = fixture.nativeElement.querySelector('[aria-label="Additional costs"]'); expect(section.textContent).toContain("Express delivery");
    expect(fixture.nativeElement.textContent.split("Express delivery")).toHaveLength(2);
    expect(fixture.nativeElement.textContent).toContain("Recorded spending total: GBP 11.99"); expect(fixture.nativeElement.textContent).toContain("Bundle");
  });
  it("fetches a fresh revision before editing, then returns to view after saving", async () => {
    const c = await page(false); get.mockResolvedValueOnce(spendingRecord("first", { revision: "fresh", notes: "Current" }));
    await c.beginEdit(); await settle(); expect(c.editing()?.revision).toBe("fresh"); expect(c.capture()?.draft().notes).toBe("Current");
    expect(fixture.nativeElement.querySelector("#record-title")).toBeNull();
    c.capture()!.set("notes", "Correction"); send.mockResolvedValueOnce(spendingRecord("first", { revision: "saved", notes: "Correction" }));
    await c.capture()!.save(); await settle(); expect(c.editing()).toBeNull(); expect(c.capture()).toBeUndefined();
    expect(c.selected()?.notes).toBe("Correction"); expect(c.notice()).toBe("Purchase changes saved."); expect(c.hasUnsavedChanges()).toBe(false);
    expect(send.mock.calls[0][2].expectedRevision).toBe("fresh");
  });
  it("confirms cancellation and retains the latest saved detail after editor reload", async () => {
    const c = await page(false); await c.beginEdit(); await settle(); c.capture()!.set("notes", "Draft");
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false); c.capture()!.cancelEdit(); expect(c.editing()).not.toBeNull();
    confirm.mockReturnValue(true); get.mockResolvedValueOnce(spendingRecord("first", { revision: "latest", notes: "Other change" }));
    await c.capture()!.reloadEdit(); await settle(); c.capture()!.cancelEdit(); await settle();
    expect(c.selected()?.revision).toBe("latest"); expect(c.selected()?.notes).toBe("Other change"); expect(c.capture()).toBeUndefined();
  });
  it.each(["missing", "foreign"])("handles %s detail without exposing stale data or an empty creation form", async kind => {
    if (kind === "missing") get.mockRejectedValueOnce(new HttpErrorResponse({ status: 404 }));
    else get.mockResolvedValueOnce(spendingRecord("second"));
    const c = await page(false); expect(c.selected()).toBeNull(); expect(c.capture()).toBeUndefined(); expect(c.detailError()).not.toBe("");
    get.mockResolvedValueOnce(spendingRecord()); await c.reloadDetail(); await settle(); expect(c.selected()?.id).toBe("record"); expect(c.detailError()).toBe("");
  });
  it("rejects late record responses after parameter changes and household round trips", async () => {
    const c = await page(false), pending = deferred<SpendingRecord>(); get.mockReturnValueOnce(pending.promise);
    const loading = c.reloadDetail(); params.next(convertToParamMap({ recordId: "other" })); await settle();
    pending.resolve(spendingRecord()); await loading; expect(c.selected()?.id).toBe("other"); expect(c.detailLoading()).toBe(false);
    const old = deferred<SpendingRecord>(); get.mockReturnValueOnce(old.promise); const reload = c.reloadDetail();
    current.set({ id: "second", name: "Other", people: [] }); await settle(); current.set({ id: "first", name: "Home", people: [] }); await settle();
    old.resolve(spendingRecord("first", { id: "other", notes: "Stale" })); await reload; expect(c.selected()?.notes).not.toBe("Stale");
  });
  it.each(["refund", "voided"])("keeps %s records read-only", async kind => {
    get.mockResolvedValueOnce(spendingRecord("first", { kind: kind === "refund" ? "Refund" : "Payment", isVoided: kind === "voided" }));
    const c = await page(false); get.mockClear(); await c.beginEdit(); expect(get).not.toHaveBeenCalled(); expect(c.capture()).toBeUndefined();
  });
  it("keeps view mode on failed or newly voided edit reads", async () => {
    const c = await page(false); get.mockRejectedValueOnce(new Error("Offline")); await c.beginEdit(); expect(c.editing()).toBeNull(); expect(c.detailError()).toBe("Offline");
    get.mockResolvedValueOnce(spendingRecord("first", { isVoided: true })); await c.beginEdit(); expect(c.editing()).toBeNull(); expect(c.selected()?.isVoided).toBe(true);
  });
  it("uses explicit confirmation and revision to void, blocking leave while writing and reload before retry", async () => {
    const c = await page(false), confirm = vi.spyOn(window, "confirm").mockReturnValue(false); await c.voidRecord(); expect(send).not.toHaveBeenCalled();
    confirm.mockReturnValue(true); const pending = deferred<SpendingRecord>(); send.mockReturnValueOnce(pending.promise); const writing = c.voidRecord();
    expect(c.canLeave()).toBe(false); const event = new Event("beforeunload", { cancelable: true }); window.dispatchEvent(event); expect(event.defaultPrevented).toBe(true);
    pending.reject(new HttpErrorResponse({ status: 409 })); await writing; expect(c.needsReload().has("record")).toBe(true);
    await c.voidRecord(); expect(send).toHaveBeenCalledTimes(1);
    get.mockResolvedValueOnce(spendingRecord("first", { revision: "latest" })); await c.reloadDetail();
    send.mockResolvedValueOnce(spendingRecord("first", { isVoided: true, countedAmount: 0 })); await c.voidRecord();
    expect(send.mock.calls[1][2]).toEqual({ expectedRevision: "latest" }); expect(c.selected()?.isVoided).toBe(true); expect(c.selected()?.totalAmount).toBe(10);
  });
  it.each([["2026-06-01", "1 Jun 2026"], ["2026-01-01", "1 Jan 2026"]])("preserves saved calendar date %s", async (date, expected) => {
    get.mockResolvedValueOnce(spendingRecord("first", { spendingDate: date })); await page(false); expect(fixture.nativeElement.textContent).toContain(expected);
  });

  const root = () => TestbedHarnessEnvironment.documentRootLoader(fixture);
  async function itemClosed(c: SpendingPurchase) {
    await vi.waitFor(() => expect(c.capture()?.itemEditor() === null).toBe(true)); await settle();
  }
  async function createDialog(c: SpendingPurchase, key = c.capture()!.draft().lines[0].key) {
    c.capture()!.createItem(key); await settle();
    return c.capture()!.itemEditor()!.componentInstance;
  }
  it("creates an item through the rendered purchase form and selects it on the initiating line without saving the purchase", async () => {
    const c = await page(), capture = c.capture()!;
    capture.set("merchantId", "first-shop"); capture.setCurrency("GBP"); capture.set("notes", "Keep this visit");
    const first = capture.draft().lines[0].key;
    capture.setLineKind(first, "category");
    capture.changeLine(first, "categoryId", "first-food"); capture.changeLine(first, "amount", "4");
    capture.addLine(); const key = capture.draft().lines[1].key; capture.changeLine(key, "categoryId", "first-food");
    await settle();
    await (await root().getHarness(MatButtonHarness.with({ selector: '[aria-label="Create item for line 2"]' }))).click();
    const dialog = await root().getHarness(MatDialogHarness);
    await vi.waitFor(() => expect(document.activeElement?.getAttribute("name")).toBe("name"));
    const fields = { code: "MILK-1L", name: "Milk", unitLabel: "Litre", notes: "Keep chilled" };
    const internalKey = capture.itemEditor()!.componentInstance.draft().unitKey;
    expect(internalKey).toMatch(/^[a-f0-9]{32}$/);
    (document.querySelector('form[aria-label="Create spending item"] details') as HTMLDetailsElement).open = true;
    for (const [name, value] of Object.entries(fields)) {
      await (await dialog.getHarness(MatInputHarness.with({ selector: `[name="${name}"]` }))).setValue(value);
    }
    const created = spendingItem("first", { id: "milk", unitKey: internalKey, ...fields }); send.mockResolvedValueOnce(created);
    await (await dialog.getHarness(MatButtonHarness.with({ text: "Create & use item" }))).click();
    await settle();
    await itemClosed(c);
    expect(send).toHaveBeenCalledExactlyOnceWith("POST", "households/first/spending/items", { ...fields, unitKey: internalKey, expenseCategoryId: "first-food" });
    expect(c.items()).toContainEqual(created);
    expect(capture.line(key)).toMatchObject({ itemId: "milk", categoryId: "first-food", quantity: "1", unitPrice: "" });
    expect(capture.line(first)?.amount).toBe("4"); expect(capture.draft().notes).toBe("Keep this visit");
    const picker = await root().getHarness(MatAutocompleteHarness.with({ selector: '[aria-label="Item for line 2"]' }));
    expect(await picker.getValue()).toBe("Milk");
    expect(fixture.nativeElement.textContent).toContain("MILK-1L · Litre");
    expect(await root().getAllHarnesses(MatDialogHarness)).toHaveLength(0);
    capture.changeLine(key, "unitPrice", "2");
    send.mockImplementationOnce(async (_method: string, _path: string, body: { id: string }) => spendingRecord("first", { id: body.id })); await capture.save();
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
    const c = await page(false); await c.beginEdit(); await settle();
    const capture = c.capture()!; capture.setLineKind(capture.draft().lines[0].key, "item");
    const editor = await createDialog(c); editor.draft.set(spendingDraft());
    const older = deferred<SpendingItem[]>();
    get.mockImplementationOnce(async () => [spendingEntry("food")]).mockImplementationOnce(async () => [spendingEntry("shop")]).mockReturnValueOnce(older.promise);
    const reading = c.loadLookups(); const created = spendingItem("first", { id: "milk" }); send.mockResolvedValueOnce(created);
    await editor.save(); await itemClosed(c);
    older.resolve([spendingItem()]); await reading; await settle();
    expect(c.items()).toContainEqual(created); expect(capture.draft().lines[0].itemId).toBe("milk");
    expect(c.editing()?.id).toBe("record"); expect(capture.editOperation().kind).toBe("replace");
  });

  it.each([false, true])("returns keyboard focus to the initiating receipt line after closing creation (saved=%s)", async saved => {
    const c = await page(), capture = c.capture()!; capture.set("merchantId", "first-shop"); capture.addLine(); await settle();
    const trigger = fixture.nativeElement.querySelector('button[aria-label="Create item for line 2"]') as HTMLButtonElement;
    trigger.focus(); const editor = await createDialog(c, capture.draft().lines[1].key);
    expect(trigger.disabled).toBe(false);
    await vi.waitFor(() => expect(document.activeElement?.getAttribute("name")).toBe("name"));
    if (saved) { editor.draft.set(spendingDraft()); send.mockResolvedValueOnce(spendingItem("first", { id: "milk" })); await editor.save(); }
    else editor.dismiss();
    await itemClosed(c); expect(document.activeElement === trigger).toBe(true);
  });

  it("uses a newly created item's actual category even when it differs from the initial filter", async () => {
    const c = await page(); c.categories.update(categories => [...categories, { ...spendingEntry("food"), id: "clothing", name: "Clothing" }]); await settle();
    const capture = c.capture()!, key = capture.draft().lines[0].key;
    capture.changeLine(key, "categoryId", "first-food"); const editor = await createDialog(c);
    expect(editor.draft().expenseCategoryId).toBe("first-food");
    for (const [field, value] of Object.entries({ code: "COAT", name: "Coat", unitLabel: "Each", expenseCategoryId: "clothing" })) editor.change(field as "code" | "name" | "unitLabel" | "expenseCategoryId", value);
    const created = spendingItem("first", { id: "coat", code: "COAT", name: "Coat", expenseCategoryId: "clothing", unitLabel: "Each", unitKey: editor.draft().unitKey });
    send.mockResolvedValueOnce(created); await editor.save(); await itemClosed(c);
    expect(capture.line(key)).toMatchObject({ kind: "item", itemId: "coat", categoryId: "clothing" });
    expect(capture.matchingItems(capture.line(key)!)).toContainEqual(created);
    const picker = await root().getHarness(MatAutocompleteHarness.with({ selector: '[aria-label="Item for line 1"]' }));
    expect(await picker.getValue()).toBe("Coat"); expect(send.mock.calls[0][1]).toBe("households/first/spending/items");
  });

});
