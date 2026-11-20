import { signal } from "@angular/core";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { HttpErrorResponse } from "@angular/common/http";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { TestKey } from "@angular/cdk/testing";
import { MatAutocompleteHarness } from "@angular/material/autocomplete/testing";
import { MatSelectHarness } from "@angular/material/select/testing";
import { MatButtonHarness } from "@angular/material/button/testing";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Api } from "../core/api";
import { Workspace } from "../core/workspace";
import { Household } from "../setup/models";
import { SpendingCapture } from "./capture";
import { SpendingRecord, SpendingUsualPrice, SpendingLastPrice } from "./models";
import { MatInputHarness } from "@angular/material/input/testing";
import { deferred, spendingEntry, spendingItem, spendingPrice, spendingRecord } from "../../testing/spending";

describe("Spending purchase capture", () => {
  let fixture: ComponentFixture<SpendingCapture>;
  let current: ReturnType<typeof signal<Household | null>>;
  let get: ReturnType<typeof vi.fn>, send: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    current = signal<Household | null>({ id: "first", name: "Home", people: [] });
    get = vi.fn(async (path: string) => path.includes("/last-paid-price?") ? null : spendingPrice()); send = vi.fn(async () => spendingRecord());
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
    c.setLineKind(key, "category");
    c.changeLine(key, "categoryId", "first-food"); c.changeLine(key, "amount", amount);
    return key;
  }
  const button = (text: string) => TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(MatButtonHarness.with({ text }));

  it("adds a separate delivery charge through the form and reconciles the VetUK example", async () => {
    const c = await page(); quick(c, "45.98"); c.set("receiptTotal", "49.97"); await settle();
    await (await button("Add additional cost")).click(); const cost = c.costLines()[0];
    expect(cost.categoryId).toBe("first-food"); const loader = TestbedHarnessEnvironment.loader(fixture);
    await (await loader.getHarness(MatInputHarness.with({ selector: '[name="cost-description-' + cost.key + '"]' }))).setValue("Delivery");
    await (await loader.getHarness(MatInputHarness.with({ selector: '[name="cost-amount-' + cost.key + '"]' }))).setValue("3.99");
    expect(c.purchaseLines()).toHaveLength(1); expect(c.costLines()).toHaveLength(1);
    expect(c.purchaseTotal()).toBe("45.98"); expect(c.costTotal()).toBe("3.99"); expect(c.total()).toBe("49.97"); expect(c.difference()).toBe("0.00");
    await c.save(); expect(send.mock.calls[0][2].lines).toHaveLength(2);
    expect(send.mock.calls[0][2].lines[1]).toMatchObject({ isAdditionalCost: true, netAmount: 3.99, description: "Delivery", expenseCategoryId: "first-food" });
    expect(send.mock.calls[0][2].lines[0].netAmount).toBe(45.98); expect(send.mock.calls[0][2].lines[1].spendingItemId).toBeUndefined();
    expect(c.costLines()).toHaveLength(0); expect(c.hasUnsavedChanges()).toBe(false);
  });
  it.each(["0", "-1", "0.001", ""])("rejects incomplete or invalid additional cost %s", async amount => {
    const c = await page(); quick(c); c.addCost(); const cost = c.costLines()[0];
    c.changeLine(cost.key, "description", "Delivery"); c.changeLine(cost.key, "amount", amount);
    expect(c.valid()).toBe(false); await c.save(); expect(send).not.toHaveBeenCalled();
  });
  it("requires a cost description and category and allows multiple explicitly assigned charges plus discounts", async () => {
    const c = await page(); quick(c); c.addLine("category"); const other = c.purchaseLines()[1]; c.changeLine(other.key, "categoryId", "delivery"); c.changeLine(other.key, "amount", "1");
    c.addCost(); const cost = c.costLines()[0]; expect(cost.categoryId).toBe(""); c.changeLine(cost.key, "amount", "3.99"); expect(c.valid()).toBe(false);
    c.changeLine(cost.key, "description", "Delivery"); c.changeLine(cost.key, "categoryId", "delivery"); expect(c.valid()).toBe(true);
    c.addCost(); const second = c.costLines()[1]; c.changeLine(second.key, "description", "Handling"); c.changeLine(second.key, "categoryId", "first-food"); c.changeLine(second.key, "amount", "1");
    c.addDiscount(); const discount = c.draft().discounts[0]; c.changeDiscount(discount.key, "categoryId", "delivery"); c.changeDiscount(discount.key, "amount", "2");
    expect(c.total()).toBe("13.99"); expect(c.valid()).toBe(true); c.removeLine(second.key); expect(c.total()).toBe("12.99");
  });
  it("restores charges to their own section and uses a narrow amount correction", async () => {
    const record = spendingRecord(); record.lines.push({ ...record.lines[0], id: "delivery-line", position: 2, isAdditionalCost: true, description: "Delivery", netAmount: 3.99 });
    const c = await edit(record); expect(c.costLines()[0].description).toBe("Delivery"); expect(c.purchaseLines()).toHaveLength(1); expect(c.hasUnsavedChanges()).toBe(false);
    c.changeLine("delivery-line", "amount", "4.99"); expect(c.editOperation().kind).toBe("line"); await c.save();
    expect(send.mock.calls[0][1]).toBe("households/first/spending/records/record/lines/delivery-line"); expect(send.mock.calls[0][2].netAmount).toBe(4.99);
  });
  it("preserves charges on header changes and sends their classification in replacements", async () => {
    const record = spendingRecord(); record.lines.push({ ...record.lines[0], id: "charge", position: 2, isAdditionalCost: true, description: "Delivery", netAmount: 3.99 });
    const c = await edit(record); c.set("notes", "Updated"); expect(c.editOperation().kind).toBe("header");
    c.changeLine("charge", "description", "Express delivery"); expect(c.editOperation().kind).toBe("replace"); await c.save();
    expect(send.mock.calls[0][2].lines[1]).toMatchObject({ isAdditionalCost: true, description: "Express delivery", netAmount: 3.99 });
  });
  it("locks charges into exact retries and includes them in dirty and currency guards", async () => {
    const c = await page(); quick(c); c.addCost(); const cost = c.costLines()[0]; c.changeLine(cost.key, "description", "Delivery"); c.changeLine(cost.key, "amount", "3.99");
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false); expect(c.canLeave()).toBe(false); c.setCurrency("EUR"); expect(c.draft().currencyCode).toBe("GBP");
    send.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 })); await c.save(); const body = structuredClone(send.mock.calls[0][2]);
    c.changeLine(cost.key, "amount", "5"); c.removeLine(cost.key); c.addCost(); await c.save(); expect(send.mock.calls[1][2]).toEqual(body); expect(confirm).toHaveBeenCalled();
  });

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
    expect(get).toHaveBeenCalledWith("households/first/spending/items/eggs/usual-price?merchantId=first-shop&currencyCode=GBP");
    expect(get.mock.calls.filter(([path]) => path.includes("/usual-price?"))).toHaveLength(1);
    expect(c.line(key)).toMatchObject({ categoryId: "first-food", quantity: "1", unitPrice: "3", amount: "3.00" });
    c.changeLine(key, "quantity", "2");
    expect(c.line(key)?.amount).toBe("6.00");
    c.changeLine(key, "amount", "5"); c.changeLine(key, "promotion", "Yes"); c.changeLine(key, "reference", "EGG SPECIAL");
    c.changeLine(key, "unitPrice", "3.125");
    expect(c.line(key)?.amount).toBe("5");
    c.addLine("category");
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
    const first = deferred<SpendingUsualPrice>(); let usual = true;
    get.mockImplementation(async (path: string) => { if (path.includes("/last-paid-price?")) return null; if (usual) { usual = false; return first.promise; } return null; });
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
  it("opens saved values without applying suggestions or recalculation and fixes currency", async () => {
    const record = spendingRecord();
    record.lines[0] = { ...record.lines[0], spendingItemId: "eggs", quantity: 2, unitPrice: 3, netAmount: 5, itemName: "Saved eggs", unitKey: "pack-6", unitLabel: "Old pack" };
    const c = await edit(record);
    expect(c.draft().lines[0]).toMatchObject({ quantity: "2", unitPrice: "3", amount: "5", autoAmount: false });
    expect(c.hasUnsavedChanges()).toBe(false); expect(get).toHaveBeenCalledTimes(2);
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
    c.addLine("category"); const key = c.draft().lines[1].key;
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

  const autocomplete = () => TestbedHarnessEnvironment.loader(fixture).getHarness(MatAutocompleteHarness.with({ selector: '[aria-label="Item for line 1"]' }));
  it("requires a selected catalogue identity on default item lines and treats search text as an unsaved draft", async () => {
    const c = await page(), key = c.draft().lines[0].key;
    expect(c.line(key)?.kind).toBe("item"); expect(c.hasUnsavedChanges()).toBe(false);
    await (await autocomplete()).enterText("Eggs");
    expect(c.hasUnsavedChanges()).toBe(true); expect(c.line(key)?.itemId).toBe("");
    c.set("merchantId", "first-shop"); c.setCurrency("GBP"); c.changeLine(key, "categoryId", "first-food"); c.changeLine(key, "amount", "3");
    expect(c.valid()).toBe(false); await c.save(); expect(send).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).not.toContain("Catalogue item (optional)");
    vi.spyOn(window, "confirm").mockReturnValue(false); expect(c.canLeave()).toBe(false);
  });
  it("searches names, codes and pack labels, showing readable names plus secondary identifiers", async () => {
    const c = await page(); fixture.componentRef.setInput("items", [spendingItem(), spendingItem("first", { id: "big", code: "EGGS-12", unitLabel: "Box of twelve" })]);
    await settle(); const picker = await autocomplete();
    await picker.enterText("eGgS-6"); const options = await picker.getOptions(); expect(options).toHaveLength(1);
    expect(await options[0].getText()).toContain("Eggs"); expect(await options[0].getText()).toContain("EGGS-6");
    await picker.clear(); await picker.enterText("twelve"); expect((await picker.getOptions())).toHaveLength(1);
    await picker.selectOption({ text: /EGGS-12/ }); expect(await picker.getValue()).toBe("Eggs");
    expect(c.draft().lines[0].itemId).toBe("big"); expect(fixture.nativeElement.textContent).toContain("EGGS-12 · Box of twelve");
    await picker.clear(); await picker.enterText("missing item"); expect(c.draft().lines[0].itemId).toBe("");
    const empty = await picker.getOptions(); expect(await empty[0].getText()).toContain("No matching items"); expect(await empty[0].isDisabled()).toBe(true);
  });
  it("filters by category then sets and locks the selected item's category with keyboard selection", async () => {
    const c = await page(), key = c.draft().lines[0].key;
    fixture.componentRef.setInput("categories", [spendingEntry("food"), { ...spendingEntry("food"), id: "clothing", name: "Clothing" }]);
    fixture.componentRef.setInput("items", [spendingItem(), spendingItem("first", { id: "coat", code: "COAT", name: "Coat", expenseCategoryId: "clothing" })]);
    await settle(); const category = await TestbedHarnessEnvironment.loader(fixture).getHarness(MatSelectHarness.with({ selector: '[aria-label="Category for line 1"]' }));
    await category.clickOptions({ text: "Clothing" }); const picker = await autocomplete(); await picker.focus();
    expect((await picker.getOptions())).toHaveLength(1); expect(await (await picker.getOptions())[0].getText()).toContain("Coat");
    await (await picker.host()).sendKeys(TestKey.DOWN_ARROW, TestKey.ENTER);
    expect(c.line(key)?.itemId).toBe("coat"); expect(await category.isDisabled()).toBe(true);
    c.changeLine(key, "categoryId", "first-food"); expect(c.line(key)?.categoryId).toBe("clothing");
    await (await button("Change item")).click(); await settle(); expect(await picker.isFocused()).toBe(true); expect(await category.isDisabled()).toBe(false); expect(c.line(key)?.categoryId).toBe("");
    await picker.enterText("EGGS-6"); await picker.selectOption({ text: /EGGS-6/ }); expect(c.line(key)?.categoryId).toBe("first-food");
  });
  it("invalidates a previous selection on typing without losing actual amounts or accepting a late price", async () => {
    const c = await page(), key = c.draft().lines[0].key, price = deferred<SpendingUsualPrice>(); get.mockReturnValueOnce(price.promise);
    c.set("merchantId", "first-shop"); c.setCurrency("GBP"); c.selectItem(key, "eggs"); c.changeLine(key, "amount", "2.50");
    c.searchItem(key, "Milk"); price.resolve(spendingPrice()); await settle();
    expect(c.line(key)).toMatchObject({ itemId: "", categoryId: "", amount: "2.50", unitPrice: "" }); expect(c.valid()).toBe(false);
  });
  it("switches line type only with confirmation when item details would be removed, retaining actuals and receipt metadata", async () => {
    const record = spendingRecord("first", { receiptTotal: 5 });
    record.lines[0] = { ...record.lines[0], spendingItemId: "eggs", unitKey: "pack-6", unitLabel: "Six eggs", quantity: 2, unitPrice: 3, netAmount: 5,
      receiptDescription: "EGGS", merchantItemReference: "E1", notes: "Keep", promotionStatus: "Yes" };
    const c = await edit(record), original = structuredClone(c.line("saved-line"));
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    c.setLineKind("saved-line", "category"); expect(c.line("saved-line")).toEqual(original);
    confirm.mockReturnValue(true); c.setLineKind("saved-line", "category");
    expect(c.line("saved-line")).toMatchObject({ kind: "category", itemId: "", quantity: "", unitPrice: "", amount: "5", notes: "Keep", promotion: "Yes" });
    expect(c.difference()).toBe("0.00"); await c.save();
    const line = send.mock.calls[0][2].lines[0];
    expect(line).toMatchObject({ netAmount: 5, receiptDescription: "EGGS", merchantItemReference: "E1", notes: "Keep", promotionStatus: "Yes" });
    expect(line.kind).toBeUndefined(); expect(line.spendingItemId).toBeUndefined(); expect(line.quantity).toBeUndefined();
  });
  it("preserves historical category and absent quantity pairs without blocking header corrections", async () => {
    const record = spendingRecord(); record.lines[0] = { ...record.lines[0], spendingItemId: "eggs", itemName: "Saved eggs", unitKey: "pack-6", unitLabel: "Old pack" };
    const c = await edit(record); fixture.componentRef.setInput("items", [spendingItem("first", { expenseCategoryId: "new-category", name: "Renamed eggs" })]); await settle();
    expect(c.line("saved-line")?.categoryId).toBe("first-food"); expect(c.itemText(c.line("saved-line")!)).toBe("Saved eggs");
    expect(c.valid()).toBe(true); expect(fixture.nativeElement.textContent).toContain("recorded category is kept");
    c.set("notes", "Only correct notes"); await c.save(); expect(send.mock.calls[0][1]).toBe("households/first/spending/records/record/header");
  });
  it("requires quantity and price for newly added item lines while editing a saved record", async () => {
    const c = await edit(); c.addLine("item"); const key = c.draft().lines[1].key; c.selectItem(key, "eggs"); await settle();
    c.changeLine(key, "quantity", ""); c.changeLine(key, "unitPrice", ""); c.changeLine(key, "amount", "3");
    expect(c.valid()).toBe(false); await c.save(); expect(send).not.toHaveBeenCalled();
  });
  it("keeps receipt notes as a one-row textarea without dropping multiline values", async () => {
    const c = await page(); quick(c); c.set("notes", "First line\nSecond line"); await settle();
    const notes = fixture.nativeElement.querySelector('textarea[name="notes"]') as HTMLTextAreaElement;
    expect(notes.rows).toBe(1); expect(notes.value).toBe("First line\nSecond line");
    await c.save(); expect(send.mock.calls[0][2].notes).toBe("First line\nSecond line");
  });

  it("requires quantities after deliberately reselecting the same historical amount-only item", async () => {
    const record = spendingRecord(); record.lines[0] = { ...record.lines[0], spendingItemId: "eggs", itemName: "Eggs", unitKey: "pack-6", unitLabel: "Old pack" };
    const c = await edit(record); expect(c.valid()).toBe(true);
    c.changeItem("saved-line"); c.selectItem("saved-line", "eggs"); await settle();
    c.changeLine("saved-line", "quantity", ""); c.changeLine("saved-line", "unitPrice", "");
    expect(c.historicalAmountOnly(c.line("saved-line")!)).toBe(false);
    expect(c.valid()).toBe(false); await c.save(); expect(send).not.toHaveBeenCalled();
    vi.spyOn(window, "confirm").mockReturnValue(true); get.mockResolvedValueOnce(record);
    await c.reloadEdit(); expect(c.historicalAmountOnly(c.line("saved-line")!)).toBe(true);
  });
  it("confirms before discarding unresolved item searches when switching line type", async () => {
    const c = await page(), key = c.draft().lines[0].key;
    c.searchItem(key, "Chicken thighs");
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    c.setLineKind(key, "category");
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining("item/search"));
    expect(c.line(key)?.kind).toBe("item"); expect(c.itemText(c.line(key)!)).toBe("Chicken thighs");
    confirm.mockReturnValue(true); c.setLineKind(key, "category");
    expect(c.line(key)?.kind).toBe("category"); expect(c.itemText(c.line(key)!)).toBe("");
  });

  it("captures the printed Stir Fry deal as a Food receipt discount and reconciles the net total", async () => {
    const c = await page(); quick(c, "21.95"); c.set("receiptTotal", "19.85"); c.addDiscount(); await settle();
    const discount = c.draft().discounts[0]; expect(discount.categoryId).toBe("first-food");
    const loader = TestbedHarnessEnvironment.loader(fixture);
    await (await loader.getHarness(MatInputHarness.with({ selector: '[name="discount-amount-' + discount.key + '"]' }))).setValue("2.10");
    await (await loader.getHarness(MatInputHarness.with({ selector: '[name="discount-description-' + discount.key + '"]' }))).setValue("£8 Stir Fry Dine In");
    expect(c.gross()).toBe("21.95"); expect(c.discountTotal()).toBe("2.10"); expect(c.total()).toBe("19.85"); expect(c.difference()).toBe("0.00");
    await c.save(); expect(send.mock.calls[0][2].discounts).toEqual([{ expenseCategoryId: "first-food", amount: 2.1, description: "£8 Stir Fry Dine In" }]);
    expect(send.mock.calls[0][2].lines[0].netAmount).toBe(21.95); expect(send.mock.calls[0][2].rememberPrices).toBeUndefined();
    expect(c.draft().discounts).toEqual([]); expect(c.hasUnsavedChanges()).toBe(false);
  });
  it.each(["0", "-1", "0.001", "22"])("rejects invalid or excessive receipt discount %s without crashing reconciliation", async amount => {
    const c = await page(); quick(c, "21.95"); c.set("receiptTotal", "19.85"); c.addDiscount(); c.changeDiscount(c.draft().discounts[0].key, "amount", amount);
    expect(c.valid()).toBe(false); expect(() => c.difference()).not.toThrow(); await c.save(); expect(send).not.toHaveBeenCalled();
  });
  it("limits discounts within each category rather than only the receipt total", async () => {
    const c = await page(); quick(c, "10"); c.addLine("category"); const other = c.draft().lines[1]; c.changeLine(other.key, "categoryId", "cleaning"); c.changeLine(other.key, "amount", "20");
    c.addDiscount(); c.changeDiscount(c.draft().discounts[0].key, "categoryId", "first-food"); c.changeDiscount(c.draft().discounts[0].key, "amount", "11");
    expect(c.total()).toBe("19.00"); expect(c.valid()).toBe(false); c.changeDiscount(c.draft().discounts[0].key, "amount", "10"); expect(c.valid()).toBe(true);
  });
  it("edits or clears only discounts through the narrow revision-checked endpoint", async () => {
    const c = await edit(); c.addDiscount(); c.changeDiscount(c.draft().discounts[0].key, "amount", "2.10");
    expect(c.editOperation().kind).toBe("discounts"); await c.save(); expect(send).toHaveBeenCalledExactlyOnceWith("PUT", "households/first/spending/records/record/discounts", {
      expectedRevision: "record-r1", discounts: [{ expenseCategoryId: "first-food", amount: 2.1, description: null }],
    });
  });
  it("keeps discount metadata during a narrow line edit and explicitly clears it when removed", async () => {
    const c = await page(); const record = spendingRecord("first", { discounts: [{ id: "discount", position: 1, expenseCategoryId: "first-food", expenseCategoryName: "Saved food", amount: 2, description: "Bundle" }], grossAmount: 10, discountAmount: 2, totalAmount: 8, countedAmount: 8 });
    fixture.componentRef.setInput("record", record); await settle(); c.changeLine("saved-line", "amount", "11"); expect(c.editOperation().kind).toBe("line");
    await c.save(); expect(send.mock.calls[0][1]).toContain("/lines/"); expect(send.mock.calls[0][2].discounts).toBeUndefined();
    fixture.componentRef.setInput("record", { ...record, revision: "new-r" }); await settle(); c.removeDiscount("discount"); await c.save();
    expect(send.mock.calls[1][2]).toEqual({ expectedRevision: "new-r", discounts: [] });
  });
  it("includes changed discounts in an atomic wider replacement and retains them after a rejected save", async () => {
    const c = await edit(); c.addDiscount(); c.changeDiscount(c.draft().discounts[0].key, "amount", "2"); c.set("notes", "Header change");
    expect(c.editOperation().kind).toBe("replace"); send.mockRejectedValueOnce(new HttpErrorResponse({ status: 400 })); await c.save();
    expect(send.mock.calls[0][2].discounts).toHaveLength(1); expect(c.draft().discounts[0].amount).toBe("2"); expect(c.editNeedsReload()).toBe(false);
  });
  it("freezes receipt discounts into the identical create retry and guards unsaved discount edits", async () => {
    const c = await page(); quick(c); c.addDiscount(); c.changeDiscount(c.draft().discounts[0].key, "amount", "1");
    vi.spyOn(window, "confirm").mockReturnValue(false); expect(c.canLeave()).toBe(false);
    send.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 })); await c.save(); const original = structuredClone(send.mock.calls[0][2]);
    c.changeDiscount(c.draft().discounts[0].key, "amount", "5"); c.removeDiscount(c.draft().discounts[0].key); await c.save();
    expect(send.mock.calls[1][2]).toEqual(original);
  });
  const lastPrice = (extra: Partial<SpendingLastPrice> = {}): SpendingLastPrice => ({ spendingRecordId: "past", spendingLineId: "past-line", spendingDate: "2026-11-17", currencyCode: "GBP", unitKey: "pack-6", unitLabel: "Six eggs", quantity: 1, unitPrice: 1.95, netAmount: 1.95, effectiveUnitPrice: 1.95, promotionStatus: "Unknown", hasReceiptDiscount: false, ...extra });
  it("loads historical references immediately on editing and reloading without changing saved prices", async () => {
    const c = await page(); const record = spendingRecord();
    record.lines[0] = { ...record.lines[0], spendingItemId: "eggs", itemName: "Eggs", unitKey: "pack-6", unitLabel: "Six eggs", quantity: 2, unitPrice: 5 };
    get.mockImplementation(async path => path.includes("/last-paid-price?") ? lastPrice() : path.includes("/usual-price?") ? spendingPrice() : record);
    fixture.componentRef.setInput("record", record); await settle();
    expect(c.lastPrices()["saved-line"]?.unitPrice).toBe(1.95); expect(c.line("saved-line")?.unitPrice).toBe("5");
    expect(c.hasUnsavedChanges()).toBe(false);
    expect(get).toHaveBeenCalledWith(expect.stringContaining("excludeRecordId=record"));
    get.mockClear(); await c.reloadEdit(); await settle();
    expect(get).toHaveBeenCalledWith(expect.stringContaining("excludeRecordId=record"));
    expect(c.lastPrices()["saved-line"]?.unitPrice).toBe(1.95); expect(c.line("saved-line")?.amount).toBe("10");
    expect(c.hasUnsavedChanges()).toBe(false);
  });
  it("offers the last recorded price when there is no usual price, using it only after explicit selection", async () => {
    const c = await page(); get.mockImplementation(async (path: string) => path.includes("/last-paid-price?") ? lastPrice() : null);
    c.set("date", "2026-11-18"); c.set("merchantId", "first-shop"); const key = c.draft().lines[0].key; c.selectItem(key, "eggs"); await settle();
    expect(c.line(key)?.unitPrice).toBe(""); expect(c.lastPrices()[key]?.unitPrice).toBe(1.95);
    expect(get).toHaveBeenCalledWith("households/first/spending/items/eggs/last-paid-price?merchantId=first-shop&currencyCode=GBP&onOrBefore=2026-11-18");
    await (await button("Use this price")).click(); expect(c.line(key)).toMatchObject({ unitPrice: "1.95", amount: "1.95", priceTouched: true }); expect(send).not.toHaveBeenCalled();
  });
  it("does not let a late usual price overwrite an explicitly selected historical price", async () => {
    const c = await page(), pending = deferred<SpendingUsualPrice>(); get.mockImplementation(async (path: string) => path.includes("/last-paid-price?") ? lastPrice() : pending.promise);
    c.set("merchantId", "first-shop"); const key = c.draft().lines[0].key; c.selectItem(key, "eggs"); await settle(); c.useLastPrice(key);
    pending.resolve(spendingPrice()); await settle(); expect(c.line(key)?.unitPrice).toBe("1.95");
  });
  it("labels category receipt discounts on historical prices and clears them when merchant context is missing", async () => {
    const c = await page(); get.mockImplementation(async path => path.includes("/last-paid-price?") ? lastPrice({ hasReceiptDiscount: true }) : null);
    c.set("merchantId", "first-shop"); const key = c.draft().lines[0].key; c.selectItem(key, "eggs"); await settle();
    expect(fixture.nativeElement.textContent).toContain("final item price was not allocated"); c.set("merchantId", ""); expect(c.lastPrices()[key]).toBeNull();
  });
  it("excludes the edited record from last-price lookup and rejects late suggestions for an old date", async () => {
    const c = await edit(), pending = deferred<SpendingLastPrice>(); get.mockImplementation(async path => path.includes("/last-paid-price?") ? pending.promise : null);
    c.set("date", "2026-11-17"); const key = c.draft().lines[0].key; c.setLineKind(key, "item"); c.selectItem(key, "eggs"); c.set("date", "2026-11-16");
    expect(get.mock.calls.some(([path]) => path.includes("onOrBefore=2026-11-17") && path.includes("excludeRecordId=record"))).toBe(true);
    c.set("merchantId", ""); pending.resolve(lastPrice()); await settle(); expect(c.lastPrices()[key]).toBeNull();
  });

});
