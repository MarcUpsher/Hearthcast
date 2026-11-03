import {
  Directive,
  ElementRef,
  HostListener,
  Input,
  forwardRef,
  inject,
} from "@angular/core";
import {
  ControlValueAccessor,
  NG_VALUE_ACCESSOR,
  NG_VALIDATORS,
  Validator,
} from "@angular/forms";

const numbers = new Intl.NumberFormat("en-GB", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});
const symbols = new Map<string, string>();

export function currencySymbol(currency: string): string {
  if (!currency) return "";
  let symbol = symbols.get(currency);
  if (!symbol) {
    try {
      symbol =
        new Intl.NumberFormat("en-GB", {
          style: "currency",
          currency,
          currencyDisplay: "narrowSymbol",
        })
          .formatToParts(0)
          .find((p) => p.type === "currency")?.value ?? currency;
    } catch {
      symbol = currency;
    }
    symbols.set(currency, symbol);
  }
  return symbol;
}

export function money(
  value: number | null | undefined,
  currency: string,
): string {
  if (value == null) return "Unknown";
  return (
    (value < 0 ? "-" : "") +
    currencySymbol(currency) +
    " " +
    numbers.format(Math.abs(value)).replaceAll(",", " ")
  );
}

// Group the display on blur while keeping an unformatted model for API requests.
@Directive({
  selector: "input[appMoney]",
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => MoneyInput),
      multi: true,
    },
    {
      provide: NG_VALIDATORS,
      useExisting: forwardRef(() => MoneyInput),
      multi: true,
    },
  ],
})
export class MoneyInput implements ControlValueAccessor, Validator {
  @Input() appMoney: "text" | "number" = "text";
  private readonly input =
    inject<ElementRef<HTMLInputElement>>(ElementRef).nativeElement;
  private raw = "";
  private focused = false;
  private change: (value: string | number | null) => void = () => {};
  private touched: () => void = () => {};
  writeValue(value: string | number | null | undefined) {
    this.raw = value == null ? "" : String(value);
    this.render();
  }
  registerOnChange(fn: typeof this.change) {
    this.change = fn;
  }
  registerOnTouched(fn: () => void) {
    this.touched = fn;
  }
  setDisabledState(disabled: boolean) {
    this.input.disabled = disabled;
  }
  @HostListener("focus") focus() {
    this.focused = true;
    this.render();
  }
  @HostListener("blur") blur() {
    this.focused = false;
    this.render();
    this.touched();
  }
  @HostListener("input") inputChanged() {
    this.raw = this.input.value.replace(/\s/g, "");
    this.change(
      this.appMoney === "number"
        ? this.raw === ""
          ? null
          : this.valid()
            ? Number(this.raw)
            : this.raw
        : this.raw,
    );
  }
  private valid() {
    return /^\d+(\.\d+)?$/.test(this.raw) && Number.isFinite(Number(this.raw));
  }
  validate() {
    return this.raw === "" || this.valid() ? null : { money: true };
  }
  private render() {
    this.input.value = this.focused
      ? this.raw
      : this.raw.replace(
          /^(\d+)(\.\d*)?$/,
          (_, whole: string, fraction: string | undefined) =>
            whole.replace(/\B(?=(\d{3})+(?!\d))/g, " ") + (fraction ?? ""),
        );
  }
}
