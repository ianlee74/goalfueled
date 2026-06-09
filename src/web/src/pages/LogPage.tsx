import { useEffect, useMemo, useState } from 'react'
import { consumptionApi, mealApi, recipeApi } from '../api/services'
import { NutritionLabel } from '../components/NutritionLabel'
import {
  CONSUMPTION_SOURCE_LABELS,
  type ConsumptionDay, type ConsumptionSource, type Meal, type Recipe,
} from '../api/types'

// Today's calendar date as "yyyy-MM-dd" in the user's local time.
function todayIso(): string {
  const now = new Date()
  const offset = now.getTimezoneOffset() * 60_000
  return new Date(now.getTime() - offset).toISOString().slice(0, 10)
}

function round(n: number): string {
  return Number.isInteger(n) ? String(n) : n.toFixed(1)
}

export function LogPage() {
  const today = todayIso()
  const [date, setDate] = useState(today)
  const [day, setDay] = useState<ConsumptionDay | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Catalog pickers (the user's own + app-level + public).
  const [meals, setMeals] = useState<Meal[]>([])
  const [recipes, setRecipes] = useState<Recipe[]>([])

  // Add-entry form state.
  const [source, setSource] = useState<ConsumptionSource>('Meal')
  const [sourceId, setSourceId] = useState('')
  const [servings, setServings] = useState('1')
  const [freeName, setFreeName] = useState('')
  const [freeCalories, setFreeCalories] = useState('')
  const [saving, setSaving] = useState(false)

  // Load catalog options once.
  useEffect(() => {
    let cancelled = false
    Promise.all([mealApi.list(), recipeApi.list()])
      .then(([m, r]) => { if (!cancelled) { setMeals(m); setRecipes(r) } })
      .catch(() => {})
    return () => { cancelled = true }
  }, [])

  // Load the selected day's log whenever the date changes.
  useEffect(() => {
    let cancelled = false
    consumptionApi.day(date)
      .then((d) => { if (!cancelled) setDay(d) })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load the log.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [date])

  const options = source === 'Recipe' ? recipes : meals
  // The effective catalog selection: the chosen id if still valid, else the first option. Derived
  // (not stored) so it stays in sync with the source/options without a setState-in-effect.
  const effectiveSourceId = source === 'FreeForm'
    ? ''
    : (options.some((o) => o.id === sourceId) ? sourceId : (options[0]?.id ?? ''))

  const dayTotal = useMemo(() => day?.totalNutrition ?? [], [day])
  const totalCalories = useMemo(
    () => dayTotal.find((n) => n.nutrientId === 'calories')?.value ?? 0,
    [dayTotal])

  async function add() {
    setError(null)
    const qty = Number(servings)
    if (!(qty > 0)) { setError('Servings must be greater than zero.'); return }

    setSaving(true)
    try {
      if (source === 'FreeForm') {
        if (!freeName.trim()) { setError('Enter a name for the item.'); setSaving(false); return }
        const kcal = Number(freeCalories)
        const nutrition = kcal > 0 ? [{ nutrientId: 'calories', value: kcal }] : undefined
        await consumptionApi.log({ source: 'FreeForm', name: freeName.trim(), servings: qty, nutrition, date })
      } else {
        if (!effectiveSourceId) { setError(`Select a ${source.toLowerCase()} to log.`); setSaving(false); return }
        await consumptionApi.log({ source, sourceId: effectiveSourceId, servings: qty, date })
      }
      // Reload the day so totals reflect the new entry.
      setDay(await consumptionApi.day(date))
      setServings('1')
      setFreeName('')
      setFreeCalories('')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to log item.')
    } finally {
      setSaving(false)
    }
  }

  async function remove(id: string) {
    setError(null)
    try {
      await consumptionApi.remove(id)
      setDay(await consumptionApi.day(date))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to remove item.')
    }
  }

  const entries = day?.entries ?? []

  return (
    <div className="stack">
      <div className="row spread">
        <h1>Food Log</h1>
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

      {error && <div className="notice error">{error}</div>}

      <div className="card stack">
        <h2>Log an item</h2>
        <div className="row" style={{ gap: '0.5rem', flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <label className="field">
            Type
            <select
              className="select"
              value={source}
              onChange={(e) => setSource(e.target.value as ConsumptionSource)}
            >
              <option value="Meal">{CONSUMPTION_SOURCE_LABELS.Meal}</option>
              <option value="Recipe">{CONSUMPTION_SOURCE_LABELS.Recipe}</option>
              <option value="FreeForm">{CONSUMPTION_SOURCE_LABELS.FreeForm}</option>
            </select>
          </label>

          {source === 'FreeForm' ? (
            <>
              <label className="field" style={{ flex: 1, minWidth: '8rem' }}>
                Item
                <input
                  className="input"
                  value={freeName}
                  placeholder="e.g. Handful of almonds"
                  onChange={(e) => setFreeName(e.target.value)}
                />
              </label>
              <label className="field">
                Calories <span className="muted small">(optional, per serving)</span>
                <input
                  className="input"
                  type="number"
                  min="0"
                  step="any"
                  value={freeCalories}
                  placeholder="e.g. 160"
                  onChange={(e) => setFreeCalories(e.target.value)}
                />
              </label>
            </>
          ) : (
            <label className="field" style={{ flex: 1, minWidth: '8rem' }}>
              {source === 'Recipe' ? 'Recipe' : 'Meal'}
              <select className="select" value={effectiveSourceId} onChange={(e) => setSourceId(e.target.value)}>
                <option value="">Select a {source.toLowerCase()}…</option>
                {options.map((o) => <option key={o.id} value={o.id}>{o.name}</option>)}
              </select>
            </label>
          )}

          <label className="field" style={{ maxWidth: '6rem' }}>
            Servings
            <input
              className="input"
              type="number"
              min="0"
              step="any"
              value={servings}
              onChange={(e) => setServings(e.target.value)}
            />
          </label>
        </div>
        <button className="btn btn-primary" disabled={saving} onClick={add}>
          {saving ? 'Adding…' : '+ Add to log'}
        </button>
        {source !== 'FreeForm' && options.length === 0 && (
          <p className="muted small" style={{ margin: 0 }}>
            No {source.toLowerCase()}s available yet — create one first, or log a free-form item.
          </p>
        )}
      </div>

      <div className="card stack">
        <div className="row spread">
          <h2>Logged{date === today ? ' today' : ''}</h2>
          <span className="small muted">{Math.round(totalCalories)} kcal total</span>
        </div>

        {loading && entries.length === 0 && <p className="muted">Loading…</p>}
        {!loading && entries.length === 0 && (
          <p className="muted small" style={{ margin: 0 }}>Nothing logged for this day yet.</p>
        )}

        <div className="list">
          {entries.map((e) => {
            const kcal = e.nutrition.find((n) => n.nutrientId === 'calories')?.value
            return (
              <div key={e.id} className="card ingredient-item">
                <div className="ingredient-thumb">
                  {e.source === 'Meal' ? '🍽️' : e.source === 'Recipe' ? '🍳' : '🍪'}
                </div>
                <div style={{ flex: 1, minWidth: 0 }}>
                  <div className="row spread">
                    <strong>{e.name}</strong>
                    <button className="btn btn-sm btn-danger" title="Remove" onClick={() => remove(e.id)}>✕</button>
                  </div>
                  <div className="small muted">
                    {round(e.servings)} serving{e.servings === 1 ? '' : 's'}
                    {' · '}{CONSUMPTION_SOURCE_LABELS[e.source]}
                    {kcal != null && ` · ${Math.round(kcal)} kcal`}
                    {e.sourceMissing && ' · source removed'}
                  </div>
                </div>
              </div>
            )
          })}
        </div>
      </div>

      {dayTotal.length > 0 && (
        <div className="card stack">
          <h2>Day total <span className="muted small">nutrition</span></h2>
          <NutritionLabel amounts={dayTotal} />
        </div>
      )}
    </div>
  )
}
