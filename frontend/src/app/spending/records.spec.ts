import { signal } from "@angular/core";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { HttpErrorResponse } from "@angular/common/http";
import { provideRouter } from "@angular/router";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Api } from "../core/api";
import { Workspace } from "../core/workspace";
import { Household } from "../setup/models";
import { SpendingRecords } from "./records";
import { SpendingHistoryPage, SpendingRecord } from "./models";
import { deferred, spendingEntry, spendingItem, spendingRecord } from "../../testing/spending";

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
});
