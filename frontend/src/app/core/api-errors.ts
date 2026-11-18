import { HttpErrorResponse } from "@angular/common/http";

export function message(e: unknown) {
  if (e instanceof HttpErrorResponse) {
    if (e.status === 0)
      return "Cannot reach the API. Check that Hearthcast is running, then retry.";
    let problem = e.error;
    if (typeof problem === "string") {
      try { problem = JSON.parse(problem); } catch { problem = null; }
    }
    return (
      [problem?.detail, ...Object.values(problem?.errors ?? {}).flat()]
        .filter(Boolean)
        .join("\n") ||
      problem?.title ||
      "The request failed."
    );
  }
  return e instanceof Error ? e.message : "The request failed.";
}
