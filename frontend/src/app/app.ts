import { Component, inject } from "@angular/core";
import { RouterLink, RouterLinkActive, RouterOutlet } from "@angular/router";
import { FormsModule } from "@angular/forms";
import { MatButtonModule } from "@angular/material/button";
import { MatSelectModule } from "@angular/material/select";
import { Workspace } from "./core/api";
@Component({
  selector: "app-root",
  imports: [
    RouterLink,
    RouterLinkActive,
    RouterOutlet,
    FormsModule,
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
        ><a mat-button routerLink="/record" routerLinkActive="active"
          >Record snapshot</a
        ><a mat-button routerLink="/setup" routerLinkActive="active"
          >Household setup</a
        >
      </nav>
      @if (w.households().length) {
        <mat-select
          aria-label="Household"
          style="width:200px;margin-left:auto"
          [ngModel]="w.current()?.id"
          (ngModelChange)="w.select($event)"
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
  constructor() {
    void this.w.load();
  }
}
