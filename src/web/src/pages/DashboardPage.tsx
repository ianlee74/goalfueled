import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { consumptionApi, dashboardApi, goalApi, weightApi } from '../api/services'
import { WEIGHT_UNIT_LABELS, type Dashboard, type Goal, type UnitsPreference, type UpcomingMeal, type WeightUnit } from '../api/types'
import { NetBalanceChart } from '../components/NetBalanceChart'
import { goalUnitLabel } from '../lib/goalUnit'
import { convertWeight, formatWeight, roundWeight, weightUnitFor } from '../lib/weight'

// How many active goals to surface on the dashboard before showing a "View all" link.
const GOALS_PREVIEW_LIMIT = 4

// Today's calendar date as "yyyy-MM-dd" in the user's local time.
function todayIso(): string {
  const now = new Date()
  const offset = now.getTimezoneOffset() * 60_000
  return new Date(now.getTime() - offset).toISOString().slice(0, 10)
}

function round(n: number): string {
  return Number.isInteger(n) ? String(n) : n.toFixed(1)
}

// "08:30:00" → "8:30 AM"; blank/undefined → "Anytime".
function formatTime(t?: string): string {
  if (!t) return 'Anytime'
  const [hStr, m] = t.split(':')
  const h = Number(hStr)
  const period = h < 12 ? 'AM' : 'PM'
  const h12 = h % 12 === 0 ? 12 : h % 12
  return `${h12}:${m} ${period}`
}

// "yyyy-MM-dd" → "Mon" for an occurrence on a later day.
function weekdayLabel(iso: string): string {
  const [y, m, d] = iso.split('-').map(Number)
  return new Date(y, m - 1, d).toLocaleDateString(undefined, { weekday: 'short' })
}

export function DashboardPage() {
  const { profile } = useAuth()
  const preference = profile?.unitsPreference ?? 'UsImperial'
  const navigate = useNavigate()
  const today = todayIso()

  const [data, setData] = useState<Dashboard | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [completingRef, setCompletingRef] = useState<string | null>(null)

  // Quick-log-weight form (only shown when a measurement is due today and not yet recorded).
  const [weightValue, setWeightValue] = useState('')
  // Default the quick-log unit to the user's preferred weight unit.
  const [weightUnit, setWeightUnit] = useState<WeightUnit>(() => weightUnitFor(preference))
  const [savingWeight, setSavingWeight] = useState(false)

  // Goals summary — fetched independently of the dashboard payload so a goals failure never blocks the
  // rest of the page (and vice versa). Each goal already carries a computed `progress` snapshot.
  const [goals, setGoals] = useState<Goal[] | null>(null)
  const [goalsError, setGoalsError] = useState<string | null>(null)

  // Reload the dashboard (used by action handlers after a mutation). The setState calls live in the
  // promise callbacks so they don't run synchronously in any effect body (avoids cascading renders).
  const reload = useCallback(() =>
    dashboardApi.get()
      .then((dash) => {
        setData(dash)
        // Default the quick-log unit to the user's preferred weight unit.
        setWeightUnit(weightUnitFor(preference))
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load your dashboard.'))
      .finally(() => setLoading(false)),
  [preference])

  // Load the dashboard once on mount.
  useEffect(() => {
    let cancelled = false
    dashboardApi.get()
      .then((dash) => {
        if (cancelled) return
        setData(dash)
        setWeightUnit(weightUnitFor(preference))
      })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load your dashboard.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
    // Runs once on mount; the preferred unit is read for the initial default only.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Load the user's goals once on mount (separate from the dashboard load so neither blocks the other).
  useEffect(() => {
    let cancelled = false
    goalApi.list()
      .then((g) => { if (!cancelled) setGoals(g) })
      .catch((e) => { if (!cancelled) setGoalsError(e instanceof Error ? e.message : 'Failed to load your goals.') })
    return () => { cancelled = true }
  }, [])

  async function completeMeal(meal: UpcomingMeal) {
    setError(null)
    setCompletingRef(meal.ref)
    try {
      await consumptionApi.completePlanned({ ref: meal.ref, date: meal.date })
      await reload()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to mark the meal complete.')
    } finally {
      setCompletingRef(null)
    }
  }

  async function logWeight() {
    setError(null)
    const num = Number(weightValue)
    if (!(num > 0)) { setError('Enter a weight greater than zero.'); return }
    setSavingWeight(true)
    try {
      await weightApi.record({ value: num, unit: weightUnit, date: today })
      setWeightValue('')
      await reload()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to record weight.')
    } finally {
      setSavingWeight(false)
    }
  }

  if (loading) return <p className="muted">Loading your dashboard…</p>

  const firstName = (profile?.displayName ?? '').split(' ')[0]

  return (
    <div className="stack">
      <div className="row spread">
        <h1>{firstName ? `Welcome back, ${firstName}` : 'Your dashboard'} 🚀</h1>
      </div>

      {error && <div className="notice error">{error}</div>}

      {/* Weight-due prompt: only when a measurement is due today. */}
      {data?.weightDue && (
        <WeightDueCard
          recorded={data.weightDue.alreadyRecorded}
          recordedValue={data.weightDue.recordedValue}
          recordedUnit={data.weightDue.recordedUnit}
          preference={preference}
          value={weightValue}
          unit={weightUnit}
          saving={savingWeight}
          onValue={setWeightValue}
          onUnit={setWeightUnit}
          onSubmit={logWeight}
        />
      )}

      {/* Upcoming meals. */}
      <div className="card stack">
        <h2>Up next 🍽️</h2>
        {!data?.activePlanId ? (
          <p className="muted small" style={{ margin: 0 }}>
            You're not following a meal plan yet.{' '}
            <Link to="/log">Pick one in The Log</Link> to see your upcoming meals here.
          </p>
        ) : data.upcomingMeals.length === 0 ? (
          <p className="muted small" style={{ margin: 0 }}>
            Nothing scheduled coming up. Enjoy the breather! 🎉
          </p>
        ) : (
          <div className="list">
            {data.upcomingMeals.map((m) => (
              <div key={`${m.ref}:${m.date}`} className="card ingredient-item">
                <div className="fb-icon fb-fuel" aria-hidden="true">🍽️</div>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div className="row spread">
                    <strong>{m.mealName ?? 'Meal'}</strong>
                    <span className="small muted" style={{ whiteSpace: 'nowrap' }}>
                      {m.isLaterDay && `${weekdayLabel(m.date)} · `}{formatTime(m.time)}
                    </span>
                  </div>
                  <div className="small muted">{round(m.calories)} kcal</div>
                </div>
                {m.isLaterDay ? (
                  <span className="badge" title="Scheduled for a later day">later</span>
                ) : (
                  <button
                    className="btn btn-sm btn-primary"
                    disabled={completingRef === m.ref}
                    onClick={() => completeMeal(m)}
                  >
                    {completingRef === m.ref ? '…' : 'Done'}
                  </button>
                )}
              </div>
            ))}
          </div>
        )}
        <Link to="/log" className="small">Open The Log →</Link>
      </div>

      {/* Goals summary. */}
      <div className="card stack">
        <h2>Your goals 🎯</h2>
        <GoalsSummary goals={goals} error={goalsError} preference={preference} />
      </div>

      {/* 7-day progress chart + weight-change estimate. */}
      <div className="card stack">
        <h2>Past 7 days 📈</h2>
        {data && <ProgressCard data={data} preference={preference} onNavigate={() => navigate('/profile')} />}
      </div>
    </div>
  )
}

type GoalTone = 'positive' | 'negative' | 'neutral'

// Mirror GoalProgressVisual's tone/emoji logic so the dashboard's compact rows match the full GoalsPage.
function goalTone(goal: Goal): GoalTone {
  const p = goal.progress
  if (p.status === 'NoData') return 'neutral'
  if (p.status === 'Missed') return 'negative'
  // Achieved or in-progress: positive unless the outcome flag says otherwise (e.g. over budget).
  return p.outcomePositive ? 'positive' : 'negative'
}

function goalEmoji(goal: Goal): string {
  const p = goal.progress
  if (p.status === 'NoData') return '📊'
  if (p.status === 'Achieved') return p.outcomePositive ? '🎉' : '⚠️'
  if (p.status === 'Missed') return '😞'
  return p.outcomePositive ? '🚀' : '⚠️'
}

const GOAL_TONE_COLOR: Record<GoalTone, string> = {
  positive: '#16a34a',
  negative: '#dc2626',
  neutral: '#6b7280',
}

function goalNum(v: number): string {
  return Math.round(v * 10) / 10 + ''
}

function GoalsSummary({ goals, error, preference }: { goals: Goal[] | null; error: string | null; preference: UnitsPreference }) {
  if (error) {
    return <p className="muted small" style={{ margin: 0 }}>{error}</p>
  }
  if (goals === null) {
    return <p className="muted small" style={{ margin: 0 }}>Loading your goals…</p>
  }

  // "Active" = currently worth nudging the user about. Recurring goals are always active. One-time goals
  // are active until their deadline passes unmet — i.e. we drop only one-time goals whose status is
  // 'Missed' (deadline gone, target not hit) to keep the motivational list uncluttered. Everything else
  // (InProgress, Achieved, NoData, and any still-running recurring goal) stays.
  const active = goals.filter((g) => !(g.cadence === 'OneTime' && g.progress.status === 'Missed'))

  if (goals.length === 0) {
    return (
      <p className="muted small" style={{ margin: 0 }}>
        You haven't set any goals yet. <Link to="/goals">Set a goal</Link> and we'll cheer you on toward it right here. 🎯
      </p>
    )
  }

  if (active.length === 0) {
    // Goals exist but none are currently active (e.g. all one-time deadlines passed).
    return (
      <p className="muted small" style={{ margin: 0 }}>
        No active goals right now. Fresh start? <Link to="/goals">Set a new goal</Link> and let's chase it together. 💪
      </p>
    )
  }

  const shown = active.slice(0, GOALS_PREVIEW_LIMIT)
  const hasMore = active.length > shown.length

  return (
    <>
      <div className="list">
        {shown.map((g) => <GoalRow key={g.id} goal={g} preference={preference} />)}
      </div>
      {hasMore ? (
        <Link to="/goals" className="small">View all {active.length} goals →</Link>
      ) : (
        <Link to="/goals" className="small">Open your goals →</Link>
      )}
    </>
  )
}

function GoalRow({ goal, preference }: { goal: Goal; preference: UnitsPreference }) {
  const p = goal.progress
  const tone = goalTone(goal)
  const color = GOAL_TONE_COLOR[tone]
  const pct = Math.max(0, Math.min(100, p.percent))
  const rounded = Math.round(pct)

  return (
    <div className="card ingredient-item">
      <div className="fb-icon" aria-hidden="true" style={{ fontSize: '1.3rem' }}>{goalEmoji(goal)}</div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div className="row spread" style={{ gap: '0.5rem' }}>
          <strong style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{goal.name}</strong>
          <span className="small" style={{ color, fontWeight: 700, whiteSpace: 'nowrap' }}>{rounded}%</span>
        </div>
        {/* Accessible horizontal progress bar tinted with the same tone as the full GoalsPage ring. */}
        <div
          role="progressbar"
          aria-valuenow={rounded}
          aria-valuemin={0}
          aria-valuemax={100}
          aria-label={`${goal.name} progress`}
          style={{
            height: 8, borderRadius: 999, background: 'rgba(0,0,0,0.08)',
            overflow: 'hidden', margin: '0.35rem 0',
          }}
        >
          <div style={{ width: `${pct}%`, height: '100%', background: color, borderRadius: 999, transition: 'width 0.6s ease' }} />
        </div>
        <div className="small" style={{ color, fontWeight: 600 }}>{p.message}</div>
        {p.status !== 'NoData' && (
          <div className="small muted">
            {goalNum(p.currentValue)} / {goalNum(p.targetValue)} {goalUnitLabel(goal, preference)} · {p.periodLabel}
          </div>
        )}
      </div>
    </div>
  )
}

function ProgressCard({ data, preference, onNavigate }: { data: Dashboard; preference: UnitsPreference; onNavigate: () => void }) {
  const p = data.progress
  if (!p.hasData) {
    return (
      <p className="muted small" style={{ margin: 0 }}>
        Nothing logged yet this week. Log a meal or some exercise in{' '}
        <Link to="/log">The Log</Link> and your progress will show up here.
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
  const tone = isLoss ? 'var(--primary-dark)' : isGain ? 'var(--accent)' : 'var(--muted)'
  const headline = isLoss
    ? `▼ ${round(Math.abs(lost))} ${unit} estimated loss`
    : isGain
      ? `▲ ${round(Math.abs(lost))} ${unit} estimated gain`
      : 'Holding steady'

  return (
    <div className="stack">
      <div className="row spread" style={{ alignItems: 'baseline' }}>
        <strong style={{ color: tone, fontSize: '1.15rem' }}>{headline}</strong>
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
          <button className="btn btn-sm" onClick={onNavigate} style={{ padding: '0 0.3rem' }}>profile</button>{' '}
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

function WeightDueCard(props: {
  recorded: boolean
  recordedValue?: number
  recordedUnit?: WeightUnit
  preference: UnitsPreference
  value: string
  unit: WeightUnit
  saving: boolean
  onValue: (v: string) => void
  onUnit: (u: WeightUnit) => void
  onSubmit: () => void
}) {
  if (props.recorded) {
    return (
      <div className="notice info">
        ⚖️ Weight logged today
        {props.recordedValue != null && props.recordedUnit
          ? `: ${formatWeight(props.recordedValue, props.recordedUnit, props.preference)}`
          : ''}
        . Nice work staying on top of it!
      </div>
    )
  }
  return (
    <div className="card stack" style={{ borderLeft: '3px solid var(--accent)' }}>
      <h2>⚖️ Weigh-in due today</h2>
      <div className="row" style={{ gap: '0.5rem', alignItems: 'flex-end' }}>
        <label className="field" style={{ flex: 1 }}>
          Weight
          <input
            className="input"
            type="number"
            min="0"
            step="any"
            value={props.value}
            placeholder="e.g. 180"
            onChange={(e) => props.onValue(e.target.value)}
          />
        </label>
        <label className="field">
          Unit
          <select className="select" value={props.unit} onChange={(e) => props.onUnit(e.target.value as WeightUnit)}>
            <option value="Pound">{WEIGHT_UNIT_LABELS.Pound}</option>
            <option value="Kilogram">{WEIGHT_UNIT_LABELS.Kilogram}</option>
          </select>
        </label>
        <button className="btn btn-primary" disabled={props.saving} onClick={props.onSubmit}>
          {props.saving ? 'Saving…' : 'Log'}
        </button>
      </div>
    </div>
  )
}
