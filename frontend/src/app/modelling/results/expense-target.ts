import { Component, computed, input } from "@angular/core";
import { money } from "../../core/money";
import { ProjectionRun } from "../models";
import { expenseMilestone } from "../chart/modelling-chart";

@Component({
  selector: "app-expense-target",
  templateUrl: "expense-target.html",
  styleUrl: "expense-target.scss",
})
export class ExpenseTarget {
  result = input.required<ProjectionRun>();
  money = money;
  expenseMilestone = computed(() => expenseMilestone(this.result()));
}
