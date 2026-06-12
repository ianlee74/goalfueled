import { useEffect, useId, useMemo, useRef, useState } from 'react'
import { UNIT_LABELS, type Ingredient } from '../api/types'

// The ingredient finder: a searchable, filterable combobox that replaces the old plain
// <select>. Type-ahead search matches an ingredient's name, aliases, tags and non-English
// names — case- and accent-insensitively — and the tag chips narrow the list by category.
// Filtering happens client-side over the already-loaded catalog (the same list the old
// dropdown rendered), mirroring the server's SearchText folding rules.

/** Mirrors SearchText.Fold on the server: lower-case + strip combining diacritics, so "jalapeno" matches "jalapeño" in either direction. */
function fold(value: string): string {
  return value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
}

interface FinderHit {
  ingredient: Ingredient
  /** Why a non-name match surfaced (e.g. "aka garbanzo beans", "es: garbanzos", "#legume"); null when the name itself matched. */
  hint: string | null
}

function findHit(i: Ingredient, foldedTerm: string): FinderHit | null {
  if (fold(i.name).includes(foldedTerm)) return { ingredient: i, hint: null }
  const alias = i.aliases.find((a) => fold(a).includes(foldedTerm))
  if (alias) return { ingredient: i, hint: `aka ${alias}` }
  const localized = i.localizedNames.find((l) => fold(l.name).includes(foldedTerm))
  if (localized) return { ingredient: i, hint: `${localized.language}: ${localized.name}` }
  const tag = i.tags.find((t) => fold(t).includes(foldedTerm))
  if (tag) return { ingredient: i, hint: `#${tag}` }
  return null
}

function caloriesOf(i: Ingredient): number | null {
  const c = i.nutritionPerServing.find((n) => n.nutrientId === 'calories')
  return c ? Math.round(c.value) : null
}

// Cap the rendered options so a large catalog never bloats the DOM; the footer nudges
// the user to keep typing instead.
const VISIBLE_MAX = 50
// How many tag chips to offer; the most common tags double as browse categories.
const TAG_CHIPS_MAX = 10

export function IngredientFinder({
  ingredients,
  value,
  onSelect,
  disabled,
  placeholder = 'Search ingredients…',
  title,
}: {
  ingredients: Ingredient[]
  /** Selected ingredient id, or '' for none. */
  value: string
  onSelect: (ingredient: Ingredient) => void
  disabled?: boolean
  placeholder?: string
  title?: string
}) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [active, setActive] = useState(0)
  const [tag, setTag] = useState<string | null>(null)
  const listRef = useRef<HTMLDivElement>(null)
  const listId = useId()

  const selected = useMemo(
    () => ingredients.find((i) => i.id === value) ?? null, [ingredients, value])

  // The most common tags across the catalog become the filter chips.
  const topTags = useMemo(() => {
    const counts = new Map<string, number>()
    for (const i of ingredients) {
      for (const t of i.tags) counts.set(t, (counts.get(t) ?? 0) + 1)
    }
    return [...counts]
      .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
      .slice(0, TAG_CHIPS_MAX)
      .map(([t]) => t)
  }, [ingredients])

  const hits = useMemo(() => {
    const term = fold(query.trim())
    const out: FinderHit[] = []
    for (const i of ingredients) {
      if (tag && !i.tags.includes(tag)) continue
      const hit = term ? findHit(i, term) : { ingredient: i, hint: null }
      if (hit) out.push(hit)
    }
    return out
  }, [ingredients, query, tag])

  const visible = hits.slice(0, VISIBLE_MAX)

  // Keep the highlighted option on-screen as it moves. (The highlight itself is reset in the
  // handlers that change the result set — typing, toggling a tag chip, opening — not in an
  // effect, so no cascading render.)
  useEffect(() => {
    listRef.current?.querySelector('[data-active="true"]')?.scrollIntoView({ block: 'nearest' })
  }, [active])

  function openFinder() {
    if (!disabled && !open) { setOpen(true); setQuery(''); setActive(0) }
  }
  function close() {
    setOpen(false)
    setQuery('')
    setTag(null)
    setActive(0)
  }
  function choose(i: Ingredient) {
    onSelect(i)
    close()
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    if (!open) {
      if (e.key === 'ArrowDown' || e.key === 'Enter') { e.preventDefault(); openFinder() }
      return
    }
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setActive((a) => Math.min(a + 1, visible.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((a) => Math.max(a - 1, 0))
    } else if (e.key === 'Enter') {
      e.preventDefault()
      if (visible[active]) choose(visible[active].ingredient)
    } else if (e.key === 'Escape') {
      e.preventDefault()
      close()
    }
  }

  return (
    <div className="finder" title={title}>
      <input
        className="input"
        role="combobox"
        aria-expanded={open}
        aria-controls={listId}
        aria-autocomplete="list"
        aria-label={title ?? 'Ingredient'}
        disabled={disabled}
        placeholder={selected ? selected.name : placeholder}
        value={open ? query : selected?.name ?? ''}
        onFocus={openFinder}
        onClick={openFinder}
        onChange={(e) => { setQuery(e.target.value); setActive(0); if (!open) setOpen(true) }}
        onKeyDown={handleKeyDown}
        onBlur={close}
      />
      {open && (
        // preventDefault on mousedown keeps focus in the input, so clicking an option or a
        // tag chip never fires the input's blur-close before the click lands.
        <div className="finder-pop" onMouseDown={(e) => e.preventDefault()}>
          {topTags.length > 0 && (
            <div className="finder-tags" aria-label="Filter by tag">
              {topTags.map((t) => (
                <button key={t} type="button"
                  className={`finder-tag ${tag === t ? 'on' : ''}`}
                  aria-pressed={tag === t}
                  onClick={() => { setTag(tag === t ? null : t); setActive(0) }}>
                  {t}
                </button>
              ))}
            </div>
          )}
          <div className="finder-list" id={listId} ref={listRef} role="listbox">
            {visible.length === 0 && (
              <p className="muted small finder-empty">
                No ingredients match{query.trim() ? ` "${query.trim()}"` : ''}{tag ? ` in #${tag}` : ''}.
              </p>
            )}
            {visible.map((h, idx) => {
              const kcal = caloriesOf(h.ingredient)
              return (
                <button key={h.ingredient.id} type="button" role="option"
                  aria-selected={h.ingredient.id === value}
                  data-active={idx === active || undefined}
                  className={[
                    'finder-option',
                    idx === active ? 'active' : '',
                    h.ingredient.id === value ? 'selected' : '',
                  ].filter(Boolean).join(' ')}
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
            {hits.length > visible.length && (
              <p className="muted small finder-empty">
                {hits.length - visible.length} more — keep typing to narrow it down.
              </p>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
