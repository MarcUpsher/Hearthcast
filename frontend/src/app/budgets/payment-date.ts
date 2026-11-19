import { Directive, ElementRef, forwardRef, inject } from "@angular/core";
import { AbstractControl, NG_VALIDATORS, ValidationErrors, Validator } from "@angular/forms";
import { validPaymentAnchor } from "./payment-schedule";

@Directive({
  selector: "input[appBudgetPaymentDate]",
  providers: [{ provide: NG_VALIDATORS, useExisting: forwardRef(() => BudgetPaymentDate), multi: true }],
})
export class BudgetPaymentDate implements Validator {
  private readonly input = inject<ElementRef<HTMLInputElement>>(ElementRef);
  validate(control: AbstractControl): ValidationErrors | null {
    return !this.input.nativeElement.validity.badInput && validPaymentAnchor(String(control.value ?? "")) ? null : { paymentDate: true };
  }
}
