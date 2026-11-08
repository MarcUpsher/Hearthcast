import { bootstrapApplication } from "@angular/platform-browser";
import { provideHttpClient } from "@angular/common/http";
import { provideRouter } from "@angular/router";
import { App } from "./app/app";
import { routes } from "./app/app.routes";
import { MAT_FORM_FIELD_DEFAULT_OPTIONS } from "@angular/material/form-field";
bootstrapApplication(App, {
  providers: [
    provideHttpClient(),
    {
      provide: MAT_FORM_FIELD_DEFAULT_OPTIONS,
      useValue: { appearance: "outline", subscriptSizing: "dynamic" },
    },
    provideRouter(routes),
  ],
}).catch(console.error);
