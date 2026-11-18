import { signal } from "@angular/core";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { provideRouter } from "@angular/router";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { MatButtonHarness } from "@angular/material/button/testing";
import { MatDialogHarness } from "@angular/material/dialog/testing";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Api } from "../core/api";
import { Workspace } from "../core/workspace";
import { Household } from "../setup/models";
import { SpendingItems } from "./items";
import { SpendingItem } from "./models";
import { deferred, spendingDraft, spendingEntry, spendingItem, spendingPrice } from "../../testing/spending";

describe("Spending item catalogue", () => {
  let fixture: ComponentFixture<SpendingItems>;
  let current: ReturnType<typeof signal<Household | null>>;
  let get: ReturnType<typeof vi.fn>;
  let send: ReturnType<typeof vi.fn>;
  const home = (id: string): Household => ({ id, name: id, people: [] });
  beforeEach(() => {
    current = signal<Household | null>(home("first"));
    get = vi.fn(async (path: string) => {
      const id = path.split("/")[1];
      if (path.includes("/usual-prices")) return [spendingPrice(id)];
      if (path.includes("/categories")) return [spendingEntry("food", id)];
      if (path.includes("/merchants")) return [spendingEntry("shop", id)];
      return [spendingItem(id), spendingItem(id, { id: "old", code: "OLD", name: "Old item", isRetired: true })];
    });
    send = vi.fn();
    TestBed.configureTestingModule({ providers: [provideRouter([]),
      { provide: Workspace, useValue: { current } }, { provide: Api, useValue: { get, send } }] });
  });
  afterEach(() => { fixture?.destroy(); vi.restoreAllMocks(); });
  async function settle() { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
  async function page() {
    fixture = TestBed.createComponent(SpendingItems);
    await settle();
    return fixture.componentInstance;
  }
  const root = () => TestbedHarnessEnvironment.documentRootLoader(fixture);
  const button = (text: string) => root().getHarness(MatButtonHarness.with({ text }));

  it("loads the selected household's catalogue and lookups with retired entries without writing", async () => {
    const c = await page();
    expect(get.mock.calls.map(([path]) => path).sort()).toEqual([
      "households/first/spending/categories?includeRetired=true", "households/first/spending/items?includeRetired=true",
      "households/first/spending/merchants?includeRetired=true",
    ]);
    expect(c.visibleRows().map(row => row.name)).toEqual(["Eggs"]);
    expect(send).not.toHaveBeenCalled();
    expect(fixture.nativeElement.textContent).toContain("EGGS-6");
    expect(fixture.nativeElement.querySelector('a[href="/spending/setup"]')).not.toBeNull();
  });

  it("creates an item with the full explicit contract and resets the successful form", async () => {
    const c = await page();
    c.createDraft.set(spendingDraft());
    const saved = spendingItem("first", { ...spendingDraft(), id: "milk" });
    send.mockResolvedValue(saved);
    await settle();
    await (await button("Add item")).click();
    expect(send).toHaveBeenCalledExactlyOnceWith("POST", "households/first/spending/items", spendingDraft());
    expect(c.rows()).toContainEqual(saved);
    expect(c.hasUnsavedChanges()).toBe(false);
    await settle();
    const form = fixture.nativeElement.querySelector('form[aria-label="Add item"]');
    expect(form.querySelector('input[name="name"]').value).toBe("");
    expect(form.classList.contains("ng-submitted")).toBe(false);
    expect(form.querySelectorAll(".mat-form-field-invalid")).toHaveLength(0);
  });

  it("preserves every creation field and the error after a rejected save", async () => {
    const c = await page();
    c.createDraft.set(spendingDraft());
    send.mockRejectedValue(new Error("This item code is reserved"));
    await c.submit(false);
    await settle();
    expect(c.createDraft()).toEqual(spendingDraft());
    expect(c.createError()).toBe("This item code is reserved");
    expect(c.rows()).toHaveLength(2);
    expect(c.canWrite()).toBe(true);
    expect(fixture.nativeElement.textContent).toContain("This item code is reserved");
  });

  it.each(["code", "name", "unitKey", "unitLabel", "expenseCategoryId"] as const)("requires %s before creation", async field => {
    const c = await page();
    c.createDraft.set({ ...spendingDraft(), [field]: "" });
    expect(c.valid(false)).toBe(false);
    await c.submit(false);
    expect(send).not.toHaveBeenCalled();
  });

  it("edits mutable metadata in a sidebar while keeping code and unit identity read-only", async () => {
    const c = await page();
    c.beginEdit(spendingItem());
    await settle();
    const form = document.querySelector<HTMLFormElement>('form[aria-label="Edit item"]')!;
    expect(form.querySelector<HTMLInputElement>('input[name="code"]')!.readOnly).toBe(true);
    expect(form.querySelector('input[name="unitKey"]')).toBeNull();
    expect(c.editDraft().unitKey).toBe("pack-6");
    await vi.waitFor(() => expect(document.activeElement?.getAttribute("name")).toBe("name"));
    c.change(true, "name", "Free range eggs");
    c.change(true, "unitLabel", "Box of six");
    c.change(true, "notes", "");
    send.mockResolvedValue(spendingItem("first", { name: "Free range eggs", unitLabel: "Box of six" }));
    await settle();
    await (await button("Save item")).click();
    expect(send).toHaveBeenCalledExactlyOnceWith("PUT", "households/first/spending/items/eggs/details", {
      expenseCategoryId: "first-food", name: "Free range eggs", unitLabel: "Box of six", notes: null,
    });
    expect(c.rows().find(row => row.id === "eggs")?.name).toBe("Free range eggs");
    await vi.waitFor(async () => expect(await root().getAllHarnesses(MatDialogHarness)).toHaveLength(0));
  });

  it("retains rejected item edits and Cancel discards only the editor draft", async () => {
    const c = await page();
    c.createDraft.set(spendingDraft());
    c.beginEdit(spendingItem());
    await settle();
    c.change(true, "name", "Edited eggs");
    send.mockRejectedValue(new Error("Edit failed"));
    await c.submit(true);
    expect(c.editDraft().name).toBe("Edited eggs");
    expect(c.editError()).toBe("Edit failed");
    expect(c.editing()).not.toBeNull();
    c.cancelEdit();
    expect(c.createDraft()).toEqual(spendingDraft());
    expect(c.editing()).toBeNull();
    expect(c.rows().find(row => row.id === "eggs")?.name).toBe("Eggs");
  });

  it("retires and restores an item using its own flag without hiding items in retired categories", async () => {
    const c = await page();
    c.categories.set([{ ...spendingEntry("food"), isRetired: true }]);
    expect(c.visibleRows()).toContainEqual(spendingItem());
    send.mockResolvedValueOnce(spendingItem("first", { isRetired: true }));
    await c.setRetirement(spendingItem());
    expect(send).toHaveBeenLastCalledWith("PUT", "households/first/spending/items/eggs/retirement", { isRetired: true });
    expect(c.visibleRows()).toEqual([]);
    c.status.set("retired");
    send.mockResolvedValueOnce(spendingItem());
    await c.setRetirement(spendingItem("first", { isRetired: true }));
    expect(send).toHaveBeenLastCalledWith("PUT", "households/first/spending/items/eggs/retirement", { isRetired: false });
    expect(c.visibleRows().some(row => row.id === "eggs")).toBe(false);
  });

  it("combines category, retirement and code/name/unit/notes search", async () => {
    const c = await page();
    c.search.set("pack-6");
    expect(c.visibleRows().map(row => row.id)).toEqual(["eggs"]);
    c.search.set("EGGS-6");
    expect(c.visibleRows().map(row => row.id)).toEqual(["eggs"]);
    c.category.set("other-category");
    expect(c.visibleRows()).toEqual([]);
    c.category.set("");
    c.status.set("all");
    c.search.set("OLD");
    expect(c.visibleRows().map(row => row.id)).toEqual(["old"]);
    c.rows.set([spendingItem("first", { notes: "Organic weekly delivery" })]);
    c.search.set("weekly");
    expect(c.visibleRows()).toHaveLength(1);
  });

  it("shows setup guidance for an empty catalogue and makes no calls without a household", async () => {
    current.set(null);
    const c = await page();
    expect(get).not.toHaveBeenCalled();
    expect(c.canWrite()).toBe(false);
    expect(fixture.nativeElement.textContent).toContain("Select or create a household");
    get.mockResolvedValue([]);
    current.set(home("first"));
    await settle();
    await vi.waitFor(() => expect(c.loading()).toBe(false));
    await settle();
    expect(fixture.nativeElement.textContent).toContain("Add a spending category");
    expect(fixture.nativeElement.textContent).toContain("No catalogue items yet");
  });

  it("keeps an entered item through a failed reload and retry", async () => {
    const c = await page();
    c.createDraft.set(spendingDraft());
    get.mockRejectedValueOnce(new Error("Catalogue unavailable"));
    await c.load();
    expect(c.error()).toBe("Catalogue unavailable");
    expect(c.canWrite()).toBe(false);
    await c.load();
    expect(c.error()).toBe("");
    expect(c.createDraft()).toEqual(spendingDraft());
    expect(c.rows()).toHaveLength(2);
  });

  it("clears submitted validation state when an abandoned creation moves to another household", async () => {
    const c = await page();
    c.createDraft.set(spendingDraft());
    send.mockRejectedValue(new Error("Failed save"));
    await settle();
    await (await button("Add item")).click();
    current.set(home("second"));
    await settle();
    await vi.waitFor(() => expect(c.loading()).toBe(false));
    await settle();
    const form = fixture.nativeElement.querySelector('form[aria-label="Add item"]');
    expect(form.classList.contains("ng-submitted")).toBe(false);
    expect(form.querySelectorAll(".mat-form-field-invalid")).toHaveLength(0);
  });

  it("ignores late reads after switching away and back to the same household", async () => {
    const c = await page();
    const pending = deferred<SpendingItem[]>();
    get.mockReturnValueOnce(pending.promise);
    const reading = c.load();
    current.set(home("second"));
    await settle();
    expect(c.rows()[0].householdId).toBe("second");
    current.set(home("first"));
    await settle();
    pending.resolve([spendingItem("first", { name: "Stale response" })]);
    await reading;
    expect(c.rows()[0].name).toBe("Eggs");
  });

  it.each([false, true])("ignores an old household's pending creation completion (failure=%s)", async fail => {
    const c = await page();
    c.createDraft.set(spendingDraft());
    const pending = deferred<SpendingItem>();
    send.mockReturnValue(pending.promise);
    const writing = c.submit(false);
    expect(c.canLeave()).toBe(false);
    current.set(home("second"));
    await settle();
    c.createDraft.set(spendingDraft("second"));
    if (fail) pending.reject(new Error("Old failure")); else pending.resolve(spendingItem());
    await writing;
    expect(c.rows().every(row => row.householdId === "second")).toBe(true);
    expect(c.createDraft()).toEqual(spendingDraft("second"));
    expect(c.createError()).toBe("");
    expect(c.saving()).toBe(false);
    expect(c.notice()).toBe("");
  });

  it("includes nested price drafts and writes in navigation guards and protects item selection", async () => {
    const c = await page();
    c.showPrices(spendingItem());
    await settle();
    c.prices()!.change("amount", "4");
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    c.showPrices(spendingItem("first", { id: "milk", name: "Milk" }));
    expect(c.selectedItem()?.id).toBe("eggs");
    expect(c.canLeave()).toBe(false);
    c.prices()!.saving.set(true);
    confirm.mockClear();
    expect(c.canLeave()).toBe(false);
    expect(confirm).not.toHaveBeenCalled();
    c.prices()!.saving.set(false);
    confirm.mockReturnValue(true);
    c.showPrices(spendingItem("first", { id: "milk", name: "Milk" }));
    await settle();
    expect(c.selectedItem()?.id).toBe("milk");
    expect(c.prices()!.draft().amount).toBe("");
  });

  it("retains a price draft and copied price unit label when the item's label is edited", async () => {
    const c = await page();
    c.showPrices(spendingItem());
    await settle();
    const prices = c.prices()!;
    prices.beginEdit(spendingPrice());
    prices.change("amount", "4.125");
    c.beginEdit(spendingItem());
    await settle();
    c.change(true, "unitLabel", "Box of six");
    send.mockResolvedValue(spendingItem("first", { unitLabel: "Box of six" }));
    await c.submit(true);
    await settle();
    expect(c.selectedItem()?.unitLabel).toBe("Box of six");
    expect(prices.draft().amount).toBe("4.125");
    expect(prices.rows()[0].unitLabel).toBe("Six eggs");
  });
  it("generates a hidden unit identity once, retains it across failure/label edits, and resets cleanly after saving", async () => {
    const c = await page(), key = c.createDraft().unitKey;
    expect(key).toMatch(/^[a-f0-9]{32}$/); expect(c.hasUnsavedChanges()).toBe(false);
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    expect(c.canLeave()).toBe(true); expect(confirm).not.toHaveBeenCalled();
    expect(fixture.nativeElement.querySelector('[name="unitKey"]')).toBeNull();
    const { unitKey: _key, ...fields } = spendingDraft();
    for (const [field, value] of Object.entries(fields)) c.change(false, field as keyof typeof fields, value);
    send.mockRejectedValueOnce(new Error("Try again")); await c.submit(false);
    c.change(false, "unitLabel", "Bottle"); expect(c.createDraft().unitKey).toBe(key);
    send.mockResolvedValueOnce(spendingItem("first", { id: "milk", unitKey: key })); await c.submit(false);
    expect(send.mock.calls[0][2].unitKey).toBe(key); expect(send.mock.calls[1][2].unitKey).toBe(key);
    expect(c.createDraft().unitKey).not.toBe(key); expect(c.hasUnsavedChanges()).toBe(false);
    expect(fixture.nativeElement.textContent).not.toContain("(pack-6)");
  });
  it("preserves existing hidden keys and collapsed notes when editing another item field", async () => {
    const c = await page(); c.beginEdit(spendingItem("first", { notes: "Keep existing notes" })); await settle();
    const form = document.querySelector('form[aria-label="Edit item"]')!;
    expect((form.querySelector("details") as HTMLDetailsElement).open).toBe(false);
    c.change(true, "name", "New label");
    send.mockResolvedValueOnce(spendingItem("first", { name: "New label", notes: "Keep existing notes" }));
    await c.submit(true);
    expect(send.mock.calls[0][2].notes).toBe("Keep existing notes");
    expect(send.mock.calls[0][2].unitKey).toBeUndefined(); expect(c.rows().find(item => item.id === "eggs")?.unitKey).toBe("pack-6");
  });

});
