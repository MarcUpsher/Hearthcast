export interface PathPoint {
  x: number;
  y: number;
}

// Monotone cubic Hermite interpolation: shared tangents soften corners without
// inventing peaks or troughs between readings. Each call represents one gap-free segment.
export function smoothPath(points: readonly PathPoint[]): string {
  if (!points.length) return "";
  const coordinate = (value: number) => value.toFixed(3);
  const slopes = points
    .slice(1)
    .map(
      (point, index) =>
        (point.y - points[index].y) / (point.x - points[index].x),
    );
  const tangents = points.map((_, index) => {
    if (index === 0) return slopes[0] ?? 0;
    if (index === points.length - 1) return slopes.at(-1)!;
    const before = slopes[index - 1];
    const after = slopes[index];
    if (before * after <= 0) return 0;
    // The harmonic mean bounds each tangent to twice either neighboring slope.
    return 2 / (1 / before + 1 / after);
  });
  let path = `M${coordinate(points[0].x)} ${coordinate(points[0].y)} `;
  points.slice(1).forEach((point, index) => {
    const previous = points[index];
    const third = (point.x - previous.x) / 3;
    path += `C${coordinate(previous.x + third)} ${coordinate(previous.y + third * tangents[index])} ${coordinate(point.x - third)} ${coordinate(point.y - third * tangents[index + 1])} ${coordinate(point.x)} ${coordinate(point.y)} `;
  });
  return path;
}
