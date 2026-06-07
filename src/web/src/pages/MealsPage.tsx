import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { mealApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import type { Meal } from '../api/types'

export function MealsPage() {
  const { profile } = useAuth()
  const [items, setItems] = useState<Meal[]>([])
  const [search, setSearch] = useState('')
  const [showAll, setShowAll] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const isAdmin = !!profile?.isActingAsAdmin

  // Debounced search. The admin "show all" flag is only sent when acting as an admin.
  useEffect(() => {
    let cancelled = false
    const handle = setTimeout(async () => {
      setLoading(true)
      setError(null)
      try {
        const result = await mealApi.list(search.trim() || undefined, isAdmin && showAll)
        if (!cancelled) setItems(result)
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load meals.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }, 250)
    return () => { cancelled = true; clearTimeout(handle) }
  }, [search, showAll, isAdmin])

  return (
    <div className="stack">
      <div className="row spread">
        <h1>Meals</h1>
        <Link className="btn btn-primary btn-sm" to="/meals/new">+ New</Link>
      </div>

      <input
        className="input"
        placeholder="Search meals…"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />

      {isAdmin && (
        <label className="toggle" title="Show items owned by all users">
          <input type="checkbox" checked={showAll} onChange={(e) => setShowAll(e.target.checked)} />
          Show all users' items
        </label>
      )}

      {error && <div className="notice error">{error}</div>}
      {loading && items.length === 0 && <p className="muted">Loading…</p>}
      {!loading && items.length === 0 && !error && (
        <div className="card">
          <p className="muted" style={{ margin: 0 }}>
            No meals found. Create your first one with <strong>+ New</strong>.
          </p>
        </div>
      )}

      <div className="list">
        {items.map((m) => (
          <Link key={m.id} to={`/meals/${m.id}`} className="card ingredient-item">
            <div className="ingredient-thumb">🍽️</div>
            <div style={{ flex: 1, minWidth: 0 }}>
              <div className="row spread">
                <strong>{m.name}</strong>
                <span className={`badge ${m.scope === 'App' ? 'app' : 'mine'}`}>
                  {m.scope === 'App' ? 'App' : 'Mine'}
                </span>
              </div>
              <div className="small muted">
                {m.portions.length} recipe{m.portions.length === 1 ? '' : 's'}
                {caloriesOf(m) != null && ` · ${caloriesOf(m)} kcal total`}
              </div>
            </div>
          </Link>
        ))}
      </div>
    </div>
  )
}

function caloriesOf(m: Meal): number | null {
  const c = m.totalNutrition.find((n) => n.nutrientId === 'calories')
  return c ? Math.round(c.value) : null
}
