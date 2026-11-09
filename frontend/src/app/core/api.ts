import { inject, Injectable } from "@angular/core";
import { HttpClient } from "@angular/common/http";
import { firstValueFrom } from "rxjs";

@Injectable({ providedIn: "root" })
export class Api {
  private http = inject(HttpClient);
  get<T>(path: string) {
    return firstValueFrom(this.http.get<T>("/api/" + path));
  }
  send<T>(method: string, path: string, body: unknown) {
    return firstValueFrom(
      this.http.request<T>(method, "/api/" + path, { body }),
    );
  }
}
