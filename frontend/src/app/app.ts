import { Component, inject, viewChild } from "@angular/core";
import { RouterLink, RouterLinkActive, RouterOutlet } from "@angular/router";
import { MatButtonModule } from "@angular/material/button";
import { MatSelect, MatSelectModule } from "@angular/material/select";
import { Workspace } from "./core/workspace";
@Component({
  selector: "app-root",
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    MatButtonModule,
    MatSelectModule,
  ],
  template: ` <header>
      <a class="brand" routerLink="/">Hearthcast</a>
      <nav aria-label="Main navigation">
        <a mat-button routerLink="/snapshots" routerLinkActive="active"
          >Snapshots</a
        ><a mat-button routerLink="/history" routerLinkActive="active"
          >History</a
        ><a mat-button routerLink="/modelling" routerLinkActive="active"
          >Modelling</a
        ><a mat-button routerLink="/record" routerLinkActive="active"
          >Record snapshot</a
        ><a mat-button routerLink="/setup" routerLinkActive="active"
          >Household setup</a
        >
      </nav>
      @if (w.households().length) {
        <mat-select
          #householdPicker
          aria-label="Household"
          class="household-picker"
          [value]="w.current()?.id"
          (selectionChange)="selectHousehold($event.value, householdPicker)"
        >
          @for (h of w.households(); track h.id) {
            <mat-option [value]="h.id">{{ h.name }}</mat-option>
          }
        </mat-select>
      }
    </header>
    <main>
      @if (w.error()) {
        <p class="error" role="alert">{{ w.error() }}</p>
        <button mat-button (click)="w.load()">Retry connection</button>
      }
      @if (w.current() || !w.error()) {
        <router-outlet />
      }
    </main>`,
})
export class App {
  w = inject(Workspace);
  private outlet = viewChild(RouterOutlet);
  constructor() {
    void this.w.load();
  }
  selectHousehold(id: string, picker: MatSelect) {
    const currentId = this.w.current()?.id;
    if (id === currentId) return;
    const outlet = this.outlet();
    const page = outlet?.isActivated
      ? (outlet.component as { canLeave?: () => boolean })
      : null;
    if (
      !this.w.households().some((household) => household.id === id) ||
      (typeof page?.canLeave === "function" && !page.canLeave())
    ) {
      picker.value = currentId;
      return;
    }
    this.w.select(id);
  }
}
