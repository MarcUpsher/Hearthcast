import { Component, computed, effect, input, signal } from "@angular/core";
import { money } from "../../core/money";
import { ProjectionRun } from "../models";
import {
  buildModellingChart,
  chartReading,
  expenseMilestone,
  PayoffEvent,
  projectionSeries,
} from "./modelling-chart";

@Component({
  selector: "app-projection-chart",
  templateUrl: "projection-chart.html",
  styleUrl: "projection-chart.scss",
})
export class ProjectionChart {
  result = input.required<ProjectionRun>();
  adjusted = input(false);
  pending = input(false);
  assetNames = input<ReadonlyMap<string, string>>(new Map());
  hovered = signal<number | null>(null);
  lines = projectionSeries;
  money = money;
  chart = computed(() =>
    buildModellingChart(this.result(), this.adjusted(), this.hovered()),
  );
  reading = computed(() =>
    chartReading(this.result(), this.adjusted(), this.hovered()),
  );
  expenseMilestone = computed(() => expenseMilestone(this.result()));
  constructor() {
    effect(() => {
      this.result();
      this.adjusted();
      this.pending();
      this.hovered.set(null);
    });
  }
  hover(event: MouseEvent) {
    const months = this.result()?.months;
    if (!months?.length) return;
    const bounds = (
      event.currentTarget as SVGSVGElement
    ).getBoundingClientRect();
    if (!bounds.width) return;
    const x = ((event.clientX - bounds.left) / bounds.width) * 1000;
    this.hovered.set(
      Math.max(
        0,
        Math.min(
          months.length - 1,
          Math.round(((x - 88) / 832) * (months.length - 1)),
        ),
      ),
    );
  }
  moveReading(delta: number, event: Event) {
    event.preventDefault();
    const length = this.result()?.months?.length ?? 0;
    if (length)
      this.hovered.set(
        Math.max(0, Math.min(length - 1, (this.hovered() ?? 0) + delta)),
      );
  }
  inspectMonth(index: number, event: Event) {
    event.preventDefault();
    event.stopPropagation();
    this.hovered.set(index);
  }
  payoffLabel(event: PayoffEvent) {
    const names = event.assetIds
      .map((id) => this.assetNames().get(id) ?? id)
      .join(", ");
    return `${names} · ${event.index === 0 ? "Already paid off at start" : "Loan paid off"} · ${event.date}`;
  }
}
