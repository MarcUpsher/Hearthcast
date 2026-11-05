import { describe, expect, it } from "vitest";
import { smoothPath } from "./smooth-path";

describe("Smooth history paths", () => {
  it("passes through unevenly spaced readings without overshooting rises, flats or peaks", () => {
    const points = [
      { x: 0, y: 0 },
      { x: 5, y: 100 },
      { x: 100, y: 101 },
      { x: 110, y: 0 },
      { x: 140, y: 0 },
    ];
    const curves = smoothPath(points).match(/C[^MC]+/g)!;
    expect(curves).toHaveLength(points.length - 1);
    curves.forEach((curve, index) => {
      const [x1, y1, x2, y2, x3, y3] = curve
        .slice(1)
        .trim()
        .split(/\s+/)
        .map(Number);
      expect(x3).toBe(points[index + 1].x);
      expect(y3).toBe(points[index + 1].y);
      for (let step = 0; step <= 100; step++) {
        const t = step / 100;
        const u = 1 - t;
        const y =
          u ** 3 * points[index].y +
          3 * u ** 2 * t * y1 +
          3 * u * t ** 2 * y2 +
          t ** 3 * y3;
        const x =
          u ** 3 * points[index].x +
          3 * u ** 2 * t * x1 +
          3 * u * t ** 2 * x2 +
          t ** 3 * x3;
        expect(y).toBeGreaterThanOrEqual(Math.min(points[index].y, y3) - 0.002);
        expect(y).toBeLessThanOrEqual(Math.max(points[index].y, y3) + 0.002);
        expect(x).toBeGreaterThanOrEqual(points[index].x - 0.002);
        expect(x).toBeLessThanOrEqual(x3 + 0.002);
      }
    });
  });
  it("handles empty and isolated readings", () => {
    expect(smoothPath([])).toBe("");
    expect(smoothPath([{ x: 5, y: 8 }])).toBe("M5.000 8.000 ");
  });
});
