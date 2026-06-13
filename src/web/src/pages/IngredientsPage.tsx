import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ingredientApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import { UNIT_LABELS, type Ingredient } from '../api/types'
import { IconIngredient } from '../components/icons'
import { EmptyState, SkeletonList } from '../components/Placeholders'
import { FavoriteStar } from '../components/FavoriteStar'
import { withFavorite } from '../lib/favorites'

export function IngredientsPage() {
  const { profile } = useAuth()
  const [items, setItems] = useState<Ingredient[]>([])
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
        const result = await ingredientApi.list(search.trim() || undefined, isAdmin && showAll)
        if (!cancelled) setItems(result)
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load ingredients.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }, 250)
    return () => { cancelled = true; clearTimeout(handle) }
  }, [search, showAll, isAdmin])

  // Optimistic toggle: flip + re-float locally, then persist; revert on failure.
  async function toggleFavorite(item: Ingredient) {
    const next = !item.isFavorite
    setItems((prev) => withFavorite(prev, item.id, next))
    try {
      await ingredientApi.setFavorite(item.id, next)
    } catch {
      setItems((prev) => withFavorite(prev, item.id, !next))
    }
  }

  return (
    <div className="stack">
      <div className="row spread">
        <h1>Ingredients</h1>
        <Link className="btn btn-primary btn-sm" to="/ingredients/new">+ New</Link>
      </div>

      <input
        className="input"
        placeholder="Search by name, alias, tag, or non-English name…"
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
      {loading && items.length === 0 && <SkeletonList />}
      {!loading && items.length === 0 && !error && (
        <EmptyState icon={<IconIngredient size={28} />} title="No ingredients found">
          Create your first one with <strong>+ New</strong>.
        </EmptyState>
      )}

      <div className="list">
        {items.map((i) => (
          <Link key={i.id} to={`/ingredients/${i.id}`} className="card ingredient-item">
            <div className="ingredient-thumb">
              {i.imageUrl ? <img src={i.imageUrl} alt="" /> : <IconIngredient size={22} />}
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
              {i.tags.length > 0 && (
                <div className="row wrap" style={{ gap: '0.25rem', marginTop: '0.25rem' }}>
                  {i.tags.slice(0, 4).map((t) => <span key={t} className="badge">{t}</span>)}
                  {i.tags.length > 4 && <span className="small muted">+{i.tags.length - 4}</span>}
                </div>
              )}
            </div>
            <FavoriteStar isFavorite={i.isFavorite} onToggle={() => toggleFavorite(i)} />
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
