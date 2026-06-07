import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ingredientApi } from '../api/services'
import { UNIT_LABELS, type Ingredient } from '../api/types'

export function IngredientsPage() {
  const [items, setItems] = useState<Ingredient[]>([])
  const [search, setSearch] = useState('')
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Debounced search.
  useEffect(() => {
    let cancelled = false
    const handle = setTimeout(async () => {
      setLoading(true)
      setError(null)
      try {
        const result = await ingredientApi.list(search.trim() || undefined)
        if (!cancelled) setItems(result)
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load ingredients.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }, 250)
    return () => { cancelled = true; clearTimeout(handle) }
  }, [search])

  return (
    <div className="stack">
      <div className="row spread">
        <h1>Ingredients</h1>
        <Link className="btn btn-primary btn-sm" to="/ingredients/new">+ New</Link>
      </div>

      <input
        className="input"
        placeholder="Search ingredients…"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />

      {error && <div className="notice error">{error}</div>}
      {loading && items.length === 0 && <p className="muted">Loading…</p>}
      {!loading && items.length === 0 && !error && (
        <div className="card">
          <p className="muted" style={{ margin: 0 }}>
            No ingredients found. Create your first one with <strong>+ New</strong>.
          </p>
        </div>
      )}

      <div className="list">
        {items.map((i) => (
          <Link key={i.id} to={`/ingredients/${i.id}`} className="card ingredient-item">
            <div className="ingredient-thumb">
              {i.imageUrl ? <img src={i.imageUrl} alt="" /> : '🥗'}
            </div>
            <div style={{ flex: 1, minWidth: 0 }}>
              <div className="row spread">
                <strong>{i.name}</strong>
                <span className={`badge ${i.scope === 'App' ? 'app' : 'mine'}`}>
                  {i.scope === 'App' ? 'App' : 'Mine'}
                </span>
              </div>
              <div className="small muted">
                Per {i.servingSize} {UNIT_LABELS[i.unit]}
                {caloriesOf(i) != null && ` · ${caloriesOf(i)} kcal`}
              </div>
            </div>
          </Link>
        ))}
      </div>
    </div>
  )
}

function caloriesOf(i: Ingredient): number | null {
  const c = i.nutritionPerServing.find((n) => n.nutrientId === 'calories')
  return c ? Math.round(c.value) : null
}
