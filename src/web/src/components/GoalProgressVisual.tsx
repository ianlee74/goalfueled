import type { Goal, UnitsPreference } from '../api/types'
import { goalUnitLabel } from '../lib/goalUnit'

// A lightweight, dependency-free progress visual: an SVG ring whose sweep tracks progress toward the
// target, tinted to celebrate (green) or warn (amber/red) based on the computed outcome, with an
// emoji + message that reacts to whether the final result is positive or negative.

const RADIUS = 54
const CIRCUMFERENCE = 2 * Math.PI * RADIUS

type Tone = 'positive' | 'negative' | 'neutral'

function toneFor(goal: Goal): Tone {
  const p = goal.progress
  if (p.status === 'NoData') return 'neutral'
  if (p.status === 'Missed') return 'negative'
  // Achieved or in-progress: positive unless the outcome flag says otherwise (e.g. over budget).
  return p.outcomePositive ? 'positive' : 'negative'
}

function emojiFor(goal: Goal): string {
  const p = goal.progress
  if (p.status === 'NoData') return '📊'
  if (p.status === 'Achieved') return p.outcomePositive ? '🎉' : '⚠️'
  if (p.status === 'Missed') return '😞'
  return p.outcomePositive ? '🚀' : '⚠️'
}

const TONE_COLOR: Record<Tone, string> = {
  positive: '#16a34a',
  negative: '#dc2626',
  neutral: '#6b7280',
}

export function GoalProgressVisual({ goal, preference }: { goal: Goal; preference: UnitsPreference }) {
  const p = goal.progress
  const tone = toneFor(goal)
  const color = TONE_COLOR[tone]
  const pct = Math.max(0, Math.min(100, p.percent))
  const dash = (pct / 100) * CIRCUMFERENCE
  const celebrate = p.status === 'Achieved' && p.outcomePositive

  return (
    <div className="goal-visual" style={{ textAlign: 'center' }}>
      <div style={{ position: 'relative', width: 140, height: 140, margin: '0 auto' }}>
        <svg width={140} height={140} viewBox="0 0 140 140" role="img"
          aria-label={`Progress ${Math.round(pct)} percent`}>
          <circle cx={70} cy={70} r={RADIUS} fill="none" stroke="rgba(0,0,0,0.08)" strokeWidth={12} />
          <circle
            cx={70} cy={70} r={RADIUS} fill="none" stroke={color} strokeWidth={12}
            strokeLinecap="round"
            strokeDasharray={`${dash} ${CIRCUMFERENCE}`}
            transform="rotate(-90 70 70)"
            style={{ transition: 'stroke-dasharray 0.6s ease' }}
          />
        </svg>
        <div style={{
          position: 'absolute', inset: 0, display: 'flex', flexDirection: 'column',
          alignItems: 'center', justifyContent: 'center',
        }}>
          <span style={{ fontSize: '1.8rem', lineHeight: 1 }} aria-hidden="true">{emojiFor(goal)}</span>
          <strong style={{ fontSize: '1.2rem' }}>{Math.round(pct)}%</strong>
        </div>
      </div>

      {celebrate && (
        <div className="goal-confetti" aria-hidden="true" style={{ fontSize: '1.1rem', letterSpacing: '0.2rem' }}>
          ✨🎊✨
        </div>
      )}

      <p className="small" style={{ margin: '0.5rem 0 0', color, fontWeight: 600 }}>{p.message}</p>
      {p.status !== 'NoData' && (
        <p className="small muted" style={{ margin: '0.25rem 0 0' }}>
          {fmt(p.currentValue)} / {fmt(p.targetValue)} {goalUnitLabel(goal, preference)} · {p.periodLabel}
        </p>
      )}
      {p.basedOnLoggedConsumption && (
        <p className="small muted" style={{ margin: '0.15rem 0 0' }} title="Days you logged use your actual intake; un-logged days fall back to your meal plan.">
          📋 based on logged consumption
        </p>
      )}
    </div>
  )
}

function fmt(v: number): string {
  return Math.round(v * 10) / 10 + ''
}
