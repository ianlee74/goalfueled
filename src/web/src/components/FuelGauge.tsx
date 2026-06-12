/**
 * The hero fuel gauge (DESIGN.md §3.1): a semicircular instrument showing fuel consumed
 * against today's budget, with the remaining kcal as the screen's one big numeral.
 * Pure SVG, no dependencies; colors ride the theme tokens.
 */

const CX = 110
const CY = 120
const R = 92

interface FuelGaugeProps {
  /** kcal of fuel consumed so far. */
  consumed: number
  /** kcal budget for the day (calorie-cap goal target, or maintenance). */
  budget: number
  /** Uppercase label under the numeral, e.g. "kcal to limit". */
  label: string
}

/** Point on the gauge arc for a 0..1 fraction (0 = left end, 1 = right end). */
function arcPoint(f: number, radius: number): { x: number; y: number } {
  const angle = Math.PI * (1 - f)
  return { x: CX + radius * Math.cos(angle), y: CY - radius * Math.sin(angle) }
}

export function FuelGauge({ consumed, budget, label }: FuelGaugeProps) {
  const fraction = budget > 0 ? Math.max(0, Math.min(1, consumed / budget)) : 0
  const over = budget > 0 && consumed > budget
  const remaining = Math.round(Math.abs(budget - consumed))
  const color = over ? 'var(--danger)' : 'var(--primary)'

  const start = arcPoint(0, R)
  const end = arcPoint(1, R)
  const fillEnd = arcPoint(fraction, R)
  // The needle floats just inside the arc (a radial pointer) so the center stays clear
  // for the numeral overlay.
  const needleInner = arcPoint(fraction, R - 34)
  const needleOuter = arcPoint(fraction, R - 12)

  return (
    <div className="fuel-gauge">
      <svg
        viewBox="0 0 220 132"
        role="img"
        aria-label={`${Math.round(consumed)} of ${Math.round(budget)} kcal used — ${remaining} ${label}`}
      >
        {/* Track */}
        <path
          d={`M${start.x} ${start.y} A${R} ${R} 0 0 1 ${end.x} ${end.y}`}
          fill="none" stroke="var(--surface-muted)" strokeWidth={14}
        />
        {/* Fill */}
        {fraction > 0 && (
          <path
            d={`M${start.x} ${start.y} A${R} ${R} 0 0 1 ${fillEnd.x} ${fillEnd.y}`}
            fill="none" stroke={color} strokeWidth={14}
            style={{ transition: 'all 0.5s ease' }}
          />
        )}
        {/* Quarter ticks */}
        {[0, 0.25, 0.5, 0.75, 1].map((f) => {
          const o = arcPoint(f, R + 11)
          const i = arcPoint(f, R + 7)
          return <line key={f} x1={i.x} y1={i.y} x2={o.x} y2={o.y} stroke="var(--border)" strokeWidth={2} />
        })}
        {/* Needle */}
        <line
          x1={needleInner.x} y1={needleInner.y} x2={needleOuter.x} y2={needleOuter.y}
          stroke="var(--text)" strokeWidth={2.5}
          style={{ transition: 'all 0.5s ease' }}
        />
      </svg>
      <div className="fuel-gauge-center">
        <span className="display-num" style={{ color: over ? 'var(--danger)' : undefined }}>
          {remaining.toLocaleString()}
        </span>
        <span className="stat-label">{over ? `${label} · over` : label}</span>
      </div>
    </div>
  )
}
