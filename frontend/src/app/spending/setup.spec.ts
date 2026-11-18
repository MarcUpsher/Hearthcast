import { signal } from "@angular/core";
import { ComponentFixture, TestBed } from "@angular/core/testing";
import { HttpErrorResponse } from "@angular/common/http";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { MatButtonHarness } from "@angular/material/button/testing";
import { MatDialogHarness } from "@angular/material/dialog/testing";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { Api } from "../core/api";
import { Workspace } from "../core/workspace";
import { Household } from "../setup/models";
import { SpendingSetupEntry, SpendingSetupKind } from "./models";
import { SpendingSetup } from "./setup";

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (error: unknown) => void;
  const promise = new Promise<T>((yes, no) => { resolve = yes; reject = no; });
  return { promise, resolve, reject };
}
const row = (kind: SpendingSetupKind, home = "first", name?: string, retired = false): SpendingSetupEntry => ({
  id: `${home}-${kind}${retired ? "-old" : ""}`, householdId: home,
  name: name ?? (kind === "categories" ? "Food" : "Shop"), isRetired: retired,
});

describe("Spending setup", () => {
  let fixture: ComponentFixture<SpendingSetup>;
  let current: ReturnType<typeof signal<Household | null>>;
  let get: ReturnType<typeof vi.fn>;
  let send: ReturnType<typeof vi.fn>;
  const household = (id: string): Household => ({ id, name: id, people: [] });
  beforeEach(() => {
    current = signal<Household | null>(household("first"));
    get = vi.fn(async (path: string) => {
      const home = path.split("/")[1];
      const kind = path.includes("/categories") ? "categories" : "merchants";
      return [row(kind, home), row(kind, home, "Old entry", true)];
    });
    send = vi.fn();
    TestBed.configureTestingModule({ providers: [
      { provide: Workspace, useValue: { current } },
      { provide: Api, useValue: { get, send } },
    ] });
  });
  afterEach(() => { fixture?.destroy(); vi.restoreAllMocks(); });
  async function page() {
    fixture = TestBed.createComponent(SpendingSetup);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture.componentInstance;
  }
  async function settle() { fixture.detectChanges(); await fixture.whenStable(); fixture.detectChanges(); }
  const root = () => TestbedHarnessEnvironment.documentRootLoader(fixture);
  async function button(text: string) { return root().getHarness(MatButtonHarness.with({ text })); }
  function enter(selector: string, value: string) {
    const input = document.querySelector<HTMLInputElement>(selector)!;
    input.value = value;
    input.dispatchEvent(new Event("input", { bubbles: true }));
    fixture.detectChanges();
  }

  it("loads both household-scoped lists without writing and shows active entries initially", async () => {
    const c = await page();
    expect(get.mock.calls.map(([path]) => path).sort()).toEqual([
      "households/first/spending/categories?includeRetired=true",
      "households/first/spending/merchants?includeRetired=true",
    ]);
    expect(send).not.toHaveBeenCalled();
    expect(c.visibleRows("categories").map(r => r.name)).toEqual(["Food"]);
    expect(fixture.nativeElement.querySelector('ul[aria-label="Merchants"]').textContent).toContain("Shop");
    expect(fixture.nativeElement.textContent).not.toContain("Old entry");
  });

  it("shows guidance and makes no requests without a household", async () => {
    current.set(null);
    const c = await page();
    expect(fixture.nativeElement.textContent).toContain("Select or create a household");
    expect(get).not.toHaveBeenCalled();
    expect(c.canWrite("categories")).toBe(false);
    expect(send).not.toHaveBeenCalled();
  });

  it.each(["categories", "merchants"] as const)("creates %s through its own endpoint and preserves the other draft", async kind => {
    const c = await page();
    const other = kind === "categories" ? "merchants" : "categories";
    c.changeNewName(other, "Keep this draft");
    const saved = { ...row(kind), id: "new", name: "New entry" };
    send.mockResolvedValue(saved);
    enter(`form[aria-label="Add ${kind === "categories" ? "category" : "merchant"}"] input`, " New entry ");
    await (await button(kind === "categories" ? "Add category" : "Add merchant")).click();
    expect(send).toHaveBeenCalledExactlyOnceWith("POST", `households/first/spending/${kind}`, { name: " New entry " });
    expect(c.lists()[kind].rows).toContainEqual(saved);
    expect(c.newNames()[kind]).toBe("");
    expect(c.newNames()[other]).toBe("Keep this draft");
  });

  it("rejects a blank creation locally and retains a rejected nonblank draft", async () => {
    const c = await page();
    c.changeNewName("categories", "   ");
    await c.create("categories");
    expect(send).not.toHaveBeenCalled();
    send.mockRejectedValue(new HttpErrorResponse({ status: 400, error: { errors: { Name: ["Choose another name."] } } }));
    c.changeNewName("categories", "Rejected name");
    await c.create("categories");
    await settle();
    expect(c.newNames().categories).toBe("Rejected name");
    expect(c.createErrors().categories).toContain("Choose another name.");
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain("Choose another name.");
    expect(c.lists().categories.rows).toHaveLength(2);
    expect(c.saving()).toBe(false);
  });

  it.each(["categories", "merchants"] as const)("renames %s in a focused sidebar with an explicit name payload", async kind => {
    const c = await page();
    const original = row(kind);
    c.beginEdit(kind, original);
    await settle();
    expect(await (await root().getHarness(MatDialogHarness)).getTitleText()).toBe(`Edit ${kind === "categories" ? "category" : "merchant"}`);
    await vi.waitFor(() => expect(document.activeElement?.getAttribute("name")).toBe("editName"));
    send.mockResolvedValue({ ...original, name: "Renamed" });
    enter('input[name="editName"]', "Renamed");
    await (await button("Save")).click();
    expect(send).toHaveBeenCalledExactlyOnceWith("PUT", `households/first/spending/${kind}/${original.id}/name`, { name: "Renamed" });
    expect(c.lists()[kind].rows.find(r => r.id === original.id)?.name).toBe("Renamed");
    await vi.waitFor(async () => expect(await root().getAllHarnesses(MatDialogHarness)).toHaveLength(0));
  });

  it("Cancel discards the sidebar edit without writing or discarding a creation draft", async () => {
    const c = await page();
    c.changeNewName("merchants", "New merchant draft");
    c.beginEdit("categories", row("categories"));
    await settle();
    enter('input[name="editName"]', "Discard me");
    await (await button("Cancel")).click();
    expect(send).not.toHaveBeenCalled();
    expect(c.editing()).toBeNull();
    expect(c.lists().categories.rows[0].name).toBe("Food");
    expect(c.newNames().merchants).toBe("New merchant draft");
  });

  it("keeps a rejected sidebar edit open and permits a successful retry", async () => {
    const c = await page();
    c.beginEdit("categories", row("categories"));
    await settle();
    enter('input[name="editName"]', "Groceries");
    send.mockRejectedValueOnce(new Error("Save failed"));
    await (await button("Save")).click();
    expect(c.editing()).not.toBeNull();
    expect(c.editName()).toBe("Groceries");
    expect(c.editError()).toBe("Save failed");
    expect(c.lists().categories.rows[0].name).toBe("Food");
    send.mockResolvedValue({ ...row("categories"), name: "Groceries" });
    await (await button("Save")).click();
    expect(c.editing()).toBeNull();
    expect(c.lists().categories.rows.find(r => r.id === row("categories").id)?.name).toBe("Groceries");
  });

  it.each(["categories", "merchants"] as const)("retires and restores %s independently of editing", async kind => {
    const c = await page();
    const original = row(kind);
    send.mockResolvedValueOnce({ ...original, isRetired: true });
    await c.setRetirement(kind, original);
    expect(send).toHaveBeenLastCalledWith("PUT", `households/first/spending/${kind}/${original.id}/retirement`, { isRetired: true });
    expect(c.visibleRows(kind)).not.toContainEqual(original);
    c.status.set("retired");
    const retired = c.visibleRows(kind).find(r => r.id === original.id)!;
    expect(retired.isRetired).toBe(true);
    send.mockResolvedValueOnce(original);
    await c.setRetirement(kind, retired);
    expect(send).toHaveBeenLastCalledWith("PUT", `households/first/spending/${kind}/${original.id}/retirement`, { isRetired: false });
    expect(c.visibleRows(kind).some(r => r.id === original.id)).toBe(false);
    expect(c.editing()).toBeNull();
  });

  it("leaves retirement unchanged after failure and displays a retryable action error", async () => {
    const c = await page();
    send.mockRejectedValueOnce(new Error("Retirement failed"));
    await c.setRetirement("categories", row("categories"));
    await settle();
    expect(c.lists().categories.actionError).toBe("Retirement failed");
    expect(c.lists().categories.rows[0].isRetired).toBe(false);
    expect(c.canWrite("categories")).toBe(true);
    expect(fixture.nativeElement.textContent).toContain("Retirement failed");
  });

  it("combines search with active, retired and all filters and distinguishes filtered empty lists", async () => {
    const c = await page();
    c.status.set("retired");
    c.search.set("  OLD  ");
    await settle();
    expect(c.visibleRows("categories").map(r => r.name)).toEqual(["Old entry"]);
    c.status.set("active");
    await settle();
    expect(fixture.nativeElement.textContent).toContain("No categories match your search and status filter.");
    c.search.set("");
    c.status.set("all");
    expect(c.visibleRows("categories")).toHaveLength(2);
  });

  it("loads lists independently and retries a failed list without losing drafts", async () => {
    get.mockImplementationOnce(async () => { throw new Error("Categories unavailable"); });
    const c = await page();
    expect(c.lists().categories.error).toBe("Categories unavailable");
    expect(c.visibleRows("merchants")).toHaveLength(1);
    c.changeNewName("categories", "Keep my draft");
    expect(c.canWrite("categories")).toBe(false);
    await (await button("Retry categories")).click();
    expect(c.lists().categories.error).toBe("");
    expect(c.visibleRows("categories")).toHaveLength(1);
    expect(c.newNames().categories).toBe("Keep my draft");
    expect(get).toHaveBeenCalledTimes(3);
  });

  it("shows a genuine empty-list prompt when nothing has been defined", async () => {
    get.mockResolvedValue([]);
    await page();
    expect(fixture.nativeElement.textContent).toContain("No categories yet. Add your first category above.");
    expect(fixture.nativeElement.textContent).toContain("No merchants yet. Add your first merchant above.");
  });

  it("ignores an older retry failure after a newer list request has succeeded", async () => {
    const c = await page();
    const old = deferred<SpendingSetupEntry[]>();
    const latest = deferred<SpendingSetupEntry[]>();
    get.mockReturnValueOnce(old.promise).mockReturnValueOnce(latest.promise);
    const first = c.load("categories");
    const second = c.load("categories");
    latest.resolve([row("categories", "first", "Latest")]);
    await second;
    old.reject(new Error("Old failure"));
    await first;
    expect(c.lists().categories.error).toBe("");
    expect(c.lists().categories.loading).toBe(false);
    expect(c.lists().categories.rows[0].name).toBe("Latest");
  });

  it("ignores a stale response after switching away and back to the same household", async () => {
    const c = await page();
    const old = deferred<SpendingSetupEntry[]>();
    get.mockReturnValueOnce(old.promise);
    const pending = c.load("categories");
    current.set(household("second"));
    await settle();
    expect(c.lists().categories.rows[0].householdId).toBe("second");
    current.set(household("first"));
    await settle();
    old.resolve([row("categories", "first", "Obsolete")]);
    await pending;
    expect(c.lists().categories.rows[0].name).toBe("Food");
  });

  it.each([false, true])("ignores old-household write completion without clearing a new write (failure=%s)", async fail => {
    const c = await page();
    const old = deferred<SpendingSetupEntry>();
    const latest = deferred<SpendingSetupEntry>();
    send.mockReturnValueOnce(old.promise).mockReturnValueOnce(latest.promise);
    c.changeNewName("categories", "Old draft");
    const first = c.create("categories");
    // Force an external switch: the normal shell guard blocks switching while writing.
    current.set(household("second"));
    await settle();
    c.changeNewName("merchants", "New draft");
    const second = c.create("merchants");
    if (fail) old.reject(new Error("Old write failed"));
    else old.resolve({ ...row("categories"), id: "old-created", name: "Old draft" });
    await first;
    expect(c.saving()).toBe(true);
    expect(c.newNames().merchants).toBe("New draft");
    expect(c.createErrors().categories).toBe("");
    expect(c.lists().categories.rows.every(r => r.householdId === "second")).toBe(true);
    latest.resolve({ ...row("merchants", "second"), id: "new-created", name: "New draft" });
    await second;
    expect(c.saving()).toBe(false);
    expect(send.mock.calls[1][1]).toBe("households/second/spending/merchants");
  });

  it("guards unsaved creation and edit drafts and protects browser reload", async () => {
    const c = await page();
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    expect(c.canLeave()).toBe(true);
    c.changeNewName("categories", "Draft");
    expect(c.canLeave()).toBe(false);
    const event = new Event("beforeunload", { cancelable: true });
    window.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
    confirm.mockReturnValue(true);
    expect(c.canLeave()).toBe(true);
    c.changeNewName("categories", "");
    c.beginEdit("categories", row("categories"));
    await settle();
    c.editName.set("Edit draft");
    confirm.mockReturnValue(false);
    expect(c.canLeave()).toBe(false);
    expect(c.editName()).toBe("Edit draft");
  });

  it("asks before dismissing a dirty sidebar from its backdrop", async () => {
    const c = await page();
    c.beginEdit("categories", row("categories"));
    await settle();
    c.editName.set("Unsaved");
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
    document.querySelector<HTMLElement>(".cdk-overlay-backdrop")!.click();
    await settle();
    expect(c.editName()).toBe("Unsaved");
    expect(c.editing()).not.toBeNull();
    confirm.mockReturnValue(true);
    document.querySelector<HTMLElement>(".cdk-overlay-backdrop")!.click();
    await settle();
    expect(c.editing()).toBeNull();
    expect(send).not.toHaveBeenCalled();
  });

  it("blocks duplicate submissions, Cancel and navigation while saving", async () => {
    const c = await page();
    c.beginEdit("categories", row("categories"));
    await settle();
    c.editName.set("Updated");
    const pending = deferred<SpendingSetupEntry>();
    send.mockReturnValue(pending.promise);
    const writing = c.saveEdit();
    await c.saveEdit();
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
    c.cancelEdit();
    expect(c.canLeave()).toBe(false);
    expect(confirm).not.toHaveBeenCalled();
    expect(c.editing()).not.toBeNull();
    expect(send).toHaveBeenCalledTimes(1);
    pending.resolve({ ...row("categories"), name: "Updated" });
    await writing;
    expect(c.editing()).toBeNull();
    expect(c.canLeave()).toBe(true);
  });

  it("ignores pending responses after the page is destroyed", async () => {
    const c = await page();
    const pending = deferred<SpendingSetupEntry[]>();
    get.mockReturnValueOnce(pending.promise);
    const loading = c.load("categories");
    const before = c.lists();
    fixture.destroy();
    pending.resolve([row("categories", "first", "Too late")]);
    await loading;
    expect(c.lists()).toBe(before);
  });
});
