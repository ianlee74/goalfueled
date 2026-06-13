import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { recipeApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import { RECIPE_VISIBILITY_LABELS, type Recipe } from '../api/types'
import { IconRecipe } from '../components/icons'
import { EmptyState, SkeletonList } from '../components/Placeholders'
import { FavoriteStar } from '../components/FavoriteStar'
import { withFavorite } from '../lib/favorites'

export function RecipesPage() {
  const { profile } = useAuth()
  const [items, setItems] = useState<Recipe[]>([])
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
        const result = await recipeApi.list(search.trim() || undefined, isAdmin && showAll)
        if (!cancelled) setItems(result)
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load recipes.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }, 250)
    return () => { cancelled = true; clearTimeout(handle) }
  }, [search, showAll, isAdmin])

  // Optimistic toggle: flip + re-float locally, then persist; revert on failure.
  async function toggleFavorite(item: Recipe) {
    const next = !item.isFavorite
    setItems((prev) => withFavorite(prev, item.id, next))
    try {
      await recipeApi.setFavorite(item.id, next)
    } catch {
      setItems((prev) => withFavorite(prev, item.id, !next))
    }
  }

  return (
    <div className="stack">
      <div className="row spread">
        <h1>Recipes</h1>
        <div className="row" style={{ gap: '0.5rem' }}>
          {profile?.isActingAsAdmin && (
            <Link className="btn btn-sm" to="/recipes/review-queue">Review queue</Link>
          )}
          <Link className="btn btn-primary btn-sm" to="/recipes/new">+ New</Link>
        </div>
      </div>

      <input
        className="input"
        placeholder="Search recipes…"
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
        <EmptyState icon={<IconRecipe size={28} />} title="No recipes found">
          Create your first one with <Link to="/recipes/new">+ New</Link>.
        </EmptyState>
      )}

      <div className="list">
        {items.map((r) => (
          <Link key={r.id} to={`/recipes/${r.id}`} className="card ingredient-item">
            <div className="ingredient-thumb">
              {r.imageUrl ? <img src={r.imageUrl} alt="" /> : <IconRecipe size={22} />}
            </div>
            <div style={{ flex: 1, minWidth: 0 }}>
              <div className="row spread">
                <strong>{r.name}</strong>
                <span className={`badge ${badgeClass(r)}`}>{badgeLabel(r)}</span>
              </div>
              <div className="small muted">
                {r.items.length} ingredient{r.items.length === 1 ? '' : 's'} · {r.servings} serving{r.servings === 1 ? '' : 's'}
                {caloriesOf(r) != null && ` · ${caloriesOf(r)} kcal total`}
              </div>
            </div>
            <FavoriteStar isFavorite={r.isFavorite} onToggle={() => toggleFavorite(r)} />
          </Link>
        ))}
      </div>
    </div>
  )
}

function caloriesOf(r: Recipe): number | null {
  const c = r.totalNutrition.find((n) => n.nutrientId === 'calories')
  return c ? Math.round(c.value) : null
}

// App-level and public recipes get the "app" badge; otherwise show the lifecycle state.
function badgeClass(r: Recipe): string {
  return r.scope === 'App' || r.visibility === 'Public' ? 'app' : 'mine'
}
function badgeLabel(r: Recipe): string {
  if (r.scope === 'App') return 'App'
  if (r.visibility === 'Public') return 'Public'
  return RECIPE_VISIBILITY_LABELS[r.visibility]
}
