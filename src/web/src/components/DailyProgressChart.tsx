import type { BurnActivity, ConsumptionEntry, PlannedMealTask } from '../api/types'

// A dependency-free inline-SVG line chart of cumulative net calories (fuel − burn − kindle) across the
// hours of a single day. Two running-net trajectories share one zero baseline:
//   - EXPECTED (the plan's intent): cumulative PLANNED meal calories scheduled at/before each hour, minus
//     the evenly-spread Kindle baseline. Drawn brand-muted/dashed.
//   - ACTUAL (what really happened): cumulative logged fuel minus burn at/before each hour, minus the same
//     evenly-spread Kindle. Drawn in the brand primary (the line that matters most).
// Kindle is the passive baseline: its contribution at hour h is kindleCost·(h/24) for BOTH lines.

const W = 320
const H = 170
// Extra left padding makes room for the rotated "Calories" axis title plus the y-axis kcal labels
// (e.g. "−1000"): ~12px for the title, the rest for the numbers.
const PAD_LEFT = 46
const PAD_RIGHT = 8
const PAD_TOP = 14
const PAD_BOTTOM = 24

const ACTUAL = 'var(--primary)'
const EXPECTED = 'var(--muted)'

// Candidate "nice" kcal increments for the y-axis step, smallest first.
const NICE_STEPS = [25, 50, 100, 250, 500, 1000, 2000, 5000]

// Pick a round y-axis step: the largest "nice" increment that still fits inside the data's range
// (step ≤ maxAbs), so we always get at least one labelled tick at ±step. Falls back to the smallest
// increment for very small ranges.
function niceStep(maxAbs: number): number {
  const fitting = NICE_STEPS.filter((s) => s <= maxAbs)
  return fitting.length ? fitting[fitting.length - 1] : NICE_STEPS[0]
}

// Hour ticks to label along the x-axis.
const TICKS: { hour: number; label: string }[] = [
  { hour: 0, label: '12a' },
  { hour: 6, label: '6a' },
  { hour: 12, label: '12p' },
  { hour: 18, label: '6p' },
  { hour: 24, label: '12a' },
]

function caloriesOf(nutrition: { nutrientId: string; value: number }[]): number {
  return nutrition.find((n) => n.nutrientId === 'calories')?.value ?? 0
}

// Hour-of-day (0–24) for a planned meal: its scheduled time, or end-of-day (24) when "Anytime" — an
// untimed meal can be eaten any time, so it pulls the expected line up only at the very end of the day.
function plannedHour(t: PlannedMealTask): number {
  if (!t.time) return 24
  const [h, m, s] = t.time.split(':').map(Number)
  return ((h || 0) * 3600 + (m || 0) * 60 + (s || 0)) / 3600
}

// Hour-of-day for a burn activity: its user-specified time, else its createdAt's local time-of-day.
function burnHour(a: BurnActivity): number {
  if (a.time) {
    const [h, m, s] = a.time.split(':').map(Number)
    return ((h || 0) * 3600 + (m || 0) * 60 + (s || 0)) / 3600
  }
  const d = new Date(a.createdAt)
  return (d.getHours() * 3600 + d.getMinutes() * 60 + d.getSeconds()) / 3600
}

// Hour-of-day for a consumption entry, from its createdAt's local time-of-day.
function entryHour(e: ConsumptionEntry): number {
  const d = new Date(e.createdAt)
  return (d.getHours() * 3600 + d.getMinutes() * 60 + d.getSeconds()) / 3600
}

// Build a cumulative running-net series sampled at every whole hour 0..maxHour. `events` are calorie
// deltas placed at an hour (positive = fuel, negative = burn); each hour's value sums all events at/before
// that hour, then subtracts the evenly-spread Kindle (kindleCost·hour/24).
function buildSeries(
  events: { hour: number; delta: number }[],
  kindleCost: number,
  maxHour: number,
): { hour: number; net: number }[] {
  const sorted = [...events].sort((a, b) => a.hour - b.hour)
  const pts: { hour: number; net: number }[] = []
  for (let h = 0; h <= maxHour; h++) {
    let fuelBurn = 0
    for (const e of sorted) {
      if (e.hour <= h) fuelBurn += e.delta
      else break
    }
    pts.push({ hour: h, net: fuelBurn - kindleCost * (h / 24) })
  }
  return pts
}

export interface DailyProgressChartProps {
  tasks: PlannedMealTask[]
  entries: ConsumptionEntry[]
  activities: BurnActivity[]
  kindleCost: number | undefined
  // Hour (0–24) the ACTUAL line should extend to: 24 for a past day, the current hour for today.
  actualMaxHour: number
}

export function DailyProgressChart(
  { tasks, entries, activities, kindleCost, actualMaxHour }: DailyProgressChartProps,
) {
  const kindle = kindleCost ?? 0

  // EXPECTED: planned, non-skipped meals (a skip means it won't be eaten, so it's excluded), placed at
  // their scheduled hour (Anytime → end-of-day). Spans the full day (0..24).
  const expectedEvents = tasks
    .filter((t) => !t.skipped)
    .map((t) => ({ hour: plannedHour(t), delta: caloriesOf(t.nutrition) }))
  const expected = buildSeries(expectedEvents, kindle, 24)

  // ACTUAL: all consumption entries (planned-completed and ad-hoc) as fuel, all burn activities as a
  // negative delta. Skipped meals are already excluded from entries server-side. Spans 0..actualMaxHour.
  const actualEvents = [
    ...entries.map((e) => ({ hour: entryHour(e), delta: caloriesOf(e.nutrition) })),
    ...activities.map((a) => ({ hour: burnHour(a), delta: -a.caloriesBurned })),
  ]
  const maxHour = Math.max(0, Math.min(24, Math.round(actualMaxHour)))
  const actual = buildSeries(actualEvents, kindle, maxHour)

  // Symmetric vertical scale around the zero baseline, covering both series.
  const allNets = [...expected.map((p) => p.net), ...actual.map((p) => p.net)]
  const maxAbs = Math.max(1, ...allNets.map(Math.abs))

  const plotW = W - PAD_LEFT - PAD_RIGHT
  const plotH = H - PAD_TOP - PAD_BOTTOM
  const zeroY = PAD_TOP + plotH / 2
  const scale = plotH / 2 / maxAbs

  const x = (hour: number) => PAD_LEFT + (hour / 24) * plotW
  const y = (net: number) => zeroY - net * scale

  // Build a smooth SVG path through the series using a Catmull-Rom spline expressed as cubic Béziers.
  // The curve passes exactly through every data point; control points are derived from neighbouring
  // points, so the shape still faithfully reflects the underlying hourly values (no invented peaks).
  const toSmoothPath = (pts: { hour: number; net: number }[]): string => {
    const p = pts.map((pt) => ({ x: x(pt.hour), y: y(pt.net) }))
    if (p.length === 0) return ''
    if (p.length === 1) return `M ${p[0].x} ${p[0].y}`
    let d = `M ${p[0].x} ${p[0].y}`
    for (let i = 0; i < p.length - 1; i++) {
      // Catmull-Rom needs a point on either side of each segment; clamp at the ends.
      const p0 = p[i - 1] ?? p[i]
      const p1 = p[i]
      const p2 = p[i + 1]
      const p3 = p[i + 2] ?? p2
      // Convert the Catmull-Rom segment p1→p2 to a cubic Bézier (standard 1/6 tangent form).
      const c1x = p1.x + (p2.x - p0.x) / 6
      const c1y = p1.y + (p2.y - p0.y) / 6
      const c2x = p2.x - (p3.x - p1.x) / 6
      const c2y = p2.y - (p3.y - p1.y) / 6
      d += ` C ${c1x} ${c1y}, ${c2x} ${c2y}, ${p2.x} ${p2.y}`
    }
    return d
  }

  // Y-axis ticks: a round step with gridlines at 0, ±step, and ±2·step (the outermost pair only when
  // it stays inside the plot area). Magnitudes are labelled in kcal on the left.
  const step = niceStep(maxAbs)
  // Only keep ticks that land inside the symmetric ±maxAbs plot range so labels stay within the chart.
  const yTicks = [-2, -1, 0, 1, 2].map((m) => m * step).filter((net) => Math.abs(net) <= maxAbs)

  const expectedEnd = expected[expected.length - 1]
  const actualEnd = actual[actual.length - 1]

  return (
    <svg
      width="100%"
      viewBox={`0 0 ${W} ${H}`}
      role="img"
      aria-label="Fuel Gauge: cumulative net calories by hour of day, actual versus expected"
      style={{ display: 'block' }}
    >
      {/* Y-axis title — rotated "Calories", centred on the zero baseline at the far left. */}
      <text
        x={9}
        y={zeroY}
        transform={`rotate(-90 9 ${zeroY})`}
        textAnchor="middle"
        fontSize={9}
        fill="var(--muted)"
      >
        Calories
      </text>

      {/* Y-axis gridlines + kcal labels. The zero baseline is drawn slightly stronger than the rest. */}
      {yTicks.map((net) => {
        const isZero = net === 0
        return (
          <g key={net}>
            <line
              x1={PAD_LEFT}
              y1={y(net)}
              x2={W - PAD_RIGHT}
              y2={y(net)}
              stroke="var(--border)"
              strokeWidth={isZero ? 1 : 0.75}
              strokeDasharray={isZero ? undefined : '2 3'}
              opacity={isZero ? 1 : 0.6}
            />
            <text x={PAD_LEFT - 5} y={y(net) + 3} textAnchor="end" fontSize={8} fill="var(--muted)">
              {net > 0 ? `+${net}` : net}
            </text>
          </g>
        )
      })}

      {/* Hour ticks + labels */}
      {TICKS.map((t) => (
        <g key={t.hour}>
          <line x1={x(t.hour)} y1={H - PAD_BOTTOM} x2={x(t.hour)} y2={H - PAD_BOTTOM + 3} stroke="var(--border)" strokeWidth={1} />
          <text x={x(t.hour)} y={H - 12} textAnchor="middle" fontSize={9} fill="var(--muted)">
            {t.label}
          </text>
        </g>
      ))}

      {/* Expected trajectory — the plan's intent (muted, dashed, smoothed). */}
      <path d={toSmoothPath(expected)} fill="none" stroke={EXPECTED} strokeWidth={1.5} strokeDasharray="4 3" />
      {expectedEnd && (
        <circle cx={x(expectedEnd.hour)} cy={y(expectedEnd.net)} r={2.5} fill={EXPECTED}>
          <title>{`Expected by end of day: net ${Math.round(expectedEnd.net)} kcal`}</title>
        </circle>
      )}

      {/* Actual trajectory — what really happened (brand primary, solid, smoothed). */}
      <path d={toSmoothPath(actual)} fill="none" stroke={ACTUAL} strokeWidth={2} />
      {actualEnd && (
        <circle cx={x(actualEnd.hour)} cy={y(actualEnd.net)} r={3} fill={ACTUAL}>
          <title>{`Actual so far (hour ${actualEnd.hour}): net ${Math.round(actualEnd.net)} kcal`}</title>
        </circle>
      )}

      {/* Legend */}
      <g fontSize={9} fill="var(--muted)">
        <line x1={PAD_LEFT} y1={PAD_TOP - 6} x2={PAD_LEFT + 16} y2={PAD_TOP - 6} stroke={ACTUAL} strokeWidth={2} />
        <text x={PAD_LEFT + 20} y={PAD_TOP - 3}>Actual</text>
        <line x1={PAD_LEFT + 64} y1={PAD_TOP - 6} x2={PAD_LEFT + 80} y2={PAD_TOP - 6} stroke={EXPECTED} strokeWidth={1.5} strokeDasharray="4 3" />
        <text x={PAD_LEFT + 84} y={PAD_TOP - 3}>Expected</text>
      </g>
    </svg>
  )
}
