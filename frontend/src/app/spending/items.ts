import { Component, DestroyRef, HostListener, TemplateRef, effect, inject, signal, untracked, viewChild, viewChildren } from "@angular/core";
import { NgTemplateOutlet } from "@angular/common";
import { FormsModule, NgForm } from "@angular/forms";
import { MatButtonModule } from "@angular/material/button";
import { MatDialog, MatDialogModule, MatDialogRef } from "@angular/material/dialog";
import { MatInputModule } from "@angular/material/input";
import { MatSelectModule } from "@angular/material/select";
import { Api } from "../core/api";
import { message } from "../core/api-errors";
import { Workspace } from "../core/workspace";
import { SpendingItem, SpendingItemDraft, SpendingSetupEntry, SpendingSetupStatus } from "./models";
import { SpendingNavigation } from "./navigation";
import { SpendingPrices } from "./prices";
import { emptyItemDraft as emptyDraft, itemCreateRequest, SpendingItemFields, validItemDraft } from "./item-fields";

const itemDraft = (item: SpendingItem): SpendingItemDraft => ({ code: item.code, name: item.name,
  expenseCategoryId: item.expenseCategoryId, unitKey: item.unitKey, unitLabel: item.unitLabel, notes: item.notes ?? "" });

@Component({
  imports: [FormsModule, NgTemplateOutlet, MatButtonModule, MatDialogModule, MatInputModule, MatSelectModule,
    SpendingNavigation, SpendingPrices, SpendingItemFields],
  templateUrl: "items.html",
  styleUrl: "items.scss",
})
export class SpendingItems {
  readonly w = inject(Workspace);
  private readonly api = inject(Api);
  private readonly dialogs = inject(MatDialog);
  private readonly editPanel = viewChild.required<TemplateRef<unknown>>("editPanel");
  private readonly forms = viewChildren(NgForm);
  readonly prices = viewChild(SpendingPrices);
  private editor?: MatDialogRef<unknown>;
  private home: string | null = null;
  private generation = 0;
  private requestVersion = 0;
  private destroyed = false;
  readonly rows = signal<SpendingItem[]>([]);
  readonly categories = signal<SpendingSetupEntry[]>([]);
  readonly merchants = signal<SpendingSetupEntry[]>([]);
  readonly loading = signal(false);
  readonly error = signal("");
  readonly actionError = signal("");
  readonly saving = signal(false);
  readonly notice = signal("");
  readonly search = signal("");
  readonly status = signal<SpendingSetupStatus>("active");
  readonly category = signal("");
  readonly createDraft = signal(emptyDraft());
  readonly createError = signal("");
  readonly editing = signal<SpendingItem | null>(null);
  readonly editDraft = signal(emptyDraft());
  readonly editError = signal("");
  readonly selectedItem = signal<SpendingItem | null>(null);

  constructor() {
    effect(() => {
      const home = this.w.current()?.id ?? null;
      untracked(() => {
        if (home === this.home) return;
        this.home = home;
        this.generation++;
        this.closeEditor();
        this.selectedItem.set(null);
        this.rows.set([]);
        this.categories.set([]);
        this.merchants.set([]);
        this.loading.set(false);
        this.saving.set(false);
        this.error.set("");
        this.actionError.set("");
        this.notice.set("");
        this.clearCreate();
        for (const form of this.forms()) form.resetForm(emptyDraft());
        this.search.set("");
        this.status.set("active");
        this.category.set("");
        if (home) void this.load();
      });
    });
    inject(DestroyRef).onDestroy(() => { this.destroyed = true; this.generation++; this.editor?.close(); });
  }
  private current(generation: number, home: string) {
    return !this.destroyed && generation === this.generation && this.w.current()?.id === home;
  }
  busy() { return this.saving() || (this.prices()?.saving() ?? false); }
  canWrite() { return !!this.home && this.home === this.w.current()?.id && !this.busy() && !this.loading() && !this.error(); }
  async load() {
    const home = this.home, generation = this.generation;
    if (!home || !this.current(generation, home) || this.busy()) return;
    const version = ++this.requestVersion;
    const current = () => this.current(generation, home) && version === this.requestVersion;
    this.loading.set(true);
    this.error.set("");
    try {
      const [rows, categories, merchants] = await Promise.all([
        this.api.get<SpendingItem[]>(`households/${home}/spending/items?includeRetired=true`),
        this.api.get<SpendingSetupEntry[]>(`households/${home}/spending/categories?includeRetired=true`),
        this.api.get<SpendingSetupEntry[]>(`households/${home}/spending/merchants?includeRetired=true`),
      ]);
      if (!current()) return;
      this.rows.set(rows);
      this.categories.set(categories);
      this.merchants.set(merchants);
      const selected = this.selectedItem();
      if (selected) this.selectedItem.set(rows.find(row => row.id === selected.id) ?? selected);
    } catch (error) {
      if (current()) this.error.set(message(error));
    } finally {
      if (current()) this.loading.set(false);
    }
  }
  categoryName(id: string) { return this.categories().find(row => row.id === id)?.name ?? "Category unavailable"; }
  visibleRows() {
    const search = this.search().trim().toLocaleLowerCase();
    return this.rows().filter(row =>
      (this.status() === "all" || row.isRetired === (this.status() === "retired")) &&
      (!this.category() || row.expenseCategoryId === this.category()) &&
      [row.name, row.code, row.unitKey, row.unitLabel, row.notes ?? "", this.categoryName(row.expenseCategoryId)].some(value => value.toLocaleLowerCase().includes(search)))
      .sort((a, b) => a.name.localeCompare(b.name) || a.code.localeCompare(b.code));
  }
  draft(edit: boolean) { return edit ? this.editDraft() : this.createDraft(); }
  change(edit: boolean, field: keyof SpendingItemDraft, value: string) {
    (edit ? this.editDraft : this.createDraft).update(draft => ({ ...draft, [field]: value }));
    (edit ? this.editError : this.createError).set("");
  }
  valid(edit: boolean) {
    return validItemDraft(this.draft(edit));
  }
  clearCreate(form?: NgForm) {
    if (this.saving()) return;
    this.createDraft.set(emptyDraft());
    this.createError.set("");
    form?.resetForm(emptyDraft());
  }
  editDirty() {
    const editing = this.editing();
    return !!editing && JSON.stringify(this.editDraft()) !== JSON.stringify(itemDraft(editing));
  }
  beginEdit(item: SpendingItem) {
    if (!this.canWrite() || item.householdId !== this.home || this.editor) return;
    this.editing.set(item);
    this.editDraft.set(itemDraft(item));
    this.editError.set("");
    const editor = this.dialogs.open(this.editPanel(), {
      position: { right: "0", top: "0" }, width: "520px", maxWidth: "100vw", height: "100dvh", maxHeight: "100dvh",
      panelClass: "edit-sidebar", autoFocus: 'input[name="name"]', disableClose: true, closeOnNavigation: false,
    });
    this.editor = editor;
    const dismiss = () => {
      if (!this.busy() && (!this.editDirty() || window.confirm("Discard this unsaved item edit?"))) this.closeEditor();
    };
    editor.backdropClick().subscribe(dismiss);
    editor.keydownEvents().subscribe(event => { if (event.key === "Escape") { event.preventDefault(); dismiss(); } });
    editor.afterClosed().subscribe(() => { if (this.editor === editor) this.closeEditor(); });
  }
  cancelEdit() { if (!this.busy()) this.closeEditor(); }
  private closeEditor() {
    const editor = this.editor;
    this.editor = undefined;
    editor?.close();
    this.editing.set(null);
    this.editDraft.set(emptyDraft());
    this.editError.set("");
  }
  private upsert(saved: SpendingItem) {
    this.rows.update(rows => [...rows.filter(row => row.id !== saved.id), saved]);
    if (this.selectedItem()?.id === saved.id) this.selectedItem.set(saved);
  }
  async submit(edit: boolean, form?: NgForm) {
    if (!this.canWrite() || !this.valid(edit) || (edit && (!this.editing() || !this.editDirty()))) return;
    const home = this.home!, generation = this.generation, item = this.editing(), draft = this.draft(edit);
    const details = { expenseCategoryId: draft.expenseCategoryId, name: draft.name, unitLabel: draft.unitLabel,
      notes: draft.notes.trim() ? draft.notes : null };
    this.saving.set(true);
    this.notice.set("");
    (edit ? this.editError : this.createError).set("");
    try {
      const saved = await this.api.send<SpendingItem>(edit ? "PUT" : "POST",
        `households/${home}/spending/items${edit ? "/" + item!.id + "/details" : ""}`,
        edit ? details : itemCreateRequest(draft));
      if (!this.current(generation, home)) return;
      this.upsert(saved);
      if (edit) this.closeEditor();
      else { this.createDraft.set(emptyDraft()); form?.resetForm(emptyDraft()); }
      this.notice.set(edit ? "Item updated." : "Item added.");
    } catch (error) {
      if (this.current(generation, home)) (edit ? this.editError : this.createError).set(message(error));
    } finally {
      if (this.current(generation, home)) this.saving.set(false);
    }
  }
  async setRetirement(item: SpendingItem) {
    if (!this.canWrite() || item.householdId !== this.home) return;
    const home = this.home!, generation = this.generation;
    this.saving.set(true);
    this.actionError.set("");
    this.notice.set("");
    try {
      const saved = await this.api.send<SpendingItem>("PUT", `households/${home}/spending/items/${item.id}/retirement`, { isRetired: !item.isRetired });
      if (this.current(generation, home)) { this.upsert(saved); this.notice.set(`${saved.name} ${saved.isRetired ? "retired" : "restored"}.`); }
    } catch (error) {
      if (this.current(generation, home)) this.actionError.set(message(error));
    } finally {
      if (this.current(generation, home)) this.saving.set(false);
    }
  }
  showPrices(item: SpendingItem) {
    if (!this.canWrite() || item.householdId !== this.home || this.selectedItem()?.id === item.id) return;
    if (this.prices() && !this.prices()!.canLeave()) return;
    this.selectedItem.set(item);
  }
  hasUnsavedChanges() {
    return Object.values(this.createDraft()).some(value => !!value.trim()) || this.editDirty() || (this.prices()?.hasUnsavedChanges() ?? false);
  }
  canLeave() {
    return !this.busy() && (!this.hasUnsavedChanges() || window.confirm("Discard unsaved spending item or price changes?"));
  }
  @HostListener("window:beforeunload", ["$event"])
  beforeUnload(event: BeforeUnloadEvent) {
    if (this.busy() || this.hasUnsavedChanges()) { event.preventDefault(); event.returnValue = ""; }
  }
}
