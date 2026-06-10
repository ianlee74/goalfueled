import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { burnApi, consumptionApi, mealApi, mealPlanApi, recipeApi, userApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import { NutritionLabel } from '../components/NutritionLabel'
import { FuelBurnHeading } from '../components/FuelBurnIcon'
import {
  CONSUMPTION_SOURCE_LABELS, WEIGHT_UNIT_LABELS,
  type BurnDay, type BurnSuggestion,
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
  const [burnDay, setBurnDay] = useState<BurnDay | null>(null)
  const [burnSuggestions, setBurnSuggestions] = useState<BurnSuggestion[]>([])
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

  // Burn add-activity form state.
  const [burnName, setBurnName] = useState('')
  const [burnCalories, setBurnCalories] = useState('')
  const [savingBurn, setSavingBurn] = useState(false)

  // Load catalog + plan options + burn-name suggestions once.
  useEffect(() => {
    let cancelled = false
    Promise.all([mealApi.list(), recipeApi.list(), mealPlanApi.list(), burnApi.suggestions()])
      .then(([m, r, p, s]) => {
        if (!cancelled) { setMeals(m); setRecipes(r); setPlans(p); setBurnSuggestions(s) }
      })
      .catch(() => {})
    return () => { cancelled = true }
  }, [])

  // Reload the day's log + planned tasks + burn (used by action handlers after a mutation).
  const reload = useCallback(async () => {
    const [d, pl, b] = await Promise.all([
      consumptionApi.day(date),
      consumptionApi.plannedDay(date),
      burnApi.day(date),
    ])
    setDay(d)
    setPlanned(pl)
    setBurnDay(b)
  }, [date])

  // Load the day's log + planned tasks + burn whenever the date or the active plan changes. The setState
  // calls live in the async callbacks (not synchronously in the effect body) to avoid cascading renders.
  useEffect(() => {
    let cancelled = false
    Promise.all([consumptionApi.day(date), consumptionApi.plannedDay(date), burnApi.day(date)])
      .then(([d, pl, b]) => { if (!cancelled) { setDay(d); setPlanned(pl); setBurnDay(b) } })
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
  // Burn is stored as a positive magnitude; net calories subtract it (net = fuel − burn − kindle).
  const totalBurned = burnDay?.totalCaloriesBurned ?? 0
  // The passive "Kindle" cost (sedentary calorie usage), derived server-side from the profile + latest
  // weight. Undefined when the profile is incomplete (we then prompt the user instead of subtracting it).
  const kindleCost = burnDay?.kindleCost
  const netCalories = totalCalories - totalBurned - (kindleCost ?? 0)

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

  // When a burn name matches a past activity, default the calories to that activity's last value (the
  // calories stay editable). Matches case-insensitively against the loaded suggestions.
  function onBurnNameChange(name: string) {
    setBurnName(name)
    const match = burnSuggestions.find((s) => s.name.toLowerCase() === name.trim().toLowerCase())
    if (match) setBurnCalories(String(match.lastCalories))
  }

  async function addBurn() {
    setError(null)
    if (!burnName.trim()) { setError('Enter a name for the activity.'); return }
    const kcal = Number(burnCalories)
    if (!(kcal > 0)) { setError('Calories burned must be greater than zero.'); return }

    setSavingBurn(true)
    try {
      await burnApi.log({ name: burnName.trim(), caloriesBurned: Math.round(kcal), date })
      await reload()
      // Refresh suggestions so a brand-new name becomes available for autocomplete next time.
      setBurnSuggestions(await burnApi.suggestions())
      setBurnName('')
      setBurnCalories('')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to log activity.')
    } finally {
      setSavingBurn(false)
    }
  }

  async function removeBurn(id: string) {
    setError(null)
    try {
      await burnApi.remove(id)
      await reload()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to remove activity.')
    }
  }

  const tasks = planned?.tasks ?? []
  const weightTask = planned?.weightTask
  const doneCount = tasks.filter((t) => t.done).length
  // Ad-hoc entries are everything not created by completing a planned task (those show as tasks above).
  const adHocEntries = (day?.entries ?? []).filter((e) => !e.plannedMealRef)
  const burnActivities = burnDay?.activities ?? []

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

      {/* Plan-driven Fuel tasks for the selected day. */}
      {activePlanId && (
        <div className="card stack fb-section fb-fuel">
          <FuelBurnHeading kind="fuel">
            {tasks.length > 0 && (
              <span className="small muted">{doneCount} / {tasks.length} done</span>
            )}
          </FuelBurnHeading>
          <p className="muted small" style={{ margin: 0 }}>
            Fuel you've planned to eat{planned?.planName ? ` from ${planned.planName}` : ''} — check
            each off as you consume it.
          </p>

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

      <div className="card stack fb-section fb-fuel">
        <FuelBurnHeading kind="fuel">
          <span className="small muted">Add</span>
        </FuelBurnHeading>
        <p className="muted small" style={{ margin: 0 }}>
          Log any fuel you ate outside the plan (or with no plan).
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

      <div className="card stack fb-section fb-fuel">
        <FuelBurnHeading kind="fuel">
          <span className="small muted">+{Math.round(totalCalories)} kcal</span>
        </FuelBurnHeading>
        <p className="muted small" style={{ margin: 0 }}>
          Fuel also logged{date === today ? ' today' : ''} outside the plan.
        </p>

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

      {/* Burn — exercise that uses calories. Calories are stored as positive magnitudes and subtracted
          from the day's net (net = fuel − burn). The name field autocompletes from past activities; picking
          a past name defaults the calories to that activity's most recent value (still editable). */}
      <div className="card stack fb-section fb-burn">
        <FuelBurnHeading kind="burn">
          <span className="small muted">−{Math.round(totalBurned + (kindleCost ?? 0))} kcal</span>
        </FuelBurnHeading>
        <p className="muted small" style={{ margin: 0 }}>
          Exercise that burns calories, plus your passive Kindle cost (calories you burn just staying alive).
        </p>

        {/* Kindle — a passive, derived burn line (not a logged activity). Computed server-side from the
            profile + latest weight; shown as a friendly prompt when the profile is incomplete. */}
        <div className="card ingredient-item">
          <div className="ingredient-thumb">🔥</div>
          <div style={{ flex: 1, minWidth: 0 }}>
            <strong>Kindle cost <span className="muted small">passive</span></strong>
            {kindleCost != null ? (
              <div className="small muted">
                −{Math.round(kindleCost)} kcal · calories burned just staying alive
              </div>
            ) : (
              <div className="small muted">
                <Link to="/profile">Complete your profile</Link> (sex, date of birth, height, weight) to see your Kindle cost.
              </div>
            )}
          </div>
        </div>

        <div className="row" style={{ gap: '0.5rem', flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <label className="field" style={{ flex: 1, minWidth: '8rem' }}>
            Activity
            <input
              className="input"
              list="burn-activity-names"
              value={burnName}
              placeholder="e.g. Morning run"
              onChange={(e) => onBurnNameChange(e.target.value)}
            />
            <datalist id="burn-activity-names">
              {burnSuggestions.map((s) => <option key={s.name} value={s.name} />)}
            </datalist>
          </label>
          <label className="field">
            Calories burned
            <input
              className="input"
              type="number"
              min="0"
              step="1"
              value={burnCalories}
              placeholder="e.g. 300"
              onChange={(e) => setBurnCalories(e.target.value)}
            />
          </label>
        </div>
        <button type="button" className="btn btn-primary" disabled={savingBurn} onClick={addBurn}>
          {savingBurn ? 'Adding…' : '+ Add burn'}
        </button>

        {loading && burnActivities.length === 0 && <p className="muted">Loading…</p>}
        {!loading && burnActivities.length === 0 && (
          <p className="muted small" style={{ margin: 0 }}>No burn logged for this day yet.</p>
        )}

        <div className="list">
          {burnActivities.map((a) => (
            <div key={a.id} className="card ingredient-item">
              <div className="ingredient-thumb">🔥</div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div className="row spread">
                  <strong>{a.name}</strong>
                  <button
                    type="button"
                    className="btn btn-sm btn-danger"
                    title="Remove"
                    onClick={() => removeBurn(a.id)}
                  >✕</button>
                </div>
                <div className="small muted">−{Math.round(a.caloriesBurned)} kcal</div>
              </div>
            </div>
          ))}
        </div>
      </div>

      {/* Net calorie summary for the day: fuel consumed minus logged burn minus the passive Kindle cost. */}
      {(totalCalories > 0 || totalBurned > 0 || kindleCost != null) && (
        <div className="card stack">
          <h2>Net calories <span className="muted small">fuel − burn{kindleCost != null ? ' − kindle' : ''}</span></h2>
          <p className="muted small" style={{ margin: 0 }}>
            {Math.round(totalCalories)} fuel − {Math.round(totalBurned)} burn
            {kindleCost != null && ` − ${Math.round(kindleCost)} kindle`} ={' '}
            <strong>{Math.round(netCalories)} kcal</strong>
          </p>
          {kindleCost == null && (
            <p className="muted small" style={{ margin: 0 }}>
              Add your date of birth, sex, height, and weight in your{' '}
              <Link to="/profile">profile</Link> to include your passive Kindle cost here.
            </p>
          )}
        </div>
      )}

      {dayTotal.length > 0 && (
        <div className="card stack">
          <h2>Day total <span className="muted small">nutrition</span></h2>
          <NutritionLabel amounts={dayTotal} />
        </div>
      )}
    </div>
  )
}
