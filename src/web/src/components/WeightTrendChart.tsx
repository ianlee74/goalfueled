import { useMemo } from 'react'
import type { UnitsPreference, WeightEntry } from '../api/types'
import { WEIGHT_UNIT_LABELS } from '../api/types'
import { convertWeight, weightUnitFor } from '../lib/weight'

/**
 * Weight trend line chart (DESIGN.md §3.2): entries over time in the user's preferred unit,
 * with an optional dashed target line from a weight goal. Pure SVG; theme-token colors.
 */

const W = 560
const H = 200
const PAD = { left: 44, right: 14, top: 14, bottom: 26 }

interface Props {
  entries: WeightEntry[]
  preference: UnitsPreference
  /** Optional goal target, already in the user's preferred unit. */
  target?: number
}

export function WeightTrendChart({ entries, preference, target }: Props) {
  const unit = weightUnitFor(preference)
  const unitLabel = WEIGHT_UNIT_LABELS[unit]

  const points = useMemo(() => (
    [...entries]
      .sort((a, b) => (a.date < b.date ? -1 : 1))
      .map((e) => ({ date: e.date, value: convertWeight(e.value, e.unit, unit) }))
  ), [entries, unit])

  if (points.length < 2) {
    return (
      <p className="muted small" style={{ margin: 0 }}>
        Log a couple of weigh-ins and your trend will draw itself here.
      </p>
    )
  }

  const t0 = Date.parse(points[0].date)
  const t1 = Date.parse(points[points.length - 1].date)
  const span = Math.max(1, t1 - t0)
  const x = (date: string) => PAD.left + ((Date.parse(date) - t0) / span) * (W - PAD.left - PAD.right)

  const values = points.map((p) => p.value)
  let lo = Math.min(...values, ...(target != null ? [target] : []))
  let hi = Math.max(...values, ...(target != null ? [target] : []))
  const padY = Math.max(1, (hi - lo) * 0.12)
  lo -= padY
  hi += padY
  const y = (v: number) => PAD.top + (1 - (v - lo) / (hi - lo)) * (H - PAD.top - PAD.bottom)

  // ~4 "nice" horizontal gridlines.
  const rawStep = (hi - lo) / 4
  const mag = 10 ** Math.floor(Math.log10(rawStep))
  const step = [1, 2, 2.5, 5, 10].map((m) => m * mag).find((s) => s >= rawStep) ?? rawStep
  const ticks: number[] = []
  for (let v = Math.ceil(lo / step) * step; v <= hi; v += step) ticks.push(v)

  const line = points.map((p, i) => `${i === 0 ? 'M' : 'L'}${x(p.date).toFixed(1)} ${y(p.value).toFixed(1)}`).join(' ')

  // First / middle / last date labels keep the axis quiet.
  const labelIdx = [0, Math.floor((points.length - 1) / 2), points.length - 1]
  const fmt = (d: string) => {
    const dt = new Date(d + 'T00:00:00')
    return `${dt.getMonth() + 1}/${dt.getDate()}`
  }

  return (
    <svg className="chart" viewBox={`0 0 ${W} ${H}`} role="img"
      aria-label={`Weight trend from ${points[0].value.toFixed(1)} to ${points[points.length - 1].value.toFixed(1)} ${unitLabel}`}>
      {/* Gridlines + y labels */}
      {ticks.map((v) => (
        <g key={v}>
          <line x1={PAD.left} y1={y(v)} x2={W - PAD.right} y2={y(v)} stroke="var(--border)" strokeWidth={1} />
          <text x={PAD.left - 6} y={y(v) + 3} textAnchor="end">{Math.round(v)}</text>
        </g>
      ))}
      {/* Target */}
      {target != null && (
        <g>
          <line x1={PAD.left} y1={y(target)} x2={W - PAD.right} y2={y(target)}
            stroke="var(--primary)" strokeWidth={1.5} strokeDasharray="5 4" />
          <text x={W - PAD.right} y={y(target) - 5} textAnchor="end"
            style={{ fill: 'var(--primary)' }}>target {Math.round(target)}</text>
        </g>
      )}
      {/* Trend line + dots */}
      <path d={line} fill="none" stroke="var(--text)" strokeWidth={2} />
      {points.map((p) => (
        <circle key={p.date} cx={x(p.date)} cy={y(p.value)} r={2.6} fill="var(--text)" />
      ))}
      {/* X labels */}
      {labelIdx.map((i) => (
        <text key={i} x={x(points[i].date)} y={H - 8}
          textAnchor={i === 0 ? 'start' : i === points.length - 1 ? 'end' : 'middle'}>
          {fmt(points[i].date)}
        </text>
      ))}
    </svg>
  )
}
