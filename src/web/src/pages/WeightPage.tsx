import { useEffect, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { weightApi } from '../api/services'
import { WEIGHT_UNIT_LABELS, type WeightEntry, type WeightUnit } from '../api/types'
import { useAuth } from '../auth/AuthContext'
import { formatWeight, weightUnitFor } from '../lib/weight'
import { IconWeight } from '../components/icons'
import { EmptyState, SkeletonList } from '../components/Placeholders'
import { WeightTrendChart } from '../components/WeightTrendChart'

// Today's calendar date as "yyyy-MM-dd" in the user's local time.
function todayIso(): string {
  const now = new Date()
  const offset = now.getTimezoneOffset() * 60_000
  return new Date(now.getTime() - offset).toISOString().slice(0, 10)
}

// Only honor app-internal relative paths to avoid open redirects: a single leading
// slash, but not "//host" (protocol-relative) or anything containing a scheme.
function safeReturnTo(raw: string | null): string | null {
  if (!raw) return null
  if (!raw.startsWith('/') || raw.startsWith('//')) return null
  return raw
}

export function WeightPage() {
  const navigate = useNavigate()
  const { profile } = useAuth()
  const preference = profile?.unitsPreference ?? 'UsImperial'
  const [searchParams] = useSearchParams()
  const returnTo = safeReturnTo(searchParams.get('returnTo'))
  const [entries, setEntries] = useState<WeightEntry[]>([])
  const [value, setValue] = useState('')
  // Default the unit to the user's preferred weight unit (overridden by their last entry on load).
  const [unit, setUnit] = useState<WeightUnit>(() => weightUnitFor(preference))
  const [date, setDate] = useState(todayIso())
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const today = todayIso()
  const alreadyLogged = entries.some((e) => e.date === date)

  useEffect(() => {
    let cancelled = false
    weightApi.list()
      .then((list) => {
        if (cancelled) return
        setEntries(list)
        // Default the unit to whatever the user last used.
        if (list.length > 0) setUnit(list[0].unit)
      })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load weight history.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [])

  async function record() {
    setError(null)
    const num = Number(value)
    if (!(num > 0)) { setError('Enter a weight greater than zero.'); return }
    setSaving(true)
    try {
      const saved = await weightApi.record({ value: num, unit, date })
      // Upsert into the local list by date (most recent first).
      setEntries((prev) => {
        const without = prev.filter((e) => e.date !== saved.date)
        return [saved, ...without].sort((a, b) => (a.date < b.date ? 1 : -1))
      })
      setValue('')
      setDate(todayIso())
      // If we arrived here via the meal-plan weight gate, head back to where we came
      // from so the user can continue what they were doing. Direct visitors stay put.
      if (returnTo) { navigate(returnTo); return }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to record weight.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="stack">
      <div className="row spread">
        <h1>Weight</h1>
      </div>

      {error && <div className="notice error">{error}</div>}

      {/* Progress first: the trend leads, entry follows (DESIGN.md §3.2). */}
      {entries.length >= 2 && (
        <div className="card stack">
          <h2>Trend</h2>
          <WeightTrendChart entries={entries} preference={preference} />
        </div>
      )}

      <div className="card stack">
        <h2>{alreadyLogged ? 'Update weight' : 'Record weight'}</h2>
        <div className="row" style={{ gap: '0.5rem', alignItems: 'flex-end' }}>
          <label className="field" style={{ flex: 1 }}>
            Weight
            <input
              className="input"
              type="number"
              min="0"
              step="any"
              value={value}
              placeholder="e.g. 180"
              onChange={(e) => setValue(e.target.value)}
            />
          </label>
          <label className="field">
            Unit
            <select className="select" value={unit} onChange={(e) => setUnit(e.target.value as WeightUnit)}>
              <option value="Pound">{WEIGHT_UNIT_LABELS.Pound}</option>
              <option value="Kilogram">{WEIGHT_UNIT_LABELS.Kilogram}</option>
            </select>
          </label>
          <label className="field">
            Date
            <input
              className="input"
              type="date"
              value={date}
              max={today}
              onChange={(e) => setDate(e.target.value)}
            />
          </label>
        </div>
        <button className="btn btn-primary" disabled={saving} onClick={record}>
          {saving ? 'Saving…' : alreadyLogged ? 'Update' : 'Record'}
        </button>
        {alreadyLogged && (
          <p className="muted small" style={{ margin: 0 }}>
            You already logged a weight for {date}; saving will update it.
          </p>
        )}
      </div>

      <div className="card stack">
        <h2>History</h2>
        {loading && entries.length === 0 && <SkeletonList rows={4} />}
        {!loading && entries.length === 0 && (
          <EmptyState icon={<IconWeight size={28} />} title="No weight recorded yet">
            Use the LOG button to record your first weigh-in.
          </EmptyState>
        )}
        <div className="list">
          {entries.map((e) => (
            <div key={e.id} className="card ingredient-item">
              <div className="ingredient-thumb"><IconWeight size={22} /></div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div className="row spread">
                  <strong>{formatWeight(e.value, e.unit, preference)}</strong>
                  <span className="small muted">{e.date}</span>
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}
