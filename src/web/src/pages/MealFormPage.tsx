import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { mealApi, recipeApi } from '../api/services'
import { NutritionLabel } from '../components/NutritionLabel'
import { useAuth } from '../auth/AuthContext'
import type { MealInput, MealPortionInput, NutrientAmount, Recipe } from '../api/types'

interface PortionRow extends MealPortionInput {
  // Local-only display field; not sent to the API.
  key: number
}

let nextKey = 1

export function MealFormPage() {
  const { id } = useParams<{ id: string }>()
  const isNew = !id || id === 'new'
  const navigate = useNavigate()
  const { profile } = useAuth()

  const [name, setName] = useState('')
  const [appLevel, setAppLevel] = useState(false)
  const [portions, setPortions] = useState<PortionRow[]>([])
  const [canEdit, setCanEdit] = useState(true)

  // Recipe catalog the user can portion from (their own + app-level + public).
  const [recipes, setRecipes] = useState<Recipe[]>([])

  const [loading, setLoading] = useState(!isNew)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Load recipe picker options once.
  useEffect(() => {
    let cancelled = false
    recipeApi.list()
      .then((list) => { if (!cancelled) setRecipes(list) })
      .catch(() => {})
    return () => { cancelled = true }
  }, [])

  // Load the meal when editing.
  useEffect(() => {
    if (isNew) return
    let cancelled = false
    mealApi.get(id!)
      .then((m) => {
        if (cancelled) return
        setName(m.name)
        setAppLevel(m.scope === 'App')
        setPortions(m.portions.map((p) => ({ key: nextKey++, recipeId: p.recipeId, servings: p.servings })))
        setCanEdit(m.canEdit)
      })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [id, isNew])

  const recipeById = useMemo(
    () => new Map(recipes.map((r) => [r.id, r])), [recipes])

  // Build the input payload from current form state.
  function buildInput(): MealInput {
    return {
      name: name.trim(),
      portions: portions
        .filter((p) => p.recipeId && Number(p.servings) > 0)
        .map((p) => ({ recipeId: p.recipeId, servings: Number(p.servings) })),
      appLevel: appLevel && !!profile?.isActingAsAdmin,
    }
  }

  // Live nutrition: roll it up client-side whenever portions change. This mirrors the server's math
  // (see previewNutrition): sum over portions of the recipe's per-serving nutrition × portion servings.
  const total = useMemo(
    () => previewNutrition(portions, recipeById),
    [portions, recipeById])

  function addPortion() {
    const first = recipes[0]
    setPortions((prev) => [...prev, {
      key: nextKey++,
      recipeId: first?.id ?? '',
      servings: 1,
    }])
  }
  function updatePortion(key: number, patch: Partial<PortionRow>) {
    setPortions((prev) => prev.map((p) => (p.key === key ? { ...p, ...patch } : p)))
  }
  function removePortion(key: number) {
    setPortions((prev) => prev.filter((p) => p.key !== key))
  }

  async function save(): Promise<string | null> {
    setError(null)
    if (!name.trim()) { setError('Name is required.'); return null }

    setSaving(true)
    try {
      const input = buildInput()
      const saved = isNew ? await mealApi.create(input) : await mealApi.update(id!, input)
      return saved.id
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
      setSaving(false)
      return null
    }
  }

  async function saveAndExit() {
    const savedId = await save()
    if (savedId) navigate('/meals', { replace: true })
  }

  async function remove() {
    if (!id || isNew) return
    if (!confirm('Delete this meal? This cannot be undone.')) return
    setSaving(true)
    try {
      await mealApi.remove(id)
      navigate('/meals', { replace: true })
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
        <h1>{isNew ? 'New meal' : readOnly ? 'Meal' : 'Edit meal'}</h1>
        <button className="btn btn-sm" onClick={() => navigate(-1)}>Back</button>
      </div>

      {error && <div className="notice error">{error}</div>}
      {readOnly && <div className="notice info">This meal is read-only. You can view it but not edit it.</div>}

      <div className="card stack">
        <label className="field">
          Name
          <input className="input" value={name} disabled={readOnly}
            onChange={(e) => setName(e.target.value)} placeholder="e.g. Post-Workout Lunch" />
        </label>

        {profile?.isActingAsAdmin && (
          <label className="toggle">
            <input type="checkbox" checked={appLevel} disabled={readOnly}
              onChange={(e) => setAppLevel(e.target.checked)} />
            Make this an app-level meal (visible to all users)
          </label>
        )}
      </div>

      <div className="card stack">
        <h2>Recipe portions</h2>

        {portions.length === 0 && <p className="muted small" style={{ margin: 0 }}>No recipes added yet.</p>}

        {portions.map((portion) => (
          <div key={portion.key} className="recipe-item-row">
            <select className="select" value={portion.recipeId} disabled={readOnly}
              onChange={(e) => updatePortion(portion.key, { recipeId: e.target.value })}>
              <option value="">Select a recipe…</option>
              {recipes.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
            </select>
            <input className="input" type="number" min="0" step="any" value={portion.servings}
              disabled={readOnly}
              onChange={(e) => updatePortion(portion.key, { servings: Number(e.target.value) })} />
            <span className="muted small" style={{ alignSelf: 'center' }}>servings</span>
            {!readOnly && (
              <button className="btn btn-sm btn-danger" title="Remove"
                onClick={() => removePortion(portion.key)}>✕</button>
            )}
          </div>
        ))}

        {!readOnly && (
          <button className="btn btn-sm" onClick={addPortion} disabled={recipes.length === 0}>
            + Add recipe portion
          </button>
        )}
      </div>

      <div className="card stack">
        <h2>Nutrition <span className="muted small">whole meal</span></h2>
        <NutritionLabel amounts={total} />
      </div>

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

// Client-side roll-up mirroring the server: sum over portions of the referenced recipe's per-serving
// nutrition scaled by the portion's serving count. Portions whose recipe is missing are skipped.
function previewNutrition(
  portions: PortionRow[], byId: Map<string, Recipe>,
): NutrientAmount[] {
  const totals = new Map<string, number>()
  for (const portion of portions) {
    const recipe = byId.get(portion.recipeId)
    if (!recipe || !(portion.servings > 0)) continue
    for (const n of recipe.nutritionPerServing) {
      totals.set(n.nutrientId, (totals.get(n.nutrientId) ?? 0) + n.value * portion.servings)
    }
  }
  return [...totals].map(([nutrientId, value]) => ({ nutrientId, value }))
}
