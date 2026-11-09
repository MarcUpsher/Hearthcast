import { TestBed } from "@angular/core/testing";
import { By } from "@angular/platform-browser";
import { provideRouter, Router, RouterOutlet } from "@angular/router";
import { MatSelect, MatSelectChange } from "@angular/material/select";
import { afterEach, describe, expect, it, vi } from "vitest";
import { App } from "./app";
import { routes } from "./app.routes";
import { Api } from "./core/api";
import { Workspace } from "./core/workspace";
import { Modelling } from "./modelling/modelling";
import { SnapshotEditor } from "./snapshots/editor";

async function setup(path: string) {
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
  TestBed.configureTestingModule({
    providers: [
      provideRouter(routes),
      { provide: Api, useValue: { get, send: vi.fn() } },
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
  const page = outlet.component as Modelling | SnapshotEditor;
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
    if (page instanceof Modelling) page.presetName = "Unsaved scenario";
    else page.date = "2000-01-01";
  };
  const draft = () => (page instanceof Modelling ? page.presetName : page.date);
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

describe.each(["/modelling", "/record"])(
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
