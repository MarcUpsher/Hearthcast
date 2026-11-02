import { bootstrapApplication } from "@angular/platform-browser";
import { provideHttpClient } from "@angular/common/http";
import { provideRouter } from "@angular/router";
import { App } from "./app/app";
bootstrapApplication(App, {
  providers: [
    provideHttpClient(),
    provideRouter([
      {
        path: "snapshots",
        loadComponent: () =>
          import("./app/snapshots/browser").then((m) => m.SnapshotBrowser),
      },
      {
        path: "record",
        canDeactivate: [
          (component: import("./app/snapshots/editor").SnapshotEditor) =>
            component.canLeave(),
        ],
        loadComponent: () =>
          import("./app/snapshots/editor").then((m) => m.SnapshotEditor),
      },
      {
        path: "setup",
        loadComponent: () => import("./app/setup/setup").then((m) => m.Setup),
      },
      { path: "", redirectTo: "snapshots", pathMatch: "full" },
      { path: "**", redirectTo: "snapshots" },
    ]),
  ],
}).catch(console.error);
