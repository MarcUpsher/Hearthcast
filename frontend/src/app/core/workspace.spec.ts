import { TestBed } from "@angular/core/testing";
import { provideHttpClient } from "@angular/common/http";
import {
  provideHttpClientTesting,
  HttpTestingController,
} from "@angular/common/http/testing";
import { describe, it, expect, beforeEach, afterEach } from "vitest";
import { Workspace } from "./workspace";
describe("Household discovery", () => {
  let w: Workspace;
  let http: HttpTestingController;
  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    w = TestBed.inject(Workspace);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());
  it("discovers a household without a hardcoded ID", async () => {
    const loaded = w.load();
    http
      .expectOne("/api/households")
      .flush([{ id: "one", name: "Example", people: [] }]);
    await loaded;
    expect(w.current()?.id).toBe("one");
    expect(w.path("snapshots")).toBe("households/one/snapshots");
  });
  it("supports a fresh empty database", async () => {
    const loaded = w.load();
    http.expectOne("/api/households").flush([]);
    await loaded;
    expect(w.current()).toBeNull();
    expect(w.error()).toBe("");
  });
});
