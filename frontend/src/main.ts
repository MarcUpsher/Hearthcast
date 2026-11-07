import { bootstrapApplication } from "@angular/platform-browser";
import { provideHttpClient } from "@angular/common/http";
import { provideRouter } from "@angular/router";
import { App } from "./app/app";
import { MAT_FORM_FIELD_DEFAULT_OPTIONS } from "@angular/material/form-field";
bootstrapApplication(App, {
  providers: [
    provideHttpClient(),
    {
      provide: MAT_FORM_FIELD_DEFAULT_OPTIONS,
      useValue: { appearance: "outline", subscriptSizing: "dynamic" },
    },
    provideRouter([
      {
        path: "snapshots",
        loadComponent: () =>
          import("./app/snapshots/browser").then((m) => m.SnapshotBrowser),
      },
      {
        path: "history",
        loadComponent: () =>
          import("./app/history/history").then((m) => m.History),
      },
      {
        path: "modelling",
        loadComponent: () =>
          import("./app/modelling/modelling").then((m) => m.Modelling),
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
