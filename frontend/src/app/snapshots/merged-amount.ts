import { Component, input } from "@angular/core";
import { MergedAmount } from "../summaries/models";
import { money } from "../core/money";

@Component({
  selector: "td[appMergedAmount]",
  host: { class: "numeric", "[class.incomplete]": "!cell().isComplete" },
  template: `
    {{ money(cell().knownSubtotal, cell().currencyCode) }}
    @if (!cell().isComplete) {
      <small
        >Incomplete · {{ cell().missingValueCount }} missing values ·
        {{ cell().missingRateCount }} missing rates</small
      >
    }
    @if (cell().carriedEntryCount) {
      <small>{{ cell().carriedEntryCount }} carried</small>
    }
  `,
  styles: `
    small {
      display: block;
      font-weight: normal;
      white-space: normal;
    }
  `,
})
export class MergedAmountCell {
  cell = input.required<MergedAmount>();
  money = money;
}
