import { useCallback, useEffect, useMemo, useState } from 'react'
import { Link } from 'react-router-dom'
import { burnApi, consumptionApi, mealApi, mealPlanApi, recipeApi, userApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import { NutritionLabel } from '../components/NutritionLabel'
import { FuelBurnIcon } from '../components/FuelBurnIcon'
import {
  CONSUMPTION_SOURCE_LABELS, WEIGHT_UNIT_LABELS,
  type BurnActivity, type BurnDay, type BurnSuggestion,
  type ConsumptionDay, type ConsumptionEntry, type ConsumptionSource, type Meal, type MealPlan,
  type PlannedDay, type PlannedMealTask, type Recipe, type WeightTask,
} from '../api/types'

// Today's calendar date as "yyyy-MM-dd" in the user's local time.
function todayIso(): string {
  const now = new Date()
  const offset = now.getTimezoneOffset() * 60_000
  return new Date(now.getTime() - offset).toISOString().slice(0, 10)
}

// The current local time as "HH:mm" (the value a <input type="time"> uses).
function nowTime(): string {
  const now = new Date()
  return `${String(now.getHours()).padStart(2, '0')}:${String(now.getMinutes()).padStart(2, '0')}`
}

// Normalize a <input type="time"> value ("HH:mm") to the API's "HH:mm:ss"; blank → undefined.
function toApiTime(t: string): string | undefined {
  if (!t) return undefined
  return t.length === 5 ? `${t}:00` : t
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

// The time-of-day (seconds since midnight) used to order an event in the timeline.
//  - A scheduled "HH:mm:ss" (planned-meal time) maps to its literal time of day.
//  - An ISO timestamp (createdAt) maps to its local-time time-of-day.
// Used to sort completed events most-recent-first (descending).
function secondsFromScheduledTime(t: string): number {
  const [h, m, s] = t.split(':').map(Number)
  return (h || 0) * 3600 + (m || 0) * 60 + (s || 0)
}
function secondsFromTimestamp(iso: string): number {
  const d = new Date(iso)
  return d.getHours() * 3600 + d.getMinutes() * 60 + d.getSeconds()
}

// "8:30 AM" for an ISO timestamp's local time-of-day.
function formatClock(iso: string): string {
  const d = new Date(iso)
  const h = d.getHours()
  const period = h < 12 ? 'AM' : 'PM'
  const h12 = h % 12 === 0 ? 12 : h % 12
  return `${h12}:${String(d.getMinutes()).padStart(2, '0')} ${period}`
}

// ---- Timeline model -------------------------------------------------------
// The day's events merged into one chronological list. Each entry knows how to render itself and
// carries a `sortKey` = seconds-since-midnight for ordering (descending = latest first).
//
// Ordering, top → bottom:
//   1. PENDING planned items (planned fuel tasks not yet done + a due-but-unrecorded weigh-in) —
//      the "still to do" block, pinned above the timeline.
//   2. COMPLETED events (ad-hoc fuel, completed planned meals, burn activities, a recorded weigh-in),
//      sorted by time-of-day DESCENDING so the most recent sits at the top.
//   3. The passive KINDLE line — the first thing that happens each day, so it sits at the very bottom.
//
// Time source per kind:
//   - completed planned meal → its scheduled `time` (falls back to the completing entry's createdAt).
//   - ad-hoc fuel / burn activity → createdAt time-of-day.
//   - recorded weigh-in → it has no timestamp, so it's placed at the start of the day (bottom of the
//     completed block) just above Kindle; pending weigh-in lives in the "still to do" block instead.
//   - "Anytime" completed planned meals (no scheduled time) fall back to createdAt; if that's somehow
//     missing they sort as time 0 (start of day).
type TimelineItem = { key: string; sortKey: number; node: React.ReactNode }

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
  const [skippingRef, setSkippingRef] = useState<string | null>(null)

  // Catalog pickers (the user's own + app-level + public) for the ad-hoc log.
  const [meals, setMeals] = useState<Meal[]>([])
  const [recipes, setRecipes] = useState<Recipe[]>([])

  // Ad-hoc add-entry form state.
  const [showFuelForm, setShowFuelForm] = useState(false)
  const [source, setSource] = useState<ConsumptionSource>('Meal')
  const [sourceId, setSourceId] = useState('')
  const [servings, setServings] = useState('1')
  const [freeName, setFreeName] = useState('')
  const [freeCalories, setFreeCalories] = useState('')
  const [saving, setSaving] = useState(false)

  // Burn add-activity form state.
  const [showBurnForm, setShowBurnForm] = useState(false)
  const [burnName, setBurnName] = useState('')
  const [burnCalories, setBurnCalories] = useState('')
  // The time of day the burn occurred, "HH:mm"; defaults to now so the common case is one tap.
  const [burnTime, setBurnTime] = useState(nowTime)
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

  // Skip a planned meal (remove it from "still to do" without it counting), or undo a skip to bring it back.
  async function skipTask(ref: string, skipped: boolean) {
    setError(null)
    setSkippingRef(ref)
    try {
      if (skipped) await consumptionApi.uncompletePlanned({ ref, date })
      else await consumptionApi.skipPlanned({ ref, date })
      await reload()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to update the task.')
    } finally {
      setSkippingRef(null)
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
      setShowFuelForm(false)
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
      await burnApi.log({
        name: burnName.trim(),
        caloriesBurned: Math.round(kcal),
        date,
        time: toApiTime(burnTime),
      })
      await reload()
      // Refresh suggestions so a brand-new name becomes available for autocomplete next time.
      setBurnSuggestions(await burnApi.suggestions())
      setBurnName('')
      setBurnCalories('')
      setBurnTime(nowTime())
      setShowBurnForm(false)
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
  // Ad-hoc entries are everything not created by completing a planned task (those show as completed tasks).
  const adHocEntries = (day?.entries ?? []).filter((e) => !e.plannedMealRef)
  const burnActivities = burnDay?.activities ?? []
  // A planned meal's completing consumption entry, so we can show the time it was actually logged.
  const entryByRef = useMemo(() => {
    const m = new Map<string, ConsumptionEntry>()
    for (const e of day?.entries ?? []) if (e.plannedMealRef) m.set(e.plannedMealRef, e)
    return m
  }, [day])

  // ---- Build the "still to do" block (pending planned fuel + a due, unrecorded weigh-in). ----
  // Skipped tasks are neither done nor pending: they drop out of "still to do" into their own muted area.
  const pendingTasks = tasks.filter((t) => !t.done && !t.skipped)
  const skippedTasks = tasks.filter((t) => t.skipped)
  const pendingWeighIn = weightTask && !weightTask.done

  // ---- Build the time-ordered completed events. ----
  const completedItems = useMemo<TimelineItem[]>(() => {
    const items: TimelineItem[] = []

    // Completed planned meals — ordered by their scheduled time (fallback: completing entry's createdAt).
    for (const t of tasks) {
      if (!t.done) continue
      const entry = entryByRef.get(t.ref)
      const sortKey = t.time
        ? secondsFromScheduledTime(t.time)
        : entry ? secondsFromTimestamp(entry.createdAt) : 0
      items.push({ key: `task:${t.ref}`, sortKey, node: <DoneMealRow task={t} entry={entry} /> })
    }

    // Ad-hoc fuel — createdAt time-of-day.
    for (const e of adHocEntries) {
      items.push({
        key: `fuel:${e.id}`,
        sortKey: secondsFromTimestamp(e.createdAt),
        node: <FuelRow entry={e} onRemove={() => remove(e.id)} />,
      })
    }

    // Burn activities — the user-specified time when present, else createdAt time-of-day.
    for (const a of burnActivities) {
      items.push({
        key: `burn:${a.id}`,
        sortKey: a.time ? secondsFromScheduledTime(a.time) : secondsFromTimestamp(a.createdAt),
        node: <BurnRow activity={a} onRemove={() => removeBurn(a.id)} />,
      })
    }

    // A recorded weigh-in carries no timestamp; place it at the start of the day (above Kindle).
    if (weightTask?.done) {
      items.push({ key: 'weigh-in', sortKey: 0, node: <WeighInRow task={weightTask} /> })
    }

    // Latest first (descending). Ties broken by key for a stable order.
    items.sort((a, b) => (b.sortKey - a.sortKey) || (a.key < b.key ? -1 : 1))
    return items
    // remove/removeBurn are stable enough for this render-only memo; deps cover the data we read.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tasks, adHocEntries, burnActivities, weightTask, entryByRef])

  const hasAnyEvent = completedItems.length > 0
  const isEmptyDay =
    !loading && pendingTasks.length === 0 && !pendingWeighIn && !hasAnyEvent && skippedTasks.length === 0

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

      {/* Totals header — the day at a glance: Fuel, Burn, Net. */}
      <div className="card log-totals">
        <Total label="Fuel" kind="fuel" value={`+${Math.round(totalCalories)}`} />
        <Total label="Burn" kind="burn" value={`−${Math.round(totalBurned + (kindleCost ?? 0))}`} />
        <Total
          label="Net Calories"
          value={`${Math.round(netCalories)}`}
          sub={`fuel − burn${kindleCost != null ? ' − kindle' : ''}`}
          emphasis
        />
      </div>

      {/* Compact, rarely-changed plan picker collapsed into a one-line disclosure. */}
      <details className="card log-plan">
        <summary>
          <span className="row" style={{ gap: '0.5rem' }}>
            <FuelBurnIcon kind="fuel" />
            <span>
              <strong>Plan: </strong>
              {planned?.planName ?? (activePlanId ? '…' : 'None — free-form log')}
            </span>
          </span>
          <span className="small muted">change</span>
        </summary>
        <div className="stack" style={{ marginTop: '0.6rem' }}>
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
                ? 'Create a meal plan to follow it here, or just log items freely.'
                : 'Choose a plan to see its meals for the day as tasks to check off.'}
            </p>
          )}
        </div>
      </details>

      {/* Add buttons reveal the (otherwise hidden) add forms. */}
      <div className="row wrap" style={{ gap: '0.5rem' }}>
        <button
          type="button"
          className="btn btn-primary"
          aria-expanded={showFuelForm}
          onClick={() => setShowFuelForm((v) => !v)}
        >
          {showFuelForm ? '× Close fuel' : '+ Add fuel'}
        </button>
        <button
          type="button"
          className="btn"
          aria-expanded={showBurnForm}
          onClick={() => setShowBurnForm((v) => !v)}
        >
          {showBurnForm ? '× Close burn' : '+ Add burn'}
        </button>
      </div>

      {/* Ad-hoc fuel add form — gated behind "+ Add fuel". */}
      {showFuelForm && (
        <div className="card stack fb-section fb-fuel">
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
      )}

      {/* Burn add form — gated behind "+ Add burn". The name field autocompletes from past activities;
          picking a past name defaults the calories to that activity's most recent value (still editable). */}
      {showBurnForm && (
        <div className="card stack fb-section fb-burn">
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
            <label className="field">
              Time <span className="muted small">(optional)</span>
              <input
                className="input"
                type="time"
                value={burnTime}
                onChange={(e) => setBurnTime(e.target.value)}
              />
            </label>
          </div>
          <button type="button" className="btn btn-primary" disabled={savingBurn} onClick={addBurn}>
            {savingBurn ? 'Adding…' : '+ Add burn'}
          </button>
        </div>
      )}

      {/* The day as a single chronological timeline (latest first), with the "still to do" block on top
          and the passive Kindle line at the very bottom (the first thing that happens each day). */}
      <div className="card stack">
        <h2>Today's timeline <span className="muted small">latest first</span></h2>

        {loading && !hasAnyEvent && pendingTasks.length === 0 && !pendingWeighIn && (
          <p className="muted">Loading…</p>
        )}

        {/* "Still to do": pending planned fuel + a due, unrecorded weigh-in — pinned at the top. */}
        {(pendingTasks.length > 0 || pendingWeighIn) && (
          <div className="stack log-todo">
            <span className="small muted log-section-label">Still to do</span>
            <div className="list">
              {pendingWeighIn && weightTask && <WeighInRow task={weightTask} />}
              {pendingTasks.map((t) => (
                <PlannedMealRow
                  key={t.ref}
                  task={t}
                  busy={togglingRef === t.ref || skippingRef === t.ref}
                  onToggle={() => toggleTask(t.ref, t.done)}
                  onSkip={() => skipTask(t.ref, false)}
                />
              ))}
            </div>
          </div>
        )}

        {/* Skipped planned meals — a muted area with an Undo to bring each back into "still to do". They
            count for nothing toward the day's totals. */}
        {skippedTasks.length > 0 && (
          <div className="stack log-todo">
            <span className="small muted log-section-label">Skipped</span>
            <div className="list">
              {skippedTasks.map((t) => (
                <SkippedMealRow
                  key={t.ref}
                  task={t}
                  busy={skippingRef === t.ref}
                  onUndo={() => skipTask(t.ref, true)}
                />
              ))}
            </div>
          </div>
        )}

        {/* Completed, time-ordered events. */}
        {hasAnyEvent && (
          <div className="stack">
            {(pendingTasks.length > 0 || pendingWeighIn) && (
              <span className="small muted log-section-label">Done</span>
            )}
            <div className="list">
              {completedItems.map((item) => (
                <CompletedRow
                  key={item.key}
                  task={tasks.find((t) => `task:${t.ref}` === item.key)}
                  busy={item.key.startsWith('task:') && togglingRef === item.key.slice('task:'.length)}
                  onToggle={
                    item.key.startsWith('task:')
                      ? () => toggleTask(item.key.slice('task:'.length), true)
                      : undefined
                  }
                  node={item.node}
                />
              ))}
            </div>
          </div>
        )}

        {isEmptyDay && (
          <p className="muted small" style={{ margin: 0 }}>
            Nothing logged for this day yet. Use “+ Add fuel” or “+ Add burn” above to get started.
          </p>
        )}

        {/* Kindle — the day's passive baseline; sits at the very bottom (start of the day). Computed
            server-side from the profile + latest weight; prompts to complete the profile when unknown. */}
        <div className="list">
          <div className="card ingredient-item log-kindle">
            <div className="ingredient-thumb">🔥</div>
            <div style={{ flex: 1, minWidth: 0 }}>
              <strong>Kindle cost <span className="muted small">passive · start of day</span></strong>
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

// ---- Totals header tile. ----
function Total(
  { label, value, kind, sub, emphasis }:
  { label: string; value: string; kind?: 'fuel' | 'burn'; sub?: string; emphasis?: boolean },
) {
  return (
    <div className="log-total">
      <div className="row" style={{ gap: '0.4rem' }}>
        {kind && <FuelBurnIcon kind={kind} />}
        <span className="small muted">{label}</span>
      </div>
      <strong
        className={emphasis ? 'log-total-net' : undefined}
        style={{ fontSize: emphasis ? '1.6rem' : '1.4rem' }}
      >
        {value}
      </strong>
      {sub && <span className="small muted">{sub}</span>}
    </div>
  )
}

// ---- Timeline row components. ----

// A pending planned meal: a checkbox to complete it, plus a "Skip" button to drop it from "still to do".
// The checkbox + meal text live in a <label> (so tapping the text completes it); the Skip button sits
// outside that label so it doesn't also toggle the checkbox.
function PlannedMealRow(
  { task, busy, onToggle, onSkip }:
  { task: PlannedMealTask; busy: boolean; onToggle: () => void; onSkip: () => void },
) {
  const kcal = task.nutrition.find((n) => n.nutrientId === 'calories')?.value
  return (
    <div className="card ingredient-item">
      <label className="row" style={{ flex: 1, minWidth: 0, gap: '0.6rem', cursor: 'pointer', margin: 0 }}>
        <input
          type="checkbox"
          checked={false}
          disabled={busy}
          onChange={onToggle}
          style={{ width: '1.25rem', height: '1.25rem' }}
        />
        <div className="fb-icon fb-fuel" aria-hidden="true">🍽️</div>
        <div style={{ flex: 1, minWidth: 0 }}>
          <strong>{task.mealName ?? '(meal removed)'}</strong>
          <div className="small muted">
            {formatTime(task.time)}
            {kcal != null && ` · ${Math.round(kcal)} kcal`}
          </div>
        </div>
      </label>
      <button type="button" className="btn btn-sm" disabled={busy} onClick={onSkip} title="Skip this meal">
        Skip
      </button>
    </div>
  )
}

// A skipped planned meal: a muted row with an Undo to bring it back into "still to do". It counts for
// nothing toward the day's totals while skipped.
function SkippedMealRow(
  { task, busy, onUndo }: { task: PlannedMealTask; busy: boolean; onUndo: () => void },
) {
  const kcal = task.nutrition.find((n) => n.nutrientId === 'calories')?.value
  return (
    <div className="card ingredient-item" style={{ opacity: 0.7 }}>
      <div className="fb-icon fb-fuel" aria-hidden="true">🍽️</div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <strong style={{ textDecoration: 'line-through' }}>{task.mealName ?? '(meal removed)'}</strong>
        <div className="small muted">
          {formatTime(task.time)}
          {kcal != null && ` · ${Math.round(kcal)} kcal`}
          {' · skipped'}
        </div>
      </div>
      <button type="button" className="btn btn-sm btn-primary" disabled={busy} onClick={onUndo} title="Undo skip">
        Undo
      </button>
    </div>
  )
}

// The inner content of a completed planned meal (no checkbox — wrapped by CompletedRow).
function DoneMealRow({ task, entry }: { task: PlannedMealTask; entry?: ConsumptionEntry }) {
  const kcal = task.nutrition.find((n) => n.nutrientId === 'calories')?.value
  return (
    <>
      <div className="fb-icon fb-fuel" aria-hidden="true">🍽️</div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <strong style={{ textDecoration: 'line-through' }}>{task.mealName ?? '(meal removed)'}</strong>
        <div className="small muted">
          {entry ? formatClock(entry.createdAt) : formatTime(task.time)}
          {kcal != null && ` · ${Math.round(kcal)} kcal`}
          {' · planned'}
        </div>
      </div>
    </>
  )
}

// A completed event row. For completed planned meals we wrap the content in a checkbox <label> so it
// can be unchecked (un-complete the task); other events render their node directly inside a card.
function CompletedRow(
  { task, busy, onToggle, node }:
  { task?: PlannedMealTask; busy: boolean; onToggle?: () => void; node: React.ReactNode },
) {
  if (task && onToggle) {
    return (
      <label className="card ingredient-item" style={{ cursor: 'pointer', opacity: 0.7 }}>
        <input
          type="checkbox"
          checked
          disabled={busy}
          onChange={onToggle}
          style={{ width: '1.25rem', height: '1.25rem' }}
        />
        {node}
      </label>
    )
  }
  return <div className="card ingredient-item">{node}</div>
}

// Ad-hoc fuel entry (removable).
function FuelRow({ entry, onRemove }: { entry: ConsumptionEntry; onRemove: () => void }) {
  const kcal = entry.nutrition.find((n) => n.nutrientId === 'calories')?.value
  return (
    <>
      <div className="ingredient-thumb">
        {entry.source === 'Meal' ? '🍽️' : entry.source === 'Recipe' ? '🍳' : '🍪'}
      </div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div className="row spread">
          <strong>{entry.name}</strong>
          <button className="btn btn-sm btn-danger" title="Remove" onClick={onRemove}>✕</button>
        </div>
        <div className="small muted">
          {formatClock(entry.createdAt)}
          {' · '}{round(entry.servings)} serving{entry.servings === 1 ? '' : 's'}
          {' · '}{CONSUMPTION_SOURCE_LABELS[entry.source]}
          {kcal != null && ` · ${Math.round(kcal)} kcal`}
          {entry.sourceMissing && ' · source removed'}
        </div>
      </div>
    </>
  )
}

// Burn activity (removable).
function BurnRow({ activity, onRemove }: { activity: BurnActivity; onRemove: () => void }) {
  return (
    <>
      <div className="fb-icon fb-burn" aria-hidden="true">🔥</div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <div className="row spread">
          <strong>{activity.name}</strong>
          <button type="button" className="btn btn-sm btn-danger" title="Remove" onClick={onRemove}>✕</button>
        </div>
        <div className="small muted">
          {activity.time ? formatTime(activity.time) : formatClock(activity.createdAt)}
          {' · '}−{Math.round(activity.caloriesBurned)} kcal
        </div>
      </div>
    </>
  )
}

// The weigh-in task — pending (a measurement is due) or recorded. Links to the weight page either way.
function WeighInRow({ task }: { task: WeightTask }) {
  return (
    <div className="card ingredient-item" style={{ opacity: task.done ? 0.7 : 1 }}>
      <div className="ingredient-thumb">⚖️</div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <strong style={{ textDecoration: task.done ? 'line-through' : 'none' }}>Log your weight</strong>
        <div className="small muted">
          {task.done && task.recordedValue != null
            ? `Logged: ${round(task.recordedValue)} ${task.recordedUnit ? WEIGHT_UNIT_LABELS[task.recordedUnit] : ''}`
            : 'A weight measurement is due today.'}
        </div>
      </div>
      <Link
        className="btn btn-sm btn-primary"
        to={`/weight?returnTo=${encodeURIComponent('/log')}`}
      >
        {task.done ? 'Update' : 'Log weight'}
      </Link>
    </div>
  )
}
