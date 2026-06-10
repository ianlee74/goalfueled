import { useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { userApi } from '../api/services'
import {
  DAYS_OF_WEEK,
  WEIGHT_SCHEDULE_CADENCE_LABELS,
  type DayOfWeek,
  type WeightScheduleCadence,
} from '../api/types'

// Today's calendar date as "yyyy-MM-dd" in the user's local time.
function todayIso(): string {
  const now = new Date()
  const offset = now.getTimezoneOffset() * 60_000
  return new Date(now.getTime() - offset).toISOString().slice(0, 10)
}

// The day-of-week name for an ISO date string (parsed in local time).
function dayOfWeekOf(iso: string): DayOfWeek {
  const [y, m, d] = iso.split('-').map(Number)
  return DAYS_OF_WEEK[new Date(y, m - 1, d).getDay()]
}

// Whether a cadence uses the "every N" interval input.
function usesInterval(cadence: WeightScheduleCadence): boolean {
  return cadence === 'EveryNDays' || cadence === 'EveryNWeeks'
}

/**
 * Editor for the user's recurring weight-measurement schedule (Profile page). Configures the cadence
 * (daily / weekly / every N days / every N weeks), the start day-of-week (derived from the start date),
 * and the N for the "every N" cadences. Once set, a "Log your weight" task appears in The Log on due days.
 */
export function WeightScheduleEditor() {
  const { profile, refresh } = useAuth()
  const existing = profile?.weightSchedule

  const [enabled, setEnabled] = useState(existing != null)
  const [cadence, setCadence] = useState<WeightScheduleCadence>(existing?.cadence ?? 'Weekly')
  const [interval, setInterval] = useState(String(existing?.interval ?? 2))
  const [startDate, setStartDate] = useState(existing?.startDate ?? todayIso())

  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)

  async function save() {
    setError(null)
    setSaved(false)
    if (!enabled) {
      setSaving(true)
      try {
        await userApi.setWeightSchedule({ cadence: null })
        await refresh()
        setSaved(true)
      } catch (e) {
        setError(e instanceof Error ? e.message : 'Could not save your schedule.')
      } finally {
        setSaving(false)
      }
      return
    }

    let n = 1
    if (usesInterval(cadence)) {
      n = Number(interval)
      if (!Number.isInteger(n) || n < 1) { setError('Enter a whole number of 1 or more for the interval.'); return }
    }

    setSaving(true)
    try {
      await userApi.setWeightSchedule({ cadence, startDate, interval: n })
      await refresh()
      setSaved(true)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save your schedule.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="card stack" style={{ maxWidth: 520 }}>
      <h2>Weight measurement schedule</h2>
      <p className="muted small" style={{ margin: 0 }}>
        Choose how often you want to log your weight. A "Log your weight" task will appear in The Log on the
        days a measurement is due.
      </p>

      {error && <div className="notice error">{error}</div>}
      {saved && <div className="notice info">Schedule saved.</div>}

      <label className="row" style={{ gap: '0.5rem', alignItems: 'center' }}>
        <input
          type="checkbox"
          checked={enabled}
          disabled={saving}
          onChange={(e) => setEnabled(e.target.checked)}
        />
        Remind me to log my weight
      </label>

      {enabled && (
        <>
          <label className="field">
            How often
            <select
              className="select"
              value={cadence}
              disabled={saving}
              onChange={(e) => setCadence(e.target.value as WeightScheduleCadence)}
            >
              {(Object.keys(WEIGHT_SCHEDULE_CADENCE_LABELS) as WeightScheduleCadence[]).map((c) => (
                <option key={c} value={c}>{WEIGHT_SCHEDULE_CADENCE_LABELS[c]}</option>
              ))}
            </select>
          </label>

          {usesInterval(cadence) && (
            <label className="field" style={{ maxWidth: '8rem' }}>
              {cadence === 'EveryNDays' ? 'Every how many days' : 'Every how many weeks'}
              <input
                className="input"
                type="number"
                min="1"
                step="1"
                value={interval}
                disabled={saving}
                onChange={(e) => setInterval(e.target.value)}
              />
            </label>
          )}

          <label className="field">
            Start date
            <input
              className="input"
              type="date"
              value={startDate}
              disabled={saving}
              onChange={(e) => setStartDate(e.target.value)}
            />
          </label>
          <p className="muted small" style={{ margin: 0 }}>
            {cadence === 'Daily'
              ? 'A measurement is due every day from the start date.'
              : `Measurements start on ${dayOfWeekOf(startDate)} and recur from there.`}
          </p>
        </>
      )}

      <button className="btn btn-primary" type="button" disabled={saving} onClick={save}>
        {saving ? 'Saving…' : 'Save schedule'}
      </button>
    </div>
  )
}
