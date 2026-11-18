import { signal } from "@angular/core";
import { TestBed } from "@angular/core/testing";
import { provideRouter } from "@angular/router";
import { provideHttpClient } from "@angular/common/http";
import { HttpTestingController, provideHttpClientTesting } from "@angular/common/http/testing";
import { afterEach, expect, it } from "vitest";
import { Workspace } from "../core/workspace";
import { SpendingSummary } from "./summary";

function setup() {
  TestBed.configureTestingModule({ providers: [provideRouter([]), provideHttpClient(), provideHttpClientTesting(),
    { provide: Workspace, useValue: { current: signal({ id: "home", name: "Home", people: [] }) } }] });
  const fixture = TestBed.createComponent(SpendingSummary); fixture.detectChanges();
  const http = TestBed.inject(HttpTestingController);
  const request = http.expectOne(req => req.url === "/api/households/home/spending/summary?from=" + fixture.componentInstance.range().from + "&to=" + fixture.componentInstance.range().to);
  return { fixture, http, request };
}
afterEach(() => TestBed.inject(HttpTestingController).verify());

it("renders an exact large category sum from raw HTTP JSON without losing fractional digits", async () => {
  const { fixture, request } = setup();
  expect(request.request.responseType).toBe("text");
  request.flush(`{"householdId":"home","from":"2026-12-01","to":"2026-12-31","categories":[{"expenseCategoryId":"food","categoryName":"Food","currencyCode":"CLF","currencyMinorUnits":4,"netAmount":1900000000000.0001}],"reconciliationRecordCounts":{"notChecked":2,"reconciled":0,"unreconciled":0},"warnings":[]}`);
  await fixture.whenStable(); fixture.detectChanges();
  expect(fixture.nativeElement.querySelector("tbody").textContent).toContain("CLF 1 900 000 000 000.0001");
  expect(fixture.componentInstance.report()?.categories[0].netAmount).toBe("1900000000000.0001");
  fixture.destroy();
});
it("shows Problem Details from a text-mode HTTP error without presenting an empty report", async () => {
  const { fixture, request } = setup();
  request.flush('{"detail":"Household not found"}', { status: 404, statusText: "Not Found" });
  await fixture.whenStable(); fixture.detectChanges();
  expect(fixture.componentInstance.error()).toBe("Household not found");
  expect(fixture.componentInstance.report()).toBeNull(); fixture.destroy();
});
