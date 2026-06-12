import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { dashboardApi, gamificationApi, goalApi, weightApi } from '../api/services'
import { useNutrients } from '../hooks/useNutrients'
import { useAuth } from '../auth/AuthContext'
import { ProgressSummary } from '../components/ProgressSummary'
import { Sheet } from '../components/Sheet'
import { WeightTrendChart } from '../components/WeightTrendChart'
import { IconReview, IconTarget } from '../components/icons'
import { EmptyState, SkeletonList } from '../components/Placeholders'
import {
  GOAL_CADENCE_LABELS, GOAL_METRIC_LABELS,
  type Dashboard, type GamificationSummary, type Goal, type GoalCadence, type GoalInput,
  type GoalMetricType, type UnitsPreference, type WeightEntry,
} from '../api/types'
import { goalUnitLabel } from '../lib/goalUnit'

// Today's calendar date as "yyyy-MM-dd" in the user's local time.
function todayIso(): string {
  const now = new Date()
  const offset = now.getTimezoneOffset() * 60_000
  return new Date(now.getTime() - offset).toISOString().slice(0, 10)
}

export function GoalsPage() {
  const { profile } = useAuth()
  const navigate = useNavigate()
  const preference = profile?.unitsPreference ?? 'UsImperial'
  const [goals, setGoals] = useState<Goal[]>([])
  const [weights, setWeights] = useState<WeightEntry[]>([])
  const [dashboard, setDashboard] = useState<Dashboard | null>(null)
  const [summary, setSummary] = useState<GamificationSummary | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [formOpen, setFormOpen] = useState(false)

  useEffect(() => {
    let cancelled = false
    Promise.all([goalApi.list(), weightApi.list(), dashboardApi.get()])
      .then(([g, w, d]) => { if (!cancelled) { setGoals(g); setWeights(w); setDashboard(d) } })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load goals.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    gamificationApi.summary()
      .then((s) => { if (!cancelled) setSummary(s) })
      .catch(() => {})
    return () => { cancelled = true }
  }, [])

  async function remove(id: string) {
    setError(null)
    try {
      await goalApi.remove(id)
      setGoals((prev) => prev.filter((g) => g.id !== id))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to delete goal.')
    }
  }

  // The dashed target for the trend chart: the first weight goal's target (already in user units).
  const weightGoal = goals.find((g) => g.metricType === 'Weight')

  return (
    <div className="stack">
      <div className="row spread">
        <h1>Goals</h1>
        <button type="button" className="btn btn-primary btn-sm" onClick={() => setFormOpen(true)}>
          + New goal
        </button>
      </div>

      {error && <div className="notice error">{error}</div>}

      {/* Weekly-review entry point (DESIGN.md §3.2): a pending review waits at the top. */}
      {summary?.pendingReviewWeekStart ? (
        <Link to={`/review?weekStart=${summary.pendingReviewWeekStart}`} className="card ingredient-item" style={{ borderLeft: '3px solid var(--accent)' }}>
          <div className="fb-icon" style={{ background: 'var(--accent-soft)', color: 'var(--accent)' }} aria-hidden="true">
            <IconReview size={18} />
          </div>
          <div style={{ flex: 1, minWidth: 0 }}>
            <strong>Your week in fuel is ready</strong>
            <div className="small muted">Review last week and bank the completion bonus.</div>
          </div>
          <span className="muted" aria-hidden="true">→</span>
        </Link>
      ) : summary?.lastReview && (
        <Link to={`/review?weekStart=${summary.lastReview.weekStart}`} className="small">
          Last week scored {summary.lastReview.score} — see the review →
        </Link>
      )}

      {/* Progress first: the goals themselves lead the screen. */}
      {loading && goals.length === 0 && <SkeletonList />}
      {!loading && goals.length === 0 && (
        <EmptyState icon={<IconTarget size={28} />} title="No goals yet">
          Hit <strong>+ New goal</strong> to put a target on the board.
        </EmptyState>
      )}
      <div className="list">
        {goals.map((g) => (
          <GoalCard key={g.id} goal={g} preference={preference} onDelete={() => remove(g.id)} />
        ))}
      </div>

      {/* Weight trend, with the quick path to logging a weigh-in. */}
      <div className="card stack">
        <div className="row spread">
          <h2 style={{ margin: 0 }}>Weight trend</h2>
          <Link className="btn btn-sm" to="/weight">Log weight</Link>
        </div>
        <WeightTrendChart entries={weights} preference={preference} target={weightGoal?.targetValue} />
      </div>

      {/* The week in net calories (moved here from the old dashboard). */}
      <div className="card stack">
        <h2>Past 7 days</h2>
        {dashboard && (
          <ProgressSummary data={dashboard} preference={preference} onIncompleteProfile={() => navigate('/you')} />
        )}
      </div>

      {formOpen && (
        <NewGoalSheet
          onClose={() => setFormOpen(false)}
          onCreated={(g) => { setGoals((prev) => [g, ...prev]); setFormOpen(false) }}
        />
      )}
    </div>
  )
}

// ---- Goal card: linear meter + condensed numeral (DESIGN.md §3.2 — no rings). ----

type GoalTone = 'positive' | 'negative' | 'neutral'

function goalTone(goal: Goal): GoalTone {
  const p = goal.progress
  if (p.status === 'NoData') return 'neutral'
  if (p.status === 'Missed') return 'negative'
  // Achieved or in-progress: positive unless the outcome flag says otherwise (e.g. over budget).
  return p.outcomePositive ? 'positive' : 'negative'
}

const GOAL_TONE_COLOR: Record<GoalTone, string> = {
  positive: 'var(--primary)',
  negative: 'var(--danger)',
  neutral: 'var(--muted)',
}

function num(v: number): string {
  return Math.round(v * 10) / 10 + ''
}

function GoalCard(
  { goal, preference, onDelete }:
  { goal: Goal; preference: UnitsPreference; onDelete: () => void },
) {
  const p = goal.progress
  const color = GOAL_TONE_COLOR[goalTone(goal)]
  const pct = Math.max(0, Math.min(100, p.percent))
  const rounded = Math.round(pct)

  return (
    <div className="card stack" style={{ gap: '0.45rem' }}>
      <div className="row spread" style={{ gap: '0.5rem' }}>
        <strong style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>{goal.name}</strong>
        <span className="stat-num" style={{ color, fontSize: '1.3rem', whiteSpace: 'nowrap' }}>{rounded}%</span>
      </div>
      <div
        className="meter"
        role="progressbar"
        aria-valuenow={rounded}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label={`${goal.name} progress`}
      >
        <div className="meter-fill" style={{ width: `${pct}%`, background: color }} />
      </div>
      <div className="small" style={{ color, fontWeight: 600 }}>{p.message}</div>
      <div className="row spread" style={{ alignItems: 'baseline', gap: '0.5rem' }}>
        <span className="small muted" style={{ minWidth: 0 }}>
          {p.status !== 'NoData' && <>{num(p.currentValue)} / {num(p.targetValue)} {goalUnitLabel(goal, preference)} · </>}
          {goal.cadence === 'OneTime' ? 'One-time' : `Every ${goal.recurrenceDays} day${goal.recurrenceDays === 1 ? '' : 's'}`}
          {' · '}{p.periodLabel}
          {p.basedOnLoggedConsumption && ' · from logged intake'}
        </span>
        <button type="button" className="btn btn-sm" onClick={onDelete}>Delete</button>
      </div>
    </div>
  )
}

// ---- New-goal sheet: the old top-of-page form, now summoned on demand. ----

function NewGoalSheet({ onClose, onCreated }: { onClose: () => void; onCreated: (g: Goal) => void }) {
  const { ordered: nutrients } = useNutrients()
  const [name, setName] = useState('')
  const [metricType, setMetricType] = useState<GoalMetricType>('Weight')
  const [nutrientId, setNutrientId] = useState('calories')
  const [targetValue, setTargetValue] = useState('')
  const [cadence, setCadence] = useState<GoalCadence>('OneTime')
  const [startDate, setStartDate] = useState(todayIso())
  const [targetDate, setTargetDate] = useState('')
  const [recurrenceDays, setRecurrenceDays] = useState('7')
  const [exceedingIsPositive, setExceedingIsPositive] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function create() {
    setError(null)
    const numValue = Number(targetValue)
    if (!name.trim()) { setError('Give your goal a name.'); return }
    if (!(numValue > 0)) { setError('Enter a target value greater than zero.'); return }
    if (cadence === 'OneTime' && !targetDate) { setError('Pick a target date for a one-time goal.'); return }
    if (cadence === 'Recurring' && !(Number(recurrenceDays) >= 1)) {
      setError('Enter a recurrence period of at least one day.'); return
    }

    const input: GoalInput = {
      name: name.trim(),
      metricType,
      targetValue: numValue,
      cadence,
      exceedingIsPositive,
      nutrientId: metricType === 'Nutrient' ? nutrientId : undefined,
      startDate,
      targetDate: cadence === 'OneTime' ? targetDate : undefined,
      recurrenceDays: cadence === 'Recurring' ? Number(recurrenceDays) : undefined,
    }

    setSaving(true)
    try {
      onCreated(await goalApi.create(input))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to create goal.')
      setSaving(false)
    }
  }

  const unitHint = metricType === 'Weight'
    ? 'lb or kg'
    : (nutrients.find((n) => n.id === nutrientId)?.unit ?? '')

  return (
    <Sheet title="New goal" onClose={onClose}>
      {error && <div className="notice error">{error}</div>}

      <label className="field">
        Name
        <input className="input" value={name} placeholder="e.g. Summer cut"
          onChange={(e) => setName(e.target.value)} />
      </label>

      <div className="row wrap" style={{ gap: '0.5rem', alignItems: 'flex-end' }}>
        <label className="field" style={{ flex: 1, minWidth: 140 }}>
          Metric
          <select className="select" value={metricType}
            onChange={(e) => setMetricType(e.target.value as GoalMetricType)}>
            <option value="Weight">{GOAL_METRIC_LABELS.Weight}</option>
            <option value="Nutrient">{GOAL_METRIC_LABELS.Nutrient}</option>
          </select>
        </label>

        {metricType === 'Nutrient' && (
          <label className="field" style={{ flex: 1, minWidth: 160 }}>
            Nutrient
            <select className="select" value={nutrientId}
              onChange={(e) => setNutrientId(e.target.value)}>
              {nutrients.map((n) => (
                <option key={n.id} value={n.id}>{n.name}</option>
              ))}
            </select>
          </label>
        )}

        <label className="field" style={{ flex: 1, minWidth: 120 }}>
          Target ({unitHint})
          <input className="input" type="number" min="0" step="any" value={targetValue}
            placeholder="e.g. 170" onChange={(e) => setTargetValue(e.target.value)} />
        </label>
      </div>

      <div className="row wrap" style={{ gap: '0.5rem', alignItems: 'flex-end' }}>
        <label className="field" style={{ flex: 1, minWidth: 160 }}>
          Cadence
          <select className="select" value={cadence}
            onChange={(e) => setCadence(e.target.value as GoalCadence)}>
            <option value="OneTime">{GOAL_CADENCE_LABELS.OneTime}</option>
            <option value="Recurring">{GOAL_CADENCE_LABELS.Recurring}</option>
          </select>
        </label>

        <label className="field" style={{ minWidth: 140 }}>
          Start date
          <input className="input" type="date" value={startDate}
            onChange={(e) => setStartDate(e.target.value)} />
        </label>

        {cadence === 'OneTime' ? (
          <label className="field" style={{ minWidth: 140 }}>
            Target date
            <input className="input" type="date" value={targetDate} min={startDate}
              onChange={(e) => setTargetDate(e.target.value)} />
          </label>
        ) : (
          <label className="field" style={{ minWidth: 140 }}>
            Every (days)
            <input className="input" type="number" min="1" step="1" value={recurrenceDays}
              onChange={(e) => setRecurrenceDays(e.target.value)} />
          </label>
        )}
      </div>

      <label className="toggle" title="Whether going past the target is a good thing">
        <input type="checkbox" checked={exceedingIsPositive}
          onChange={(e) => setExceedingIsPositive(e.target.checked)} />
        Exceeding the target is a good thing
      </label>
      <p className="muted small" style={{ margin: 0 }}>
        Turn this off for budgets you shouldn't overshoot (e.g. a calorie limit), on for targets where
        more is better (e.g. losing extra weight, hitting more protein).
      </p>

      <button className="btn btn-primary" disabled={saving} onClick={create}>
        {saving ? 'Saving…' : 'Create goal'}
      </button>
    </Sheet>
  )
}
