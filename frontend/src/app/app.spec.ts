import { TestBed } from "@angular/core/testing";
import { Location } from "@angular/common";
import { provideLocationMocks } from "@angular/common/testing";
import { By } from "@angular/platform-browser";
import { NavigationCancel, NavigationEnd, provideRouter, Router, RouterOutlet } from "@angular/router";
import { MatSelect, MatSelectChange } from "@angular/material/select";
import { afterEach, describe, expect, it, vi } from "vitest";
import { App } from "./app";
import { routes } from "./app.routes";
import { Api } from "./core/api";
import { Workspace } from "./core/workspace";
import { Modelling } from "./modelling/modelling";
import { SnapshotEditor } from "./snapshots/editor";
import { SpendingSetup } from "./spending/setup";
import { SpendingItems } from "./spending/items";
import { SpendingRecords } from "./spending/records";
import { SpendingSummary } from "./spending/summary";
import { spendingEntry, spendingItem, spendingRecord } from "../testing/spending";
import { firstValueFrom, filter } from "rxjs";
import { MatDialog } from "@angular/material/dialog";

async function setup(path: string, history = false) {
  localStorage.setItem("household", "first");
  const households = ["first", "second"].map((id) => ({
    id,
    name: id,
    people: [],
  }));
  const get = vi.fn(async (path: string) => {
    if (path === "households") return households;
    if (path.includes("/spending/items?")) return [spendingItem(path.split("/")[1])];
    if (path.includes("/spending/categories?")) return [spendingEntry("food", path.split("/")[1])];
    if (path.includes("/spending/merchants?")) return [spendingEntry("shop", path.split("/")[1])];
    if (path.endsWith("/spending/currencies")) return [{ code: "GBP", minorUnits: 2 }];
    if (path.includes("/spending/records?")) return { records: [], nextCursor: null, pageSize: 25 };
    if (path.includes("/spending/records/")) return spendingRecord(path.split("/")[1]);
    if (path.includes("/spending/summary?")) {
      const query = new URL(path, "http://test/").searchParams;
      return { householdId: path.split("/")[1], from: query.get("from")!, to: query.get("to")!, categories: [],
        reconciliationRecordCounts: { notChecked: 0, reconciled: 0, unreconciled: 0 }, warnings: [] };
    }
    if (path.endsWith("/snapshots"))
      return [{ id: "latest", snapshotDate: "2026-01-01" }];
    if (path.endsWith("/snapshots/latest"))
      return {
        id: "latest",
        revision: "revision",
        snapshotDate: "2026-01-01",
        datePrecision: 1,
        people: [],
        itemSnapshots: [],
        financedAssetSnapshots: [],
      };
    return [];
  });
  const send = vi.fn();
  TestBed.configureTestingModule({
    providers: [
      provideRouter(routes),
      ...(history ? provideLocationMocks() : []),
      { provide: Api, useValue: { get, send, getText: async (path: string) => JSON.stringify(await get(path)) } },
    ],
  });
  const fixture = TestBed.createComponent(App);
  fixture.detectChanges();
  await fixture.whenStable();
  await TestBed.inject(Router).navigateByUrl(path);
  await fixture.whenStable();
  fixture.detectChanges();
  const outlet = fixture.debugElement
    .query(By.directive(RouterOutlet))
    .injector.get(RouterOutlet);
  const page = outlet.component as Modelling | SnapshotEditor | SpendingSetup | SpendingItems | SpendingRecords;
  if (page instanceof SpendingRecords) {
    await vi.waitFor(() => expect(page.lookupLoading()).toBe(false));
    fixture.detectChanges(); await fixture.whenStable();
  }
  const workspace = TestBed.inject(Workspace);
  const picker = fixture.debugElement.query(By.css(".household-picker"))
    .componentInstance as MatSelect;
  const choose = async (id: string) => {
    picker.value = id;
    picker.selectionChange.emit(new MatSelectChange(picker, id));
    fixture.detectChanges();
    await fixture.whenStable();
  };
  const edit = () => {
    if (page instanceof SpendingRecords) page.capture()!.set("notes", "Unsaved purchase");
    else if (page instanceof SpendingItems) page.change(false, "name", "Unsaved item");
    else if (page instanceof SpendingSetup) page.changeNewName("categories", "Unsaved category");
    else if (page instanceof Modelling) page.presetName = "Unsaved scenario";
    else page.date = "2000-01-01";
  };
  const draft = () => page instanceof SpendingRecords ? page.capture()!.draft().notes : page instanceof SpendingItems ? page.createDraft().name : page instanceof SpendingSetup ? page.newNames().categories :
    (page instanceof Modelling ? page.presetName : page.date);
  const writing = () => {
    if (page instanceof SpendingRecords) page.capture()!.saving.set(true);
    else if (page instanceof Modelling) page.savingPreset.set(true);
    else page.saving.set(true);
  };
  return {
    fixture,
    workspace,
    picker,
    page,
    get,
    send,
    choose,
    edit,
    draft,
    writing,
  };
}

afterEach(() => {
  vi.restoreAllMocks();
  localStorage.removeItem("household");
});

describe.each(["/modelling", "/record", "/spending/setup", "/spending/items", "/spending/records"])(
  "Household switching from %s",
  (path) => {
    it("allows a clean page to switch without confirmation", async () => {
      const { workspace, picker, choose } = await setup(path);
      const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
      await choose("second");
      expect(workspace.current()?.id).toBe("second");
      expect(picker.value).toBe("second");
      expect(localStorage.getItem("household")).toBe("second");
      expect(confirm).not.toHaveBeenCalled();
    });

    it("preserves the draft, picker and household when discard is cancelled", async () => {
      const { workspace, picker, get, choose, edit, draft } = await setup(path);
      edit();
      const before = draft();
      get.mockClear();
      const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
      await choose("second");
      expect(confirm).toHaveBeenCalledOnce();
      expect(workspace.current()?.id).toBe("first");
      expect(picker.value).toBe("first");
      expect(localStorage.getItem("household")).toBe("first");
      expect(draft()).toBe(before);
      expect(get).not.toHaveBeenCalled();
    });

    it("asks before changing the household and discards only when accepted", async () => {
      const { workspace, picker, page, choose, edit, draft } =
        await setup(path);
      edit();
      const before = draft();
      const confirm = vi.spyOn(window, "confirm").mockImplementation(() => {
        expect(workspace.current()?.id).toBe("first");
        expect(draft()).toBe(before);
        return true;
      });
      await choose("second");
      expect(confirm).toHaveBeenCalledOnce();
      expect(workspace.current()?.id).toBe("second");
      expect(picker.value).toBe("second");
      expect(
        page instanceof Modelling
          ? page.presetDirty()
          : page.hasUnsavedChanges(),
      ).toBe(false);
    });

    it("blocks switching during a write without offering to discard it", async () => {
      const { workspace, picker, get, choose, edit, draft, writing } =
        await setup(path);
      edit();
      const before = draft();
      writing();
      get.mockClear();
      const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
      await choose("second");
      expect(workspace.current()?.id).toBe("first");
      expect(picker.value).toBe("first");
      expect(localStorage.getItem("household")).toBe("first");
      expect(draft()).toBe(before);
      expect(confirm).not.toHaveBeenCalled();
      expect(get).not.toHaveBeenCalled();
    });

    it("does not discard or reload a dirty draft when selecting the same household", async () => {
      const { workspace, get, choose, edit, draft } = await setup(path);
      edit();
      const before = draft();
      get.mockClear();
      const confirm = vi.spyOn(window, "confirm").mockReturnValue(true);
      await choose("first");
      expect(workspace.current()?.id).toBe("first");
      expect(draft()).toBe(before);
      expect(confirm).not.toHaveBeenCalled();
      expect(get).not.toHaveBeenCalled();
    });
  },
);

it("guards leaving spending setup through the router and exposes its navigation link", async () => {
  const { fixture, edit, draft } = await setup("/spending/setup");
  expect(fixture.nativeElement.querySelector('header a[href="/spending"]').textContent).toContain("Spending");
  edit();
  const before = draft();
  const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
  const router = TestBed.inject(Router);
  expect(await router.navigateByUrl("/snapshots")).toBe(false);
  expect(router.url).toBe("/spending/setup");
  expect(draft()).toBe(before);
  confirm.mockReturnValue(true);
  expect(await router.navigateByUrl("/snapshots")).toBe(true);
});

it.each([
  ["/spending/setup", false], ["/spending/setup", true], ["/spending/items", false], ["/spending/items", true],
] as const)("keeps the %s sidebar and typed name on rejected browser Back (saving=%s)", async (path, saving) => {
  const { fixture, send } = await setup("/setup", true);
  const router = TestBed.inject(Router);
  router.initialNavigation();
  await router.navigateByUrl(path);
  fixture.detectChanges();
  await fixture.whenStable();
  const page = fixture.debugElement.query(By.directive(RouterOutlet)).injector.get(RouterOutlet).component as SpendingSetup | SpendingItems;
  const entry = path === "/spending/items" ? spendingItem() : { id: "food", householdId: "first", name: "Food", isRetired: false };
  if (page instanceof SpendingItems) {
    await vi.waitFor(() => expect(page.loading()).toBe(false));
    page.beginEdit(spendingItem());
    page.change(true, "name", "Groceries draft");
  } else {
    page.beginEdit("categories", entry);
    page.editName.set("Groceries draft");
  }
  const draft = () => page instanceof SpendingItems ? page.editDraft().name : page.editName();
  fixture.detectChanges();
  await fixture.whenStable();
  let finish!: (result: unknown) => void;
  let writing: Promise<void> | undefined;
  if (saving) {
    send.mockReturnValue(new Promise(resolve => { finish = resolve; }));
    writing = page instanceof SpendingItems ? page.submit(true) : page.saveEdit();
  }
  const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
  const navigation = firstValueFrom(router.events.pipe(filter(event =>
    event instanceof NavigationEnd || event instanceof NavigationCancel)));

  TestBed.inject(Location).back();
  const outcome = await navigation;
  fixture.detectChanges();
  await fixture.whenStable();

  expect(outcome).toBeInstanceOf(NavigationCancel);
  expect(router.url).toBe(path);
  expect(draft()).toBe("Groceries draft");
  expect(page instanceof SpendingItems ? page.editing()?.id : page.editing()?.entry.id).toBe(entry.id);
  expect(TestBed.inject(MatDialog).openDialogs).toHaveLength(1);
  const selector = page instanceof SpendingItems ? 'form[aria-label="Edit item"] input[name="name"]' : 'input[name="editName"]';
  expect(document.querySelector<HTMLInputElement>(selector)?.value).toBe("Groceries draft");
  if (saving) {
    expect(confirm).not.toHaveBeenCalled();
    expect(page.saving()).toBe(true);
    finish({ ...entry, name: "Groceries draft" });
    await writing;
  } else {
    expect(confirm).toHaveBeenCalledOnce();
  }
});

it("opens the item catalogue from the Spending navigation entry", async () => {
  const { fixture, page } = await setup("/spending");
  expect(page).toBeInstanceOf(SpendingItems);
  expect(TestBed.inject(Router).url).toBe("/spending/items");
  expect(fixture.nativeElement.querySelector('header a[href="/spending"]').classList.contains("active")).toBe(true);
});

it("guards saved purchase edits across route and household changes", async () => {
  const { fixture, page, get, choose, workspace } = await setup("/spending/records");
  const records = page as SpendingRecords;
  records.view(spendingRecord()); get.mockResolvedValueOnce(spendingRecord());
  await records.beginEdit(); fixture.detectChanges(); await fixture.whenStable();
  records.capture()!.set("notes", "Keep saved-record correction");
  const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
  expect(await TestBed.inject(Router).navigateByUrl("/spending/items")).toBe(false);
  await choose("second");
  expect(workspace.current()?.id).toBe("first");
  expect(records.capture()?.draft().notes).toBe("Keep saved-record correction");
  expect(records.editing()?.id).toBe("record");
  confirm.mockReturnValue(true); await choose("second");
  expect(records.editing()).toBeNull(); expect(records.hasUnsavedChanges()).toBe(false);
});

it("opens Spending by category from feature navigation and changes household without a draft prompt", async () => {
  const { fixture, page, choose, workspace, send } = await setup("/spending/summary");
  expect(page).toBeInstanceOf(SpendingSummary);
  const link = fixture.nativeElement.querySelector('a[href="/spending/summary"]');
  expect(link.textContent).toContain("Spending by category"); expect(link.classList.contains("active")).toBe(true);
  const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
  await choose("second");
  expect(workspace.current()?.id).toBe("second"); expect(confirm).not.toHaveBeenCalled(); expect(send).not.toHaveBeenCalled();
});
