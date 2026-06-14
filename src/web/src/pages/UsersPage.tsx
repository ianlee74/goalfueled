import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { adminApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import type { AdminUser } from '../api/types'
import { IconUser, IconUsers } from '../components/icons'
import { EmptyState, SkeletonList } from '../components/Placeholders'
import { useConfirm } from '../hooks/useConfirm'

/** Admin-only screen: list users and promote/demote the App Admin role. */
export function UsersPage() {
  const { profile } = useAuth()
  const navigate = useNavigate()
  const { confirm, dialog } = useConfirm()
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

  function askDelete(u: AdminUser) {
    confirm({
      title: `Delete ${u.displayName}?`,
      message: 'This permanently deletes the user and all of their personal data ' +
        '(logs, plans, goals, weights, stats, and private ingredients/recipes/meals). ' +
        'Content they created that was accepted as public/app-owned is kept. This cannot be undone.',
      confirmLabel: 'Delete user',
      pendingLabel: 'Deleting…',
      onConfirm: async () => {
        setBusyId(u.id)
        setError(null)
        try {
          await adminApi.deleteUser(u.id)
          await load()
        } catch (e) {
          setError(e instanceof Error ? e.message : 'Delete failed.')
          throw e // keep the dialog open so the admin sees it didn't complete
        } finally {
          setBusyId(null)
        }
      },
    })
  }

  return (
    <div className="stack">
      {dialog}
      <div className="row spread">
        <h1>Users</h1>
      </div>

      {error && <div className="notice error">{error}</div>}
      {loading && items.length === 0 && <SkeletonList />}
      {!loading && items.length === 0 && !error && (
        <EmptyState icon={<IconUsers size={28} />} title="No users found" />
      )}

      <div className="list">
        {items.map((u) => (
          <div key={u.id} className="card ingredient-item">
            <div className="ingredient-thumb">
              {u.avatarUrl ? <img src={u.avatarUrl} alt="" /> : <IconUser size={22} />}
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
                <button type="button" className="btn btn-danger btn-sm" disabled={busyId === u.id}
                  onClick={() => setAdmin(u.id, false)}>Demote</button>
              ) : (
                <button type="button" className="btn btn-primary btn-sm" disabled={busyId === u.id}
                  onClick={() => setAdmin(u.id, true)}>Promote</button>
              )}
              {!u.isSelf && (
                <button type="button" className="btn btn-danger btn-sm" disabled={busyId === u.id}
                  onClick={() => askDelete(u)}>Delete</button>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
