import { Link } from 'react-router-dom'
import type { ComponentType } from 'react'
import { IconIngredient, IconMeal, IconPlan, IconRecipe } from '../components/icons'

/**
 * The Library tab: the pantry/setup side of the app (DESIGN.md §3.5) — everything you
 * build once and reuse, as opposed to the daily loop on Today.
 */

const SECTIONS: { to: string; label: string; sub: string; Ico: ComponentType<{ size?: number }> }[] = [
  { to: '/ingredients', label: 'Ingredients', sub: 'Foods and their nutrition', Ico: IconIngredient },
  { to: '/recipes', label: 'Recipes', sub: 'Ingredients composed into dishes', Ico: IconRecipe },
  { to: '/fuel-sources', label: 'Fuel Sources', sub: 'Recipe portions you actually eat', Ico: IconMeal },
  { to: '/plans', label: 'Plans', sub: 'Weekly meal schedules', Ico: IconPlan },
]

export function LibraryPage() {
  return (
    <div className="stack">
      <h1>Library</h1>
      <div className="list">
        {SECTIONS.map((s) => (
          <Link key={s.to} to={s.to} className="card ingredient-item">
            <div className="ingredient-thumb"><s.Ico size={22} /></div>
            <div style={{ flex: 1, minWidth: 0 }}>
              <strong>{s.label}</strong>
              <div className="small muted">{s.sub}</div>
            </div>
            <span className="muted" aria-hidden="true">→</span>
          </Link>
        ))}
      </div>
    </div>
  )
}
