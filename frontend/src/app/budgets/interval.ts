import { Directive, forwardRef } from "@angular/core";
import { AbstractControl, NG_VALIDATORS, ValidationErrors, Validator } from "@angular/forms";

export function budgetInterval(value: unknown): number | null {
  const text = String(value ?? ""), count = Number(text);
  return /^\d+$/.test(text) && Number.isInteger(count) && count >= 1 && count <= 2147483647 ? count : null;
}

@Directive({
  selector: "input[appBudgetInterval]",
  providers: [{ provide: NG_VALIDATORS, useExisting: forwardRef(() => BudgetInterval), multi: true }],
})
export class BudgetInterval implements Validator {
  validate(control: AbstractControl): ValidationErrors | null {
    return budgetInterval(control.value) === null ? { budgetInterval: true } : null;
  }
}
