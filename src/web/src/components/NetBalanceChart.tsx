import type { ProgressDay } from '../api/types'

// A dependency-free inline SVG chart of the 7-day net calorie balance (fuel − burn − kindle). Each day is
// a bar above (surplus) or below (deficit) a zero baseline, with a line tracing the running cumulative net.
// Emerald = deficit (progress toward loss), flame-orange = surplus — mirroring the brand's fuel/burn colors.

const W = 320
const H = 160
const PAD_X = 8
const PAD_TOP = 14
const PAD_BOTTOM = 22

const DEFICIT = 'var(--primary)'
const SURPLUS = 'var(--accent)'
const LINE = 'var(--muted)'

function shortDay(iso: string): string {
  // "yyyy-MM-dd" → weekday initial. Parse as local midnight to avoid TZ drift.
  const [y, m, d] = iso.split('-').map(Number)
  const date = new Date(y, m - 1, d)
  return ['S', 'M', 'T', 'W', 'T', 'F', 'S'][date.getDay()]
}

export function NetBalanceChart({ days }: { days: ProgressDay[] }) {
  if (days.length === 0) return null

  const nets = days.map((d) => d.net)
  // Running cumulative net, for the trend line.
  const cumulative: number[] = []
  nets.reduce((acc, n, i) => (cumulative[i] = acc + n), 0)

  const maxAbs = Math.max(1, ...nets.map(Math.abs), ...cumulative.map(Math.abs))
  const plotW = W - PAD_X * 2
  const plotH = H - PAD_TOP - PAD_BOTTOM
  const zeroY = PAD_TOP + plotH / 2
  const scale = plotH / 2 / maxAbs

  const slot = plotW / days.length
  const barW = Math.min(28, slot * 0.6)

  const linePts = cumulative
    .map((c, i) => `${PAD_X + slot * i + slot / 2},${zeroY - c * scale}`)
    .join(' ')

  return (
    <svg
      width="100%"
      viewBox={`0 0 ${W} ${H}`}
      role="img"
      aria-label="Net calorie balance over the past 7 days"
      style={{ display: 'block' }}
    >
      {/* Zero baseline */}
      <line x1={PAD_X} y1={zeroY} x2={W - PAD_X} y2={zeroY} stroke="var(--border)" strokeWidth={1} />

      {days.map((d, i) => {
        const cx = PAD_X + slot * i + slot / 2
        const h = Math.abs(d.net) * scale
        const isDeficit = d.net <= 0
        const y = isDeficit ? zeroY : zeroY - h
        return (
          <g key={d.date}>
            <rect
              x={cx - barW / 2}
              y={y}
              width={barW}
              height={Math.max(1, h)}
              rx={3}
              fill={isDeficit ? DEFICIT : SURPLUS}
              opacity={0.85}
            >
              <title>{`${d.date}: net ${Math.round(d.net)} kcal`}</title>
            </rect>
            <text x={cx} y={H - 6} textAnchor="middle" fontSize={10} fill="var(--muted)">
              {shortDay(d.date)}
            </text>
          </g>
        )
      })}

      {/* Cumulative trend line */}
      <polyline points={linePts} fill="none" stroke={LINE} strokeWidth={1.5} strokeDasharray="4 3" />
      {cumulative.map((c, i) => (
        <circle
          key={i}
          cx={PAD_X + slot * i + slot / 2}
          cy={zeroY - c * scale}
          r={2}
          fill={LINE}
        />
      ))}
    </svg>
  )
}
