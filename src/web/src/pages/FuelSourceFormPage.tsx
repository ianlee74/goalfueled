import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { mealApi, recipeApi } from '../api/services'
import { NutritionLabel } from '../components/NutritionLabel'
import { useConfirm } from '../hooks/useConfirm'
import { useAuth } from '../auth/AuthContext'
import type { FuelType, MealInput, MealPortionInput, NutrientAmount, Recipe } from '../api/types'
import { FUEL_TYPE_LABELS } from '../api/types'

interface PortionRow extends MealPortionInput {
  // Local-only display field; not sent to the API.
  key: number
}

let nextKey = 1

export function FuelSourceFormPage() {
  const { id } = useParams<{ id: string }>()
  const isNew = !id || id === 'new'
  const navigate = useNavigate()
  const { confirm, dialog } = useConfirm()
  const { profile } = useAuth()

  const [name, setName] = useState('')
  const [servings, setServings] = useState('1')
  const [fuelType, setFuelType] = useState<FuelType>('Food')
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

  // Load the fuel source when editing.
  useEffect(() => {
    if (isNew) return
    let cancelled = false
    mealApi.get(id!)
      .then((m) => {
        if (cancelled) return
        setName(m.name)
        setServings(String(m.servings))
        setFuelType(m.fuelType)
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
      servings: Number(servings),
      fuelType,
    }
  }

  // Live nutrition: roll it up client-side whenever portions change. This mirrors the server's math
  // (see previewNutrition): sum over portions of the recipe's per-serving nutrition × portion servings.
  const { total, perServing } = useMemo(
    () => previewNutrition(portions, Number(servings), recipeById),
    [portions, servings, recipeById])

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
    const serv = Number(servings)
    if (!Number.isFinite(serv) || serv <= 0) { setError('Servings must be greater than zero.'); return null }

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
    if (savedId) navigate('/fuel-sources', { replace: true })
  }

  function remove() {
    if (!id || isNew) return
    confirm({
      title: 'Delete this fuel source?',
      message: 'This cannot be undone.',
      confirmLabel: 'Delete',
      pendingLabel: 'Deleting…',
      onConfirm: async () => {
        setSaving(true)
        try {
          await mealApi.remove(id)
          navigate('/fuel-sources', { replace: true })
        } catch (e) {
          setError(e instanceof Error ? e.message : 'Delete failed.')
          setSaving(false)
          throw e
        }
      },
    })
  }

  if (loading) return <p className="muted">Loading…</p>

  const readOnly = !isNew && !canEdit

  return (
    <div className="stack">
      {dialog}
      <div className="row spread">
        <h1>{isNew ? 'New fuel source' : readOnly ? 'Fuel source' : 'Edit fuel source'}</h1>
        <button className="btn btn-sm" onClick={() => navigate(-1)}>Back</button>
      </div>

      {error && <div className="notice error">{error}</div>}
      {readOnly && <div className="notice info">This fuel source is read-only. You can view it but not edit it.</div>}

      <div className="card stack">
        <label className="field">
          Name
          <input className="input" value={name} disabled={readOnly}
            onChange={(e) => setName(e.target.value)} placeholder="e.g. Post-Workout Lunch" />
        </label>

        <label className="field" style={{ maxWidth: 160 }}>
          Servings
          <input className="input" type="number" min="0" step="any" value={servings}
            disabled={readOnly} onChange={(e) => setServings(e.target.value)} />
        </label>

        <label className="field" style={{ maxWidth: 220 }}>
          Fuel type
          <select className="select" value={fuelType} disabled={readOnly}
            onChange={(e) => setFuelType(e.target.value as FuelType)}>
            {(Object.entries(FUEL_TYPE_LABELS) as [FuelType, string][]).map(([value, label]) => (
              <option key={value} value={value}>{label}</option>
            ))}
          </select>
        </label>

        {profile?.isActingAsAdmin && (
          <label className="toggle">
            <input type="checkbox" checked={appLevel} disabled={readOnly}
              onChange={(e) => setAppLevel(e.target.checked)} />
            Make this an app-level fuel source (visible to all users)
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
        <h2>Nutrition</h2>
        <NutritionLabel columns={[
          { label: 'Whole fuel source', amounts: total },
          { label: 'Per serving', amounts: perServing },
        ]} />
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
// Per-serving divides the meal total by the meal's serving yield (mirrors MealNutritionCalculator).
function previewNutrition(
  portions: PortionRow[], servings: number, byId: Map<string, Recipe>,
): { total: NutrientAmount[]; perServing: NutrientAmount[] } {
  const totals = new Map<string, number>()
  for (const portion of portions) {
    const recipe = byId.get(portion.recipeId)
    if (!recipe || !(portion.servings > 0)) continue
    for (const n of recipe.nutritionPerServing) {
      totals.set(n.nutrientId, (totals.get(n.nutrientId) ?? 0) + n.value * portion.servings)
    }
  }
  const total = [...totals].map(([nutrientId, value]) => ({ nutrientId, value }))
  const perServing = servings > 0
    ? total.map((t) => ({ nutrientId: t.nutrientId, value: t.value / servings }))
    : total
  return { total, perServing }
}
