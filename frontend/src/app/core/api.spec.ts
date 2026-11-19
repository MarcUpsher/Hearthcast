import { provideHttpClient } from "@angular/common/http";
import { HttpTestingController, provideHttpClientTesting } from "@angular/common/http/testing";
import { TestBed } from "@angular/core/testing";
import { expect, it } from "vitest";
import { Api } from "./api";
it("transports prepared JSON with exact decimal tokens and retains the raw response", async () => {
  TestBed.configureTestingModule({ providers: [provideHttpClient(), provideHttpClientTesting()] });
  const body = '{"plannedAmount":999999999999.123456}';
  const pending = TestBed.inject(Api).sendText("PUT", "households/test/budgets/snapshots/plan", body);
  const http = TestBed.inject(HttpTestingController), request = http.expectOne("/api/households/test/budgets/snapshots/plan");
  expect(request.request.headers.get("Content-Type")).toBe("application/json");
  expect(request.request.serializeBody()).toBe(body); expect(request.request.responseType).toBe("text");
  request.flush(body); expect(await pending).toBe(body); http.verify();
});
