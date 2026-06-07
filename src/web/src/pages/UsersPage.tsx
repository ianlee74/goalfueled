import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { adminApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import type { AdminUser } from '../api/types'

/** Admin-only screen: list users and promote/demote the App Admin role. */
export function UsersPage() {
  const { profile } = useAuth()
  const navigate = useNavigate()
  const [items, setItems] = useState<AdminUser[]>([])
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
      setItems(await adminApi.listUsers())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load users.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    let cancelled = false
    adminApi.listUsers()
      .then((u) => { if (!cancelled) setItems(u) })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load users.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [])

  async function setAdmin(id: string, isAdmin: boolean) {
    setBusyId(id)
    setError(null)
    try {
      await adminApi.setAdminRole(id, isAdmin)
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
        <h1>Users</h1>
      </div>

      {error && <div className="notice error">{error}</div>}
      {loading && items.length === 0 && <p className="muted">Loading…</p>}
      {!loading && items.length === 0 && !error && (
        <div className="card">
          <p className="muted" style={{ margin: 0 }}>No users found.</p>
        </div>
      )}

      <div className="list">
        {items.map((u) => (
          <div key={u.id} className="card ingredient-item">
            <div className="ingredient-thumb">
              {u.avatarUrl ? <img src={u.avatarUrl} alt="" /> : '👤'}
            </div>
            <div style={{ flex: 1, minWidth: 0 }}>
              <div className="row spread">
                <strong>{u.displayName}</strong>
                <span className={`badge ${u.isAdmin ? 'app' : 'mine'}`}>
                  {u.isAdmin ? 'Admin' : 'User'}
                </span>
              </div>
              <div className="small muted">{u.email}{u.isSelf && ' · you'}</div>
            </div>
            <div className="row" style={{ gap: '0.5rem' }}>
              {u.isAdmin ? (
                <button className="btn btn-danger btn-sm" disabled={busyId === u.id}
                  onClick={() => setAdmin(u.id, false)}>Demote</button>
              ) : (
                <button className="btn btn-primary btn-sm" disabled={busyId === u.id}
                  onClick={() => setAdmin(u.id, true)}>Promote</button>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
