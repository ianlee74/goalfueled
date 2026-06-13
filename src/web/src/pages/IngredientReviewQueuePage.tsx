import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { ingredientApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import { UNIT_LABELS, type Ingredient } from '../api/types'
import { caloriesOf } from '../lib/ingredientMatch'

/** Admin-only screen: the queue of user ingredients awaiting review, with approve/reject actions. */
export function IngredientReviewQueuePage() {
  const { profile } = useAuth()
  const navigate = useNavigate()
  const [items, setItems] = useState<Ingredient[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)

  // Non-admins shouldn't be here; bounce them back to the ingredient list.
  useEffect(() => {
    if (profile && !profile.isActingAsAdmin) navigate('/ingredients', { replace: true })
  }, [profile, navigate])

  async function load() {
    setLoading(true)
    setError(null)
    try {
      setItems(await ingredientApi.reviewQueue())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load review queue.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    let cancelled = false
    ingredientApi.reviewQueue()
      .then((q) => { if (!cancelled) setItems(q) })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load review queue.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [])

  async function act(id: string, action: 'approve' | 'reject') {
    setBusyId(id)
    setError(null)
    try {
      if (action === 'approve') await ingredientApi.approve(id)
      else await ingredientApi.reject(id)
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
        <h1>Ingredient review queue</h1>
        <Link className="btn btn-sm" to="/ingredients">Back to ingredients</Link>
      </div>

      {error && <div className="notice error">{error}</div>}
      {loading && items.length === 0 && <p className="muted">Loading…</p>}
      {!loading && items.length === 0 && !error && (
        <div className="card">
          <p className="muted" style={{ margin: 0 }}>No ingredients are awaiting review.</p>
        </div>
      )}

      <div className="list">
        {items.map((i) => {
          const kcal = caloriesOf(i)
          return (
            <div key={i.id} className="card stack">
              <div className="row spread">
                <Link to={`/ingredients/${i.id}`}><strong>{i.name}</strong></Link>
                <span className="badge mine">Pending review</span>
              </div>
              <div className="small muted">
                Per {i.servingSize} {UNIT_LABELS[i.unit]}
                {kcal != null && ` · ${kcal} kcal`}
              </div>
              <div className="row" style={{ gap: '0.5rem' }}>
                <button className="btn btn-primary btn-sm" disabled={busyId === i.id}
                  onClick={() => act(i.id, 'approve')}>Approve</button>
                <button className="btn btn-danger btn-sm" disabled={busyId === i.id}
                  onClick={() => act(i.id, 'reject')}>Reject</button>
              </div>
            </div>
          )
        })}
      </div>
    </div>
  )
}
