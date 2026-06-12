import { Link } from 'react-router-dom'
import { WEIGHT_UNIT_LABELS, type Dashboard, type UnitsPreference } from '../api/types'
import { NetBalanceChart } from './NetBalanceChart'
import { convertWeight, roundWeight, weightUnitFor } from '../lib/weight'

/**
 * The "Past 7 days" summary: estimated weight change headline + the daily net-balance
 * chart. Extracted from the old dashboard; lives on the Goals tab now.
 */

function round(n: number): string {
  return Number.isInteger(n) ? String(n) : n.toFixed(1)
}

export function ProgressSummary(
  { data, preference, onIncompleteProfile }:
  { data: Dashboard; preference: UnitsPreference; onIncompleteProfile: () => void },
) {
  const p = data.progress
  if (!p.hasData) {
    return (
      <p className="muted small" style={{ margin: 0 }}>
        Nothing logged yet this week. Log a meal or some exercise on{' '}
        <Link to="/">Today</Link> and your progress will show up here.
      </p>
    )
  }

  // The estimate arrives in the user's last-used weight unit; convert it to the preferred unit.
  const target = weightUnitFor(preference)
  const lost = roundWeight(convertWeight(p.estimatedWeightLost, p.weightUnit, target))
  const unit = WEIGHT_UNIT_LABELS[target]
  // Deficit → positive "lost"; surplus → negative (a gain).
  const isLoss = lost > 0.0005
  const isGain = lost < -0.0005
  const tone = isLoss ? 'var(--primary-dark)' : isGain ? 'var(--danger)' : 'var(--muted)'
  const headline = isLoss
    ? `▼ ${round(Math.abs(lost))} ${unit} estimated loss`
    : isGain
      ? `▲ ${round(Math.abs(lost))} ${unit} estimated gain`
      : 'Holding steady'

  return (
    <div className="stack">
      <div className="row spread" style={{ alignItems: 'baseline' }}>
        <span className="stat-num" style={{ color: tone, fontSize: '1.4rem' }}>{headline}</span>
        <span className="small muted">
          net {p.cumulativeNet > 0 ? '+' : ''}{round(p.cumulativeNet)} kcal
        </span>
      </div>

      <NetBalanceChart days={p.days} />

      <div className="row" style={{ gap: '1rem', flexWrap: 'wrap' }}>
        <Legend color="var(--primary)" label="deficit (toward loss)" />
        <Legend color="var(--accent)" label="surplus" />
        <Legend color="var(--muted)" label="cumulative net" dashed />
      </div>

      <p className="small muted" style={{ margin: 0 }}>
        Estimated from net calories (fuel − burn − kindle) at ~3,500 kcal per pound. A rough guide, not medical advice.
      </p>

      {!p.kindleAvailable && (
        <div className="notice info small">
          We're showing fuel − burn only. Add your sex, height, birth date and a recent weight in your{' '}
          <button className="btn btn-sm" onClick={onIncompleteProfile} style={{ padding: '0 0.3rem' }}>profile</button>{' '}
          to include your passive (kindle) burn for a better estimate.
        </div>
      )}
    </div>
  )
}

function Legend({ color, label, dashed }: { color: string; label: string; dashed?: boolean }) {
  return (
    <span className="row small muted" style={{ gap: '0.35rem' }}>
      <span
        aria-hidden="true"
        style={{
          display: 'inline-block', width: 14, height: dashed ? 0 : 10,
          borderTop: dashed ? `2px dashed ${color}` : undefined,
          background: dashed ? undefined : color, borderRadius: 2,
        }}
      />
      {label}
    </span>
  )
}
