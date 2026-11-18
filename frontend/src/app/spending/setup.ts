import {
  Component, DestroyRef, HostListener, TemplateRef, effect, inject, signal, untracked, viewChild,
} from "@angular/core";
import { FormsModule, NgForm } from "@angular/forms";
import { MatButtonModule } from "@angular/material/button";
import { MatDialog, MatDialogModule, MatDialogRef } from "@angular/material/dialog";
import { MatInputModule } from "@angular/material/input";
import { MatSelectModule } from "@angular/material/select";
import { Api } from "../core/api";
import { message } from "../core/api-errors";
import { Workspace } from "../core/workspace";
import { SpendingSetupEntry, SpendingSetupKind, SpendingSetupStatus } from "./models";
import { SpendingNavigation } from "./navigation";

interface ListState {
  rows: SpendingSetupEntry[];
  loading: boolean;
  error: string;
  actionError: string;
}
interface Edit {
  kind: SpendingSetupKind;
  entry: SpendingSetupEntry;
}
const emptyLists = (): Record<SpendingSetupKind, ListState> => ({
  categories: { rows: [], loading: false, error: "", actionError: "" },
  merchants: { rows: [], loading: false, error: "", actionError: "" },
});

@Component({
  imports: [FormsModule, MatButtonModule, MatDialogModule, MatInputModule, MatSelectModule, SpendingNavigation],
  templateUrl: "setup.html",
  styleUrl: "setup.scss",
})
export class SpendingSetup {
  readonly w = inject(Workspace);
  private readonly api = inject(Api);
  private readonly dialogs = inject(MatDialog);
  private readonly editPanel = viewChild.required<TemplateRef<unknown>>("editPanel");
  private editor?: MatDialogRef<unknown>;
  private householdId: string | null = null;
  private generation = 0;
  private destroyed = false;
  private loadVersions = { categories: 0, merchants: 0 };
  readonly sections = [
    { kind: "categories", title: "Categories", singular: "category", description: "Group spending in a way that makes sense to you." },
    { kind: "merchants", title: "Merchants", singular: "merchant", description: "The shops, cafés and suppliers you buy from." },
  ] as const;
  readonly lists = signal(emptyLists());
  readonly newNames = signal({ categories: "", merchants: "" });
  readonly createErrors = signal({ categories: "", merchants: "" });
  readonly search = signal("");
  readonly status = signal<SpendingSetupStatus>("active");
  readonly saving = signal(false);
  readonly editing = signal<Edit | null>(null);
  readonly editName = signal("");
  readonly editError = signal("");
  readonly notice = signal("");

  constructor() {
    effect(() => {
      const id = this.w.current()?.id ?? null;
      untracked(() => {
        if (id === this.householdId) return;
        this.householdId = id;
        this.generation++;
        this.closeEditor();
        this.lists.set(emptyLists());
        this.newNames.set({ categories: "", merchants: "" });
        this.createErrors.set({ categories: "", merchants: "" });
        this.saving.set(false);
        this.notice.set("");
        this.search.set("");
        this.status.set("active");
        if (id) for (const section of this.sections) void this.load(section.kind);
      });
    });
    inject(DestroyRef).onDestroy(() => {
      this.destroyed = true;
      this.generation++;
      this.editor?.close();
    });
  }

  private current(generation: number, home: string) {
    return !this.destroyed && generation === this.generation && home === this.w.current()?.id;
  }
  private update(kind: SpendingSetupKind, changes: Partial<ListState>) {
    this.lists.update(lists => ({ ...lists, [kind]: { ...lists[kind], ...changes } }));
  }
  async load(kind: SpendingSetupKind) {
    const home = this.householdId;
    if (!home || home !== this.w.current()?.id || this.saving()) return;
    const generation = this.generation;
    const version = ++this.loadVersions[kind];
    const current = () => this.current(generation, home) && version === this.loadVersions[kind];
    this.update(kind, { loading: true, error: "" });
    try {
      const rows = await this.api.get<SpendingSetupEntry[]>(`households/${home}/spending/${kind}?includeRetired=true`);
      if (current()) this.update(kind, { rows, actionError: "" });
    } catch (error) {
      if (current()) this.update(kind, { error: message(error) });
    } finally {
      if (current()) this.update(kind, { loading: false });
    }
  }
  visibleRows(kind: SpendingSetupKind) {
    const search = this.search().trim().toLocaleLowerCase();
    return this.lists()[kind].rows.filter(row =>
      (this.status() === "all" || row.isRetired === (this.status() === "retired")) &&
      row.name.toLocaleLowerCase().includes(search),
    ).sort((a, b) => a.name.localeCompare(b.name) || a.id.localeCompare(b.id));
  }
  changeNewName(kind: SpendingSetupKind, name: string) {
    this.newNames.update(names => ({ ...names, [kind]: name }));
    this.createErrors.update(errors => ({ ...errors, [kind]: "" }));
  }
  canWrite(kind: SpendingSetupKind) {
    return !!this.householdId && this.householdId === this.w.current()?.id && !this.saving() &&
      !this.lists()[kind].loading && !this.lists()[kind].error;
  }
  async create(kind: SpendingSetupKind, form?: NgForm) {
    const name = this.newNames()[kind];
    if (!name.trim() || !this.canWrite(kind)) return;
    await this.write(kind, "POST", "", { name }, () => {
      this.changeNewName(kind, "");
      form?.resetForm({ name: "" });
      this.notice.set(`${kind === "categories" ? "Category" : "Merchant"} added.`);
    }, error => this.createErrors.update(errors => ({ ...errors, [kind]: error })));
  }
  beginEdit(kind: SpendingSetupKind, entry: SpendingSetupEntry) {
    if (!this.canWrite(kind) || entry.householdId !== this.householdId || this.editor) return;
    this.editing.set({ kind, entry });
    this.editName.set(entry.name);
    this.editError.set("");
    const editor = this.dialogs.open(this.editPanel(), {
      position: { right: "0", top: "0" }, width: "480px", maxWidth: "100vw",
      height: "100dvh", maxHeight: "100dvh", panelClass: "edit-sidebar",
      autoFocus: 'input[name="editName"]', disableClose: true,
      closeOnNavigation: false,
    });
    this.editor = editor;
    editor.backdropClick().subscribe(() => this.dismissEditor());
    editor.keydownEvents().subscribe(event => {
      if (event.key === "Escape") { event.preventDefault(); this.dismissEditor(); }
    });
    editor.afterClosed().subscribe(() => {
      if (this.editor === editor) this.closeEditor();
    });
  }
  private dismissEditor() {
    if (this.saving()) return;
    if (this.editDirty() && !window.confirm("Discard this unsaved name change?")) return;
    this.closeEditor();
  }
  cancelEdit() {
    if (!this.saving()) this.closeEditor();
  }
  private closeEditor() {
    const editor = this.editor;
    this.editor = undefined;
    editor?.close();
    this.editing.set(null);
    this.editName.set("");
    this.editError.set("");
  }
  async saveEdit() {
    const edit = this.editing();
    if (!edit || !this.editName().trim() || !this.editDirty() || !this.canWrite(edit.kind)) return;
    await this.write(edit.kind, "PUT", `/${edit.entry.id}/name`, { name: this.editName() },
      () => { this.closeEditor(); this.notice.set("Name updated."); }, error => this.editError.set(error));
  }
  async setRetirement(kind: SpendingSetupKind, entry: SpendingSetupEntry) {
    if (!this.canWrite(kind) || entry.householdId !== this.householdId) return;
    await this.write(kind, "PUT", `/${entry.id}/retirement`, { isRetired: !entry.isRetired },
      () => this.notice.set(`${entry.name} ${entry.isRetired ? "restored" : "retired"}.`),
      error => this.update(kind, { actionError: error }));
  }
  private async write(kind: SpendingSetupKind, method: "POST" | "PUT", suffix: string, body: unknown,
    success: () => void, failure: (error: string) => void) {
    const home = this.householdId!;
    const generation = this.generation;
    this.saving.set(true);
    this.notice.set("");
    this.update(kind, { actionError: "" });
    this.createErrors.update(errors => ({ ...errors, [kind]: "" }));
    this.editError.set("");
    try {
      const saved = await this.api.send<SpendingSetupEntry>(method, `households/${home}/spending/${kind}${suffix}`, body);
      if (!this.current(generation, home)) return;
      this.update(kind, { rows: [...this.lists()[kind].rows.filter(row => row.id !== saved.id), saved] });
      success();
    } catch (error) {
      if (this.current(generation, home)) failure(message(error));
    } finally {
      if (this.current(generation, home)) this.saving.set(false);
    }
  }
  private editDirty() {
    return !!this.editing() && this.editName() !== this.editing()!.entry.name;
  }
  hasUnsavedChanges() {
    return !!this.newNames().categories.trim() || !!this.newNames().merchants.trim() || this.editDirty();
  }
  canLeave() {
    if (this.saving()) return false;
    return !this.hasUnsavedChanges() || window.confirm("Discard unsaved spending setup changes?");
  }
  @HostListener("window:beforeunload", ["$event"])
  beforeUnload(event: BeforeUnloadEvent) {
    if (this.hasUnsavedChanges() || this.saving()) { event.preventDefault(); event.returnValue = ""; }
  }
}
