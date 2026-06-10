import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { consumptionApi, mealApi, mealPlanApi, recipeApi, userApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import { NutritionLabel } from '../components/NutritionLabel'
import {
  CONSUMPTION_SOURCE_LABELS, WEIGHT_UNIT_LABELS,
  type ConsumptionDay, type ConsumptionSource, type Meal, type MealPlan,
  type PlannedDay, type Recipe,
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

// "08:30:00" → "8:30 AM"; blank/undefined → "Anytime".
function formatTime(t?: string): string {
  if (!t) return 'Anytime'
  const [hStr, m] = t.split(':')
  const h = Number(hStr)
  const period = h < 12 ? 'AM' : 'PM'
  const h12 = h % 12 === 0 ? 12 : h % 12
  return `${h12}:${m} ${period}`
}

export function LogPage() {
  const today = todayIso()
  const { profile, refresh } = useAuth()
  const activePlanId = profile?.activeMealPlanId ?? ''

  const [date, setDate] = useState(today)
  const [day, setDay] = useState<ConsumptionDay | null>(null)
  const [planned, setPlanned] = useState<PlannedDay | null>(null)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // The user's meal plans, for the "currently following" picker.
  const [plans, setPlans] = useState<MealPlan[]>([])
  const [savingPlan, setSavingPlan] = useState(false)
  const [togglingRef, setTogglingRef] = useState<string | null>(null)

  // Catalog pickers (the user's own + app-level + public) for the ad-hoc log.
  const [meals, setMeals] = useState<Meal[]>([])
  const [recipes, setRecipes] = useState<Recipe[]>([])

  // Ad-hoc add-entry form state.
  const [source, setSource] = useState<ConsumptionSource>('Meal')
  const [sourceId, setSourceId] = useState('')
  const [servings, setServings] = useState('1')
  const [freeName, setFreeName] = useState('')
  const [freeCalories, setFreeCalories] = useState('')
  const [saving, setSaving] = useState(false)

  // Load catalog + plan options once.
  useEffect(() => {
    let cancelled = false
    Promise.all([mealApi.list(), recipeApi.list(), mealPlanApi.list()])
      .then(([m, r, p]) => { if (!cancelled) { setMeals(m); setRecipes(r); setPlans(p) } })
      .catch(() => {})
    return () => { cancelled = true }
  }, [])

  // Reload the day's log + planned tasks (used by action handlers after a mutation).
  const reload = useCallback(async () => {
    const [d, pl] = await Promise.all([
      consumptionApi.day(date),
      consumptionApi.plannedDay(date),
    ])
    setDay(d)
    setPlanned(pl)
  }, [date])

  // Load the day's log + planned tasks whenever the date or the active plan changes. The setState calls
  // live in the async callbacks (not synchronously in the effect body) to avoid cascading renders.
  useEffect(() => {
    let cancelled = false
    Promise.all([consumptionApi.day(date), consumptionApi.plannedDay(date)])
      .then(([d, pl]) => { if (!cancelled) { setDay(d); setPlanned(pl) } })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load the log.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
    // activePlanId is included so switching plans refreshes the tasks.
  }, [date, activePlanId])

  const options = source === 'Recipe' ? recipes : meals
  const effectiveSourceId = source === 'FreeForm'
    ? ''
    : (options.some((o) => o.id === sourceId) ? sourceId : (options[0]?.id ?? ''))

  const dayTotal = useMemo(() => day?.totalNutrition ?? [], [day])
  const totalCalories = useMemo(
    () => dayTotal.find((n) => n.nutrientId === 'calories')?.value ?? 0,
    [dayTotal])

  async function choosePlan(planId: string) {
    setError(null)
    setSavingPlan(true)
    try {
      await userApi.setActivePlan(planId || null)
      await refresh() // updates profile.activeMealPlanId → effect reloads tasks
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to set the plan.')
    } finally {
      setSavingPlan(false)
    }
  }

  async function toggleTask(ref: string, done: boolean) {
    setError(null)
    setTogglingRef(ref)
    try {
      if (done) await consumptionApi.uncompletePlanned({ ref, date })
      else await consumptionApi.completePlanned({ ref, date })
      await reload()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to update the task.')
    } finally {
      setTogglingRef(null)
    }
  }

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
      await reload()
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
      await reload()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to remove item.')
    }
  }

  const tasks = planned?.tasks ?? []
  const weightTask = planned?.weightTask
  const doneCount = tasks.filter((t) => t.done).length
  // Ad-hoc entries are everything not created by completing a planned task (those show as tasks above).
  const adHocEntries = (day?.entries ?? []).filter((e) => !e.plannedMealRef)

  return (
    <div className="stack">
      <div className="row spread">
        <h1>The Log</h1>
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

      {/* Recurring "Log your weight" task — only on days a measurement is due. */}
      {weightTask && (
        <div className="card stack">
          <h2>Weigh-in</h2>
          <div className="card ingredient-item" style={{ opacity: weightTask.done ? 0.7 : 1 }}>
            <div className="ingredient-thumb">⚖️</div>
            <div style={{ flex: 1, minWidth: 0 }}>
              <strong style={{ textDecoration: weightTask.done ? 'line-through' : 'none' }}>
                Log your weight
              </strong>
              <div className="small muted">
                {weightTask.done && weightTask.recordedValue != null
                  ? `Logged: ${round(weightTask.recordedValue)} ${weightTask.recordedUnit ? WEIGHT_UNIT_LABELS[weightTask.recordedUnit] : ''}`
                  : 'A weight measurement is due today.'}
              </div>
            </div>
            <Link
              className="btn btn-sm btn-primary"
              to={`/weight?returnTo=${encodeURIComponent('/log')}`}
            >
              {weightTask.done ? 'Update' : 'Log weight'}
            </Link>
          </div>
        </div>
      )}

      {/* Currently-following plan picker. */}
      <div className="card stack">
        <h2>Plan you're following</h2>
        <label className="field" style={{ maxWidth: '20rem' }}>
          Meal plan
          <select
            className="select"
            value={activePlanId}
            disabled={savingPlan}
            onChange={(e) => choosePlan(e.target.value)}
          >
            <option value="">None — free-form log only</option>
            {plans.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
          </select>
        </label>
        {!activePlanId && (
          <p className="muted small" style={{ margin: 0 }}>
            {plans.length === 0
              ? 'Create a meal plan to follow it here, or just log items freely below.'
              : 'Choose a plan to see its meals for the day as tasks to check off.'}
          </p>
        )}
      </div>

      {/* Plan-driven tasks for the selected day. */}
      {activePlanId && (
        <div className="card stack">
          <div className="row spread">
            <h2>Today's plan{planned?.planName ? `: ${planned.planName}` : ''}</h2>
            {tasks.length > 0 && (
              <span className="small muted">{doneCount} / {tasks.length} done</span>
            )}
          </div>

          {loading && tasks.length === 0 && <p className="muted">Loading…</p>}
          {!loading && tasks.length === 0 && (
            <p className="muted small" style={{ margin: 0 }}>
              This plan has no meals scheduled for this day.
            </p>
          )}

          <div className="list">
            {tasks.map((t) => {
              const kcal = t.nutrition.find((n) => n.nutrientId === 'calories')?.value
              return (
                <label
                  key={t.ref}
                  className="card ingredient-item"
                  style={{ cursor: 'pointer', opacity: t.done ? 0.7 : 1 }}
                >
                  <input
                    type="checkbox"
                    checked={t.done}
                    disabled={togglingRef === t.ref}
                    onChange={() => toggleTask(t.ref, t.done)}
                    style={{ width: '1.25rem', height: '1.25rem' }}
                  />
                  <div style={{ flex: 1, minWidth: 0 }}>
                    <strong style={{ textDecoration: t.done ? 'line-through' : 'none' }}>
                      {t.mealName ?? '(meal removed)'}
                    </strong>
                    <div className="small muted">
                      {formatTime(t.time)}
                      {kcal != null && ` · ${Math.round(kcal)} kcal`}
                    </div>
                  </div>
                </label>
              )
            })}
          </div>
        </div>
      )}

      <div className="card stack">
        <h2>Log an item</h2>
        <p className="muted small" style={{ margin: 0 }}>
          Log anything you ate outside the plan (or with no plan).
        </p>
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
          <h2>Also logged{date === today ? ' today' : ''}</h2>
          <span className="small muted">{Math.round(totalCalories)} kcal total</span>
        </div>

        {loading && adHocEntries.length === 0 && <p className="muted">Loading…</p>}
        {!loading && adHocEntries.length === 0 && (
          <p className="muted small" style={{ margin: 0 }}>Nothing logged outside the plan for this day.</p>
        )}

        <div className="list">
          {adHocEntries.map((e) => {
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
