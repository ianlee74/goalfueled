import { useEffect, useMemo, useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { ingredientApi, mealApi, mealPlanApi, weightApi } from '../api/services'
import { NutritionLabel } from '../components/NutritionLabel'
import { useAuth } from '../auth/AuthContext'
import {
  DAYS_OF_WEEK, GOAL_LABELS, UNIT_LABELS,
  type DayOfWeek, type Ingredient, type Meal, type MealPlanCadence,
  type MealPlanInput, type NutrientAmount,
} from '../api/types'

interface ScheduledRow {
  key: number
  mealId: string
  dayOfWeek: DayOfWeek | ''
  time: string // "HH:mm" from the time input, or empty
}

// The bucket a weekly scheduled meal belongs to in the timeline: a specific day of week,
// or the day-less "Any day" bucket (applies every day).
type DayBucket = DayOfWeek | ''

interface UntimedRow {
  key: number
  kind: 'meal' | 'ingredient'
  refId: string
  quantity: string
  note: string
}

let nextKey = 1

// Today's calendar date as "yyyy-MM-dd" in the user's local time.
function todayIso(): string {
  const now = new Date()
  const offset = now.getTimezoneOffset() * 60_000
  return new Date(now.getTime() - offset).toISOString().slice(0, 10)
}

export function PlanFormPage() {
  const { id } = useParams<{ id: string }>()
  const isNew = !id || id === 'new'
  const navigate = useNavigate()
  const { profile } = useAuth()

  const [name, setName] = useState('')
  const [cadence, setCadence] = useState<MealPlanCadence>('Daily')
  const [scheduled, setScheduled] = useState<ScheduledRow[]>([])
  const [untimed, setUntimed] = useState<UntimedRow[]>([])
  const [canEdit, setCanEdit] = useState(true)

  // Drag-reorder state for the weekly timeline: the row being dragged and the row it's hovering
  // before (the drop indicator). `dragOverEnd` marks hovering past the last row in a day.
  const [dragKey, setDragKey] = useState<number | null>(null)
  const [dropBeforeKey, setDropBeforeKey] = useState<number | null>(null)
  const [dropEndBucket, setDropEndBucket] = useState<DayBucket | null>(null)

  // "Add meal to multiple days" form (Weekly only): pick a meal + optional time once, check the
  // days to add it to, then create one independent scheduled-meal row per checked day.
  const [multiMealId, setMultiMealId] = useState('')
  const [multiTime, setMultiTime] = useState('')
  const [multiDays, setMultiDays] = useState<Set<DayOfWeek>>(new Set())

  // Pickers: the meals/ingredients the user can reference.
  const [meals, setMeals] = useState<Meal[]>([])
  const [ingredients, setIngredients] = useState<Ingredient[]>([])

  const [loading, setLoading] = useState(!isNew)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Weight gate: before *starting* (creating) a meal plan the user must have logged a weight for
  // today. `null` = not yet checked; once known it gates the whole new-plan form. Editing an existing
  // plan is never gated.
  const [weightLoggedToday, setWeightLoggedToday] = useState<boolean | null>(isNew ? null : true)

  // Check the weight gate when creating a new plan: require a weight entry dated today.
  useEffect(() => {
    if (!isNew) return
    let cancelled = false
    weightApi.list()
      .then((list) => {
        if (cancelled) return
        const today = todayIso()
        setWeightLoggedToday(list.some((e) => e.date === today))
      })
      // If the check fails, fail open so a transient error can't permanently block plan creation.
      .catch(() => { if (!cancelled) setWeightLoggedToday(true) })
    return () => { cancelled = true }
  }, [isNew])

  // Load picker options once.
  useEffect(() => {
    let cancelled = false
    mealApi.list().then((l) => { if (!cancelled) setMeals(l) }).catch(() => {})
    ingredientApi.list().then((l) => { if (!cancelled) setIngredients(l) }).catch(() => {})
    return () => { cancelled = true }
  }, [])

  // Load the plan when editing.
  useEffect(() => {
    if (isNew) return
    let cancelled = false
    mealPlanApi.get(id!)
      .then((p) => {
        if (cancelled) return
        setName(p.name)
        setCadence(p.cadence)
        setScheduled(p.scheduledMeals.map((s) => ({
          key: nextKey++,
          mealId: s.mealId,
          dayOfWeek: s.dayOfWeek ?? '',
          time: s.time ? s.time.slice(0, 5) : '', // "HH:mm:ss" → "HH:mm"
        })))
        setUntimed(p.untimedItems.map((u) => ({
          key: nextKey++,
          kind: u.ingredientId ? 'ingredient' : 'meal',
          refId: u.ingredientId ?? u.mealId ?? '',
          quantity: u.quantity != null ? String(u.quantity) : '',
          note: u.note ?? '',
        })))
        setCanEdit(p.canEdit)
      })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [id, isNew])

  const mealById = useMemo(() => new Map(meals.map((m) => [m.id, m])), [meals])
  const ingredientById = useMemo(() => new Map(ingredients.map((i) => [i.id, i])), [ingredients])

  // Live nutrition summary mirroring the server (see previewSummary).
  const summary = useMemo(
    () => previewSummary(cadence, scheduled, untimed, mealById, ingredientById),
    [cadence, scheduled, untimed, mealById, ingredientById])

  const goalCalories = useMemo(() => {
    const c = summary.dailyAverage.find((n) => n.nutrientId === 'calories')
    return c ? Math.round(c.value) : null
  }, [summary])

  function buildInput(): MealPlanInput {
    return {
      name: name.trim(),
      cadence,
      scheduledMeals: scheduled
        .filter((s) => s.mealId)
        .map((s) => ({
          mealId: s.mealId,
          dayOfWeek: cadence === 'Weekly' && s.dayOfWeek ? s.dayOfWeek : undefined,
          time: s.time ? `${s.time}:00` : undefined,
        })),
      untimedItems: untimed
        .filter((u) => u.refId)
        .map((u) => u.kind === 'ingredient'
          ? {
            ingredientId: u.refId,
            quantity: u.quantity ? Number(u.quantity) : undefined,
            note: u.note.trim() || undefined,
          }
          : { mealId: u.refId, note: u.note.trim() || undefined }),
    }
  }

  // ---- row editing ----
  function addScheduled() {
    setScheduled((prev) => [...prev, {
      key: nextKey++, mealId: meals[0]?.id ?? '', dayOfWeek: '', time: '',
    }])
  }
  function updateScheduled(key: number, patch: Partial<ScheduledRow>) {
    setScheduled((prev) => prev.map((s) => (s.key === key ? { ...s, ...patch } : s)))
  }
  function removeScheduled(key: number) {
    setScheduled((prev) => prev.filter((s) => s.key !== key))
  }

  // Add a scheduled meal already pinned to a specific day-of-week bucket (used by the weekly timeline,
  // where "+ Add meal" sits under each day). bucket '' is the day-less "Any day" group.
  function addScheduledToDay(bucket: DayBucket) {
    setScheduled((prev) => [...prev, {
      key: nextKey++, mealId: meals[0]?.id ?? '', dayOfWeek: bucket, time: '',
    }])
  }

  // "Add meal to multiple days": create one independent scheduled-meal row per checked day, all
  // sharing the chosen meal + time. Each row is a fresh object (own `key`), so editing or removing
  // one day's row later never affects the others. Resets the form on success.
  function addScheduledToMultipleDays() {
    if (!multiMealId || multiDays.size === 0) return
    setScheduled((prev) => [
      ...prev,
      ...DAYS_OF_WEEK.filter((d) => multiDays.has(d)).map((d) => ({
        key: nextKey++, mealId: multiMealId, dayOfWeek: d as DayOfWeek | '', time: multiTime,
      })),
    ])
    setMultiDays(new Set())
  }

  function toggleMultiDay(day: DayOfWeek) {
    setMultiDays((prev) => {
      const next = new Set(prev)
      if (next.has(day)) next.delete(day)
      else next.add(day)
      return next
    })
  }

  // Reorder within a day's timeline: move the dragged row to sit immediately before `beforeKey`
  // (or to the end of the bucket when beforeKey is null), then snap its time so the timeline's
  // time-sort keeps it in the dropped position. This is the whole drag-reorder model — see report.
  function reorderWithinDay(bucket: DayBucket, dragKey: number, beforeKey: number | null) {
    setScheduled((prev) => {
      const inBucket = prev.filter((s) => bucketOf(s) === bucket)
      const ordered = sortTimeline(inBucket)
      const dragged = ordered.find((s) => s.key === dragKey)
      if (!dragged) return prev

      const without = ordered.filter((s) => s.key !== dragKey)
      const insertAt = beforeKey == null ? without.length : without.findIndex((s) => s.key === beforeKey)
      const target = insertAt < 0 ? without.length : insertAt
      const before = without[target - 1]
      const after = without[target]

      // Snap the dragged meal's time so the timeline's time-sort lands it in the dropped position.
      // The rule: take the predecessor's time when there is one (equal times preserve array order,
      // so the meal sits right after it); at the very top, take the successor's time so the meal
      // leads its time group; with no neighbours at all, keep the meal's own time.
      let newTime: string
      if (before) newTime = before.time      // slot in just after the predecessor (incl. untimed → '')
      else if (after) newTime = after.time   // dropped at the very top → share the successor's time
      else newTime = dragged.time            // only row in the day → unchanged

      // Rebuild this bucket's order; keep all other rows (other buckets) where they are.
      const newBucketOrder = [
        ...without.slice(0, target),
        { ...dragged, time: newTime },
        ...without.slice(target),
      ]
      const queue = [...newBucketOrder]
      return prev.map((s) => (bucketOf(s) === bucket ? queue.shift()! : s))
    })
  }

  function addUntimed() {
    setUntimed((prev) => [...prev, {
      key: nextKey++, kind: 'ingredient', refId: ingredients[0]?.id ?? '', quantity: '', note: '',
    }])
  }
  function updateUntimed(key: number, patch: Partial<UntimedRow>) {
    setUntimed((prev) => prev.map((u) => (u.key === key ? { ...u, ...patch } : u)))
  }
  function removeUntimed(key: number) {
    setUntimed((prev) => prev.filter((u) => u.key !== key))
  }

  async function save(): Promise<string | null> {
    setError(null)
    // Guard the gate server-trip-free: a new plan can't be started without today's weight.
    if (isNew && weightLoggedToday === false) {
      setError('Record your current weight before starting a meal plan.')
      return null
    }
    if (!name.trim()) { setError('Name is required.'); return null }
    setSaving(true)
    try {
      const input = buildInput()
      const saved = isNew ? await mealPlanApi.create(input) : await mealPlanApi.update(id!, input)
      return saved.id
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
      setSaving(false)
      return null
    }
  }

  async function saveAndExit() {
    const savedId = await save()
    if (savedId) navigate('/plans', { replace: true })
  }

  async function remove() {
    if (!id || isNew) return
    if (!confirm('Delete this meal plan? This cannot be undone.')) return
    setSaving(true)
    try {
      await mealPlanApi.remove(id)
      navigate('/plans', { replace: true })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Delete failed.')
      setSaving(false)
    }
  }

  if (loading) return <p className="muted">Loading…</p>

  const readOnly = !isNew && !canEdit

  // Gate: starting a new plan requires today's weight to be recorded first.
  if (isNew && weightLoggedToday === null) return <p className="muted">Loading…</p>
  if (isNew && weightLoggedToday === false) {
    return (
      <div className="stack">
        <div className="row spread">
          <h1>New meal plan</h1>
          <button className="btn btn-sm" onClick={() => navigate(-1)}>Back</button>
        </div>
        <div className="card stack">
          <h2>Record your weight first</h2>
          <p className="muted" style={{ margin: 0 }}>
            Before starting a meal plan, log your current weight so we can track your progress.
          </p>
          <Link className="btn btn-primary" to="/weight?returnTo=/plans/new">Go to Weight</Link>
        </div>
      </div>
    )
  }

  return (
    <div className="stack">
      <div className="row spread">
        <h1>{isNew ? 'New meal plan' : readOnly ? 'Meal plan' : 'Edit meal plan'}</h1>
        <button className="btn btn-sm" onClick={() => navigate(-1)}>Back</button>
      </div>

      {error && <div className="notice error">{error}</div>}
      {readOnly && <div className="notice info">This plan is read-only. You can view it but not edit it.</div>}

      <div className="card stack">
        <label className="field">
          Name
          <input className="input" value={name} disabled={readOnly}
            onChange={(e) => setName(e.target.value)} placeholder="e.g. Cutting Week" />
        </label>

        <div className="field">
          Cadence
          <div className="row" style={{ gap: '0.5rem' }}>
            {(['Daily', 'Weekly'] as MealPlanCadence[]).map((c) => (
              <button key={c} type="button" disabled={readOnly}
                className={`btn btn-sm ${cadence === c ? 'btn-primary' : ''}`}
                onClick={() => setCadence(c)}>
                {c}
              </button>
            ))}
          </div>
          <span className="muted small">
            {cadence === 'Daily'
              ? 'One repeating day. Meal times are optional; days are ignored.'
              : 'A full week. Pin meals to a day of week; untimed items apply every day.'}
          </span>
        </div>
      </div>

      {cadence === 'Weekly' ? (
        <div className="card stack">
          <h2>Weekly timeline <span className="muted small">drag meals up or down to reorder a day</span></h2>

          {scheduled.length === 0 && (
            <p className="muted small" style={{ margin: 0 }}>No scheduled meals yet.</p>
          )}

          {!readOnly && (
            <div className="plan-multi-add stack">
              <strong className="small">Add a meal to multiple days</strong>
              <div className="plan-multi-controls">
                <select className="select" value={multiMealId} title="Meal"
                  onChange={(e) => setMultiMealId(e.target.value)}>
                  <option value="">Select a meal…</option>
                  {meals.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
                </select>
                <input className="input" type="time" value={multiTime} title="Time of day (optional)"
                  onChange={(e) => setMultiTime(e.target.value)} />
                <button className="btn btn-sm btn-primary" type="button"
                  disabled={!multiMealId || multiDays.size === 0}
                  onClick={addScheduledToMultipleDays}>
                  Add to {multiDays.size || 'selected'} day{multiDays.size === 1 ? '' : 's'}
                </button>
              </div>
              <div className="plan-multi-days">
                {DAYS_OF_WEEK.map((d) => (
                  <label key={d} className={`plan-day-chip ${multiDays.has(d) ? 'on' : ''}`}>
                    <input type="checkbox" checked={multiDays.has(d)}
                      onChange={() => toggleMultiDay(d)} />
                    {d.slice(0, 3)}
                  </label>
                ))}
              </div>
              <span className="muted small">
                Adds one entry per selected day. Each can be moved or re-timed independently afterward.
              </span>
            </div>
          )}

          {[...DAYS_OF_WEEK, '' as DayBucket].map((bucket) => {
            const rows = sortTimeline(scheduled.filter((s) => bucketOf(s) === bucket))
            const label = bucket === '' ? 'Any day' : bucket
            return (
              <div key={bucket || '__any'} className="plan-day">
                <div className="plan-day-head">
                  <strong>{label}</strong>
                  {bucket === '' && <span className="muted small">applies to every day</span>}
                </div>

                <div className="plan-timeline">
                  {rows.length === 0 && (
                    <p className="muted small plan-timeline-empty">No meals on this day yet.</p>
                  )}
                  {rows.map((s) => (
                    <div
                      key={s.key}
                      className={[
                        'plan-tl-row',
                        dragKey === s.key ? 'dragging' : '',
                        dropBeforeKey === s.key ? 'drop-before' : '',
                      ].filter(Boolean).join(' ')}
                      draggable={!readOnly}
                      onDragStart={(e) => {
                        setDragKey(s.key)
                        e.dataTransfer.effectAllowed = 'move'
                      }}
                      onDragOver={(e) => {
                        if (readOnly || dragKey == null) return
                        // Only allow reordering within the same day bucket.
                        if (bucketOf(scheduled.find((r) => r.key === dragKey)!) !== bucket) return
                        e.preventDefault()
                        setDropBeforeKey(s.key)
                        setDropEndBucket(null)
                      }}
                      onDrop={(e) => {
                        if (readOnly || dragKey == null) return
                        e.preventDefault()
                        reorderWithinDay(bucket, dragKey, s.key)
                        setDragKey(null); setDropBeforeKey(null); setDropEndBucket(null)
                      }}
                      onDragEnd={() => { setDragKey(null); setDropBeforeKey(null); setDropEndBucket(null) }}
                    >
                      {!readOnly && <span className="plan-tl-handle" title="Drag to reorder" aria-hidden>⠿</span>}
                      <span className="plan-tl-time">{s.time || 'Anytime'}</span>
                      <ScheduledMealFields
                        row={s} meals={meals} readOnly={readOnly}
                        showDay onUpdate={(patch) => updateScheduled(s.key, patch)} />
                      {!readOnly && (
                        <button className="btn btn-sm btn-danger" title="Remove"
                          onClick={() => removeScheduled(s.key)}>✕</button>
                      )}
                    </div>
                  ))}

                  {/* Drop zone at the end of the day's timeline. */}
                  {!readOnly && (
                    <div
                      className={`plan-tl-end ${dropEndBucket === bucket ? 'drop-before' : ''}`}
                      onDragOver={(e) => {
                        if (dragKey == null) return
                        if (bucketOf(scheduled.find((r) => r.key === dragKey)!) !== bucket) return
                        e.preventDefault()
                        setDropBeforeKey(null)
                        setDropEndBucket(bucket)
                      }}
                      onDrop={(e) => {
                        if (dragKey == null) return
                        e.preventDefault()
                        reorderWithinDay(bucket, dragKey, null)
                        setDragKey(null); setDropBeforeKey(null); setDropEndBucket(null)
                      }}
                    />
                  )}
                </div>

                {!readOnly && (
                  <button className="btn btn-sm plan-day-add" onClick={() => addScheduledToDay(bucket)}
                    disabled={meals.length === 0}>
                    + Add meal{bucket === '' ? ' (any day)' : ` to ${bucket}`}
                  </button>
                )}
              </div>
            )
          })}
        </div>
      ) : (
        <div className="card stack">
          <h2>Scheduled meals</h2>

          {scheduled.length === 0 && <p className="muted small" style={{ margin: 0 }}>No scheduled meals yet.</p>}

          {scheduled.map((s) => (
            <div key={s.key} className="plan-scheduled-row">
              <ScheduledMealFields
                row={s} meals={meals} readOnly={readOnly}
                showDay={false} onUpdate={(patch) => updateScheduled(s.key, patch)} />
              {!readOnly && (
                <button className="btn btn-sm btn-danger" title="Remove"
                  onClick={() => removeScheduled(s.key)}>✕</button>
              )}
            </div>
          ))}

          {!readOnly && (
            <button className="btn btn-sm" onClick={addScheduled} disabled={meals.length === 0}>
              + Add scheduled meal
            </button>
          )}
        </div>
      )}

      <div className="card stack">
        <h2>Untimed items <span className="muted small">consumed through the day</span></h2>

        {untimed.length === 0 && <p className="muted small" style={{ margin: 0 }}>No untimed items yet.</p>}

        {untimed.map((u) => (
          <div key={u.key} className="plan-untimed-row">
            <select className="select" value={u.kind} disabled={readOnly}
              onChange={(e) => updateUntimed(u.key, { kind: e.target.value as 'meal' | 'ingredient', refId: '' })}>
              <option value="ingredient">Ingredient</option>
              <option value="meal">Meal</option>
            </select>
            <select className="select" value={u.refId} disabled={readOnly}
              onChange={(e) => updateUntimed(u.key, { refId: e.target.value })}>
              <option value="">Select…</option>
              {(u.kind === 'ingredient' ? ingredients : meals).map((o) =>
                <option key={o.id} value={o.id}>{o.name}</option>)}
            </select>
            {u.kind === 'ingredient' ? (
              <input className="input" type="number" min="0" step="any" placeholder="qty"
                value={u.quantity} disabled={readOnly}
                onChange={(e) => updateUntimed(u.key, { quantity: e.target.value })}
                title={unitHint(u.refId, ingredientById)} />
            ) : <span />}
            <input className="input" placeholder="note (optional)" value={u.note} disabled={readOnly}
              onChange={(e) => updateUntimed(u.key, { note: e.target.value })} />
            {!readOnly && (
              <button className="btn btn-sm btn-danger" title="Remove"
                onClick={() => removeUntimed(u.key)}>✕</button>
            )}
          </div>
        ))}

        {!readOnly && (
          <button className="btn btn-sm" onClick={addUntimed}
            disabled={ingredients.length === 0 && meals.length === 0}>
            + Add untimed item
          </button>
        )}
      </div>

      <div className="card stack">
        <h2>Daily nutrition
          <span className="muted small">{cadence === 'Weekly' ? ' average per day' : ' per day'}</span>
        </h2>
        {profile?.primaryGoal && (
          <p className="muted small" style={{ margin: 0 }}>
            Your goal: <strong>{GOAL_LABELS[profile.primaryGoal]}</strong>
            {goalCalories != null && ` · ${goalCalories} kcal/day`}
          </p>
        )}
        <NutritionLabel amounts={summary.dailyAverage} />
      </div>

      {cadence === 'Weekly' && summary.days.length > 0 && (
        <div className="card stack">
          <h2>Per-day breakdown</h2>
          {summary.days.map((d) => (
            <div key={d.dayOfWeek ?? 'all'} className="stack" style={{ gap: '0.25rem' }}>
              <strong className="small">{d.dayOfWeek ?? 'Every day'}</strong>
              <NutritionLabel amounts={d.nutrition} />
            </div>
          ))}
          <h2>Weekly total</h2>
          <NutritionLabel amounts={summary.weeklyTotal} />
        </div>
      )}

      {!readOnly && (
        <div className="row" style={{ gap: '0.5rem', flexWrap: 'wrap' }}>
          <button className="btn btn-primary" style={{ flex: 1 }} disabled={saving} onClick={saveAndExit}>
            {saving ? 'Saving…' : 'Save'}
          </button>
          {!isNew && (
            <button className="btn btn-danger" disabled={saving} onClick={remove}>Delete</button>
          )}
        </div>
      )}
    </div>
  )
}

// The timeline bucket a scheduled row belongs to: its day-of-week, or '' for the day-less group.
function bucketOf(s: ScheduledRow): DayBucket {
  return s.dayOfWeek
}

// Order a day's meals as a daily schedule: earliest time first, untimed ("Anytime") meals last.
// Rows with equal time (and the whole untimed group) keep their current array order — that array
// order is exactly what drag-reordering manipulates and what persists round-trip.
function sortTimeline(rows: ScheduledRow[]): ScheduledRow[] {
  return rows
    .map((row, i) => ({ row, i }))
    .sort((a, b) => {
      const at = a.row.time, bt = b.row.time
      if (at && bt) return at < bt ? -1 : at > bt ? 1 : a.i - b.i
      if (at) return -1 // timed before untimed
      if (bt) return 1
      return a.i - b.i // both untimed: preserve array order
    })
    .map((x) => x.row)
}

// The meal + (optional) day-of-week + time editing controls for one scheduled meal. Shared by the
// weekly timeline and the daily flat editor so the add/edit controls stay in one place — making the
// upcoming multi-day-select and 15-minute time-picker changes a single-spot edit.
function ScheduledMealFields({ row, meals, readOnly, showDay, onUpdate }: {
  row: ScheduledRow
  meals: Meal[]
  readOnly: boolean
  showDay: boolean
  onUpdate: (patch: Partial<ScheduledRow>) => void
}) {
  return (
    <>
      <select className="select" value={row.mealId} disabled={readOnly} title="Meal"
        onChange={(e) => onUpdate({ mealId: e.target.value })}>
        <option value="">Select a meal…</option>
        {meals.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
      </select>
      {showDay && (
        <select className="select" value={row.dayOfWeek} disabled={readOnly} title="Day of week"
          onChange={(e) => onUpdate({ dayOfWeek: e.target.value as DayOfWeek | '' })}>
          <option value="">Any day</option>
          {DAYS_OF_WEEK.map((d) => <option key={d} value={d}>{d}</option>)}
        </select>
      )}
      <input className="input" type="time" value={row.time} disabled={readOnly} title="Time of day"
        onChange={(e) => onUpdate({ time: e.target.value })} />
    </>
  )
}

function unitHint(ingredientId: string, byId: Map<string, Ingredient>): string {
  const ing = byId.get(ingredientId)
  return ing ? `in ${UNIT_LABELS[ing.unit]} (1 serving = ${ing.servingSize})` : ''
}

// ---- client-side summary preview (mirrors MealPlanService.BuildSummary) ----

type Totals = Map<string, number>

function addInto(target: Totals, amounts: NutrientAmount[], factor = 1) {
  for (const n of amounts) target.set(n.nutrientId, (target.get(n.nutrientId) ?? 0) + n.value * factor)
}

function toAmounts(totals: Totals): NutrientAmount[] {
  return [...totals].map(([nutrientId, value]) => ({ nutrientId, value }))
}

function mealNutrition(mealId: string, byId: Map<string, Meal>): NutrientAmount[] {
  return byId.get(mealId)?.totalNutrition ?? []
}

function untimedNutrition(u: UntimedRow, meals: Map<string, Meal>, ingredients: Map<string, Ingredient>): NutrientAmount[] {
  if (u.kind === 'meal') return mealNutrition(u.refId, meals)
  const ing = ingredients.get(u.refId)
  if (!ing || !(ing.servingSize > 0)) return []
  // Quantity is in the ingredient's own unit; empty means one serving.
  const qty = u.quantity ? Number(u.quantity) : ing.servingSize
  const servings = qty / ing.servingSize
  return ing.nutritionPerServing.map((n) => ({ nutrientId: n.nutrientId, value: n.value * servings }))
}

interface PreviewSummary {
  days: { dayOfWeek: DayOfWeek | null; nutrition: NutrientAmount[] }[]
  weeklyTotal: NutrientAmount[]
  dailyAverage: NutrientAmount[]
}

function previewSummary(
  cadence: MealPlanCadence,
  scheduled: ScheduledRow[],
  untimed: UntimedRow[],
  meals: Map<string, Meal>,
  ingredients: Map<string, Ingredient>,
): PreviewSummary {
  // Contributions consumed every day: untimed items + day-less scheduled meals.
  const everyDay: NutrientAmount[][] = []
  for (const u of untimed) if (u.refId) everyDay.push(untimedNutrition(u, meals, ingredients))
  for (const s of scheduled) {
    if (!s.mealId) continue
    if (cadence === 'Daily' || !s.dayOfWeek) everyDay.push(mealNutrition(s.mealId, meals))
  }

  if (cadence === 'Daily') {
    const totals: Totals = new Map()
    for (const c of everyDay) addInto(totals, c)
    const amounts = toAmounts(totals)
    return { days: [{ dayOfWeek: null, nutrition: amounts }], weeklyTotal: amounts, dailyAverage: amounts }
  }

  // Weekly: every-day sum applies to each day; day-pinned meals add to their day.
  const everyDayTotals: Totals = new Map()
  for (const c of everyDay) addInto(everyDayTotals, c)

  const days: { dayOfWeek: DayOfWeek | null; nutrition: NutrientAmount[] }[] = []
  const weekTotals: Totals = new Map()
  for (const day of DAYS_OF_WEEK) {
    const dayTotals: Totals = new Map(everyDayTotals)
    for (const s of scheduled) {
      if (s.mealId && s.dayOfWeek === day) addInto(dayTotals, mealNutrition(s.mealId, meals))
    }
    if (dayTotals.size === 0) continue
    const amounts = toAmounts(dayTotals)
    days.push({ dayOfWeek: day, nutrition: amounts })
    addInto(weekTotals, amounts)
  }

  const weeklyTotal = toAmounts(weekTotals)
  const dailyAverage = weeklyTotal.map((n) => ({ nutrientId: n.nutrientId, value: n.value / DAYS_OF_WEEK.length }))
  return { days, weeklyTotal, dailyAverage }
}
