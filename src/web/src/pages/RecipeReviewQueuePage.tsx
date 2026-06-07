import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { recipeApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import type { Recipe } from '../api/types'

/** Admin-only screen: the queue of user recipes awaiting review, with approve/reject actions. */
export function RecipeReviewQueuePage() {
  const { profile } = useAuth()
  const navigate = useNavigate()
  const [items, setItems] = useState<Recipe[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  // Non-admins shouldn't be here; bounce them back to the recipe list.
  useEffect(() => {
    if (profile && !profile.isActingAsAdmin) navigate('/recipes', { replace: true })
  }, [profile, navigate])

  async function load() {
    setLoading(true)
    setError(null)
    try {
      setItems(await recipeApi.reviewQueue())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load review queue.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    let cancelled = false
    recipeApi.reviewQueue()
      .then((q) => { if (!cancelled) setItems(q) })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load review queue.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [])

  async function act(id: string, action: 'approve' | 'reject') {
    setBusyId(id)
    setError(null)
    try {
      if (action === 'approve') await recipeApi.approve(id)
      else await recipeApi.reject(id)
      await load()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Action failed.')
    } finally {
      setBusyId(null)
    }
  }

  return (
    <div className="stack">
      <div className="row spread">
        <h1>Review queue</h1>
        <Link className="btn btn-sm" to="/recipes">Back to recipes</Link>
      </div>

      {error && <div className="notice error">{error}</div>}
      {loading && items.length === 0 && <p className="muted">Loading…</p>}
      {!loading && items.length === 0 && !error && (
        <div className="card">
          <p className="muted" style={{ margin: 0 }}>No recipes are awaiting review.</p>
        </div>
      )}

      <div className="list">
        {items.map((r) => (
          <div key={r.id} className="card stack">
            <div className="row spread">
              <Link to={`/recipes/${r.id}`}><strong>{r.name}</strong></Link>
              <span className="badge mine">Pending review</span>
            </div>
            <div className="small muted">
              {r.items.length} ingredient{r.items.length === 1 ? '' : 's'} · {r.servings} serving{r.servings === 1 ? '' : 's'}
              {caloriesOf(r) != null && ` · ${caloriesOf(r)} kcal total`}
            </div>
            <div className="row" style={{ gap: '0.5rem' }}>
              <button className="btn btn-primary btn-sm" disabled={busyId === r.id}
                onClick={() => act(r.id, 'approve')}>Approve</button>
              <button className="btn btn-danger btn-sm" disabled={busyId === r.id}
                onClick={() => act(r.id, 'reject')}>Reject</button>
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

function caloriesOf(r: Recipe): number | null {
  const c = r.totalNutrition.find((n) => n.nutrientId === 'calories')
  return c ? Math.round(c.value) : null
}
