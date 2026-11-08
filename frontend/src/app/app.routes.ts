import { Routes } from "@angular/router";

export const routes: Routes = [
  {
    path: "snapshots",
    loadComponent: () =>
      import("./snapshots/browser").then((m) => m.SnapshotBrowser),
  },
  {
    path: "history",
    loadComponent: () => import("./history/history").then((m) => m.History),
  },
  {
    path: "modelling",
    canDeactivate: [
      (component: import("./modelling/modelling").Modelling) =>
        component.canLeave(),
    ],
    loadComponent: () =>
      import("./modelling/modelling").then((m) => m.Modelling),
  },
  {
    path: "record",
    canDeactivate: [
      (component: import("./snapshots/editor").SnapshotEditor) =>
        component.canLeave(),
    ],
    loadComponent: () =>
      import("./snapshots/editor").then((m) => m.SnapshotEditor),
  },
  {
    path: "setup",
    loadComponent: () => import("./setup/setup").then((m) => m.Setup),
  },
  { path: "", redirectTo: "snapshots", pathMatch: "full" },
  { path: "**", redirectTo: "snapshots" },
];
