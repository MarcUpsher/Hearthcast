import { signal } from "@angular/core";
import { TestBed } from "@angular/core/testing";
import { provideRouter } from "@angular/router";
import { describe, expect, it, vi } from "vitest";
import { Api } from "../core/api";
import { HistoryAmount, HistorySeries } from "./models";
import { Workspace } from "../core/workspace";
import { History } from "./history";

const amount = (value: number | null, missingRateCount = 0): HistoryAmount => ({
  currencyCode: "ZAR",
  knownSubtotal: value,
  missingValueCount: 0,
  missingRateCount,
  carriedEntryCount: 0,
  isComplete: missingRateCount === 0,
});
const report = (currency = "ZAR"): HistorySeries => ({
  from: "2023-01-01",
  to: "2023-03-31",
  intervalMonths: 1,
  currencyCode: currency,
  availableCurrencies: ["GBP", "ZAR"],
  points: [
    {
      snapshotId: "one",
      snapshotDate: "2023-01-01",
      revision: "r1",
      netPosition: amount(100),
      financedEquity: amount(30),
      categories: [
        {
          categoryId: "investment",
          categoryName: "Investments",
          financialType: 1,
          amount: amount(70),
        },
      ],
      people: [
        {
          personId: "alex",
          displayName: "Alex",
          categories: [
            {
              categoryId: "investment",
              categoryName: "Investments",
              financialType: 1,
              amount: amount(30),
            },
          ],
        },
        {
          personId: "morgan",
          displayName: "Morgan",
          categories: [
            {
              categoryId: "investment",
              categoryName: "Investments",
              financialType: 1,
              amount: amount(40),
            },
          ],
        },
      ],
    },
    {
      snapshotId: "two",
      snapshotDate: "2023-02-01",
      revision: "r2",
      netPosition: amount(null, 1),
      financedEquity: amount(35),
      categories: [
        {
          categoryId: "investment",
          categoryName: "Investments",
          financialType: 1,
          amount: amount(null, 1),
        },
      ],
      people: [
        {
          personId: "alex",
          displayName: "Alex",
          categories: [
            {
              categoryId: "investment",
              categoryName: "Investments",
              financialType: 1,
              amount: amount(null, 1),
            },
          ],
        },
        {
          personId: "morgan",
          displayName: "Morgan",
          categories: [
            {
              categoryId: "investment",
              categoryName: "Investments",
              financialType: 1,
              amount: amount(null, 1),
            },
          ],
        },
      ],
    },
    {
      snapshotId: "three",
      snapshotDate: "2023-03-01",
      revision: "r3",
      netPosition: amount(130),
      financedEquity: amount(40),
      categories: [
        {
          categoryId: "investment",
          categoryName: "Investments",
          financialType: 1,
          amount: amount(90),
        },
      ],
      people: [
        {
          personId: "alex",
          displayName: "Alex",
          categories: [
            {
              categoryId: "investment",
              categoryName: "Investments",
              financialType: 1,
              amount: amount(50),
            },
          ],
        },
        {
          personId: "morgan",
          displayName: "Morgan",
          categories: [
            {
              categoryId: "investment",
              categoryName: "Investments",
              financialType: 1,
              amount: amount(40),
            },
          ],
        },
      ],
    },
  ],
});

describe("History graph", () => {
  it("breaks an incomplete line and links valid points to their snapshots", async () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: Api, useValue: { get: async () => [] } },
        { provide: Workspace, useValue: { current: () => ({ id: "home" }) } },
      ],
    });
    const fixture = TestBed.createComponent(History);
    fixture.detectChanges();
    await fixture.whenStable();
    const history = fixture.componentInstance;
    history.report.set(report());
    history.investmentCategories.set(["investment"]);
    fixture.detectChanges();
    const chart: HTMLElement = fixture.nativeElement;
    const net = chart.querySelector("path.net")!.getAttribute("d")!;
    expect(net.match(/M/g)).toHaveLength(2);
    expect(net).not.toContain("L");
    expect(
      history.chart()!.lines.find((line) => line.key === "investments")?.points,
    ).toHaveLength(2);
    expect(history.chart()!.lines).toHaveLength(5);
    expect(
      history.chart()!.lines.find((line) => line.key === "person:alex")
        ?.points[0].value,
    ).toBe(30);
    expect(
      history.chart()!.lines.find((line) => line.key === "person:morgan")
        ?.points[0].value,
    ).toBe(40);
    expect(new Set(history.chart()!.lines.map((line) => line.color)).size).toBe(
      5,
    );
    expect(chart.textContent).toContain("Alex investments");
    expect(chart.textContent).toContain("Morgan investments");
    const target = chart.querySelector(".point-target")!;
    expect(target.getAttribute("fill")).toBe("transparent");
    expect(target.getAttribute("r")).toBe("10");
    const anchor = target.closest("a")!;
    anchor.dispatchEvent(new FocusEvent("focus"));
    fixture.detectChanges();
    await fixture.whenStable();
    expect(chart.querySelector('[role="tooltip"]')!.textContent).toContain(
      "2023-01-01",
    );
    anchor.dispatchEvent(new FocusEvent("blur"));
    fixture.detectChanges();
    expect(chart.querySelector('[role="tooltip"]')).toBeNull();
    const netLine = history.chart()!.lines.find((line) => line.key === "net")!;
    expect(Math.min(...netLine.points.map((point) => point.y))).toBeGreaterThan(
      Math.min(...history.chart()!.ticks.map((tick) => tick.y)),
    );

    expect(
      chart.querySelector('a[href="/snapshots?snapshot=one"]'),
    ).not.toBeNull();
    expect(
      chart.querySelector('a[href="/snapshots?snapshot=two"]'),
    ).not.toBeNull(); // Equity remains valid.
    expect(chart.textContent).toContain(
      "Gaps mean no snapshot or an incomplete value",
    );
  });

  it("loads household dates, offers recorded currencies, and reloads when the currency changes", async () => {
    const get = vi.fn(async (path: string) =>
      path.endsWith("/snapshots")
        ? [
            {
              id: "one",
              snapshotDate: "2023-01-01",
              datePrecision: 1,
              revision: "r1",
            },
          ]
        : report(path.includes("currency=GBP") ? "GBP" : "ZAR"),
    );
    const current = signal({ id: "home" });
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: Api, useValue: { get } },
        {
          provide: Workspace,
          useValue: {
            current,
            path: (path: string) => "households/home/" + path,
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(History);
    fixture.detectChanges();
    await fixture.whenStable();
    const history = fixture.componentInstance;
    expect(history.from).toBe("2023-01-01");
    await history.load();
    expect(history.report()?.currencyCode).toBe("ZAR");
    expect(history.investmentCategories()).toEqual(["investment"]);
    history.currency = "GBP";
    await history.load();
    fixture.detectChanges();
    expect(history.report()?.currencyCode).toBe("GBP");
    expect(get).toHaveBeenCalledWith(
      expect.stringContaining("intervalMonths=6&currency=GBP"),
    );
    history.from = "2025-01-01";
    history.to = "2024-01-01";
    await history.load();
    expect(history.error()).toContain("start date");
  });
  async function setupChart(
    get = async (_path: string): Promise<unknown> => [],
  ) {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: Api, useValue: { get } },
        {
          provide: Workspace,
          useValue: {
            current: () => ({ id: "home" }),
            path: (path: string) => `households/home/${path}`,
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(History);
    fixture.detectChanges();
    await fixture.whenStable();
    return fixture.componentInstance;
  }

  it("plots an emptied category as zero but keeps absent people and unknown amounts as gaps", async () => {
    const history = await setupChart();
    const data = report();
    data.points[0].categories = [];
    data.points[0].people[0].categories = [];
    data.points[1].people = data.points[1].people.filter(
      (person) => person.personId !== "alex",
    );
    history.investmentCategories.set(["investment"]);
    history.report.set(data);
    const lines = history.chart()!.lines;
    expect(
      lines
        .find((line) => line.key === "investments")!
        .points.map((point) => point.value),
    ).toEqual([0, 90]);
    expect(
      lines
        .find((line) => line.key === "person:alex")!
        .points.map((point) => point.value),
    ).toEqual([0, 50]);
    history.categoriesChanged([]);
    expect(
      history.chart()!.lines.find((line) => line.key === "investments")!.points,
    ).toHaveLength(0);
  });

  it("keeps a person's color when another person leaves the visible range or names change", async () => {
    const history = await setupChart();
    history.investmentCategories.set(["investment"]);
    history.report.set(report());
    const color = history
      .chart()!
      .lines.find((line) => line.key === "person:morgan")!.color;
    const narrowed = report();
    for (const point of narrowed.points) {
      point.people = point.people.filter(
        (person) => person.personId === "morgan",
      );
      point.people[0].displayName = "Another name";
    }
    history.report.set(narrowed);
    expect(
      history.chart()!.lines.find((line) => line.key === "person:morgan")!
        .color,
    ).toBe(color);
  });

  it("connects preserved endpoint readings within the same calendar interval", async () => {
    const history = await setupChart();
    const data = report();
    data.intervalMonths = 12;
    data.points = [data.points[0], data.points[2]];
    history.report.set(data);
    const path = history
      .chart()!
      .lines.find((line) => line.key === "net")!.path;
    expect(path.match(/M/g)).toHaveLength(1);
    expect(path.match(/C/g)).toHaveLength(1);
  });

  it("selects the closest series and includes coincident readings", async () => {
    const history = await setupChart();
    history.report.set(report());
    const lines = history.chart()!.lines;
    const net = lines.find((line) => line.key === "net")!;
    const equity = lines.find((line) => line.key === "equity")!;
    const point = net.points[0];
    vi.stubGlobal(
      "DOMPoint",
      class {
        constructor(
          public x: number,
          public y: number,
        ) {}
        matrixTransform() {
          return this;
        }
      },
    );
    const svg = {
      tagName: "svg",
      getScreenCTM: () => ({}),
      querySelectorAll: () => [],
      getBoundingClientRect: () => ({ left: 0, width: 1000 }),
    };
    const event = {
      currentTarget: svg,
      clientX: point.x,
      clientY: point.y,
    } as unknown as MouseEvent;
    try {
      history.hoverChart(event);
      expect(history.hoveredReading()?.lineKey).toBe("net");
      equity.points[0].y = point.y;
      history.hoverChart(event);
      expect(history.hoveredReading()?.message).toContain("Financed equity");
      expect(history.hoveredReading()?.message).toContain(
        "Household net position",
      );
      history.hoverChart({ ...event, clientY: -100 } as MouseEvent);
      expect(history.hoveredReading()).toBeNull();
    } finally {
      vi.unstubAllGlobals();
    }
  });

  it("keeps isolated readings visible without adding markers to connected lines", async () => {
    const history = await setupChart();
    history.report.set(report());
    expect(
      history.chart()!.lines.find((line) => line.key === "equity")!.isolated,
    ).toHaveLength(0);
    expect(
      history.chart()!.lines.find((line) => line.key === "net")!.isolated,
    ).toHaveLength(2);
    const data = report();
    data.points = [data.points[0]];
    history.report.set(data);
    expect(
      history.chart()!.lines.find((line) => line.key === "net")!.isolated,
    ).toHaveLength(1);
  });

  it("uses distinct vertical ticks for an all-zero portfolio", async () => {
    const history = await setupChart();
    const data = report();
    data.points = [
      {
        ...data.points[0],
        netPosition: amount(0),
        financedEquity: amount(0),
        categories: [],
        people: [],
      },
    ];
    history.investmentCategories.set(["investment"]);
    history.report.set(data);
    const chart = history.chart()!;
    expect(new Set(chart.ticks.map((tick) => tick.y)).size).toBe(5);
    expect(
      chart.lines.find((line) => line.key === "net")!.points[0].y,
    ).toBeGreaterThan(chart.ticks.at(-1)!.y);
  });

  it.each(["cleared", "reversed"])(
    "discards an in-flight response after the range is %s",
    async (invalid) => {
      let resolve!: (value: HistorySeries) => void;
      const pending = new Promise<HistorySeries>((done) => (resolve = done));
      const history = await setupChart(async (path) =>
        path.endsWith("/snapshots") ? [] : pending,
      );
      history.from = "2023-01-01";
      history.to = "2023-03-31";
      const loading = history.load();
      history.from = invalid === "cleared" ? "" : "2024-01-01";
      await history.load();
      resolve(report());
      await loading;
      expect(history.report()).toBeNull();
      expect(history.error()).not.toBe("");
      expect(history.busy()).toBe(false);
    },
  );
});
