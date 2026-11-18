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
    path: "spending",
    pathMatch: "full",
    redirectTo: "spending/items",
  },
  {
    path: "spending/items",
    canDeactivate: [
      (component: import("./spending/items").SpendingItems) => component.canLeave(),
    ],
    loadComponent: () => import("./spending/items").then((m) => m.SpendingItems),
  },
  {
    path: "spending/setup",
    canDeactivate: [
      (component: import("./spending/setup").SpendingSetup) => component.canLeave(),
    ],
    loadComponent: () => import("./spending/setup").then((m) => m.SpendingSetup),
  },
  {
    path: "setup",
    loadComponent: () => import("./setup/setup").then((m) => m.Setup),
  },
  { path: "", redirectTo: "snapshots", pathMatch: "full" },
  { path: "**", redirectTo: "snapshots" },
];
