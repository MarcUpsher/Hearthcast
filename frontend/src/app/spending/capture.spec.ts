import { signal } from "@angular/core";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { HttpErrorResponse } from "@angular/common/http";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { MatSelectHarness } from "@angular/material/select/testing";
import { MatButtonHarness } from "@angular/material/button/testing";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Api } from "../core/api";
import { Workspace } from "../core/workspace";
import { Household } from "../setup/models";
import { SpendingCapture } from "./capture";
import { SpendingRecord, SpendingUsualPrice } from "./models";
import { deferred, spendingEntry, spendingItem, spendingPrice, spendingRecord } from "../../testing/spending";

describe("Spending purchase capture", () => {
  let fixture: ComponentFixture<SpendingCapture>;
  let current: ReturnType<typeof signal<Household | null>>;
  let get: ReturnType<typeof vi.fn>, send: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    current = signal<Household | null>({ id: "first", name: "Home", people: [] });
    get = vi.fn(async () => spendingPrice()); send = vi.fn(async () => spendingRecord());
    TestBed.configureTestingModule({ providers: [
      { provide: Workspace, useValue: { current } }, { provide: Api, useValue: { get, send } }] });
  });
  afterEach(() => { fixture?.destroy(); vi.restoreAllMocks(); });
  async function settle() { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
  async function page() {
    fixture = TestBed.createComponent(SpendingCapture);
    fixture.componentRef.setInput("categories", [spendingEntry("food")]);
    fixture.componentRef.setInput("merchants", [spendingEntry("shop")]);
    fixture.componentRef.setInput("items", [spendingItem()]);
    fixture.componentRef.setInput("currencies", [{ code: "GBP", minorUnits: 2 }, { code: "EUR", minorUnits: 2 }, { code: "JPY", minorUnits: 0 }, { code: "BHD", minorUnits: 3 }]);
    await settle(); return fixture.componentInstance;
  }
  function quick(c: SpendingCapture, amount = "10") {
    c.set("date", "2026-11-18"); c.set("merchantId", "first-shop"); c.setCurrency("GBP");
    const key = c.draft().lines[0].key;
    c.changeLine(key, "categoryId", "first-food"); c.changeLine(key, "amount", amount);
    return key;
  }
  const button = (text: string) => TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(MatButtonHarness.with({ text }));

  it("captures a quick category amount with explicit currency, date, notes and safe client identity", async () => {
    const c = await page();
    expect(c.hasUnsavedChanges()).toBe(false);
    const key = quick(c, "0");
    c.set("receiptTotal", "0"); c.set("notes", "Coffee");
    c.changeLine(key, "description", "Morning visit");
    await settle();
    await (await button("Save purchase")).click();
    const body = send.mock.calls[0][2];
    expect(send.mock.calls[0].slice(0, 2)).toEqual(["POST", "households/first/spending/records"]);
    expect(body).toMatchObject({ id: expect.any(String), kind: "Payment", currencyCode: "GBP", spendingDate: "2026-11-18", merchantId: "first-shop", receiptTotal: 0, notes: "Coffee" });
    expect(body.lines).toEqual([{ expenseCategoryId: "first-food", netAmount: 0, description: "Morning visit", merchantItemReference: null, promotionStatus: "Unknown" }]);
    expect(body.rememberPrices).toBeUndefined();
    expect(c.hasUnsavedChanges()).toBe(false);
    await settle();
    const form = fixture.nativeElement.querySelector("form");
    expect(form.classList.contains("ng-submitted")).toBe(false);
    expect(form.querySelectorAll(".mat-form-field-invalid")).toHaveLength(0);
  });

  it("prefills an exact usual price for the selected identity, calculates, and preserves an actual discount override", async () => {
    const c = await page();
    c.set("merchantId", "first-shop"); c.setCurrency("GBP");
    const key = c.draft().lines[0].key;
    c.selectItem(key, "eggs");
    await settle();
    expect(get).toHaveBeenCalledExactlyOnceWith("households/first/spending/items/eggs/usual-price?merchantId=first-shop&currencyCode=GBP");
    expect(c.line(key)).toMatchObject({ categoryId: "first-food", quantity: "1", unitPrice: "3", amount: "3.00" });
    c.changeLine(key, "quantity", "2");
    expect(c.line(key)?.amount).toBe("6.00");
    c.changeLine(key, "amount", "5"); c.changeLine(key, "promotion", "Yes"); c.changeLine(key, "reference", "EGG SPECIAL");
    c.changeLine(key, "unitPrice", "3.125");
    expect(c.line(key)?.amount).toBe("5");
    c.addLine();
    const other = c.draft().lines[1].key;
    c.changeLine(other, "categoryId", "first-food"); c.changeLine(other, "amount", "2");
    c.set("receiptTotal", "8");
    expect(c.total()).toBe("7.00"); expect(c.difference()).toBe("1.00");
    await c.save();
    expect(send.mock.calls[0][2].lines[0]).toMatchObject({ spendingItemId: "eggs", quantity: 2, unitPrice: 3.125, netAmount: 5, promotionStatus: "Yes", merchantItemReference: "EGG SPECIAL" });
    expect(send.mock.calls[0][2].lines).toHaveLength(2);
    expect(send.mock.calls[0][2].rememberPrices).toBeUndefined();
  });

  it("does not let late usual-price suggestions replace manually entered values", async () => {
    const c = await page();
    const price = deferred<SpendingUsualPrice>(); get.mockReturnValueOnce(price.promise);
    c.set("merchantId", "first-shop"); c.setCurrency("GBP");
    const key = c.draft().lines[0].key; c.selectItem(key, "eggs");
    c.changeLine(key, "unitPrice", "2"); c.changeLine(key, "amount", "1.75");
    price.resolve(spendingPrice()); await settle();
    expect(c.line(key)).toMatchObject({ unitPrice: "2", amount: "1.75" });
  });

  it("clears old suggestions when merchant changes and ignores an older identity's result", async () => {
    const c = await page();
    const first = deferred<SpendingUsualPrice>(); get.mockReturnValueOnce(first.promise).mockResolvedValueOnce(null);
    c.set("merchantId", "first-shop"); c.setCurrency("GBP");
    const key = c.draft().lines[0].key; c.selectItem(key, "eggs");
    c.set("merchantId", "other-shop"); await settle();
    first.resolve(spendingPrice()); await settle();
    expect(c.line(key)?.unitPrice).toBe("");
    expect(c.priceStates()[key]).toContain("No usual price");
    c.changeLine(key, "unitPrice", "4");
    expect(c.valid()).toBe(true);
  });

  it("keeps capture usable after a failed suggestion lookup and rounds the entered calculation exactly", async () => {
    const c = await page(); get.mockRejectedValue(new Error("Offline suggestion"));
    c.set("merchantId", "first-shop"); c.setCurrency("GBP");
    const key = c.draft().lines[0].key; c.selectItem(key, "eggs");
    await settle();
    expect(c.priceStates()[key]).toContain("unavailable");
    c.changeLine(key, "unitPrice", "1.005");
    expect(c.line(key)?.amount).toBe("1.01");
    expect(c.valid()).toBe(true);
  });

  it("supports currency-specific precision and treats blank amounts differently from zero", async () => {
    const c = await page(); const key = quick(c, "");
    expect(c.valid()).toBe(false);
    c.changeLine(key, "amount", "0"); expect(c.valid()).toBe(true);
    vi.spyOn(window, "confirm").mockReturnValue(true);
    c.setCurrency("JPY"); c.changeLine(key, "amount", "1.01"); expect(c.valid()).toBe(false);
    c.changeLine(key, "amount", "1"); expect(c.valid()).toBe(true);
    c.setCurrency("BHD"); c.changeLine(key, "amount", "1.001"); expect(c.valid()).toBe(true);
    c.changeLine(key, "amount", "1.0001"); expect(c.valid()).toBe(false);
  });

  it.each([400, 404])("retains editable fields after a known HTTP %s rejection and uses the same identity after correction", async status => {
    const c = await page(); const key = quick(c);
    send.mockRejectedValueOnce(new HttpErrorResponse({ status, error: { detail: "Fix this amount" } }));
    await c.save();
    const first = send.mock.calls[0][2];
    expect(c.frozen()).toBeNull(); expect(c.line(key)?.amount).toBe("10");
    c.changeLine(key, "amount", "9"); await c.save();
    expect(send.mock.calls[1][2].id).toBe(first.id);
    expect(send.mock.calls[1][2].lines[0].netAmount).toBe(9);
  });

  it.each([0, 408, 503])("locks an uncertain save and retries exactly the same request after HTTP %s", async status => {
    const c = await page(); const key = quick(c);
    send.mockRejectedValueOnce(new HttpErrorResponse({ status }));
    await c.save();
    const first = JSON.stringify(send.mock.calls[0][2]);
    expect(c.frozen()).not.toBeNull();
    c.changeLine(key, "amount", "999"); c.set("notes", "Changed");
    expect(c.line(key)?.amount).toBe("10");
    await c.save();
    expect(JSON.stringify(send.mock.calls[1][2])).toBe(first);
    expect(c.hasUnsavedChanges()).toBe(false);
  });

  it("can recover a saved record after a lost response without issuing another write", async () => {
    const c = await page(); quick(c);
    send.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 })); await c.save();
    const id = c.frozen()!.id;
    get.mockResolvedValueOnce(spendingRecord("first", { id }));
    const received = vi.fn(); c.saved.subscribe(received);
    await c.checkSaved();
    expect(get).toHaveBeenLastCalledWith(`households/first/spending/records/${id}`);
    expect(send).toHaveBeenCalledTimes(1);
    expect(received).toHaveBeenCalledOnce();
    expect(c.hasUnsavedChanges()).toBe(false);
  });

  it("does not treat a not-yet-found save as permission to generate a new purchase identity", async () => {
    const c = await page(); quick(c);
    send.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 })); await c.save();
    const id = c.frozen()!.id;
    get.mockRejectedValueOnce(new HttpErrorResponse({ status: 404 })); await c.checkSaved();
    expect(c.frozen()?.id).toBe(id);
    expect(c.error()).toContain("Retry the same save");
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    c.clear(); expect(c.frozen()?.id).toBe(id); expect(confirm).toHaveBeenCalledOnce();
    confirm.mockReturnValue(true); c.clear(); expect(c.hasUnsavedChanges()).toBe(false);
  });

  it.each(["0", "3.25"])("confirms currency changes when only receipt total %s is populated", async amount => {
    const c = await page();
    c.setCurrency("GBP"); c.set("receiptTotal", amount);
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    c.setCurrency("EUR");
    expect(confirm).toHaveBeenCalledOnce();
    expect(c.draft().currencyCode).toBe("GBP"); expect(c.draft().receiptTotal).toBe(amount);
    confirm.mockReturnValue(true); c.setCurrency("EUR");
    expect(c.draft().currencyCode).toBe("EUR"); expect(c.draft().receiptTotal).toBe(amount);
  });

  it("protects a draft and blocks duplicate submissions while writing", async () => {
    const c = await page(); quick(c);
    const pending = deferred<SpendingRecord>(); send.mockReturnValueOnce(pending.promise);
    const writing = c.save(); await c.save();
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    expect(c.canLeave()).toBe(false); c.clear(); expect(confirm).not.toHaveBeenCalled();
    expect(send).toHaveBeenCalledTimes(1);
    pending.resolve(spendingRecord()); await writing;
    quick(c); confirm.mockReturnValue(false); expect(c.canLeave()).toBe(false);
  });

  it("ignores pending save completions after a forced household switch", async () => {
    const c = await page(); quick(c);
    const pending = deferred<SpendingRecord>(); send.mockReturnValueOnce(pending.promise);
    const writing = c.save();
    current.set({ id: "second", name: "Second", people: [] }); await settle();
    c.set("notes", "Keep new draft");
    pending.resolve(spendingRecord()); await writing;
    expect(c.draft().notes).toBe("Keep new draft"); expect(c.saving()).toBe(false); expect(c.frozen()).toBeNull();
  });

  it("removes pending item lines without reviving them when the suggestion arrives", async () => {
    const c = await page(); const pending = deferred<SpendingUsualPrice>(); get.mockReturnValueOnce(pending.promise);
    c.set("merchantId", "first-shop"); c.setCurrency("GBP");
    const key = c.draft().lines[0].key; c.selectItem(key, "eggs"); c.addLine(); c.removeLine(key);
    pending.resolve(spendingPrice()); await settle();
    expect(c.draft().lines).toHaveLength(1); expect(c.line(key)).toBeUndefined();
  });
  async function edit(record = spendingRecord()) {
    const c = await page();
    fixture.componentRef.setInput("record", record); await settle();
    return c;
  }
  it("opens saved values without price lookups or recalculation and fixes currency", async () => {
    const record = spendingRecord();
    record.lines[0] = { ...record.lines[0], spendingItemId: "eggs", quantity: 2, unitPrice: 3, netAmount: 5, itemName: "Saved eggs", unitKey: "pack-6", unitLabel: "Old pack" };
    const c = await edit(record);
    expect(c.draft().lines[0]).toMatchObject({ quantity: "2", unitPrice: "3", amount: "5", autoAmount: false });
    expect(c.hasUnsavedChanges()).toBe(false); expect(get).not.toHaveBeenCalled();
    c.setCurrency("EUR"); expect(c.draft().currencyCode).toBe("GBP");
    const picker = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatSelectHarness.with({ selector: '[name="currency"]' }));
    expect(await picker.isDisabled()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain("Saved eggs");
    expect(fixture.nativeElement.textContent).toContain("Old pack");
  });
  it("corrects only the header with explicit nullable fields and the saved revision", async () => {
    const c = await edit(spendingRecord("first", { receiptTotal: 10, externalReference: "old", notes: "old" }));
    c.set("date", "2026-12-01"); c.set("receiptTotal", ""); c.set("reference", ""); c.set("notes", "");
    const received = vi.fn(); c.saved.subscribe(received);
    await (await button("Save changes")).click();
    expect(send).toHaveBeenCalledExactlyOnceWith("PUT", "households/first/spending/records/record/header", {
      expectedRevision: "record-r1", spendingDate: "2026-12-01", receiptTotal: null, externalReference: null, notes: null,
    });
    expect(received).toHaveBeenCalledOnce(); expect(c.hasUnsavedChanges()).toBe(false);
  });
  it("corrects one line's actual values without refreshing historical labels", async () => {
    const c = await edit(); const key = c.draft().lines[0].key;
    c.changeLine(key, "amount", "0"); c.changeLine(key, "promotion", "Yes"); c.changeLine(key, "notes", "A refund adjustment");
    expect(c.editOperation().kind).toBe("line"); await c.save();
    expect(send).toHaveBeenCalledExactlyOnceWith("PUT", "households/first/spending/records/record/lines/saved-line", {
      expectedRevision: "record-r1", netAmount: 0, quantity: null, unitPrice: null, promotionStatus: "Yes", notes: "A refund adjustment",
    });
  });
  it("replaces wider changes atomically, retaining receipt metadata and standalone units", async () => {
    const record = spendingRecord();
    record.lines[0] = { ...record.lines[0], quantity: 2, unitPrice: 6, unitKey: "cup", unitLabel: "Cup", receiptDescription: "COFFEE", notes: "Keep this", merchantItemReference: "C01" };
    const c = await edit(record);
    c.set("notes", "Corrected visit"); c.changeLine("saved-line", "amount", "11");
    c.addLine(); const key = c.draft().lines[1].key;
    c.changeLine(key, "categoryId", "first-food"); c.changeLine(key, "amount", "2");
    expect(c.editOperation().kind).toBe("replace"); await settle();
    expect(fixture.nativeElement.textContent).toContain("refreshes saved merchant, category and item labels");
    await (await button("Save changes & refresh labels")).click();
    const [method, path, body] = send.mock.calls[0];
    expect([method, path]).toEqual(["PUT", "households/first/spending/records/record"]);
    expect(body).toMatchObject({ expectedRevision: "record-r1", merchantId: "first-shop", notes: "Corrected visit" });
    expect(body.lines).toHaveLength(2);
    expect(body.lines[0]).toMatchObject({ netAmount: 11, quantity: 2, unitPrice: 6, unitKey: "cup", unitLabel: "Cup", receiptDescription: "COFFEE", notes: "Keep this", merchantItemReference: "C01" });
    expect(body.id).toBeUndefined(); expect(body.kind).toBeUndefined(); expect(body.currencyCode).toBeUndefined(); expect(body.rememberPrices).toBeUndefined();
  });
  it("preserves linked lines without quantities and does not override item units on replacement", async () => {
    const record = spendingRecord();
    record.lines[0] = { ...record.lines[0], spendingItemId: "eggs", itemName: "Eggs", unitKey: "pack-6", unitLabel: "Pack" };
    const c = await edit(record); c.set("merchantId", "another-shop");
    expect(c.valid()).toBe(true); await c.save();
    expect(send.mock.calls[0][2].lines[0]).toMatchObject({ spendingItemId: "eggs", netAmount: 10 });
    expect(send.mock.calls[0][2].lines[0].unitKey).toBeUndefined();
    expect(send.mock.calls[0][2].lines[0].quantity).toBeUndefined();
  });
  it("retains entered amounts when editing quantity until calculation is explicitly requested", async () => {
    const record = spendingRecord();
    record.lines[0] = { ...record.lines[0], spendingItemId: "eggs", quantity: 2, unitPrice: 3, netAmount: 5 };
    const c = await edit(record); c.changeLine("saved-line", "quantity", "3");
    expect(c.line("saved-line")?.amount).toBe("5");
    c.useSuggestion("saved-line"); expect(c.line("saved-line")?.amount).toBe("9.00");
    expect(c.editOperation().kind).toBe("line");
  });
  it.each([0, 408, 409, 500])("retains edits and requires an explicit reload after HTTP %s without replaying a new revision", async status => {
    const c = await edit(); c.set("notes", "Keep proposed changes");
    send.mockRejectedValueOnce(new HttpErrorResponse({ status })); await c.save();
    expect(c.editNeedsReload()).toBe(true); expect(c.draft().notes).toBe("Keep proposed changes");
    await c.save(); expect(send).toHaveBeenCalledTimes(1);
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    await c.reloadEdit(); expect(get).not.toHaveBeenCalled();
    confirm.mockReturnValue(true); get.mockResolvedValueOnce(spendingRecord("first", { revision: "r2", notes: "Another user" }));
    await c.reloadEdit(); expect(c.draft().notes).toBe("Another user");
    expect(c.editNeedsReload()).toBe(false); expect(c.hasUnsavedChanges()).toBe(false);
    expect(send).toHaveBeenCalledTimes(1);
    c.set("notes", "New intentional correction"); await c.save();
    expect(send.mock.calls[1][2].expectedRevision).toBe("r2");
  });
  it("keeps validation rejections editable, and failed reloads retain the draft", async () => {
    const c = await edit(); c.set("notes", "Keep");
    send.mockRejectedValueOnce(new HttpErrorResponse({ status: 400 })); await c.save();
    expect(c.editNeedsReload()).toBe(false); c.set("notes", "Fixed"); expect(c.draft().notes).toBe("Fixed");
    vi.spyOn(window, "confirm").mockReturnValue(true); get.mockRejectedValueOnce(new Error("Offline"));
    await c.reloadEdit(); expect(c.draft().notes).toBe("Fixed"); expect(c.error()).toBe("Offline");
  });
  it("does not allow saving a record found voided during reload", async () => {
    const c = await edit(); get.mockResolvedValueOnce(spendingRecord("first", { isVoided: true }));
    await c.reloadEdit(); expect(c.editNeedsReload()).toBe(true); expect(c.error()).toContain("voided");
    await c.save(); expect(send).not.toHaveBeenCalled();
  });
  it("guards cancellation, blocks it during saves, and ignores a previous household's late correction", async () => {
    const c = await edit(); c.set("notes", "Keep"); const cancelled = vi.fn(); c.cancelled.subscribe(cancelled);
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    c.cancelEdit(); expect(cancelled).not.toHaveBeenCalled();
    const pending = deferred<SpendingRecord>(); send.mockReturnValueOnce(pending.promise); const writing = c.save();
    c.cancelEdit(); expect(confirm).toHaveBeenCalledOnce(); expect(cancelled).not.toHaveBeenCalled();
    const received = vi.fn(); c.saved.subscribe(received);
    current.set({ id: "second", name: "Other", people: [] }); await settle();
    c.set("notes", "New household draft"); pending.resolve(spendingRecord()); await writing;
    expect(c.draft().notes).toBe("New household draft"); expect(c.editing()).toBeNull(); expect(received).not.toHaveBeenCalled();
  });
  it("preserves a draft when household metadata changes without switching household", async () => {
    const c = await edit(); c.set("notes", "Keep");
    current.set({ id: "first", name: "Renamed", people: [] }); await settle();
    expect(c.draft().notes).toBe("Keep");
  });

  it("compares persisted decimal values and ignores UI flags when classifying corrections", async () => {
    const record = spendingRecord();
    record.lines[0] = { ...record.lines[0], spendingItemId: "eggs", quantity: 2, unitPrice: 5 };
    const c = await edit(record);
    c.changeLine("saved-line", "amount", "10.00"); c.changeLine("saved-line", "quantity", "2.000");
    c.useSuggestion("saved-line"); expect(c.hasUnsavedChanges()).toBe(false);
    c.set("date", "2026-12-01"); expect(c.editOperation().kind).toBe("header");
    await c.save(); expect(send.mock.calls[0][1]).toBe("households/first/spending/records/record/header");
  });

});
