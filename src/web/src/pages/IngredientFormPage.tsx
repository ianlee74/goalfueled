import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ingredientApi } from '../api/services'
import { ImageUpload } from '../components/ImageUpload'
import { useNutrients } from '../hooks/useNutrients'
import { useAuth } from '../auth/AuthContext'
import {
  NUTRIENT_UNIT_ABBR, UNIT_LABELS,
  type IngredientInput, type NutrientAmount, type UnitOfMeasure,
} from '../api/types'

const UNIT_OPTIONS = Object.keys(UNIT_LABELS) as UnitOfMeasure[]

export function IngredientFormPage() {
  const { id } = useParams<{ id: string }>()
  const isNew = !id || id === 'new'
  const navigate = useNavigate()
  const { profile } = useAuth()
  const { ordered: nutrients, byId } = useNutrients()

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [imageUrl, setImageUrl] = useState('')
  const [unit, setUnit] = useState<UnitOfMeasure>('Gram')
  const [servingSize, setServingSize] = useState('100')
  const [appLevel, setAppLevel] = useState(false)
  const [amounts, setAmounts] = useState<NutrientAmount[]>([])
  const [canEdit, setCanEdit] = useState(true)

  const [loading, setLoading] = useState(!isNew)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (isNew) return
    // `loading` is already true for edit mode (initialized from !isNew), so the fetch starts
    // without an extra synchronous setState.
    let cancelled = false
    ingredientApi.get(id!)
      .then((i) => {
        if (cancelled) return
        setName(i.name)
        setDescription(i.description ?? '')
        setImageUrl(i.imageUrl ?? '')
        setUnit(i.unit)
        setServingSize(String(i.servingSize))
        setAmounts(i.nutritionPerServing)
        setAppLevel(i.scope === 'App')
        setCanEdit(i.canEdit)
      })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [id, isNew])

  // Nutrients available to add (not already in the list).
  const present = useMemo(() => new Set(amounts.map((a) => a.nutrientId)), [amounts])
  const addable = nutrients.filter((n) => !present.has(n.id))

  function addNutrient(nutrientId: string) {
    if (!nutrientId) return
    setAmounts((prev) => [...prev, { nutrientId, value: 0 }])
  }
  function setValue(nutrientId: string, value: number) {
    setAmounts((prev) => prev.map((a) => (a.nutrientId === nutrientId ? { ...a, value } : a)))
  }
  function removeNutrient(nutrientId: string) {
    setAmounts((prev) => prev.filter((a) => a.nutrientId !== nutrientId))
  }

  async function save() {
    setError(null)
    if (!name.trim()) { setError('Name is required.'); return }
    const serving = Number(servingSize)
    if (!Number.isFinite(serving) || serving <= 0) { setError('Serving size must be greater than zero.'); return }

    const input: IngredientInput = {
      name: name.trim(),
      description: description.trim() || undefined,
      imageUrl: imageUrl.trim() || undefined,
      unit,
      servingSize: serving,
      nutritionPerServing: amounts.filter((a) => Number.isFinite(a.value)),
      appLevel: appLevel && !!profile?.isActingAsAdmin,
    }

    setSaving(true)
    try {
      if (isNew) await ingredientApi.create(input)
      else await ingredientApi.update(id!, input)
      navigate('/ingredients', { replace: true })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
      setSaving(false)
    }
  }

  async function remove() {
    if (!id || isNew) return
    if (!confirm('Delete this ingredient? This cannot be undone.')) return
    setSaving(true)
    try {
      await ingredientApi.remove(id)
      navigate('/ingredients', { replace: true })
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
        <h1>{isNew ? 'New ingredient' : readOnly ? 'Ingredient' : 'Edit ingredient'}</h1>
        <button className="btn btn-sm" onClick={() => navigate(-1)}>Back</button>
      </div>

      {error && <div className="notice error">{error}</div>}
      {readOnly && <div className="notice info">This is an app-level ingredient. You can view it but not edit it.</div>}

      <div className="card stack">
        <label className="field">
          Name
          <input className="input" value={name} disabled={readOnly}
            onChange={(e) => setName(e.target.value)} placeholder="e.g. Greek Yogurt" />
        </label>

        <label className="field">
          Description <span className="hint">HTML allowed</span>
          <textarea className="input" rows={3} value={description} disabled={readOnly}
            onChange={(e) => setDescription(e.target.value)} />
        </label>

        <ImageUpload value={imageUrl} onChange={setImageUrl} disabled={readOnly} />

        <div className="row" style={{ gap: '0.75rem' }}>
          <label className="field" style={{ flex: 1 }}>
            Serving size
            <input className="input" type="number" min="0" step="any" value={servingSize}
              disabled={readOnly} onChange={(e) => setServingSize(e.target.value)} />
          </label>
          <label className="field" style={{ flex: 1.5 }}>
            Unit
            <select className="select" value={unit} disabled={readOnly}
              onChange={(e) => setUnit(e.target.value as UnitOfMeasure)}>
              {UNIT_OPTIONS.map((u) => <option key={u} value={u}>{UNIT_LABELS[u]}</option>)}
            </select>
          </label>
        </div>

        {profile?.isActingAsAdmin && (
          <label className="toggle">
            <input type="checkbox" checked={appLevel} disabled={readOnly}
              onChange={(e) => setAppLevel(e.target.checked)} />
            Make this an app-level ingredient (visible to all users)
          </label>
        )}
      </div>

      <div className="card stack">
        <h2>Nutrition <span className="muted small">per serving</span></h2>

        {amounts.length === 0 && <p className="muted small" style={{ margin: 0 }}>No nutrients added yet.</p>}

        {amounts.map((a) => {
          const def = byId.get(a.nutrientId)
          return (
            <div key={a.nutrientId} className="nutrient-editor-row">
              <span>{def?.name ?? a.nutrientId}</span>
              <div className="row" style={{ gap: 4 }}>
                <input className="input" type="number" step="any" min="0" value={a.value}
                  disabled={readOnly}
                  onChange={(e) => setValue(a.nutrientId, Number(e.target.value))} />
                <span className="small muted">{def ? NUTRIENT_UNIT_ABBR[def.unit] : ''}</span>
              </div>
              {!readOnly && (
                <button className="btn btn-sm btn-danger" title="Remove"
                  onClick={() => removeNutrient(a.nutrientId)}>✕</button>
              )}
            </div>
          )
        })}

        {!readOnly && (
          <label className="field">
            Add nutrient
            <select className="select" value="" onChange={(e) => { addNutrient(e.target.value); e.target.value = '' }}>
              <option value="">Select a nutrient…</option>
              {addable.map((n) => (
                <option key={n.id} value={n.id}>{n.name} ({NUTRIENT_UNIT_ABBR[n.unit]})</option>
              ))}
            </select>
          </label>
        )}
      </div>

      {!readOnly && (
        <div className="row" style={{ gap: '0.5rem' }}>
          <button className="btn btn-primary" style={{ flex: 1 }} disabled={saving} onClick={save}>
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
