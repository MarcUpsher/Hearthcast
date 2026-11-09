import { HttpErrorResponse } from "@angular/common/http";

export function message(e: unknown) {
  if (e instanceof HttpErrorResponse) {
    if (e.status === 0)
      return "Cannot reach the API. Check that Hearthcast is running, then retry.";
    return (
      [e.error?.detail, ...Object.values(e.error?.errors ?? {}).flat()]
        .filter(Boolean)
        .join("\n") ||
      e.error?.title ||
      "The request failed."
    );
  }
  return e instanceof Error ? e.message : "The request failed.";
}
