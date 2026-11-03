import { Component } from "@angular/core";
import { FormsModule } from "@angular/forms";
import { TestBed } from "@angular/core/testing";
import { describe, it, expect } from "vitest";
import { MoneyInput, money } from "./money";

@Component({
  imports: [FormsModule, MoneyInput],
  template: `<form #form="ngForm">
    <input name="text" appMoney="text" [(ngModel)]="text" />
    <input name="number" appMoney="number" [(ngModel)]="number" />
    <button [disabled]="form.invalid">Save</button>
  </form>`,
})
class Host {
  text = "12028.123456";
  number: number | null = 0;
}

describe("Money presentation", () => {
  it("uses currency symbols, space grouping and two decimal places without losing zero or unknown", () => {
    expect(money(12028, "ZAR")).toBe("R 12 028.00");
    expect(money(1234567.89, "GBP")).toBe("£ 1 234 567.89");
    expect(money(-1234.5, "GBP")).toBe("-£ 1 234.50");
    expect(money(0, "ZAR")).toBe("R 0.00");
    expect(money(null, "GBP")).toBe("Unknown");
  });
  it("groups editable amounts without changing their raw value or precision and accepts pasted spaces", async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    await fixture.whenStable();
    const input: HTMLInputElement =
      fixture.nativeElement.querySelector('[name="text"]');
    expect(input.value).toBe("12 028.123456");
    input.dispatchEvent(new Event("focus"));
    expect(input.value).toBe("12028.123456");
    input.dispatchEvent(new Event("blur"));
    expect(fixture.componentInstance.text).toBe("12028.123456");
    input.value = "12\u00a0028.50";
    input.dispatchEvent(new Event("input"));
    input.dispatchEvent(new Event("blur"));
    await fixture.whenStable();
    expect(fixture.componentInstance.text).toBe("12028.50");
    expect(input.value).toBe("12 028.50");
  });
  it("keeps numeric fields numeric, distinguishes zero from empty, and rejects invalid pasted text", async () => {
    const fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
    await fixture.whenStable();
    const input: HTMLInputElement =
      fixture.nativeElement.querySelector('[name="number"]');
    const enter = async (value: string) => {
      input.value = value;
      input.dispatchEvent(new Event("input"));
      fixture.detectChanges();
      await fixture.whenStable();
    };
    expect(fixture.componentInstance.number).toBe(0);
    await enter("12 028.50");
    expect(fixture.componentInstance.number).toBe(12028.5);
    await enter("invalid");
    expect(fixture.nativeElement.querySelector("button").disabled).toBe(true);
    await enter("");
    expect(fixture.componentInstance.number).toBeNull();
    expect(fixture.nativeElement.querySelector("button").disabled).toBe(false);
    await enter("0");
    expect(fixture.componentInstance.number).toBe(0);
  });
});
