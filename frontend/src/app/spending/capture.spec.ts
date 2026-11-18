import { signal } from "@angular/core";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { HttpErrorResponse } from "@angular/common/http";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
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
});
