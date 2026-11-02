import { TestBed } from "@angular/core/testing";
import { provideRouter } from "@angular/router";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { Api, Workspace, Report, Snapshot } from "../core/api";
import { SnapshotBrowser } from "./browser";
import { TestbedHarnessEnvironment } from "@angular/cdk/testing/testbed";
import { MatSelectHarness } from "@angular/material/select/testing";

describe("Snapshot rendering", () => {
  beforeEach(() => vi.stubEnv("TZ", "Europe/London"));
  afterEach(() => vi.unstubAllEnvs());
  it("renders returned totals and names the selected individual", async () => {
    TestBed.configureTestingModule({
      imports: [SnapshotBrowser],
      providers: [
        provideRouter([]),
        { provide: Api, useValue: {} },
        { provide: Workspace, useValue: { current: () => null } },
      ],
    });
    const fixture = TestBed.createComponent(SnapshotBrowser);
    const c = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();
    const totals = {
      knownNetPosition: 123,
      ordinaryAssets: {
        knownSubtotal: 123,
        missingEntryCount: 0,
        carriedForwardEntryCount: 1,
      },
      ordinaryDebts: {
        knownSubtotal: 0,
        missingEntryCount: 0,
        carriedForwardEntryCount: 0,
      },
      financedAssetEquity: {
        knownSubtotal: 0,
        missingEntryCount: 0,
        carriedForwardEntryCount: 0,
      },
    };
    const currency = {
      currencyCode: "GBP",
      totals,
      holdingsTotals: totals,
      allRecordedTotals: totals,
      categories: [],
    };
    const report = {
      currencies: [currency],
      people: [
        {
          person: { personId: "a", displayName: "Alex" },
          currencies: [currency],
        },
      ],
    } as Report;
    c.reports.set([report, report, report]);
    c.saved.set({
      id: "snapshot",
      people: [],
      itemSnapshots: [
        {
          trackedItemId: "sample",
          itemName: "Sample holding",
          groupName: "Pension",
          categoryName: "Investments",
          currencyCode: "GBP",
          amount: 123,
          valueOrigin: 3,
          lastConfirmedDate: "2026-06-01",
          holderSnapshotPersonId: "holder",
          includeInHousehold: true,
        },
      ],
      financedAssetSnapshots: [],
    } as unknown as Snapshot);
    c.inventory.set([
      {
        id: "snapshot",
        snapshotDate: "2026-09-01",
        datePrecision: 2,
        revision: "v",
      },
    ]);
    c.selected = "snapshot";
    c.year = "2026";
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain("£123.00");
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain("September");
    expect(fixture.nativeElement.textContent.replace(/\s+/g, " ")).toContain(
      "Jun 2026",
    );
    expect(fixture.nativeElement.textContent).toContain("1 carried entries");
    const loader = TestbedHarnessEnvironment.loader(fixture);
    const view = await loader.getHarness(
      MatSelectHarness.with({ selector: '[aria-label="Summary view"]' }),
    );
    await view.open();
    await view.clickOptions({ text: "Individual holdings" });
    expect(c.person).toBe("a");
    fixture.detectChanges();
    await fixture.whenStable();
    expect(fixture.nativeElement.textContent).toContain("Alex");
  });
});
