import { useEffect, useId, useMemo, useRef, useState } from 'react'
import { useLocation, useNavigate, useParams } from 'react-router-dom'
import { ingredientApi } from '../api/services'
import { ImageUpload } from '../components/ImageUpload'
import { SanitizedHtml } from '../components/SanitizedHtml'
import { fold } from '../lib/fold'
import { caloriesOf, findHit, type FinderHit } from '../lib/ingredientMatch'
import { useNutrients } from '../hooks/useNutrients'
import { useConfirm } from '../hooks/useConfirm'
import { useAuth } from '../auth/AuthContext'
import {
  INGREDIENT_VISIBILITY_LABELS, NUTRIENT_UNIT_ABBR, UNIT_LABELS,
  type Ingredient, type IngredientInput, type IngredientVisibility,
  type LocalizedName, type NutrientAmount, type UnitOfMeasure,
} from '../api/types'

const UNIT_OPTIONS = Object.keys(UNIT_LABELS) as UnitOfMeasure[]

/**
 * Prefill handed to the create form by the barcode scanner (router state). Carries product data pulled
 * from Open Food Facts (per 100 g) or read from a nutrition-label photo (per serving), plus the barcode
 * so the saved ingredient can be contributed back to OFF.
 */
export interface ScanPrefill {
  name?: string
  barcode?: string
  imageUrl?: string
  unit: UnitOfMeasure
  servingSize: number
  nutritionPerServing: NutrientAmount[]
  note?: string
}

// A localized-name editor row; keyed locally so removing a row never re-targets inputs.
interface LocalizedRow extends LocalizedName {
  key: number
}

let nextLocalizedKey = 1

export function IngredientFormPage() {
  const { id } = useParams<{ id: string }>()
  const isNew = !id || id === 'new'
  const navigate = useNavigate()
  const location = useLocation()
  const { confirm, dialog } = useConfirm()
  const { profile } = useAuth()
  const { ordered: nutrients, byId } = useNutrients()

  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [imageUrl, setImageUrl] = useState('')
  const [barcode, setBarcode] = useState('')
  const [unit, setUnit] = useState<UnitOfMeasure>('Gram')
  const [servingSize, setServingSize] = useState('100')
  const [appLevel, setAppLevel] = useState(false)
  const [amounts, setAmounts] = useState<NutrientAmount[]>([])
  const [canEdit, setCanEdit] = useState(true)
  const [canSubmit, setCanSubmit] = useState(false)
  const [visibility, setVisibility] = useState<IngredientVisibility>('Private')

  // Finder search metadata: tags double as browse categories, aliases cover alternate names,
  // localized names let the finder match non-English searches.
  const [tags, setTags] = useState<string[]>([])
  const [aliases, setAliases] = useState<string[]>([])
  const [localizedNames, setLocalizedNames] = useState<LocalizedRow[]>([])

  const [loading, setLoading] = useState(!isNew)
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)

  // The ingredient as loaded from the server (edit/read-only mode), kept so "Customize" can seed a
  // new form from the exact source rather than reconstructing it from the editable field state.
  const loadedRef = useRef<Ingredient | null>(null)

  // Automatic nutrition lookup (USDA via the backend).
  const [lookingUp, setLookingUp] = useState(false)
  const [lookupError, setLookupError] = useState<string | null>(null)
  const [lookupInfo, setLookupInfo] = useState<string | null>(null)

  // "Does this already exist?" — a debounced, server-side search of existing ingredients shown
  // inline under the Name field on the create form only. Selecting a match seeds the whole form
  // from that item so the user can adapt it instead of creating a near-duplicate.
  const [matches, setMatches] = useState<FinderHit[]>([])
  const [matchesOpen, setMatchesOpen] = useState(false)
  const [seededInfo, setSeededInfo] = useState<string | null>(null)
  // The name we last seeded the form from / chose to dismiss; we don't re-search while the name
  // still equals it, so picking a match (or seeding) doesn't immediately re-open the popover.
  const skipMatchName = useRef<string | null>(null)
  const matchesId = useId()

  useEffect(() => {
    if (isNew) return
    // `loading` is already true for edit mode (initialized from !isNew), so the fetch starts
    // without an extra synchronous setState.
    let cancelled = false
    ingredientApi.get(id!)
      .then((i) => {
        if (cancelled) return
        loadedRef.current = i
        setName(i.name)
        setDescription(i.description ?? '')
        setImageUrl(i.imageUrl ?? '')
        setBarcode(i.barcode ?? '')
        setUnit(i.unit)
        setServingSize(String(i.servingSize))
        setAmounts(i.nutritionPerServing)
        setTags(i.tags)
        setAliases(i.aliases)
        setLocalizedNames(i.localizedNames.map((l) => ({ key: nextLocalizedKey++, ...l })))
        setAppLevel(i.scope === 'App')
        setCanEdit(i.canEdit)
        setCanSubmit(i.canSubmitForReview)
        setVisibility(i.visibility)
      })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [id, isNew])

  // Debounced "similar ingredients" lookup — create form only, min 2 chars, ~280ms. Skipped while
  // the name still equals a just-seeded/dismissed value so we don't re-search our own selection.
  useEffect(() => {
    if (!isNew) return
    const query = name.trim()
    if (query.length < 2 || query === skipMatchName.current) {
      setMatches([])
      setMatchesOpen(false)
      return
    }
    let cancelled = false
    const handle = setTimeout(async () => {
      try {
        const results = await ingredientApi.list(query)
        if (cancelled) return
        // Re-run the finder's own classification client-side so each row can explain *why* it
        // matched (name / alias / localized / tag), matching the IngredientFinder UX.
        const folded = fold(query)
        const hits = results.map((i) => findHit(i, folded) ?? { ingredient: i, hint: null })
        setMatches(hits)
        setMatchesOpen(hits.length > 0)
      } catch {
        // A failed "did you mean" lookup is non-blocking — the user can still create the ingredient.
        if (!cancelled) { setMatches([]); setMatchesOpen(false) }
      }
    }, 280)
    return () => { cancelled = true; clearTimeout(handle) }
  }, [name, isNew])

  // Seed the whole (still editable) form from an existing ingredient the user picked from the
  // matches list, so they can adapt it rather than re-create a near-duplicate. Keeps the typed
  // name (the user is naming a *new* ingredient) but pulls in everything else.
  function seedFrom(i: Ingredient) {
    setDescription(i.description ?? '')
    setImageUrl(i.imageUrl ?? '')
    setUnit(i.unit)
    setServingSize(String(i.servingSize))
    setAmounts(i.nutritionPerServing.map((a) => ({ ...a })))
    setTags([...i.tags])
    setAliases([...i.aliases])
    setLocalizedNames(i.localizedNames.map((l) => ({ key: nextLocalizedKey++, ...l })))
    // Don't re-open the matches popover for the name we just seeded from.
    skipMatchName.current = name.trim()
    setMatches([])
    setMatchesOpen(false)
    setLookupError(null)
    setLookupInfo(null)
    setSeededInfo(`Filled this form from "${i.name}" (per ${i.servingSize} ${UNIT_LABELS[i.unit]}). ` +
      `Adjust the name and values, then save — or open "${i.name}" itself if you don't need a new one.`)
  }

  // "Customize" an app-level ingredient: the read-only view navigates here (to the create form)
  // with the source ingredient in router state. We seed *every* field — including the name — into
  // a brand-new, unsaved form, so nothing is created server-side until the user presses Save.
  // The state is consumed once (replaced away) so a refresh/back doesn't re-seed unexpectedly.
  const customizeFrom = (location.state as { customizeFrom?: Ingredient } | null)?.customizeFrom
  const seededRef = useRef(false)
  useEffect(() => {
    if (!isNew || !customizeFrom || seededRef.current) return
    seededRef.current = true
    const i = customizeFrom
    setName(i.name)
    setDescription(i.description ?? '')
    setImageUrl(i.imageUrl ?? '')
    setUnit(i.unit)
    setServingSize(String(i.servingSize))
    setAmounts(i.nutritionPerServing.map((a) => ({ ...a })))
    setTags([...i.tags])
    setAliases([...i.aliases])
    setLocalizedNames(i.localizedNames.map((l) => ({ key: nextLocalizedKey++, ...l })))
    // The seeded name matches an existing ingredient; suppress the "similar ingredients" popover
    // for it (the user is intentionally copying that one).
    skipMatchName.current = i.name.trim()
    setSeededInfo(`Started a private copy of "${i.name}". Adjust the name and values, then press ` +
      `Save to create your own version.`)
    // Drop the router state so this only seeds once.
    navigate('/ingredients/new', { replace: true })
  }, [isNew, customizeFrom, navigate])

  // Pre-fill from a barcode scan (Open Food Facts data, or a nutrition-label photo). Seeds a brand-new
  // unsaved form; nothing is created server-side until the user reviews and presses Save. State is
  // consumed once so a refresh/back doesn't re-seed.
  const scanPrefill = (location.state as { scanPrefill?: ScanPrefill } | null)?.scanPrefill
  const scanSeededRef = useRef(false)
  useEffect(() => {
    if (!isNew || !scanPrefill || scanSeededRef.current) return
    scanSeededRef.current = true
    const p = scanPrefill
    if (p.name) setName(p.name)
    if (p.imageUrl) setImageUrl(p.imageUrl)
    if (p.barcode) setBarcode(p.barcode)
    setUnit(p.unit)
    setServingSize(String(p.servingSize))
    setAmounts(p.nutritionPerServing.map((a) => ({ ...a })))
    // Suppress the "similar ingredients" popover for the seeded name (this is a specific product).
    if (p.name) skipMatchName.current = p.name.trim()
    if (p.note) setSeededInfo(p.note)
    // Drop the router state so this only seeds once.
    navigate('/ingredients/new', { replace: true })
  }, [isNew, scanPrefill, navigate])

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

  async function save(): Promise<string | null> {
    setError(null)
    if (!name.trim()) { setError('Name is required.'); return null }
    const serving = Number(servingSize)
    if (!Number.isFinite(serving) || serving <= 0) { setError('Serving size must be greater than zero.'); return null }

    const input: IngredientInput = {
      name: name.trim(),
      description: description.trim() || undefined,
      imageUrl: imageUrl.trim() || undefined,
      barcode: barcode.trim() || undefined,
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
      const saved = isNew ? await ingredientApi.create(input) : await ingredientApi.update(id!, input)
      return saved.id
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Save failed.')
      setSaving(false)
      return null
    }
  }

  async function saveAndExit() {
    const savedId = await save()
    if (savedId) navigate('/ingredients', { replace: true })
  }

  async function submitForReview() {
    // Persist first so pending review reflects the latest content.
    const savedId = await save()
    if (!savedId) return
    try {
      await ingredientApi.submitForReview(savedId)
      navigate('/ingredients', { replace: true })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Submit failed.')
      setSaving(false)
    }
  }

  // "Customize" an app-level (read-only) ingredient: open the create form pre-filled from this
  // ingredient. Nothing is persisted until the user reviews and presses Save (creating their own
  // private copy via the normal create path), so the Save button stays idle until they submit.
  function customize() {
    const source = loadedRef.current
    if (isNew || canEdit || !source) return
    navigate('/ingredients/new', { state: { customizeFrom: source } })
  }

  function remove() {
    if (!id || isNew) return
    confirm({
      title: 'Delete this ingredient?',
      message: 'This cannot be undone.',
      confirmLabel: 'Delete',
      pendingLabel: 'Deleting…',
      onConfirm: async () => {
        setSaving(true)
        try {
          await ingredientApi.remove(id)
          navigate('/ingredients', { replace: true })
        } catch (e) {
          setError(e instanceof Error ? e.message : 'Delete failed.')
          setSaving(false)
          throw e // keep the dialog open so the inline error is visible for retry
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
        <h1>{isNew ? 'New ingredient' : readOnly ? 'Ingredient' : 'Edit ingredient'}</h1>
        <button className="btn btn-sm" onClick={() => navigate(-1)}>Back</button>
      </div>

      {error && <div className="notice error">{error}</div>}
      {readOnly && (
        <div className="notice info">
          <div className="row spread" style={{ gap: '0.5rem' }}>
            <span>This is an app-level ingredient. You can view it but not edit it.</span>
            <button type="button" className="btn btn-sm" onClick={customize}
              title="Make a private copy you can edit">
              Customize
            </button>
          </div>
        </div>
      )}
      {!isNew && !readOnly && (
        <div className="notice info">Status: <strong>{INGREDIENT_VISIBILITY_LABELS[visibility]}</strong></div>
      )}
      {!isNew && (loadedRef.current?.sourcedFromOff || loadedRef.current?.contributedToOffAt) && (
        <div className="row" style={{ gap: '0.4rem', flexWrap: 'wrap' }}>
          {loadedRef.current?.sourcedFromOff && (
            <span className="badge" title="Nutrition verified to match the Open Food Facts record for this barcode">
              Sourced from Open Food Facts
            </span>
          )}
          {loadedRef.current?.contributedToOffAt && (
            <span className="badge"
              title={`Contributed on ${new Date(loadedRef.current.contributedToOffAt).toLocaleDateString()}`}>
              Shared to Open Food Facts
            </span>
          )}
        </div>
      )}

      <div className="card stack">
        <label className="field">
          Name
          <div className="finder" style={{ width: '100%' }}>
            <div className="lookup-row">
              <input className="input" value={name} disabled={readOnly}
                role="combobox" aria-expanded={matchesOpen} aria-controls={matchesId}
                aria-autocomplete="list" aria-label="Name"
                onChange={(e) => { setName(e.target.value); skipMatchName.current = null }}
                onFocus={() => { if (isNew && matches.length > 0) setMatchesOpen(true) }}
                placeholder="e.g. Greek Yogurt" />
              {!readOnly && (
                <button type="button" className="btn btn-sm" disabled={lookingUp || !name.trim()}
                  title="Auto-fill nutrition for this name from USDA FoodData Central (falls back to your ingredient catalog if the live service is unavailable)"
                  onClick={lookupNutrition}>
                  {lookingUp ? 'Looking up…' : 'Look up nutrition'}
                </button>
              )}
            </div>
            {isNew && matchesOpen && (
              <div className="finder-pop">
                <div className="row spread finder-empty" style={{ gap: '0.5rem', alignItems: 'flex-start' }}>
                  <p className="muted small" style={{ margin: 0 }}>
                    {matches.length === 1 ? 'A similar ingredient already exists' : 'Similar ingredients already exist'}
                    {' '}— pick one to fill this form from it, or keep typing to create a new one.
                  </p>
                  <button type="button" className="btn btn-sm"
                    onClick={() => { skipMatchName.current = name.trim(); setMatchesOpen(false) }}>
                    Dismiss
                  </button>
                </div>
                <div className="finder-list" id={matchesId} role="listbox" aria-label="Similar existing ingredients">
                  {matches.map((h) => {
                    const kcal = caloriesOf(h.ingredient)
                    return (
                      <button key={h.ingredient.id} type="button" role="option" aria-selected={false}
                        className="finder-option" onClick={() => seedFrom(h.ingredient)}>
                        <span className="finder-option-top">
                          <span className="finder-option-name">{h.ingredient.name}</span>
                          {h.ingredient.scope === 'User' && <span className="badge mine">Mine</span>}
                        </span>
                        <span className="small muted">
                          Per {h.ingredient.servingSize} {UNIT_LABELS[h.ingredient.unit]}
                          {kcal != null && ` · ${kcal} kcal`}
                          {h.hint && <span className="finder-aka"> · {h.hint}</span>}
                        </span>
                      </button>
                    )
                  })}
                </div>
              </div>
            )}
          </div>
        </label>

        {seededInfo && <div className="notice info">{seededInfo}</div>}
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

        {(barcode || !readOnly) && (
          <label className="field">
            Barcode <span className="hint">UPC/EAN — set when scanned</span>
            <input className="input" inputMode="numeric" value={barcode} disabled={readOnly}
              placeholder="e.g. 737628064502"
              onChange={(e) => setBarcode(e.target.value)} />
          </label>
        )}

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
        <div className="row" style={{ gap: '0.5rem', flexWrap: 'wrap' }}>
          <button className="btn btn-primary" style={{ flex: 1 }} disabled={saving} onClick={saveAndExit}>
            {saving ? 'Saving…' : 'Save'}
          </button>
          {!isNew && canSubmit && (
            <button className="btn" disabled={saving} onClick={submitForReview}
              title="Submit this ingredient for admin review to become public">
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
