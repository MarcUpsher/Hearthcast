import { HttpErrorResponse } from "@angular/common/http";
import { describe, expect, it } from "vitest";
import { message } from "./api-errors";

describe("API error messages", () => {
  it.each([false, true])("reads validation Problem Details from JSON objects or text (text=%s)", text => {
    const error = { detail: "Invalid range", errors: { from: ["Supply a date"] } };
    expect(message(new HttpErrorResponse({ status: 400, error: text ? JSON.stringify(error) : error }))).toBe("Invalid range\nSupply a date");
  });
  it("handles unstructured text, network errors and title-only Problems", () => {
    expect(message(new HttpErrorResponse({ status: 500, error: "<html>Unavailable</html>" }))).toBe("The request failed.");
    expect(message(new HttpErrorResponse({ status: 0 }))).toContain("Cannot reach the API");
    expect(message(new HttpErrorResponse({ status: 404, error: '{"title":"Not found"}' }))).toBe("Not found");
  });
});
