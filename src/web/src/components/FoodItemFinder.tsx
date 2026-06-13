import { useEffect, useId, useRef, useState } from 'react'
import { UNIT_LABELS, type Ingredient } from '../api/types'
import { ingredientApi } from '../api/services'
import { fold } from '../lib/fold'
import { caloriesOf, findHit, type FinderHit } from '../lib/ingredientMatch'

// The free-form fuel item finder: a free-text combobox for the Add-Fuel form's FreeForm source.
// As the user types the item description we debounce-search the ingredient catalog (server-side,
// since that catalog is large — unlike ActivityFinder, which filters the user's small preloaded
// activity history). Picking a match fills the item name AND the per-serving calories; but the
// field stays open-ended — any typed description is valid and the lookup is only a shortcut.
// Mirrors ActivityFinder's keyboard/ARIA/styling; the matching/labeling helpers (findHit,
// caloriesOf) are shared with IngredientFinder and the new-ingredient dedup lookup.

// Min query length and debounce before hitting the server, matching the new-ingredient dedup lookup.
const MIN_QUERY = 2
const DEBOUNCE_MS = 280

export function FoodItemFinder({
  value,
  onChange,
  onSelect,
  disabled,
  placeholder = 'e.g. Handful of almonds',
}: {
  /** The item description — free text, not a selection; this IS the field's value. */
  value: string
  /** The user typed (or cleared) the description. */
  onChange: (name: string) => void
  /** The user picked a matching ingredient (callers fill the name AND its per-serving calories). */
  onSelect: (ingredient: Ingredient) => void
  disabled?: boolean
  placeholder?: string
}) {
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const [hits, setHits] = useState<FinderHit[]>([])
  const [loading, setLoading] = useState(false)
  const listRef = useRef<HTMLDivElement>(null)
  const listId = useId()
  // The query we last searched the server for, so picking a match (which sets `value` to the
  // ingredient name) doesn't immediately re-search for that same name.
  const lastQuery = useRef<string | null>(null)

  // Debounced server search over the ingredient catalog. Below the min length we clear the list;
  // the field then behaves as a plain text input. We re-classify each result with findHit so a row
  // can explain *why* it matched (alias / localized name / tag), matching the IngredientFinder UX.
  useEffect(() => {
    const query = value.trim()
    if (query.length < MIN_QUERY || query === lastQuery.current) {
      setHits([])
      setLoading(false)
      return
    }
    let cancelled = false
    setLoading(true)
    const handle = setTimeout(async () => {
      try {
        const results = await ingredientApi.list(query)
        if (cancelled) return
        const folded = fold(query)
        setHits(results.map((i) => findHit(i, folded) ?? { ingredient: i, hint: null }))
        setActive(0)
      } catch {
        // A failed lookup is non-blocking — the user can still type and submit a free-form item.
        if (!cancelled) setHits([])
      } finally {
        if (!cancelled) setLoading(false)
      }
    }, DEBOUNCE_MS)
    return () => { cancelled = true; clearTimeout(handle) }
  }, [value])

  // Keep the highlighted option on-screen as it moves (the highlight itself is reset where the
  // result set changes — typing/searching — not in an effect, to avoid a cascading render).
  useEffect(() => {
    listRef.current?.querySelector('[data-active="true"]')?.scrollIntoView({ block: 'nearest' })
  }, [active])

  const popoverOpen = open && value.trim().length >= MIN_QUERY

  function choose(i: Ingredient) {
    // Remember the picked name so the debounced effect doesn't re-search for it.
    lastQuery.current = i.name.trim()
    onSelect(i)
    setOpen(false)
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    if (!popoverOpen) {
      if (e.key === 'ArrowDown' && hits.length > 0) { e.preventDefault(); setOpen(true) }
      return
    }
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setActive((a) => Math.min(a + 1, hits.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((a) => Math.max(a - 1, 0))
    } else if (e.key === 'Enter') {
      // Enter takes the highlighted match; with no match it just closes the popover, leaving the
      // typed (free-form) description in place.
      e.preventDefault()
      if (hits[active]) choose(hits[active].ingredient)
      else setOpen(false)
    } else if (e.key === 'Escape') {
      e.preventDefault()
      setOpen(false)
    }
  }

  return (
    <div className="finder">
      <input
        className="input"
        role="combobox"
        aria-expanded={popoverOpen}
        aria-controls={listId}
        aria-autocomplete="list"
        disabled={disabled}
        placeholder={placeholder}
        value={value}
        onFocus={() => setOpen(true)}
        onClick={() => setOpen(true)}
        onChange={(e) => { onChange(e.target.value); lastQuery.current = null; setActive(0); setOpen(true) }}
        onKeyDown={handleKeyDown}
        onBlur={() => setOpen(false)}
      />
      {popoverOpen && (
        // preventDefault on mousedown keeps focus in the input, so clicking an option never fires
        // the input's blur-close before the click lands.
        <div className="finder-pop" onMouseDown={(e) => e.preventDefault()}>
          <div className="finder-list" id={listId} ref={listRef} role="listbox" aria-label="Matching ingredients">
            {hits.length === 0 && (
              <p className="muted small finder-empty">
                {loading
                  ? 'Searching…'
                  : `No ingredients match — “${value.trim()}” will be logged as you typed it.`}
              </p>
            )}
            {hits.map((h, idx) => {
              const kcal = caloriesOf(h.ingredient)
              return (
                <button key={h.ingredient.id} type="button" role="option"
                  aria-selected={fold(h.ingredient.name) === fold(value.trim())}
                  data-active={idx === active || undefined}
                  className={`finder-option ${idx === active ? 'active' : ''}`}
                  onMouseEnter={() => setActive(idx)}
                  onClick={() => choose(h.ingredient)}>
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
  )
}
