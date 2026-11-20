import { signal } from "@angular/core";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { ViewportScroller } from "@angular/common";
import { provideRouter } from "@angular/router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Api } from "../core/api";
import { Workspace } from "../core/workspace";
import { Household } from "../setup/models";
import { SpendingRecords } from "./records";
import { SpendingHistoryNavigation } from "./history-state";
import { SpendingHistoryPage } from "./models";
import { deferred, spendingRecord } from "../../testing/spending";

describe("Purchases & history list", () => {
  let fixture: ComponentFixture<SpendingRecords>;
  let current: ReturnType<typeof signal<Household | null>>;
  let get: ReturnType<typeof vi.fn>, send: ReturnType<typeof vi.fn>;
  let scroll: [number, number];
  const viewport = { getScrollPosition: () => scroll, scrollToPosition: vi.fn() };
  beforeEach(() => {
    scroll = [0, 0]; viewport.scrollToPosition.mockClear();
    current = signal<Household | null>({ id: "first", name: "Home", people: [] });
    get = vi.fn(async (path: string) => ({ records: [spendingRecord(path.split("/")[1])], nextCursor: null, pageSize: 25 }));
    send = vi.fn();
    TestBed.configureTestingModule({ providers: [provideRouter([]),
      { provide: ViewportScroller, useValue: viewport }, { provide: Workspace, useValue: { current } }, { provide: Api, useValue: { get, send } }] });
  });
  afterEach(() => { fixture?.destroy(); vi.restoreAllMocks(); });
  async function settle() { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
  async function page() {
    fixture = TestBed.createComponent(SpendingRecords); await settle();
    await vi.waitFor(() => expect(fixture.componentInstance.historyLoading()).toBe(false)); await settle(); return fixture.componentInstance;
  }
  it("loads only bounded history and links to dedicated pages without an embedded form or details", async () => {
    await page(); expect(get).toHaveBeenCalledExactlyOnceWith("households/first/spending/records?includeVoided=false&pageSize=25");
    expect(fixture.nativeElement.querySelector("app-spending-capture")).toBeNull();
    expect(fixture.nativeElement.querySelector("#record-title")).toBeNull();
    expect(fixture.nativeElement.querySelector('a[href="/spending/records/new"]').textContent).toContain("Record purchase");
    expect(fixture.nativeElement.querySelector('a[href="/spending/records/record"]').textContent).toContain("View details");
    expect(send).not.toHaveBeenCalled();
  });
  it("binds pagination to the applied inclusive filters and preserves previous cursors", async () => {
    const c = await page(); c.changeFilter("from", "2026-12-28"); c.changeFilter("to", "2027-01-10"); c.changeFilter("includeVoided", true);
    get.mockResolvedValueOnce({ records: [spendingRecord()], nextCursor: "cursor A", pageSize: 25 }); await c.loadHistory();
    get.mockResolvedValueOnce({ records: [spendingRecord("first", { id: "next" })], nextCursor: "cursor B", pageSize: 25 }); await c.loadHistory("next");
    expect(Object.fromEntries(new URL(get.mock.calls.at(-1)![0], "http://test/").searchParams)).toEqual({ from: "2026-12-28", to: "2027-01-10", includeVoided: "true", pageSize: "25", cursor: "cursor A" });
    expect(c.previous()).toEqual([null]);
    get.mockResolvedValueOnce({ records: [spendingRecord()], nextCursor: "cursor A", pageSize: 25 }); await c.loadHistory("previous");
    expect(new URL(get.mock.calls.at(-1)![0], "http://test/").searchParams.get("cursor")).toBeNull();
    c.changeFilter("from", "2027-01-01"); expect(c.filtersChanged()).toBe(true);
    get.mockResolvedValueOnce({ records: [], nextCursor: null, pageSize: 25 }); await c.loadHistory(); expect(c.previous()).toEqual([]);
  });
  it("retains the last successful page on errors and rejects invalid filters without requests", async () => {
    const c = await page(); c.page.set({ records: [spendingRecord()], nextCursor: "next", pageSize: 25 });
    get.mockRejectedValueOnce(new Error("Offline")); await c.loadHistory("next");
    expect(c.historyError()).toBe("Offline"); expect(c.page().records).toHaveLength(1); expect(c.previous()).toEqual([]);
    get.mockClear(); c.changeFilter("from", "2026-12-31"); c.changeFilter("to", "2026-12-01"); await c.loadHistory();
    expect(c.historyError()).toContain("valid dates"); expect(get).not.toHaveBeenCalled();
    c.changeFilter("from", "2026-02-30"); await c.loadHistory(); expect(get).not.toHaveBeenCalled();
  });
  it("restores filters, current page, previous cursors and scroll after leaving the list", async () => {
    let c = await page(); c.changeFilter("from", "2026-11-01");
    get.mockResolvedValueOnce({ records: [spendingRecord()], nextCursor: "anchor", pageSize: 25 }); await c.loadHistory();
    get.mockResolvedValueOnce({ records: [spendingRecord("first", { id: "second-page" })], nextCursor: "later", pageSize: 25 }); await c.loadHistory("next");
    c.changeFilter("to", "2026-11-30"); scroll = [0, 640]; fixture.destroy(); get.mockClear();
    c = await page(); expect(get).not.toHaveBeenCalled(); expect(c.filters().to).toBe("2026-11-30"); expect(c.applied().to).toBe("");
    expect(c.page().records[0].id).toBe("second-page"); expect(c.previous()).toEqual([null]);
    expect(TestBed.inject(SpendingHistoryNavigation).read("first")?.scroll).toEqual([0, 640]);
    await c.loadHistory("current"); expect(get.mock.calls.at(-1)![0]).toContain("cursor=anchor");
  });
  it("refreshes a changed retained page using its cursor and keeps stale results if refresh fails", async () => {
    let c = await page(); get.mockResolvedValueOnce({ records: [spendingRecord()], nextCursor: "anchor", pageSize: 25 }); await c.loadHistory();
    get.mockResolvedValueOnce({ records: [spendingRecord("first", { id: "second-page" })], nextCursor: null, pageSize: 25 }); await c.loadHistory("next");
    fixture.destroy(); TestBed.inject(SpendingHistoryNavigation).changed("first"); get.mockRejectedValueOnce(new Error("Offline"));
    c = await page(); expect(c.page().records[0].id).toBe("second-page"); expect(c.historyError()).toBe("Offline"); expect(c.refreshNeeded()).toBe(true);
    expect(get.mock.calls.at(-1)![0]).toContain("cursor=anchor"); expect(c.previous()).toEqual([null]);
    await c.loadHistory("current"); expect(c.refreshNeeded()).toBe(false); expect(c.previous()).toEqual([null]);
  });
  it("clears state across household changes and ignores late responses, including A to B to A", async () => {
    const c = await page(), pending = deferred<SpendingHistoryPage>(); get.mockReturnValueOnce(pending.promise); const reading = c.loadHistory();
    current.set({ id: "second", name: "Other", people: [] }); await settle(); current.set({ id: "first", name: "Home", people: [] }); await settle();
    pending.resolve({ records: [spendingRecord("first", { id: "stale" })], nextCursor: null, pageSize: 25 }); await reading;
    expect(c.page().records[0].id).toBe("record"); expect(c.filters().from).toBe("");
  });
  it.each([["2026-06-01", "1 Jun 2026"], ["2026-01-01", "1 Jan 2026"]])("preserves calendar date %s", async (date, expected) => {
    const c = await page(); c.page.set({ records: [spendingRecord("first", { spendingDate: date })], nextCursor: null, pageSize: 25 }); await settle();
    expect(fixture.nativeElement.textContent).toContain(expected); expect(fixture.nativeElement.textContent).not.toContain("31 May 2026");
  });
});
