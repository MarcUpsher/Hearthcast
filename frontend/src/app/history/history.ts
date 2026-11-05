import { smoothPath } from "./smooth-path";
import {
  Component,
  computed,
  effect,
  inject,
  signal,
  afterRenderEffect,
  viewChild,
  ElementRef,
} from "@angular/core";
import { FormsModule } from "@angular/forms";
import { Router, RouterLink } from "@angular/router";
import { MatButtonModule } from "@angular/material/button";
import { MatFormFieldModule } from "@angular/material/form-field";
import { MatInputModule } from "@angular/material/input";
import { MatSelectModule } from "@angular/material/select";
import {
  Api,
  HistoryAmount,
  HistoryPoint,
  HistorySeries,
  Inventory,
  Workspace,
  message,
} from "../core/api";
import { money } from "../core/money";

type ChartLine = {
  key: string;
  label: string;
  color: string;
  width: number;
  path: string;
  isolated: { x: number; y: number }[];
  points: {
    x: number;
    y: number;
    snapshotId: string;
    date: string;
    value: number;
    carried: number;
  }[];
};
type Chart = {
  lines: ChartLine[];
  ticks: { y: number; label: string }[];
  dates: { x: number; label: string }[];
  hasValues: boolean;
};
const width = 1000;
const height = 420;
const left = 88;
const right = 80;
const top = 25;
const bottom = 55;
const dateNumber = (date: string) => Date.parse(date + "T00:00:00Z");
const personColors = ["#ff7d77", "#b9e769", "#b9a0ff", "#ffad5e", "#57d5c5"];
const valid = (amount: HistoryAmount) =>
  amount.isComplete && amount.knownSubtotal !== null
    ? amount.knownSubtotal
    : null;
const today = () => {
  const date = new Date();
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
};

@Component({
  imports: [
    FormsModule,
    RouterLink,
    MatButtonModule,
    MatFormFieldModule,
    MatInputModule,
    MatSelectModule,
  ],
  templateUrl: "history.html",
  styleUrl: "history.scss",
})
export class History {
  private api = inject(Api);
  private router = inject(Router);
  hoveredReading = signal<{
    lineKey: string;
    message: string;
    x: number;
    y: number;
  } | null>(null);
  w = inject(Workspace);
  inventory = signal<Inventory[]>([]);
  report = signal<HistorySeries | null>(null);
  busy = signal(false);
  error = signal("");
  from = "";
  to = today();
  interval = 6;
  currency = "ZAR";
  investmentCategories = signal<string[]>([]);
  private categoriesInitialized = false;
  private generation = 0;
  private personColorById = new Map<string, string>();
  private animationTimer: ReturnType<typeof setTimeout> | undefined;
  outgoing = signal<Chart | null>(null);
  chart = computed(() =>
    this.makeChart(this.report(), this.investmentCategories()),
  );
  categoryOptions = computed(() => {
    const categories = new Map<string, string>();
    for (const point of this.report()?.points ?? [])
      for (const category of point.categories)
        if (category.financialType === 1)
          categories.set(category.categoryId, category.categoryName);
    return [...categories]
      .map(([id, name]) => ({ id, name }))
      .sort((a, b) => a.name.localeCompare(b.name));
  });
  money = money;

  private popup = viewChild<ElementRef<HTMLElement>>("readingPopup");

  constructor() {
    afterRenderEffect(() => {
      const reading = this.hoveredReading();
      const element = this.popup()?.nativeElement;
      if (!reading || !element) return;
      const bounds = element.getBoundingClientRect();
      const x = Math.max(
        8,
        Math.min(reading.x, window.innerWidth - bounds.width - 8),
      );
      const y = Math.max(
        8,
        Math.min(reading.y, window.innerHeight - bounds.height - 8),
      );
      if (x !== reading.x || y !== reading.y)
        this.hoveredReading.set({ ...reading, x, y });
    });
    effect(() => {
      const householdId = this.w.current()?.id;
      this.generation++;
      this.hoveredReading.set(null);
      this.report.set(null);
      this.inventory.set([]);
      this.personColorById.clear();
      this.categoriesInitialized = false;
      this.investmentCategories.set([]);
      if (householdId) void this.initialize(householdId);
    });
  }

  async initialize(householdId: string) {
    const generation = ++this.generation;
    this.busy.set(true);
    this.error.set("");
    try {
      const inventory = await this.api.get<Inventory[]>(
        `households/${householdId}/snapshots`,
      );
      if (generation !== this.generation) return;
      this.inventory.set(inventory);
      if (inventory.length) {
        this.from = inventory.reduce(
          (earliest, row) =>
            row.snapshotDate < earliest ? row.snapshotDate : earliest,
          inventory[0].snapshotDate,
        );
        await this.load();
      }
    } catch (error) {
      if (generation === this.generation) this.error.set(message(error));
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }

  async load() {
    const generation = ++this.generation;
    this.hoveredReading.set(null);
    this.busy.set(false);
    this.error.set("");
    if (!this.w.current()) return;
    if (!this.from || !this.to) {
      this.error.set("Select both a start and end date.");
      return;
    }
    if (this.from > this.to) {
      this.error.set("The start date must be on or before the end date.");
      return;
    }
    this.busy.set(true);
    try {
      const query = new URLSearchParams({
        from: this.from,
        to: this.to,
        intervalMonths: String(this.interval),
        currency: this.currency,
      });
      let report = await this.api.get<HistorySeries>(
        this.w.path(`summary-history?${query}`),
      );
      if (generation !== this.generation) return;
      if (
        report.availableCurrencies.length &&
        !report.availableCurrencies.includes(this.currency)
      ) {
        this.currency = report.availableCurrencies[0];
        query.set("currency", this.currency);
        report = await this.api.get<HistorySeries>(
          this.w.path(`summary-history?${query}`),
        );
        if (generation !== this.generation) return;
      }
      this.outgoing.set(this.chart());
      this.report.set(report);
      if (!this.categoriesInitialized) {
        this.investmentCategories.set(
          this.categoryOptions()
            .filter((category) => /invest/i.test(category.name))
            .map((category) => category.id),
        );
        this.categoriesInitialized = true;
      }
      clearTimeout(this.animationTimer);
      this.animationTimer = setTimeout(() => this.outgoing.set(null), 450);
    } catch (error) {
      if (generation === this.generation) this.error.set(message(error));
    } finally {
      if (generation === this.generation) this.busy.set(false);
    }
  }

  categoriesChanged(ids: string[]) {
    this.investmentCategories.set(ids);
    this.outgoing.set(null);
    this.hoveredReading.set(null);
  }

  private nearestReading(event: MouseEvent, line: ChartLine) {
    const svg =
      (event.currentTarget as SVGElement).tagName === "svg"
        ? (event.currentTarget as SVGSVGElement)
        : (event.currentTarget as SVGElement).ownerSVGElement;
    const bounds = svg?.getBoundingClientRect();
    if (!bounds?.width || !line.points.length) return null;
    const x = ((event.clientX - bounds.left) * width) / bounds.width;
    return line.points.reduce((nearest, point) =>
      Math.abs(point.x - x) < Math.abs(nearest.x - x) ? point : nearest,
    );
  }

  hoverChart(event: MouseEvent) {
    const svg = event.currentTarget as SVGSVGElement;
    const matrix = svg.getScreenCTM();
    if (!matrix) return;
    const candidates: { line: ChartLine; distance: number }[] = [];
    for (const line of this.chart()?.lines ?? []) {
      const path = [
        ...svg.querySelectorAll<SVGPathElement>(".current-line"),
      ].find((path) => path.dataset["key"] === line.key);
      let distance = Infinity;
      if (path) {
        // Sample the rendered curve in screen pixels so SVG scaling and smoothing
        // are both included in hit testing. Isolated readings are checked below.
        const length = path.getTotalLength();
        for (
          let offset = 0;
          offset <= length;
          offset += Math.max(1, length / 600)
        ) {
          const point = path.getPointAtLength(offset).matrixTransform(matrix);
          distance = Math.min(
            distance,
            Math.hypot(point.x - event.clientX, point.y - event.clientY),
          );
        }
      }
      for (const point of line.points) {
        const screen = new DOMPoint(point.x, point.y).matrixTransform(matrix);
        distance = Math.min(
          distance,
          Math.hypot(screen.x - event.clientX, screen.y - event.clientY),
        );
      }
      candidates.push({ line, distance });
    }
    candidates.sort((a, b) => a.distance - b.distance);
    const nearest = candidates[0];
    if (!nearest || nearest.distance > 10) {
      this.hoveredReading.set(null);
      return;
    }
    const matches = candidates.filter(
      (candidate) => candidate.distance <= nearest.distance + 1,
    );
    const readings = matches.map(({ line }) => ({
      line,
      point: this.nearestReading(event, line)!,
    }));
    this.showReading(
      nearest.line,
      readings[0].point,
      event.clientX,
      event.clientY,
    );
    this.hoveredReading.update(
      (reading) =>
        reading && {
          ...reading,
          message: readings
            .map(({ line, point }) => this.readingMessage(line, point))
            .join("\n"),
        },
    );
  }

  private readingMessage(line: ChartLine, point: ChartLine["points"][number]) {
    return `${line.label} · ${point.date} · ${money(point.value, this.report()!.currencyCode)}${point.carried ? ` · ${point.carried} carried` : ""}`;
  }

  focusReading(
    event: FocusEvent,
    line: ChartLine,
    point: ChartLine["points"][number],
  ) {
    const bounds = (event.currentTarget as SVGElement).getBoundingClientRect();
    this.showReading(line, point, bounds.right, bounds.top);
  }

  private showReading(
    line: ChartLine,
    point: ChartLine["points"][number],
    x: number,
    y: number,
  ) {
    this.hoveredReading.set({
      lineKey: line.key,
      x: Math.max(8, Math.min(x + 14, window.innerWidth - 8)),
      y: Math.max(8, Math.min(y + 14, window.innerHeight - 8)),
      message: this.readingMessage(line, point),
    });
  }

  openReading(event: MouseEvent) {
    if ((event.target as Element).closest("a")) return;
    const line = this.chart()?.lines.find(
      (line) => line.key === this.hoveredReading()?.lineKey,
    );
    if (!line) return;
    const point = this.nearestReading(event, line);
    if (point)
      void this.router.navigate(["/snapshots"], {
        queryParams: { snapshot: point.snapshotId },
      });
  }

  private personColor(personId: string): string {
    let color = this.personColorById.get(personId);
    if (!color) {
      color = personColors[this.personColorById.size % personColors.length];
      this.personColorById.set(personId, color);
    }
    return color;
  }

  private investment(
    categories: HistoryPoint["categories"] | undefined,
    selected: Set<string>,
  ): HistoryAmount {
    const rows = (categories ?? [])
      .filter((category) => selected.has(category.categoryId))
      .map((category) => category.amount);
    const known = rows.filter((row) => row.knownSubtotal !== null);
    return {
      currencyCode: this.currency,
      knownSubtotal:
        !categories || !selected.size
          ? null
          : !rows.length
            ? 0
            : !known.length
              ? null
              : known.reduce((sum, row) => sum + row.knownSubtotal!, 0),
      missingValueCount: rows.reduce(
        (sum, row) => sum + row.missingValueCount,
        0,
      ),
      missingRateCount: rows.reduce(
        (sum, row) => sum + row.missingRateCount,
        0,
      ),
      carriedEntryCount: rows.reduce(
        (sum, row) => sum + row.carriedEntryCount,
        0,
      ),
      isComplete:
        categories !== undefined &&
        selected.size > 0 &&
        rows.every((row) => row.isComplete),
    };
  }

  private makeChart(
    report: HistorySeries | null,
    selectedIds: string[],
  ): Chart | null {
    if (!report || !report.points.length) return null;
    const selected = new Set(selectedIds);
    const people = new Map<string, string>();
    for (const point of report.points)
      for (const person of point.people)
        if (
          person.categories.some((category) =>
            selected.has(category.categoryId),
          )
        )
          people.set(person.personId, person.displayName);
    const definitions: (Pick<ChartLine, "key" | "label" | "color" | "width"> & {
      values: (number | null)[];
      amounts: HistoryAmount[];
    })[] = [
      {
        key: "investments",
        label: "Household investments",
        color: "#42c9ff",
        width: 3,
        amounts: report.points.map((point) =>
          this.investment(point.categories, selected),
        ),
        values: [],
      },
      {
        key: "equity",
        label: "Financed equity",
        color: "#ffc35a",
        width: 2.7,
        amounts: report.points.map((point) => point.financedEquity),
        values: [],
      },
      {
        key: "net",
        label: "Household net position",
        color: "#ec8cda",
        width: 3.5,
        amounts: report.points.map((point) => point.netPosition),
        values: [],
      },
    ];
    [...people.entries()]
      .sort((a, b) => a[1].localeCompare(b[1]) || a[0].localeCompare(b[0]))
      .forEach(([personId, name]) =>
        definitions.push({
          key: `person:${personId}`,
          label: `${name} investments`,
          color: this.personColor(personId),
          width: 2.4,
          amounts: report.points.map((point) => {
            const person = point.people.find(
              (row) => row.personId === personId,
            );
            return this.investment(person?.categories, selected);
          }),
          values: [],
        }),
      );
    for (const line of definitions) line.values = line.amounts.map(valid);
    const values = definitions
      .flatMap((line) => line.values)
      .filter((value): value is number => value !== null);
    const minimum = Math.min(0, ...values);
    const observedMaximum = Math.max(0, ...values);
    const maximum =
      observedMaximum === minimum
        ? minimum + 1
        : observedMaximum + (observedMaximum - minimum) * 0.1;
    const span = maximum - minimum;
    const minDate = dateNumber(report.from);
    const maxDate = Math.max(minDate + 86400000, dateNumber(report.to));
    const x = (date: string) =>
      left +
      ((dateNumber(date) - minDate) / (maxDate - minDate)) *
        (width - left - right);
    const y = (value: number) =>
      top + ((maximum - value) / span) * (height - top - bottom);
    const bucket = (date: string) => {
      const year = Number(date.slice(0, 4));
      const month = Number(date.slice(5, 7)) - 1;
      return Math.floor((year * 12 + month) / report.intervalMonths);
    };
    const lines: ChartLine[] = definitions.map((line) => {
      const segments: { x: number; y: number }[][] = [];
      let previousBucket: number | null = null;
      const points: ChartLine["points"] = [];
      report.points.forEach((point, index) => {
        const value = line.values[index];
        const currentBucket = bucket(point.snapshotDate);
        if (value === null) {
          previousBucket = null;
          return;
        }
        const px = x(point.snapshotDate);
        const py = y(value);
        const segment = segments.at(-1);
        if (
          previousBucket !== null &&
          currentBucket - previousBucket <= 1 &&
          segment &&
          px > segment.at(-1)!.x
        )
          segment.push({ x: px, y: py });
        else segments.push([{ x: px, y: py }]);
        previousBucket = currentBucket;
        points.push({
          x: px,
          y: py,
          snapshotId: point.snapshotId,
          date: point.snapshotDate,
          value,
          carried: line.amounts[index].carriedEntryCount,
        });
      });
      return {
        key: line.key,
        label: line.label,
        color: line.color,
        width: line.width,
        path: segments.map(smoothPath).join(""),
        isolated: segments.filter((segment) => segment.length === 1).flat(),
        points,
      };
    });
    const ticks = Array.from({ length: 5 }, (_, index) => {
      const value = minimum + ((maximum - minimum) * index) / 4;
      return {
        y: y(value),
        label: new Intl.NumberFormat("en-GB", {
          notation: "compact",
          maximumFractionDigits: 1,
        }).format(value),
      };
    });
    const dates = Array.from({ length: 5 }, (_, index) => {
      const timestamp = minDate + ((maxDate - minDate) * index) / 4;
      return {
        x: left + ((width - left - right) * index) / 4,
        label: new Intl.DateTimeFormat("en-GB", {
          month: "short",
          year: "numeric",
          timeZone: "UTC",
        }).format(timestamp),
      };
    });
    return { lines, ticks, dates, hasValues: values.length > 0 };
  }
}
