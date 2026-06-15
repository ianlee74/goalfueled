import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { mealApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import type { FuelType, Meal } from '../api/types'
import { FUEL_TYPE_LABELS } from '../api/types'

// The fuel types offered as filter chips, in the order they appear on the chip row.
const FUEL_TYPES: FuelType[] = ['Food', 'Supplement', 'Hydration']
import { IconMeal } from '../components/icons'
import { EmptyState, SkeletonList } from '../components/Placeholders'
import { FavoriteStar } from '../components/FavoriteStar'
import { withFavorite } from '../lib/favorites'

export function FuelSourcesPage() {
  const { profile } = useAuth()
  const [items, setItems] = useState<Meal[]>([])
  const [search, setSearch] = useState('')
  // Client-side narrowing by fuel type; null = "All" (no filter). The server already returns the
  // full (search-matched) set, so this is applied over the loaded items without a re-fetch.
  const [fuelType, setFuelType] = useState<FuelType | null>(null)
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
        if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load fuel sources.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }, 250)
    return () => { cancelled = true; clearTimeout(handle) }
  }, [search, showAll, isAdmin])

  // Optimistic toggle: flip + re-float locally, then persist; revert on failure.
  async function toggleFavorite(item: Meal) {
    const next = !item.isFavorite
    setItems((prev) => withFavorite(prev, item.id, next))
    try {
      await mealApi.setFavorite(item.id, next)
    } catch {
      setItems((prev) => withFavorite(prev, item.id, !next))
    }
  }

  // Apply the fuel-type chip filter over the already-loaded (search-matched) items. The server
  // returns favorites first, so filtering in place preserves that ordering within the subset.
  const visible = fuelType ? items.filter((m) => m.fuelType === fuelType) : items

  return (
    <div className="stack">
      <div className="row spread">
        <h1>Fuel Sources</h1>
        <Link className="btn btn-primary btn-sm" to="/fuel-sources/new">+ New</Link>
      </div>

      <input
        className="input"
        placeholder="Search fuel sources…"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />

      <div className="finder-tags" aria-label="Filter by fuel type">
        <button
          type="button"
          className={`finder-tag ${fuelType === null ? 'on' : ''}`}
          aria-pressed={fuelType === null}
          onClick={() => setFuelType(null)}
        >
          All
        </button>
        {FUEL_TYPES.map((ft) => (
          <button
            key={ft}
            type="button"
            className={`finder-tag ${fuelType === ft ? 'on' : ''}`}
            aria-pressed={fuelType === ft}
            onClick={() => setFuelType(fuelType === ft ? null : ft)}
          >
            {FUEL_TYPE_LABELS[ft]}
          </button>
        ))}
      </div>

      {isAdmin && (
        <label className="toggle" title="Show items owned by all users">
          <input type="checkbox" checked={showAll} onChange={(e) => setShowAll(e.target.checked)} />
          Show all users' items
        </label>
      )}

      {error && <div className="notice error">{error}</div>}
      {loading && items.length === 0 && <SkeletonList />}
      {!loading && items.length === 0 && !error && (
        <EmptyState icon={<IconMeal size={28} />} title="No fuel sources found">
          Create your first one with <strong>+ New</strong>.
        </EmptyState>
      )}
      {!loading && items.length > 0 && visible.length === 0 && fuelType && !error && (
        <EmptyState icon={<IconMeal size={28} />} title={`No ${FUEL_TYPE_LABELS[fuelType]} fuel sources`}>
          Try a different fuel type or clear the filter.
        </EmptyState>
      )}

      <div className="list">
        {visible.map((m) => (
          <Link key={m.id} to={`/fuel-sources/${m.id}`} className="card ingredient-item">
            <div className="ingredient-thumb"><IconMeal size={22} /></div>
            <div style={{ flex: 1, minWidth: 0 }}>
              <div className="row spread">
                <strong>{m.name}</strong>
                <div className="row" style={{ gap: '0.35rem' }}>
                  <span className="badge fuel-type">{FUEL_TYPE_LABELS[m.fuelType]}</span>
                  <span className={`badge ${m.scope === 'App' ? 'app' : 'mine'}`}>
                    {m.scope === 'App' ? 'App' : 'Mine'}
                  </span>
                </div>
              </div>
              <div className="small muted">
                {m.portions.length} recipe{m.portions.length === 1 ? '' : 's'}
                {caloriesOf(m) != null && ` · ${caloriesOf(m)} kcal total`}
              </div>
            </div>
            <FavoriteStar isFavorite={m.isFavorite} onToggle={() => toggleFavorite(m)} />
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
