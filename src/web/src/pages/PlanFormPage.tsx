import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ingredientApi, mealApi, mealPlanApi } from '../api/services'
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

interface UntimedRow {
  key: number
  kind: 'meal' | 'ingredient'
  refId: string
  quantity: string
  note: string
}

let nextKey = 1

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

  // Pickers: the meals/ingredients the user can reference.
  const [meals, setMeals] = useState<Meal[]>([])
  const [ingredients, setIngredients] = useState<Ingredient[]>([])

  const [loading, setLoading] = useState(!isNew)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

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

      <div className="card stack">
        <h2>Scheduled meals</h2>

        {scheduled.length === 0 && <p className="muted small" style={{ margin: 0 }}>No scheduled meals yet.</p>}

        {scheduled.map((s) => (
          <div key={s.key} className="plan-scheduled-row">
            <select className="select" value={s.mealId} disabled={readOnly}
              onChange={(e) => updateScheduled(s.key, { mealId: e.target.value })}>
              <option value="">Select a meal…</option>
              {meals.map((m) => <option key={m.id} value={m.id}>{m.name}</option>)}
            </select>
            {cadence === 'Weekly' && (
              <select className="select" value={s.dayOfWeek} disabled={readOnly}
                onChange={(e) => updateScheduled(s.key, { dayOfWeek: e.target.value as DayOfWeek | '' })}>
                <option value="">Any day</option>
                {DAYS_OF_WEEK.map((d) => <option key={d} value={d}>{d}</option>)}
              </select>
            )}
            <input className="input" type="time" value={s.time} disabled={readOnly}
              onChange={(e) => updateScheduled(s.key, { time: e.target.value })} />
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
