import { Component } from "@angular/core";
import { RouterLink, RouterLinkActive } from "@angular/router";
import { MatButtonModule } from "@angular/material/button";

@Component({
  selector: "app-spending-navigation",
  imports: [RouterLink, RouterLinkActive, MatButtonModule],
  template: `<nav aria-label="Spending">
    <a mat-button routerLink="/spending/records" routerLinkActive="active">Purchases &amp; history</a>
    <a mat-button routerLink="/spending/summary" routerLinkActive="active">Spending by category</a>
    <a mat-button routerLink="/spending/overview" routerLinkActive="active">Outgoings overview</a>
    <a mat-button routerLink="/spending/outgoings" routerLinkActive="active">Regular outgoings</a>
    <a mat-button routerLink="/spending/plans" routerLinkActive="active">Outgoing plans</a>
    <a mat-button routerLink="/spending/items" routerLinkActive="active">Items</a>
    <a mat-button routerLink="/spending/setup" routerLinkActive="active">Categories &amp; merchants</a>
  </nav>`,
  styles: `:host { display: block; margin-bottom: 20px; }`,
})
export class SpendingNavigation {}
