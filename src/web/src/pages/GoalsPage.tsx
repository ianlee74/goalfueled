import { useEffect, useState } from 'react'
import { goalApi } from '../api/services'
import { useNutrients } from '../hooks/useNutrients'
import { GoalProgressVisual } from '../components/GoalProgressVisual'
import {
  GOAL_CADENCE_LABELS, GOAL_METRIC_LABELS,
  type Goal, type GoalCadence, type GoalInput, type GoalMetricType,
} from '../api/types'

// Today's calendar date as "yyyy-MM-dd" in the user's local time.
function todayIso(): string {
  const now = new Date()
  const offset = now.getTimezoneOffset() * 60_000
  return new Date(now.getTime() - offset).toISOString().slice(0, 10)
}

const emptyForm = (): GoalInput => ({
  name: '',
  metricType: 'Weight',
  targetValue: 0,
  cadence: 'OneTime',
  exceedingIsPositive: true,
  nutrientId: undefined,
  startDate: todayIso(),
  targetDate: undefined,
  recurrenceDays: 7,
})

export function GoalsPage() {
  const { ordered: nutrients } = useNutrients()
  const [goals, setGoals] = useState<Goal[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Create form state.
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

  useEffect(() => {
    let cancelled = false
    goalApi.list()
      .then((list) => { if (!cancelled) setGoals(list) })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load goals.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [])

  function resetForm() {
    const f = emptyForm()
    setName(f.name)
    setMetricType(f.metricType)
    setNutrientId('calories')
    setTargetValue('')
    setCadence(f.cadence)
    setStartDate(todayIso())
    setTargetDate('')
    setRecurrenceDays('7')
    setExceedingIsPositive(true)
  }

  async function create() {
    setError(null)
    const num = Number(targetValue)
    if (!name.trim()) { setError('Give your goal a name.'); return }
    if (!(num > 0)) { setError('Enter a target value greater than zero.'); return }
    if (cadence === 'OneTime' && !targetDate) { setError('Pick a target date for a one-time goal.'); return }
    if (cadence === 'Recurring' && !(Number(recurrenceDays) >= 1)) {
      setError('Enter a recurrence period of at least one day.'); return
    }

    const input: GoalInput = {
      name: name.trim(),
      metricType,
      targetValue: num,
      cadence,
      exceedingIsPositive,
      nutrientId: metricType === 'Nutrient' ? nutrientId : undefined,
      startDate,
      targetDate: cadence === 'OneTime' ? targetDate : undefined,
      recurrenceDays: cadence === 'Recurring' ? Number(recurrenceDays) : undefined,
    }

    setSaving(true)
    try {
      const saved = await goalApi.create(input)
      setGoals((prev) => [saved, ...prev])
      resetForm()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to create goal.')
    } finally {
      setSaving(false)
    }
  }

  async function remove(id: string) {
    setError(null)
    try {
      await goalApi.remove(id)
      setGoals((prev) => prev.filter((g) => g.id !== id))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to delete goal.')
    }
  }

  const unitHint = metricType === 'Weight'
    ? 'lb or kg'
    : (nutrients.find((n) => n.id === nutrientId)?.unit ?? '')

  return (
    <div className="stack">
      <div className="row spread">
        <h1>Goals</h1>
      </div>

      {error && <div className="notice error">{error}</div>}

      <div className="card stack">
        <h2>Define a goal</h2>

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
      </div>

      <div className="card stack">
        <h2>Your goals</h2>
        {loading && goals.length === 0 && <p className="muted">Loading…</p>}
        {!loading && goals.length === 0 && (
          <p className="muted small" style={{ margin: 0 }}>No goals yet. Define your first one above.</p>
        )}

        <div className="list">
          {goals.map((g) => (
            <div key={g.id} className="card stack" style={{ gap: '0.6rem' }}>
              <div className="row spread">
                <div>
                  <strong>{g.name}</strong>
                  <div className="small muted">
                    {g.metricType === 'Nutrient' ? g.nutrientName : 'Weight'}
                    {' · '}
                    {g.cadence === 'OneTime' ? 'One-time' : `Every ${g.recurrenceDays} days`}
                    {' · target '}{g.targetValue} {g.metricUnit}
                  </div>
                </div>
                <button className="btn btn-sm btn-danger" onClick={() => remove(g.id)}>Delete</button>
              </div>
              <GoalProgressVisual goal={g} />
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
