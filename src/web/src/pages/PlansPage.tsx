import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { mealPlanApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import { MEAL_PLAN_CADENCE_LABELS, type MealPlan } from '../api/types'
import { IconPlan } from '../components/icons'
import { EmptyState, SkeletonList } from '../components/Placeholders'

export function PlansPage() {
  const { profile } = useAuth()
  const [items, setItems] = useState<MealPlan[]>([])
  const [search, setSearch] = useState('')
  const [showAll, setShowAll] = useState(false)
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  const isAdmin = !!profile?.isActingAsAdmin
  // While acting via a share, only show plan-editing affordances when the grant includes ManageMealPlans.
  const canManagePlans = !profile?.isActingViaShare || !!profile?.actingPermissions?.includes('ManageMealPlans')

  // Debounced search. The admin "show all" flag is only sent when acting as an admin.
  useEffect(() => {
    let cancelled = false
    const handle = setTimeout(async () => {
      setLoading(true)
      setError(null)
      try {
        const result = await mealPlanApi.list(search.trim() || undefined, isAdmin && showAll)
        if (!cancelled) setItems(result)
      } catch (e) {
        if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load meal plans.')
      } finally {
        if (!cancelled) setLoading(false)
      }
    }, 250)
    return () => { cancelled = true; clearTimeout(handle) }
  }, [search, showAll, isAdmin])

  return (
    <div className="stack">
      <div className="row spread">
        <h1>Meal plans</h1>
        {canManagePlans && <Link className="btn btn-primary btn-sm" to="/plans/new">+ New</Link>}
      </div>

      <input
        className="input"
        placeholder="Search plans…"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />

      {isAdmin && (
        <label className="toggle" title="Show plans owned by all users">
          <input type="checkbox" checked={showAll} onChange={(e) => setShowAll(e.target.checked)} />
          Show all users' plans
        </label>
      )}

      {error && <div className="notice error">{error}</div>}
      {loading && items.length === 0 && <SkeletonList />}
      {!loading && items.length === 0 && !error && (
        <EmptyState icon={<IconPlan size={28} />} title="No meal plans yet">
          Create your first one with <strong>+ New</strong>.
        </EmptyState>
      )}

      <div className="list">
        {items.map((p) => (
          <Link key={p.id} to={`/plans/${p.id}`} className="card ingredient-item">
            <div className="ingredient-thumb"><IconPlan size={22} /></div>
            <div style={{ flex: 1, minWidth: 0 }}>
              <div className="row spread">
                <strong>{p.name}</strong>
                <span className="badge mine">{MEAL_PLAN_CADENCE_LABELS[p.cadence]}</span>
              </div>
              <div className="small muted">
                {p.scheduledMeals.length} scheduled · {p.untimedItems.length} untimed
                {averageCaloriesOf(p) != null && ` · ${averageCaloriesOf(p)} kcal/day`}
              </div>
            </div>
          </Link>
        ))}
      </div>
    </div>
  )
}

// For both daily and weekly plans the server's DailyAverage is the per-day calorie figure to show.
function averageCaloriesOf(p: MealPlan): number | null {
  const c = p.summary.dailyAverage.find((n) => n.nutrientId === 'calories')
  return c ? Math.round(c.value) : null
}
