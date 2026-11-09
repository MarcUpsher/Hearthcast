import { inject, Injectable, signal } from "@angular/core";
import { Api } from "./api";
import { message } from "./api-errors";
import { Household } from "../setup/models";

@Injectable({ providedIn: "root" })
export class Workspace {
  api = inject(Api);
  households = signal<Household[]>([]);
  current = signal<Household | null>(null);
  error = signal("");
  async load(id?: string) {
    try {
      const rows = await this.api.get<Household[]>("households");
      this.households.set(rows);
      this.current.set(
        rows.find(
          (h) =>
            h.id ===
            (id ?? this.current()?.id ?? localStorage.getItem("household")),
        ) ??
          rows[0] ??
          null,
      );
      this.error.set("");
    } catch (e) {
      this.error.set(message(e));
    }
  }
  select(id: string) {
    this.current.set(this.households().find((h) => h.id === id) ?? null);
    localStorage.setItem("household", id);
  }
  path(suffix = "") {
    return "households/" + this.current()!.id + (suffix ? "/" + suffix : "");
  }
}
