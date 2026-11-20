import { signal } from "@angular/core";
import { TestBed } from "@angular/core/testing";
import { Location, ViewportScroller } from "@angular/common";
import { provideLocationMocks } from "@angular/common/testing";
import { NavigationCancel, NavigationEnd, provideRouter, Router } from "@angular/router";
import { RouterTestingHarness } from "@angular/router/testing";
import { HttpErrorResponse } from "@angular/common/http";
import { firstValueFrom, filter } from "rxjs";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { routes } from "../app.routes";
import { Api } from "../core/api";
import { Workspace } from "../core/workspace";
import { SpendingRecords } from "./records";
import { SpendingPurchase } from "./purchase";
import { SpendingRecord } from "./models";
import { deferred, spendingEntry, spendingItem, spendingRecord } from "../../testing/spending";

describe("Purchase page navigation", () => {
  let get: ReturnType<typeof vi.fn>, send: ReturnType<typeof vi.fn>;
  let harness: RouterTestingHarness;
  let position: [number, number];
  const viewport = { getScrollPosition: () => position, scrollToPosition: vi.fn((value: [number, number]) => { position = value; }) };
  beforeEach(() => {
    position = [0, 0]; viewport.scrollToPosition.mockClear();
    get = vi.fn(async (path: string) => {
      if (path.includes("/categories?")) return [spendingEntry("food")];
      if (path.includes("/merchants?")) return [spendingEntry("shop")];
      if (path.includes("/items?")) return [spendingItem()];
      if (path.endsWith("/currencies")) return [{ code: "GBP", minorUnits: 2 }];
      if (path.includes("/usual-price?") || path.includes("/last-paid-price?")) return null;
      if (path.includes("/records?")) return { records: [spendingRecord()], nextCursor: null, pageSize: 25 };
      return spendingRecord("first", { id: path.split("/").at(-1) });
    });
    send = vi.fn(async (_method: string, path: string, body: { id?: string }) => spendingRecord("first", { id: path.endsWith("/records") ? body.id : "record", notes: "Saved correction", revision: "saved" }));
    TestBed.configureTestingModule({ providers: [provideRouter(routes), provideLocationMocks(),
      { provide: ViewportScroller, useValue: viewport }, { provide: Api, useValue: { get, send } },
      { provide: Workspace, useValue: { current: signal({ id: "first", name: "Home", people: [] }) } }] });
  });
  afterEach(() => { harness?.fixture.destroy(); vi.restoreAllMocks(); });
  async function settle() { harness.detectChanges(); await harness.fixture.whenStable(); harness.detectChanges(); }
  async function start(url: string) { TestBed.inject(Router).setUpLocationChangeListener(); harness = await RouterTestingHarness.create(url); await settle(); }
  async function back() {
    const result = firstValueFrom(TestBed.inject(Router).events.pipe(filter(event => event instanceof NavigationEnd || event instanceof NavigationCancel)));
    TestBed.inject(Location).back(); const event = await result; await settle(); return event;
  }
  it("uses the literal new route for creation and a direct record URL for read-only details", async () => {
    await start("/spending/records/new"); let page = harness.routeDebugElement!.componentInstance as SpendingPurchase;
    expect(page.creating).toBe(true); expect(page.capture()).toBeDefined(); expect(get.mock.calls.some(([path]) => path.endsWith("/records/new"))).toBe(false);
    expect(harness.routeNativeElement!.querySelector("#history-title")).toBeNull();
    await harness.navigateByUrl("/spending/records/record"); await settle(); page = harness.routeDebugElement!.componentInstance;
    expect(page.selected()?.id).toBe("record"); expect(page.capture()).toBeUndefined(); expect(page.editing()).toBeNull();
    expect(harness.routeNativeElement!.querySelector('nav[aria-label="Spending"] a[href="/spending/records"]')?.classList.contains("active")).toBe(true);
  });
  it("restores filters, page and scroll with browser Back and the Back to purchases link", async () => {
    await start("/spending/records"); let list = harness.routeDebugElement!.componentInstance as SpendingRecords;
    list.changeFilter("from", "2026-11-01"); get.mockResolvedValueOnce({ records: [spendingRecord()], nextCursor: "anchor", pageSize: 25 }); await list.loadHistory();
    get.mockResolvedValueOnce({ records: [spendingRecord("first", { id: "second-page" })], nextCursor: null, pageSize: 25 }); await list.loadHistory("next"); await settle();
    position = [0, 560]; await harness.navigateByUrl("/spending/records/second-page"); await settle(); expect(position).toEqual([0, 0]); get.mockClear();
    expect(await back()).toBeInstanceOf(NavigationEnd); list = harness.routeDebugElement!.componentInstance;
    expect(list.filters().from).toBe("2026-11-01"); expect(list.page().records[0].id).toBe("second-page"); expect(list.previous()).toEqual([null]);
    expect(position).toEqual([0, 560]); expect(get).not.toHaveBeenCalled();
    await harness.navigateByUrl("/spending/records/second-page"); await settle();
    (harness.routeNativeElement!.querySelector('a[href="/spending/records"]') as HTMLAnchorElement).click(); await settle();
    expect(TestBed.inject(Router).url).toBe("/spending/records"); expect(position).toEqual([0, 560]);
  });
  it("guards dirty edits across record parameters and browser Back", async () => {
    await start("/spending/records"); await harness.navigateByUrl("/spending/records/record"); await settle();
    const page = harness.routeDebugElement!.componentInstance as SpendingPurchase; await page.beginEdit(); await settle(); page.capture()!.set("notes", "Keep edit");
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    expect(await TestBed.inject(Router).navigateByUrl("/spending/records/other")).toBe(false); expect(page.capture()?.draft().notes).toBe("Keep edit");
    expect(await back()).toBeInstanceOf(NavigationCancel); expect(TestBed.inject(Router).url).toBe("/spending/records/record");
    confirm.mockReturnValue(true); await harness.navigateByUrl("/spending/records/other"); await settle();
    expect((harness.routeDebugElement!.componentInstance as SpendingPurchase).selected()?.id).toBe("other");
    expect((harness.routeDebugElement!.componentInstance as SpendingPurchase).capture()).toBeUndefined();
  });
  it("replaces the creation URL on save and browser Back returns to the retained list", async () => {
    await start("/spending/records"); const list = harness.routeDebugElement!.componentInstance as SpendingRecords; list.changeFilter("from", "2026-11-01"); await list.loadHistory();
    await harness.navigateByUrl("/spending/records/new"); await settle(); const page = harness.routeDebugElement!.componentInstance as SpendingPurchase;
    const capture = page.capture()!, key = capture.draft().lines[0].key; capture.set("merchantId", "first-shop"); capture.setLineKind(key, "category"); capture.changeLine(key, "categoryId", "first-food"); capture.changeLine(key, "amount", "12");
    await capture.save(); await settle(); expect(TestBed.inject(Router).url).toBe("/spending/records/" + send.mock.calls[0][2].id);
    const detail = harness.routeDebugElement!.componentInstance as SpendingPurchase; expect(detail.capture()).toBeUndefined(); expect(detail.notice()).toBe("Purchase saved.");
    expect(await back()).toBeInstanceOf(NavigationEnd); expect(TestBed.inject(Router).url).toBe("/spending/records");
    expect((harness.routeDebugElement!.componentInstance as SpendingRecords).filters().from).toBe("2026-11-01");
  });
  it("keeps uncertain new purchases until the user permits leaving", async () => {
    await start("/spending/records/new"); const page = harness.routeDebugElement!.componentInstance as SpendingPurchase;
    const capture = page.capture()!; capture.set("notes", "Receipt"); capture.frozen.set({ id: "pending", merchantId: "first-shop", spendingDate: "2026-11-20", currencyCode: "GBP", kind: "Payment", receiptTotal: null, externalReference: null, notes: "Receipt", lines: [] });
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false); expect(await TestBed.inject(Router).navigateByUrl("/spending/records")).toBe(false);
    expect(capture.frozen()?.id).toBe("pending"); confirm.mockReturnValue(true); expect(await TestBed.inject(Router).navigateByUrl("/spending/records")).toBe(true);
  });
  it.each(["create", "edit", "void"])("refreshes the retained cursor after an ambiguous %s response", async operation => {
    await start("/spending/records"); const list = harness.routeDebugElement!.componentInstance as SpendingRecords;
    get.mockResolvedValueOnce({ records: [spendingRecord()], nextCursor: "anchor", pageSize: 25 }); await list.loadHistory();
    get.mockResolvedValueOnce({ records: [spendingRecord()], nextCursor: null, pageSize: 25 }); await list.loadHistory("next");
    await harness.navigateByUrl(operation === "create" ? "/spending/records/new" : "/spending/records/record"); await settle();
    const page = harness.routeDebugElement!.componentInstance as SpendingPurchase; vi.spyOn(window, "confirm").mockReturnValue(true);
    send.mockRejectedValueOnce(new HttpErrorResponse({ status: 0 }));
    if (operation === "void") await page.voidRecord();
    else {
      if (operation === "edit") { await page.beginEdit(); await settle(); page.capture()!.set("notes", "Change"); }
      else {
        const c = page.capture()!, key = c.draft().lines[0].key; c.set("merchantId", "first-shop"); c.setLineKind(key, "category"); c.changeLine(key, "categoryId", "first-food"); c.changeLine(key, "amount", "12");
      }
      await page.capture()!.save();
    }
    get.mockClear(); await harness.navigateByUrl("/spending/records"); await settle();
    expect(get.mock.calls.some(([path]) => path.includes("/records?") && path.includes("cursor=anchor"))).toBe(true);
    expect((harness.routeDebugElement!.componentInstance as SpendingRecords).previous()).toEqual([null]);
  });
  it("blocks navigation during saves and discards detail reads after leaving", async () => {
    await start("/spending/records/record"); const page = harness.routeDebugElement!.componentInstance as SpendingPurchase;
    await page.beginEdit(); await settle(); page.capture()!.saving.set(true);
    expect(await TestBed.inject(Router).navigateByUrl("/spending/records")).toBe(false); page.capture()!.saving.set(false);
    page.editing.set(null); await settle(); const pending = deferred<SpendingRecord>(); get.mockReturnValueOnce(pending.promise); const reading = page.reloadDetail();
    await harness.navigateByUrl("/spending/records"); await settle(); pending.resolve(spendingRecord()); await reading;
    expect(TestBed.inject(Router).url).toBe("/spending/records"); expect(harness.routeDebugElement!.componentInstance).toBeInstanceOf(SpendingRecords);
  });
});
