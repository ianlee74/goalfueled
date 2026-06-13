import { useEffect, useId, useMemo, useRef, useState } from 'react'
import type { BurnSuggestion } from '../api/types'
import { fold } from '../lib/fold'

// The burn-activity finder: a free-text combobox that replaces the native <datalist>
// autocomplete on the Add-Burn form (the browser-drawn datalist popup is tiny, unthemable
// and clashes with the instrument look). The user's past activities — distinct names,
// most recent first, each carrying the calories from its last entry — render in the same
// styled popover as the ingredient finder; picking one fills the name AND defaults the
// calories. Unlike the ingredient finder the field stays open-ended: there is no fixed
// activity catalog, so any typed name is valid and the list is only a shortcut.

// Cap the rendered options so a long history never bloats the DOM; the footer nudges
// the user to keep typing instead.
const VISIBLE_MAX = 30

export function ActivityFinder({
  suggestions,
  value,
  onChange,
  onSelect,
  disabled,
  placeholder = 'e.g. Morning run',
}: {
  /** Past activities, most recent first (the order the API returns). */
  suggestions: BurnSuggestion[]
  /** The activity name — free text, not a selection; this IS the field's value. */
  value: string
  /** The user typed (or cleared) the name. */
  onChange: (name: string) => void
  /** The user picked a past activity from the list (callers also default the calories). */
  onSelect: (suggestion: BurnSuggestion) => void
  disabled?: boolean
  placeholder?: string
}) {
  const [open, setOpen] = useState(false)
  const [active, setActive] = useState(0)
  const listRef = useRef<HTMLDivElement>(null)
  const listId = useId()

  // Case-/accent-insensitive substring match over past names; an empty box shows them all
  // (the popover doubles as a "recent activities" browse list).
  const matches = useMemo(() => {
    const term = fold(value.trim())
    return term ? suggestions.filter((s) => fold(s.name).includes(term)) : suggestions
  }, [suggestions, value])

  const visible = matches.slice(0, VISIBLE_MAX)

  // Keep the highlighted option on-screen as it moves (same pattern as the ingredient
  // finder: the highlight itself is reset in the handlers that change the result set).
  useEffect(() => {
    listRef.current?.querySelector('[data-active="true"]')?.scrollIntoView({ block: 'nearest' })
  }, [active])

  // With no history there is nothing to offer; the field behaves as a plain text input.
  const hasSuggestions = suggestions.length > 0
  const popoverOpen = open && hasSuggestions

  function openFinder() {
    if (!disabled && !open) { setOpen(true); setActive(0) }
  }
  function choose(s: BurnSuggestion) {
    onSelect(s)
    setOpen(false)
  }

  function handleKeyDown(e: React.KeyboardEvent) {
    if (!popoverOpen) {
      if (e.key === 'ArrowDown') { e.preventDefault(); openFinder() }
      return
    }
    if (e.key === 'ArrowDown') {
      e.preventDefault()
      setActive((a) => Math.min(a + 1, visible.length - 1))
    } else if (e.key === 'ArrowUp') {
      e.preventDefault()
      setActive((a) => Math.max(a - 1, 0))
    } else if (e.key === 'Enter') {
      // Enter takes the highlighted past activity; with no match it just closes the
      // popover, leaving the typed (new) name in place.
      e.preventDefault()
      if (visible[active]) choose(visible[active])
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
        onFocus={openFinder}
        onClick={openFinder}
        onChange={(e) => { onChange(e.target.value); setActive(0); if (!open) setOpen(true) }}
        onKeyDown={handleKeyDown}
        onBlur={() => setOpen(false)}
      />
      {popoverOpen && (
        // preventDefault on mousedown keeps focus in the input, so clicking an option
        // never fires the input's blur-close before the click lands.
        <div className="finder-pop" onMouseDown={(e) => e.preventDefault()}>
          <div className="finder-list" id={listId} ref={listRef} role="listbox" aria-label="Past activities">
            {visible.length === 0 && (
              <p className="muted small finder-empty">
                No past activities match — “{value.trim()}” will be logged as a new one.
              </p>
            )}
            {visible.map((s, idx) => (
              <button key={s.name} type="button" role="option"
                aria-selected={fold(s.name) === fold(value.trim())}
                data-active={idx === active || undefined}
                className={`finder-option ${idx === active ? 'active' : ''}`}
                onMouseEnter={() => setActive(idx)}
                onClick={() => choose(s)}>
                <span className="finder-option-name">{s.name}</span>
                <span className="small muted">
                  <span className="finder-burn">−{Math.round(s.lastCalories)} kcal</span> last time
                </span>
              </button>
            ))}
            {matches.length > visible.length && (
              <p className="muted small finder-empty">
                {matches.length - visible.length} more — keep typing to narrow it down.
              </p>
            )}
          </div>
        </div>
      )}
    </div>
  )
}
