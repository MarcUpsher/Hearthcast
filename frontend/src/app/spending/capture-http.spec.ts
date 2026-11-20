import { signal } from "@angular/core";
import { TestBed } from "@angular/core/testing";
import { provideHttpClient } from "@angular/common/http";
import { HttpTestingController, provideHttpClientTesting, TestRequest } from "@angular/common/http/testing";
import { ActivatedRoute, convertToParamMap, provideRouter } from "@angular/router";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { MatSelectHarness } from "@angular/material/select/testing";
import { MatAutocompleteHarness } from "@angular/material/autocomplete/testing";
import { MatInputHarness } from "@angular/material/input/testing";
import { of } from "rxjs";
import { expect, it, vi } from "vitest";
import { Workspace } from "../core/workspace";
import { SpendingPurchase } from "./purchase";
import { spendingEntry, spendingItem } from "../../testing/spending";

it.each([200, 204, 503])("renders the HTTP price result after choosing Merchant and an item (history status %s)", async status => {
  vi.spyOn(window, "scrollTo").mockImplementation(() => {});
  const params = convertToParamMap({});
  TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
    { provide: ActivatedRoute, useValue: { snapshot: { data: { create: true }, paramMap: params }, paramMap: of(params) } },
    { provide: Workspace, useValue: { current: signal({ id: "first", name: "Home", people: [] }) } }] });
  const fixture = TestBed.createComponent(SpendingPurchase); fixture.detectChanges(); const http = TestBed.inject(HttpTestingController);
  http.expectOne("/api/households/first/spending/categories?includeRetired=true").flush([spendingEntry("food")]);
  http.expectOne("/api/households/first/spending/merchants?includeRetired=true").flush([spendingEntry("shop", "first")]);
  http.expectOne("/api/households/first/spending/items?includeRetired=true").flush([spendingItem("first", { id: "beef", name: "Beef Stir Fry", code: "BEEF_STIR_FRY", unitKey: "pack", unitLabel: "Pack" })]);
  http.expectOne("/api/households/first/spending/currencies").flush([{ code: "GBP", minorUnits: 2 }]);
  await fixture.whenStable(); fixture.detectChanges(); const c = fixture.componentInstance.capture()!; c.set("date", "2026-11-20");
  const loader = TestbedHarnessEnvironment.loader(fixture);
  const merchant = await loader.getHarness(MatSelectHarness.with({ selector: '[name="merchant"]' }));
  await merchant.open(); await merchant.clickOptions({ text: "Shop" });
  const item = await loader.getHarness(MatAutocompleteHarness.with({ selector: '[aria-label="Item for line 1"]' }));
  await item.focus(); const selecting = item.selectOption({ text: /Beef Stir Fry/ });
  let usual: TestRequest | undefined, last: TestRequest | undefined;
  await vi.waitFor(() => {
    usual ??= http.expectOne("/api/households/first/spending/items/beef/usual-price?merchantId=first-shop&currencyCode=GBP");
    last ??= http.expectOne("/api/households/first/spending/items/beef/last-paid-price?merchantId=first-shop&currencyCode=GBP&onOrBefore=2026-11-20");
  });
  expect(c.draft().lines[0].itemId).toBe("beef"); expect(c.draft().lines[0].priceTouched).toBe(false);
  usual!.flush(null, { status: 204, statusText: "No Content" });
  const history = { spendingRecordId: "past", spendingLineId: "past-line", spendingDate: "2026-11-19", currencyCode: "GBP", unitKey: "pack", unitLabel: "Pack", quantity: 2, unitPrice: 5.2, netAmount: 10.4, effectiveUnitPrice: 5.2, promotionStatus: "Unknown", hasReceiptDiscount: true };
  last!.flush(status === 200 ? history : null, { status, statusText: status === 200 ? "OK" : status === 204 ? "No Content" : "Service Unavailable" });
  await selecting; await fixture.whenStable(); fixture.detectChanges();
  const key = c.draft().lines[0].key;
  const price = await loader.getHarness(MatInputHarness.with({ selector: '[name="price-' + key + '"]' }));
  expect(await price.getValue()).toBe(status === 200 ? "5.2" : "");
  const field = fixture.nativeElement.querySelector('input[name="price-' + key + '"]')!.closest("mat-form-field");
  expect(field.textContent).toContain(status === 200 ? "Last recorded at Shop: GBP 5.2" : status === 204 ? "No earlier price found at Shop" : "Recorded price lookup unavailable");
  expect(fixture.nativeElement.textContent).toContain(status === 200 ? "Last recorded price suggested" : status === 204 ? "No usual price or earlier recorded price at Shop" : "Recorded price lookup unavailable");
  expect(c.draft().lines[0].itemId).toBe("beef"); http.verify(); fixture.destroy(); vi.restoreAllMocks();
});
