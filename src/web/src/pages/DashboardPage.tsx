import { useCallback, useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { consumptionApi, dashboardApi, weightApi } from '../api/services'
import { WEIGHT_UNIT_LABELS, type Dashboard, type UpcomingMeal, type WeightUnit } from '../api/types'
import { NetBalanceChart } from '../components/NetBalanceChart'

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
  const navigate = useNavigate()
  const today = todayIso()

  const [data, setData] = useState<Dashboard | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [completingRef, setCompletingRef] = useState<string | null>(null)

  // Quick-log-weight form (only shown when a measurement is due today and not yet recorded).
  const [weightValue, setWeightValue] = useState('')
  const [weightUnit, setWeightUnit] = useState<WeightUnit>('Pound')
  const [savingWeight, setSavingWeight] = useState(false)

  // Reload the dashboard (used by action handlers after a mutation). The setState calls live in the
  // promise callbacks so they don't run synchronously in any effect body (avoids cascading renders).
  const reload = useCallback(() =>
    dashboardApi.get()
      .then((dash) => {
        setData(dash)
        // Default the unit to the one used in the progress series (the user's last-used unit).
        setWeightUnit(dash.progress.weightUnit)
      })
      .catch((e) => setError(e instanceof Error ? e.message : 'Failed to load your dashboard.'))
      .finally(() => setLoading(false)),
  [])

  // Load the dashboard once on mount.
  useEffect(() => {
    let cancelled = false
    dashboardApi.get()
      .then((dash) => {
        if (cancelled) return
        setData(dash)
        setWeightUnit(dash.progress.weightUnit)
      })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load your dashboard.') })
      .finally(() => { if (!cancelled) setLoading(false) })
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

      {/* 7-day progress chart + weight-change estimate. */}
      <div className="card stack">
        <h2>Past 7 days 📈</h2>
        {data && <ProgressCard data={data} onNavigate={() => navigate('/profile')} />}
      </div>
    </div>
  )
}

function ProgressCard({ data, onNavigate }: { data: Dashboard; onNavigate: () => void }) {
  const p = data.progress
  if (!p.hasData) {
    return (
      <p className="muted small" style={{ margin: 0 }}>
        Nothing logged yet this week. Log a meal or some exercise in{' '}
        <Link to="/log">The Log</Link> and your progress will show up here.
      </p>
    )
  }

  const lost = p.estimatedWeightLost
  const unit = WEIGHT_UNIT_LABELS[p.weightUnit]
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
          ? `: ${props.recordedValue} ${WEIGHT_UNIT_LABELS[props.recordedUnit]}`
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
