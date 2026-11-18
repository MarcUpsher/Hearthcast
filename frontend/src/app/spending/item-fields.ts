import { Component, inject, input, output } from "@angular/core";
import { ControlContainer, FormsModule } from "@angular/forms";
import { MatInputModule } from "@angular/material/input";
import { MatSelectModule } from "@angular/material/select";
import { SpendingItemDraft, SpendingSetupEntry } from "./models";

export const emptyItemDraft = (): SpendingItemDraft => ({ code: "", name: "", expenseCategoryId: "", unitKey: crypto.randomUUID().replaceAll("-", ""), unitLabel: "", notes: "" });
export function itemDraftDirty(draft: SpendingItemDraft) {
  return [draft.code, draft.name, draft.expenseCategoryId, draft.unitLabel, draft.notes].some(value => !!value.trim());
}
export function validItemDraft(draft: SpendingItemDraft) {
  return !!draft.name.trim() && !!draft.expenseCategoryId && !!draft.unitLabel.trim() &&
    /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/.test(draft.code.trim()) &&
    /^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$/.test(draft.unitKey.trim());
}
export function itemCreateRequest(draft: SpendingItemDraft) {
  return { ...draft, notes: draft.notes.trim() ? draft.notes : null };
}

@Component({
  selector: "app-spending-item-fields",
  imports: [FormsModule, MatInputModule, MatSelectModule],
  viewProviders: [{ provide: ControlContainer, useFactory: () => inject(ControlContainer, { skipSelf: true }) }],
  templateUrl: "item-fields.html",
  styles: `
    :host { display: block; min-width: 0; }
    .item-fields { display: grid; align-items: start; grid-template-columns: repeat(auto-fit, minmax(220px, 1fr)); gap: 24px 16px; }
    .item-fields.single-column { grid-template-columns: 1fr; }
    .span { grid-column: 1 / -1; }
    .item-notes { width: 100%; margin-top: 16px; }
    @media (max-width: 500px) { .item-fields { grid-template-columns: 1fr; } }
  `,
})
export class SpendingItemFields {
  readonly value = input.required<SpendingItemDraft>();
  readonly categories = input<SpendingSetupEntry[]>([]);
  readonly disabled = input(false);
  readonly immutable = input(false);
  readonly changed = output<{ field: keyof SpendingItemDraft; value: string }>();
  change(field: keyof SpendingItemDraft, value: string) { this.changed.emit({ field, value }); }
}
