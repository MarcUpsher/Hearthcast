import { signal } from "@angular/core";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { HttpErrorResponse } from "@angular/common/http";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { MatButtonHarness } from "@angular/material/button/testing";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Api } from "../core/api";
import { Workspace } from "../core/workspace";
import { Household } from "../setup/models";
import { SpendingPrices } from "./prices";
import { SpendingUsualPrice } from "./models";
import { deferred, spendingEntry, spendingItem, spendingPrice } from "../../testing/spending";

describe("Spending usual prices", () => {
  let fixture: ComponentFixture<SpendingPrices>;
  let current: ReturnType<typeof signal<Household | null>>;
  let get: ReturnType<typeof vi.fn>;
  let send: ReturnType<typeof vi.fn>;
  beforeEach(() => {
    current = signal<Household | null>({ id: "first", name: "Home", people: [] });
    get = vi.fn(async () => [spendingPrice()]);
    send = vi.fn();
    TestBed.configureTestingModule({ providers: [
      { provide: Workspace, useValue: { current } }, { provide: Api, useValue: { get, send } }] });
  });
  afterEach(() => { fixture?.destroy(); vi.restoreAllMocks(); });
  async function settle() { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
  async function page() {
    fixture = TestBed.createComponent(SpendingPrices);
    fixture.componentRef.setInput("item", spendingItem());
    fixture.componentRef.setInput("merchants", [spendingEntry("shop"), { ...spendingEntry("shop"), id: "other-shop", name: "Other shop", isRetired: true }]);
    await settle();
    return fixture.componentInstance;
  }
  const button = (text: string) => TestbedHarnessEnvironment.documentRootLoader(fixture).getHarness(MatButtonHarness.with({ text }));
  function newPrice(c: SpendingPrices, amount = "3") {
    c.change("merchantId", "other-shop"); c.change("currencyCode", "gbp"); c.change("amount", amount);
  }

  it("reads prices only for the selected item and retains saved precision and unit labels", async () => {
    get.mockResolvedValue([spendingPrice("first", { usualUnitPrice: 3.123456, unitLabel: "Original pack label" })]);
    const c = await page();
    expect(get).toHaveBeenCalledExactlyOnceWith("households/first/spending/items/eggs/usual-prices");
    expect(send).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain("GBP 3.123456");
    expect(fixture.nativeElement.textContent).toContain("Original pack label");
    expect(c.canLeave()).toBe(true);
  });

  it.each(["0", "3.123456"])("adds an exact explicit price %s and resets only after success", async amount => {
    const c = await page();
    newPrice(c, amount);
    const saved = spendingPrice("first", { id: "new-price", merchantId: "other-shop", usualUnitPrice: Number(amount) });
    send.mockResolvedValue(saved);
    await settle();
    await (await button("Add price")).click();
    expect(send).toHaveBeenCalledExactlyOnceWith("POST", "households/first/spending/usual-prices", {
      spendingItemId: "eggs", merchantId: "other-shop", currencyCode: "GBP", usualUnitPrice: Number(amount),
    });
    expect(c.rows()).toContainEqual(saved);
    expect(c.hasUnsavedChanges()).toBe(false);
    await settle();
    const form = fixture.nativeElement.querySelector('form[aria-label="Usual price"]');
    expect(form.classList.contains("ng-submitted")).toBe(false);
    expect(form.querySelectorAll(".mat-form-field-invalid")).toHaveLength(0);
  });

  it.each(["", "-1", "1.1234567", "1000000000", "1e3", "NaN"])("rejects invalid price %s without a request", async amount => {
    const c = await page();
    newPrice(c, amount);
    expect(c.canSave()).toBe(false);
    await c.save();
    expect(send).not.toHaveBeenCalled();
  });

  it("accepts pasted grouping spaces without rounding away six decimal places", async () => {
    const c = await page();
    newPrice(c, "");
    await settle();
    const input = fixture.nativeElement.querySelector('input[name="amount"]') as HTMLInputElement;
    input.value = "1 234.123456";
    input.dispatchEvent(new Event("input", { bubbles: true }));
    await settle();
    expect(c.draft().amount).toBe("1234.123456");
    send.mockResolvedValue(spendingPrice("first", { merchantId: "other-shop", usualUnitPrice: 1234.123456 }));
    await (await button("Add price")).click();
    expect(send.mock.calls[0][2].usualUnitPrice).toBe(1234.123456);
  });

  it("updates or explicitly reconfirms a price using only its revision and value", async () => {
    const c = await page();
    c.beginEdit(spendingPrice());
    send.mockResolvedValue(spendingPrice("first", { revision: "r2" }));
    await settle();
    expect(c.canSave()).toBe(true);
    expect(fixture.nativeElement.querySelector('input[name="currencyCode"]')).toBeNull();
    await (await button("Save price")).click();
    expect(send).toHaveBeenCalledExactlyOnceWith("PUT", "households/first/spending/usual-prices/price", { expectedRevision: "r1", usualUnitPrice: 3 });
    expect(c.rows()[0].revision).toBe("r2");
  });

  it("keeps a stale-price draft, reloads the latest revision explicitly, and never retries a write automatically", async () => {
    const c = await page();
    c.beginEdit(spendingPrice());
    c.change("amount", "5.125");
    send.mockRejectedValueOnce(new HttpErrorResponse({ status: 409 }));
    await c.save();
    expect(c.conflict()).toBe(true);
    expect(c.draft().amount).toBe("5.125");
    expect(c.canSave()).toBe(false);
    get.mockResolvedValueOnce(spendingPrice("first", { revision: "r2", usualUnitPrice: 4.25 }));
    await c.reloadCurrent();
    expect(get).toHaveBeenLastCalledWith("households/first/spending/usual-prices/price");
    expect(c.editing()?.usualUnitPrice).toBe(4.25);
    expect(c.draft().amount).toBe("5.125");
    expect(c.conflict()).toBe(false);
    expect(send).toHaveBeenCalledTimes(1);
    send.mockResolvedValueOnce(spendingPrice("first", { revision: "r3", usualUnitPrice: 5.125 }));
    await c.save();
    expect(send).toHaveBeenLastCalledWith("PUT", "households/first/spending/usual-prices/price", { expectedRevision: "r2", usualUnitPrice: 5.125 });
  });

  it("preserves duplicate-create inputs and prevents blind overwriting of an existing pair", async () => {
    const c = await page();
    c.change("merchantId", "first-shop"); c.change("currencyCode", "gbp"); c.change("amount", "7");
    expect(c.duplicate()?.id).toBe("price");
    expect(c.canSave()).toBe(false);
    c.change("merchantId", "other-shop");
    send.mockRejectedValueOnce(new HttpErrorResponse({ status: 409 }));
    await c.save();
    expect(c.draft().amount).toBe("7");
    expect(c.error()).toContain("already exists");
    get.mockResolvedValueOnce([spendingPrice("first", { merchantId: "other-shop" })]);
    await c.load();
    expect(c.duplicate()?.merchantId).toBe("other-shop");
    expect(send).toHaveBeenCalledTimes(1);
  });

  it("flags a changed revision revealed by list reload without presenting the editor's old value as current", async () => {
    const c = await page();
    c.beginEdit(spendingPrice());
    c.change("amount", "4.375");
    get.mockResolvedValueOnce([spendingPrice("first", { revision: "r2", usualUnitPrice: 5 })]);
    await c.load();
    await settle();
    expect(c.rows()[0].usualUnitPrice).toBe(5);
    expect(c.editing()?.revision).toBe("r1");
    expect(c.draft().amount).toBe("4.375");
    expect(c.conflict()).toBe(true);
    expect(c.canSave()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain("Previously loaded price");
    expect(fixture.nativeElement.textContent).not.toContain("Current saved price:");
    await c.save();
    expect(send).not.toHaveBeenCalled();
  });

  it("retains an entered price across load failures and retry", async () => {
    const c = await page();
    newPrice(c, "0");
    get.mockRejectedValueOnce(new Error("Prices unavailable"));
    await c.load();
    expect(c.loadError()).toBe("Prices unavailable");
    expect(c.canSave()).toBe(false);
    await c.load();
    expect(c.loadError()).toBe("");
    expect(c.draft().amount).toBe("0");
  });

  it("does not revive a cancelled edit when a revision reload completes", async () => {
    const c = await page();
    c.beginEdit(spendingPrice());
    c.change("amount", "5");
    const pending = deferred<SpendingUsualPrice>();
    get.mockReturnValueOnce(pending.promise);
    const reload = c.reloadCurrent();
    c.reset();
    newPrice(c, "6");
    pending.resolve(spendingPrice("first", { revision: "r2", usualUnitPrice: 4 }));
    await reload;
    expect(c.editing()).toBeNull();
    expect(c.draft().amount).toBe("6");
    expect(c.loading()).toBe(false);
  });

  it("ignores a late price-list read for a previous item", async () => {
    const c = await page();
    const pending = deferred<SpendingUsualPrice[]>();
    get.mockReturnValueOnce(pending.promise);
    const reading = c.load();
    get.mockResolvedValueOnce([spendingPrice("first", { id: "milk-price", spendingItemId: "milk" })]);
    fixture.componentRef.setInput("item", spendingItem("first", { id: "milk", name: "Milk" }));
    await settle();
    pending.resolve([spendingPrice()]);
    await reading;
    expect(c.rows().map(row => row.id)).toEqual(["milk-price"]);
  });

  it.each([false, true])("ignores old household price-write completion (failure=%s)", async fail => {
    const c = await page();
    newPrice(c, "5");
    const pending = deferred<SpendingUsualPrice>();
    send.mockReturnValueOnce(pending.promise);
    const writing = c.save();
    current.set({ id: "second", name: "Second", people: [] });
    get.mockResolvedValue([spendingPrice("second")]);
    fixture.componentRef.setInput("item", spendingItem("second"));
    fixture.componentRef.setInput("merchants", [spendingEntry("shop", "second")]);
    await settle();
    c.change("amount", "New household draft");
    if (fail) pending.reject(new Error("Old failure")); else pending.resolve(spendingPrice());
    await writing;
    expect(c.rows()[0].householdId).toBe("second");
    expect(c.draft().amount).toBe("New household draft");
    expect(c.error()).toBe("");
    expect(c.saving()).toBe(false);
  });

  it("asks before replacing a price draft, preserves it on cancellation, and blocks leaving while saving", async () => {
    const c = await page();
    newPrice(c, "5");
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    c.beginEdit(spendingPrice());
    expect(c.editing()).toBeNull();
    expect(c.draft().amount).toBe("5");
    confirm.mockReturnValue(true);
    c.beginEdit(spendingPrice());
    c.change("amount", "4");
    const pending = deferred<SpendingUsualPrice>();
    send.mockReturnValueOnce(pending.promise);
    const writing = c.save();
    confirm.mockClear();
    expect(c.canLeave()).toBe(false);
    c.reset();
    expect(c.editing()).not.toBeNull();
    expect(confirm).not.toHaveBeenCalled();
    pending.resolve(spendingPrice("first", { usualUnitPrice: 4 }));
    await writing;
    expect(c.canLeave()).toBe(true);
  });
});
