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
      { provide: Api, useValue: { get, send } },
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
  const page = outlet.component as Modelling | SnapshotEditor | SpendingSetup;
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
    if (page instanceof SpendingSetup) page.changeNewName("categories", "Unsaved category");
    else if (page instanceof Modelling) page.presetName = "Unsaved scenario";
    else page.date = "2000-01-01";
  };
  const draft = () => page instanceof SpendingSetup ? page.newNames().categories :
    (page instanceof Modelling ? page.presetName : page.date);
  const writing = () => {
    if (page instanceof Modelling) page.savingPreset.set(true);
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

describe.each(["/modelling", "/record", "/spending/setup"])(
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
  expect(fixture.nativeElement.querySelector('a[href="/spending/setup"]').textContent).toContain("Spending");
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

it.each([false, true])("keeps the spending sidebar and typed name on rejected browser Back (saving=%s)", async saving => {
  const { fixture, send } = await setup("/setup", true);
  const router = TestBed.inject(Router);
  router.initialNavigation();
  await router.navigateByUrl("/spending/setup");
  fixture.detectChanges();
  await fixture.whenStable();
  const page = fixture.debugElement.query(By.directive(RouterOutlet)).injector.get(RouterOutlet).component as SpendingSetup;
  const entry = { id: "food", householdId: "first", name: "Food", isRetired: false };
  page.beginEdit("categories", entry);
  page.editName.set("Groceries draft");
  fixture.detectChanges();
  await fixture.whenStable();
  let finish!: (result: unknown) => void;
  let writing: Promise<void> | undefined;
  if (saving) {
    send.mockReturnValue(new Promise(resolve => { finish = resolve; }));
    writing = page.saveEdit();
  }
  const confirm = vi.spyOn(window, "confirm").mockReturnValue(false);
  const navigation = firstValueFrom(router.events.pipe(filter(event =>
    event instanceof NavigationEnd || event instanceof NavigationCancel)));

  TestBed.inject(Location).back();
  const outcome = await navigation;
  fixture.detectChanges();
  await fixture.whenStable();

  expect(outcome).toBeInstanceOf(NavigationCancel);
  expect(router.url).toBe("/spending/setup");
  expect(page.editName()).toBe("Groceries draft");
  expect(page.editing()?.entry.id).toBe(entry.id);
  expect(TestBed.inject(MatDialog).openDialogs).toHaveLength(1);
  expect(document.querySelector<HTMLInputElement>('input[name="editName"]')?.value).toBe("Groceries draft");
  if (saving) {
    expect(confirm).not.toHaveBeenCalled();
    expect(page.saving()).toBe(true);
    finish({ ...entry, name: "Groceries draft" });
    await writing;
  } else {
    expect(confirm).toHaveBeenCalledOnce();
  }
});
