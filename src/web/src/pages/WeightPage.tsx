import { useEffect, useState } from 'react'
import { weightApi } from '../api/services'
import { WEIGHT_UNIT_LABELS, type WeightEntry, type WeightUnit } from '../api/types'

// Today's calendar date as "yyyy-MM-dd" in the user's local time.
function todayIso(): string {
  const now = new Date()
  const offset = now.getTimezoneOffset() * 60_000
  return new Date(now.getTime() - offset).toISOString().slice(0, 10)
}

export function WeightPage() {
  const [entries, setEntries] = useState<WeightEntry[]>([])
  const [value, setValue] = useState('')
  const [unit, setUnit] = useState<WeightUnit>('Pound')
  const [loading, setLoading] = useState(true)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const today = todayIso()
  const recordedToday = entries.some((e) => e.date === today)

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
      const saved = await weightApi.record({ value: num, unit })
      // Upsert into the local list by date (most recent first).
      setEntries((prev) => {
        const without = prev.filter((e) => e.date !== saved.date)
        return [saved, ...without].sort((a, b) => (a.date < b.date ? 1 : -1))
      })
      setValue('')
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

      <div className="card stack">
        <h2>{recordedToday ? "Update today's weight" : 'Record today’s weight'}</h2>
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
        </div>
        <button className="btn btn-primary" disabled={saving} onClick={record}>
          {saving ? 'Saving…' : recordedToday ? 'Update' : 'Record'}
        </button>
        {recordedToday && (
          <p className="muted small" style={{ margin: 0 }}>You already logged a weight today; saving will update it.</p>
        )}
      </div>

      <div className="card stack">
        <h2>History</h2>
        {loading && entries.length === 0 && <p className="muted">Loading…</p>}
        {!loading && entries.length === 0 && (
          <p className="muted small" style={{ margin: 0 }}>No weight recorded yet.</p>
        )}
        <div className="list">
          {entries.map((e) => (
            <div key={e.id} className="card ingredient-item">
              <div className="ingredient-thumb">⚖️</div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div className="row spread">
                  <strong>{e.value} {WEIGHT_UNIT_LABELS[e.unit]}</strong>
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
