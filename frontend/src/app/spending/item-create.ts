import { Component, DestroyRef, effect, inject, signal } from "@angular/core";
import { FormsModule } from "@angular/forms";
import { MatButtonModule } from "@angular/material/button";
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from "@angular/material/dialog";
import { Api } from "../core/api";
import { message } from "../core/api-errors";
import { Workspace } from "../core/workspace";
import { emptyItemDraft, itemCreateRequest, SpendingItemFields, validItemDraft } from "./item-fields";
import { SpendingItem, SpendingItemDraft, SpendingSetupEntry } from "./models";

export interface SpendingItemCreateData {
  householdId: string;
  categories: SpendingSetupEntry[];
  expenseCategoryId: string;
}

@Component({
  imports: [FormsModule, MatButtonModule, MatDialogModule, SpendingItemFields],
  template: `
    <h2 mat-dialog-title>Create spending item</h2>
    <form aria-label="Create spending item" [attr.aria-busy]="saving()" (ngSubmit)="save()">
      <mat-dialog-content>
        <p>This adds an item to your catalogue. Your purchase is saved separately.</p>
        <app-spending-item-fields [value]="draft()" [categories]="data.categories" [disabled]="saving()"
          (changed)="change($event.field, $event.value)" />
        @if (!data.categories.length) { <p class="notice">Add a spending category in Categories &amp; merchants first.</p> }
        @if (error()) { <p class="error" role="alert">{{ error() }}</p> }
      </mat-dialog-content>
      <mat-dialog-actions>
        <button mat-button type="button" [disabled]="saving()" (click)="dismiss()">Cancel</button>
        <button mat-flat-button type="submit" [disabled]="!canSave()">{{ saving() ? 'Creating…' : 'Create & use item' }}</button>
      </mat-dialog-actions>
    </form>
  `,
  styles: `mat-dialog-content { padding-top: 8px !important; } mat-dialog-actions { padding: 16px 24px; }`,
})
export class SpendingItemCreate {
  readonly data = inject<SpendingItemCreateData>(MAT_DIALOG_DATA);
  private readonly api = inject(Api);
  private readonly w = inject(Workspace);
  private readonly dialog = inject<MatDialogRef<SpendingItemCreate, SpendingItem>>(MatDialogRef);
  private destroyed = false;
  private readonly initial = { ...emptyItemDraft(), expenseCategoryId: this.data.expenseCategoryId };
  readonly draft = signal<SpendingItemDraft>({ ...this.initial });
  readonly saving = signal(false);
  readonly error = signal("");

  constructor() {
    effect(() => { if (this.w.current()?.id !== this.data.householdId) this.dialog.close(); });
    this.dialog.backdropClick().subscribe(() => this.dismiss());
    this.dialog.keydownEvents().subscribe(event => { if (event.key === "Escape") { event.preventDefault(); this.dismiss(); } });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; });
  }
  private current() { return !this.destroyed && this.w.current()?.id === this.data.householdId; }
  change(field: keyof SpendingItemDraft, value: string) {
    if (this.saving()) return;
    this.draft.update(draft => ({ ...draft, [field]: value })); this.error.set("");
  }
  dirty() { return JSON.stringify(this.draft()) !== JSON.stringify(this.initial); }
  canSave() { return this.current() && !this.saving() && validItemDraft(this.draft()); }
  dismiss() {
    if (!this.saving() && (!this.dirty() || window.confirm("Discard this unsaved item? Your purchase draft will be kept."))) this.dialog.close();
  }
  async save() {
    if (!this.canSave()) return;
    this.saving.set(true); this.error.set("");
    try {
      const saved = await this.api.send<SpendingItem>("POST", `households/${this.data.householdId}/spending/items`, itemCreateRequest(this.draft()));
      if (this.current()) this.dialog.close(saved);
    } catch (error) { if (this.current()) { this.error.set(message(error)); this.saving.set(false); } }
  }
}
