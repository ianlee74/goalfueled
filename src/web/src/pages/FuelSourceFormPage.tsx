import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { mealApi, recipeApi } from '../api/services'
import { NutritionLabel } from '../components/NutritionLabel'
import { useConfirm } from '../hooks/useConfirm'
import { useAuth } from '../auth/AuthContext'
import type { FuelType, MealInput, MealPortionInput, NutrientAmount, Recipe } from '../api/types'
import { FUEL_TYPE_LABELS } from '../api/types'

// Friendly lower-case label for the picker's empty/hint state.
const FUEL_TYPE_HINT: Record<FuelType, string> = {
  Food: 'food', Supplement: 'supplement', Hydration: 'hydration',
}

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

  // A Fuel Source can only be composed of recipes whose fuel type matches its own, so the picker only
  // offers recipes of the currently-selected fuel type. Already-added portions are kept even if their
  // recipe no longer matches (the user chose them deliberately); those mismatched rows surface a hint
  // below and keep their current recipe selectable so the dropdown still shows what's chosen.
  const matchingRecipes = useMemo(
    () => recipes.filter((r) => r.fuelType === fuelType), [recipes, fuelType])

  // The fuel type is locked while any recipe portion exists: portions are constrained to the chosen
  // fuel type, so switching it would orphan them. Removing the last portion unlocks it again. This is
  // enforced on the backend too; here it's just good UX. (New sources start empty, so it's editable.)
  const fuelTypeLocked = portions.length > 0

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
    // New rows default to the first recipe matching the selected fuel type (the picker only offers those).
    const first = matchingRecipes[0]
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
          <select className="select" value={fuelType} disabled={readOnly || fuelTypeLocked}
            onChange={(e) => setFuelType(e.target.value as FuelType)}>
            {(Object.entries(FUEL_TYPE_LABELS) as [FuelType, string][]).map(([value, label]) => (
              <option key={value} value={value}>{label}</option>
            ))}
          </select>
          {!readOnly && fuelTypeLocked && (
            <span className="muted small">Remove all recipe portions to change the fuel type.</span>
          )}
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
        <p className="muted small" style={{ margin: 0 }}>
          Only <strong>{FUEL_TYPE_HINT[fuelType]}</strong> recipes can be portioned into a {FUEL_TYPE_HINT[fuelType]} fuel source.
        </p>

        {portions.length === 0 && <p className="muted small" style={{ margin: 0 }}>No recipes added yet.</p>}

        {portions.map((portion) => {
          // A previously-chosen recipe that no longer matches the selected fuel type is kept (the user
          // picked it on purpose), but flagged. Keep it as an option so the dropdown still shows it.
          const chosen = recipeById.get(portion.recipeId)
          const mismatched = !!chosen && chosen.fuelType !== fuelType
          return (
            <div key={portion.key} className="stack" style={{ gap: '0.25rem' }}>
              <div className="recipe-item-row">
                <select className="select" value={portion.recipeId} disabled={readOnly}
                  title="Recipe"
                  onChange={(e) => updatePortion(portion.key, { recipeId: e.target.value })}>
                  <option value="">Select a recipe…</option>
                  {mismatched && chosen && (
                    <option value={chosen.id}>
                      {chosen.name} ({FUEL_TYPE_LABELS[chosen.fuelType]})
                    </option>
                  )}
                  {matchingRecipes.map((r) => <option key={r.id} value={r.id}>{r.name}</option>)}
                </select>
                <input className="input" type="number" min="0" step="any" value={portion.servings}
                  disabled={readOnly}
                  onChange={(e) => updatePortion(portion.key, { servings: Number(e.target.value) })} />
                <span className="muted small" style={{ alignSelf: 'center' }}>servings</span>
                {!readOnly && (
                  <button type="button" className="btn btn-sm btn-danger" title="Remove"
                    onClick={() => removePortion(portion.key)}>✕</button>
                )}
              </div>
              {mismatched && chosen && (
                <p className="muted small" style={{ margin: 0 }}>
                  This recipe is {FUEL_TYPE_HINT[chosen.fuelType]}, not {FUEL_TYPE_HINT[fuelType]}. Pick a matching recipe or change the fuel type above.
                </p>
              )}
            </div>
          )
        })}

        {!readOnly && matchingRecipes.length === 0 && (
          <p className="muted small" style={{ margin: 0 }}>
            No {FUEL_TYPE_HINT[fuelType]} recipes yet. Create one (with this fuel type) to portion it here.
          </p>
        )}

        {!readOnly && (
          <button type="button" className="btn btn-sm" onClick={addPortion} disabled={matchingRecipes.length === 0}>
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
