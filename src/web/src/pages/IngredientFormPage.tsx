import { useEffect, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { ingredientApi } from '../api/services'
import { ImageUpload } from '../components/ImageUpload'
import { SanitizedHtml } from '../components/SanitizedHtml'
import { useNutrients } from '../hooks/useNutrients'
import { useAuth } from '../auth/AuthContext'
import {
  NUTRIENT_UNIT_ABBR, UNIT_LABELS,
  type IngredientInput, type LocalizedName, type NutrientAmount, type UnitOfMeasure,
} from '../api/types'

const UNIT_OPTIONS = Object.keys(UNIT_LABELS) as UnitOfMeasure[]

// A localized-name editor row; keyed locally so removing a row never re-targets inputs.
interface LocalizedRow extends LocalizedName {
  key: number
}

let nextLocalizedKey = 1

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

  // Finder search metadata: tags double as browse categories, aliases cover alternate names,
  // localized names let the finder match non-English searches.
  const [tags, setTags] = useState<string[]>([])
  const [aliases, setAliases] = useState<string[]>([])
  const [localizedNames, setLocalizedNames] = useState<LocalizedRow[]>([])

  const [loading, setLoading] = useState(!isNew)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // Automatic nutrition lookup (USDA via the backend).
  const [lookingUp, setLookingUp] = useState(false)
  const [lookupError, setLookupError] = useState<string | null>(null)
  const [lookupInfo, setLookupInfo] = useState<string | null>(null)

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
        setTags(i.tags)
        setAliases(i.aliases)
        setLocalizedNames(i.localizedNames.map((l) => ({ key: nextLocalizedKey++, ...l })))
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

  function addLocalized() {
    setLocalizedNames((prev) => [...prev, { key: nextLocalizedKey++, language: '', name: '' }])
  }
  function updateLocalized(key: number, patch: Partial<LocalizedName>) {
    setLocalizedNames((prev) => prev.map((l) => (l.key === key ? { ...l, ...patch } : l)))
  }
  function removeLocalized(key: number) {
    setLocalizedNames((prev) => prev.filter((l) => l.key !== key))
  }

  // Fetches nutrition for the typed name and pre-fills the (still editable) nutrient fields. The
  // looked-up values are per 100 g, so we also set the serving basis to 100 g to keep them correct.
  // Lookup never auto-saves and never blocks manual entry — failures just surface a message.
  async function lookupNutrition() {
    setLookupError(null)
    setLookupInfo(null)
    const query = name.trim()
    if (!query) { setLookupError('Enter an ingredient name first.'); return }

    setLookingUp(true)
    try {
      const result = await ingredientApi.nutritionLookup(query)
      const known = result.nutritionPer100g.filter((a) => byId.has(a.nutrientId))
      if (known.length === 0) {
        setLookupError(`Found "${result.matchedName}" but it had no nutrients we track. Enter values manually.`)
        return
      }
      // Looked-up data is per 100 g; align the serving basis so the stored values stay meaningful.
      setUnit('Gram')
      setServingSize('100')
      setAmounts(known.map((a) => ({ nutrientId: a.nutrientId, value: a.value })))
      setLookupInfo(`Filled ${known.length} nutrient${known.length === 1 ? '' : 's'} from ${result.source} ` +
        `("${result.matchedName}", per 100 g). Review and edit before saving.`)
    } catch (e) {
      setLookupError(e instanceof Error ? e.message : 'Lookup failed. Enter values manually.')
    } finally {
      setLookingUp(false)
    }
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
      // Always sent (even when empty) so editing never silently wipes saved metadata; the
      // server trims and de-duplicates.
      tags,
      aliases,
      localizedNames: localizedNames
        .map(({ language, name: n }) => ({ language: language.trim(), name: n.trim() }))
        .filter((l) => l.language && l.name),
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

  // Clone an app-level (read-only) ingredient into a private copy, then open it for editing.
  async function customize() {
    if (!id || isNew) return
    setError(null)
    setSaving(true)
    try {
      const clone = await ingredientApi.customize(id)
      navigate(`/ingredients/${clone.id}`, { replace: true })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Customize failed.')
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
      {readOnly && (
        <div className="notice info">
          <div className="row spread" style={{ gap: '0.5rem' }}>
            <span>This is an app-level ingredient. You can view it but not edit it.</span>
            <button type="button" className="btn btn-sm" disabled={saving} onClick={customize}
              title="Make a private copy you can edit">
              {saving ? 'Customizing…' : 'Customize'}
            </button>
          </div>
        </div>
      )}

      <div className="card stack">
        <label className="field">
          Name
          <div className="lookup-row">
            <input className="input" value={name} disabled={readOnly}
              onChange={(e) => setName(e.target.value)} placeholder="e.g. Greek Yogurt" />
            {!readOnly && (
              <button type="button" className="btn btn-sm" disabled={lookingUp || !name.trim()}
                title="Auto-fill nutrition for this name from USDA FoodData Central"
                onClick={lookupNutrition}>
                {lookingUp ? 'Looking up…' : 'Look up nutrition'}
              </button>
            )}
          </div>
        </label>

        {lookupError && <div className="notice error">{lookupError}</div>}
        {lookupInfo && <div className="notice info">{lookupInfo}</div>}

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
        <h2>Search & discovery <span className="muted small">helps the ingredient finder</span></h2>

        <TermListEditor
          label="Tags"
          hint="categories like protein, vegetable, breakfast"
          placeholder="e.g. protein"
          terms={tags}
          onChange={setTags}
          readOnly={readOnly}
        />

        <TermListEditor
          label="Aliases"
          hint="other names this ingredient goes by"
          placeholder="e.g. garbanzo beans"
          terms={aliases}
          onChange={setAliases}
          readOnly={readOnly}
        />

        <div className="field">
          <span>Non-English names <span className="hint">language code + name, e.g. es / garbanzos</span></span>
          {localizedNames.length === 0 && readOnly && (
            <p className="muted small" style={{ margin: 0 }}>None.</p>
          )}
          {localizedNames.map((l) => (
            <div key={l.key} className="localized-row">
              <input className="input" value={l.language} disabled={readOnly}
                placeholder="es" title="Language code" maxLength={20}
                onChange={(e) => updateLocalized(l.key, { language: e.target.value })} />
              <input className="input" value={l.name} disabled={readOnly}
                placeholder="garbanzos" title="Name in that language"
                onChange={(e) => updateLocalized(l.key, { name: e.target.value })} />
              {!readOnly && (
                <button type="button" className="btn btn-sm btn-danger" title="Remove"
                  onClick={() => removeLocalized(l.key)}>✕</button>
              )}
            </div>
          ))}
          {!readOnly && (
            <button type="button" className="btn btn-sm" style={{ alignSelf: 'flex-start' }}
              onClick={addLocalized}>
              + Add non-English name
            </button>
          )}
        </div>
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

/** Chip-list editor for short free-text search terms (tags / aliases). Enter or Add commits the draft. */
function TermListEditor({ label, hint, placeholder, terms, onChange, readOnly }: {
  label: string
  hint: string
  placeholder: string
  terms: string[]
  onChange: (terms: string[]) => void
  readOnly: boolean
}) {
  const [draft, setDraft] = useState('')

  function add() {
    const term = draft.trim()
    if (!term) return
    // Case-insensitive de-dup, mirroring the server's normalization.
    if (!terms.some((t) => t.toLowerCase() === term.toLowerCase())) onChange([...terms, term])
    setDraft('')
  }
  function remove(term: string) {
    onChange(terms.filter((t) => t !== term))
  }

  return (
    <div className="field">
      <span>{label} <span className="hint">{hint}</span></span>
      {terms.length > 0 && (
        <div className="term-chips">
          {terms.map((t) => (
            <span key={t} className={`term-chip ${readOnly ? 'static' : ''}`}>
              {t}
              {!readOnly && (
                <button type="button" title={`Remove ${t}`} onClick={() => remove(t)}>✕</button>
              )}
            </span>
          ))}
        </div>
      )}
      {terms.length === 0 && readOnly && <p className="muted small" style={{ margin: 0 }}>None.</p>}
      {!readOnly && (
        <div className="term-add">
          <input className="input" value={draft} placeholder={placeholder} title={label} maxLength={200}
            onChange={(e) => setDraft(e.target.value)}
            onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); add() } }} />
          <button type="button" className="btn btn-sm" onClick={add} disabled={!draft.trim()}>Add</button>
        </div>
      )}
    </div>
  )
}
