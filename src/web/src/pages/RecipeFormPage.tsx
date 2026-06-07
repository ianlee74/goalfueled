import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ingredientApi, recipeApi } from '../api/services'
import { ImageUpload } from '../components/ImageUpload'
import { NutritionLabel } from '../components/NutritionLabel'
import { SanitizedHtml } from '../components/SanitizedHtml'
import { useAuth } from '../auth/AuthContext'
import {
  RECIPE_VISIBILITY_LABELS, UNIT_LABELS,
  type Ingredient, type NutrientAmount, type RecipeInput,
  type RecipeItemInput, type RecipeVisibility, type UnitOfMeasure,
} from '../api/types'

const UNIT_OPTIONS = Object.keys(UNIT_LABELS) as UnitOfMeasure[]

interface ItemRow extends RecipeItemInput {
  // Local-only display fields; not sent to the API.
  key: number
}

let nextKey = 1

export function RecipeFormPage() {
  const { id } = useParams<{ id: string }>()
  const isNew = !id || id === 'new'
  const navigate = useNavigate()
  const { profile } = useAuth()

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [imageUrl, setImageUrl] = useState('')
  const [servings, setServings] = useState('1')
  const [appLevel, setAppLevel] = useState(false)
  const [items, setItems] = useState<ItemRow[]>([])
  const [canEdit, setCanEdit] = useState(true)
  const [canSubmit, setCanSubmit] = useState(false)
  const [visibility, setVisibility] = useState<RecipeVisibility>('Private')

  // Ingredient catalog the user can pick from (their own + app-level + public).
  const [ingredients, setIngredients] = useState<Ingredient[]>([])

  const [loading, setLoading] = useState(!isNew)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Load ingredient picker options once.
  useEffect(() => {
    let cancelled = false
    ingredientApi.list()
      .then((list) => { if (!cancelled) setIngredients(list) })
      .catch(() => {})
    return () => { cancelled = true }
  }, [])

  // Load the recipe when editing.
  useEffect(() => {
    if (isNew) return
    let cancelled = false
    recipeApi.get(id!)
      .then((r) => {
        if (cancelled) return
        setName(r.name)
        setDescription(r.description ?? '')
        setImageUrl(r.imageUrl ?? '')
        setServings(String(r.servings))
        setAppLevel(r.scope === 'App')
        setItems(r.items.map((i) => ({ key: nextKey++, ingredientId: i.ingredientId, quantity: i.quantity, unit: i.unit })))
        setCanEdit(r.canEdit)
        setCanSubmit(r.canSubmitForReview)
        setVisibility(r.visibility)
      })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [id, isNew])

  const ingredientById = useMemo(
    () => new Map(ingredients.map((i) => [i.id, i])), [ingredients])

  // Build the input payload from current form state.
  function buildInput(): RecipeInput {
    return {
      name: name.trim(),
      description: description.trim() || undefined,
      imageUrl: imageUrl.trim() || undefined,
      servings: Number(servings),
      items: items
        .filter((i) => i.ingredientId && Number(i.quantity) > 0)
        .map((i) => ({ ingredientId: i.ingredientId, quantity: Number(i.quantity), unit: i.unit })),
      appLevel: appLevel && !!profile?.isActingAsAdmin,
    }
  }

  // Live nutrition: roll it up client-side from the catalog whenever items/servings change. This
  // mirrors the server's conversion math (see previewNutrition) so the preview matches the saved value.
  const { total, perServing } = useMemo(
    () => previewNutrition(items, Number(servings), ingredientById),
    [items, servings, ingredientById])

  function addItem() {
    const first = ingredients[0]
    setItems((prev) => [...prev, {
      key: nextKey++,
      ingredientId: first?.id ?? '',
      quantity: 1,
      unit: first?.unit ?? 'Gram',
    }])
  }
  function updateItem(key: number, patch: Partial<ItemRow>) {
    setItems((prev) => prev.map((i) => (i.key === key ? { ...i, ...patch } : i)))
  }
  function removeItem(key: number) {
    setItems((prev) => prev.filter((i) => i.key !== key))
  }

  async function save(): Promise<string | null> {
    setError(null)
    if (!name.trim()) { setError('Name is required.'); return null }
    const serv = Number(servings)
    if (!Number.isFinite(serv) || serv <= 0) { setError('Servings must be greater than zero.'); return null }

    setSaving(true)
    try {
      const input = buildInput()
      const saved = isNew ? await recipeApi.create(input) : await recipeApi.update(id!, input)
      return saved.id
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
      setSaving(false)
      return null
    }
  }

  async function saveAndExit() {
    const savedId = await save()
    if (savedId) navigate('/recipes', { replace: true })
  }

  async function submitForReview() {
    // Persist first so pending review reflects the latest content.
    const savedId = await save()
    if (!savedId) return
    try {
      await recipeApi.submitForReview(savedId)
      navigate('/recipes', { replace: true })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Submit failed.')
      setSaving(false)
    }
  }

  async function remove() {
    if (!id || isNew) return
    if (!confirm('Delete this recipe? This cannot be undone.')) return
    setSaving(true)
    try {
      await recipeApi.remove(id)
      navigate('/recipes', { replace: true })
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
        <h1>{isNew ? 'New recipe' : readOnly ? 'Recipe' : 'Edit recipe'}</h1>
        <button className="btn btn-sm" onClick={() => navigate(-1)}>Back</button>
      </div>

      {error && <div className="notice error">{error}</div>}
      {readOnly && <div className="notice info">This recipe is read-only. You can view it but not edit it.</div>}
      {!isNew && (
        <div className="notice info">Status: <strong>{RECIPE_VISIBILITY_LABELS[visibility]}</strong></div>
      )}

      <div className="card stack">
        <label className="field">
          Name
          <input className="input" value={name} disabled={readOnly}
            onChange={(e) => setName(e.target.value)} placeholder="e.g. Chicken & Rice Bowl" />
        </label>

        <label className="field">
          Description <span className="hint">HTML allowed</span>
          <textarea className="input" rows={3} value={description} disabled={readOnly}
            onChange={(e) => setDescription(e.target.value)} />
        </label>

        {description.trim() && (
          <div className="field">
            Preview <span className="hint">sanitized</span>
            <SanitizedHtml className="html-preview" html={description} />
          </div>
        )}

        <ImageUpload value={imageUrl} onChange={setImageUrl} disabled={readOnly} />

        <label className="field" style={{ maxWidth: 160 }}>
          Servings
          <input className="input" type="number" min="0" step="any" value={servings}
            disabled={readOnly} onChange={(e) => setServings(e.target.value)} />
        </label>

        {profile?.isActingAsAdmin && (
          <label className="toggle">
            <input type="checkbox" checked={appLevel} disabled={readOnly}
              onChange={(e) => setAppLevel(e.target.checked)} />
            Make this an app-level recipe (visible to all users)
          </label>
        )}
      </div>

      <div className="card stack">
        <h2>Ingredients</h2>

        {items.length === 0 && <p className="muted small" style={{ margin: 0 }}>No ingredients added yet.</p>}

        {items.map((item) => (
          <div key={item.key} className="recipe-item-row">
            <select className="select" value={item.ingredientId} disabled={readOnly}
              onChange={(e) => {
                const ing = ingredientById.get(e.target.value)
                updateItem(item.key, { ingredientId: e.target.value, unit: ing?.unit ?? item.unit })
              }}>
              <option value="">Select an ingredient…</option>
              {ingredients.map((i) => <option key={i.id} value={i.id}>{i.name}</option>)}
            </select>
            <input className="input" type="number" min="0" step="any" value={item.quantity}
              disabled={readOnly}
              onChange={(e) => updateItem(item.key, { quantity: Number(e.target.value) })} />
            <select className="select" value={item.unit} disabled={readOnly}
              onChange={(e) => updateItem(item.key, { unit: e.target.value as UnitOfMeasure })}>
              {UNIT_OPTIONS.map((u) => <option key={u} value={u}>{UNIT_LABELS[u]}</option>)}
            </select>
            {!readOnly && (
              <button className="btn btn-sm btn-danger" title="Remove"
                onClick={() => removeItem(item.key)}>✕</button>
            )}
          </div>
        ))}

        {!readOnly && (
          <button className="btn btn-sm" onClick={addItem} disabled={ingredients.length === 0}>
            + Add ingredient
          </button>
        )}
      </div>

      <div className="card stack">
        <h2>Nutrition <span className="muted small">whole recipe</span></h2>
        <NutritionLabel amounts={total} />
        <h2>Nutrition <span className="muted small">per serving</span></h2>
        <NutritionLabel amounts={perServing} />
      </div>

      {!readOnly && (
        <div className="row" style={{ gap: '0.5rem', flexWrap: 'wrap' }}>
          <button className="btn btn-primary" style={{ flex: 1 }} disabled={saving} onClick={saveAndExit}>
            {saving ? 'Saving…' : 'Save'}
          </button>
          {!isNew && canSubmit && (
            <button className="btn" disabled={saving} onClick={submitForReview}
              title="Submit this recipe for admin review to become public">
              Submit for public
            </button>
          )}
          {!isNew && (
            <button className="btn btn-danger" disabled={saving} onClick={remove}>Delete</button>
          )}
        </div>
      )}
    </div>
  )
}

// Client-side roll-up mirroring the server: for each item whose unit is dimension-compatible with the
// ingredient's serving unit, scale the ingredient's per-serving nutrition by the number of servings.
function previewNutrition(
  items: ItemRow[], servings: number, byId: Map<string, Ingredient>,
): { total: NutrientAmount[]; perServing: NutrientAmount[] } {
  const totals = new Map<string, number>()
  for (const item of items) {
    const ing = byId.get(item.ingredientId)
    if (!ing || !(ing.servingSize > 0) || !(item.quantity > 0)) continue
    const itemBase = item.quantity * baseFactor(item.unit)
    const servingBase = ing.servingSize * baseFactor(ing.unit)
    if (!(servingBase > 0)) continue
    if (dimension(item.unit) !== dimension(ing.unit)) continue // incompatible units skipped
    const count = itemBase / servingBase
    for (const n of ing.nutritionPerServing) {
      totals.set(n.nutrientId, (totals.get(n.nutrientId) ?? 0) + n.value * count)
    }
  }
  const total = [...totals].map(([nutrientId, value]) => ({ nutrientId, value }))
  const perServing = servings > 0
    ? total.map((t) => ({ nutrientId: t.nutrientId, value: t.value / servings }))
    : total
  return { total, perServing }
}

// Mass→g, volume→ml, count→pc; mirrors UnitOfMeasureInfo.ToBaseFactor on the server.
const FACTORS: Record<UnitOfMeasure, number> = {
  Gram: 1, Milligram: 0.001, Kilogram: 1000, Ounce: 28.3495, Pound: 453.592,
  Milliliter: 1, Liter: 1000, Teaspoon: 4.92892, Tablespoon: 14.7868, FluidOunce: 29.5735, Cup: 236.588,
  Piece: 1, Serving: 1,
}
const DIMENSIONS: Record<UnitOfMeasure, string> = {
  Gram: 'mass', Milligram: 'mass', Kilogram: 'mass', Ounce: 'mass', Pound: 'mass',
  Milliliter: 'vol', Liter: 'vol', Teaspoon: 'vol', Tablespoon: 'vol', FluidOunce: 'vol', Cup: 'vol',
  Piece: 'count', Serving: 'count',
}
function baseFactor(u: UnitOfMeasure): number { return FACTORS[u] }
function dimension(u: UnitOfMeasure): string { return DIMENSIONS[u] }
