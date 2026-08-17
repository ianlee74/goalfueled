import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { adminApi, featureApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import {
  FEATURE_ACCESS_MODE_LABELS, SUBSCRIPTION_LEVELS, SUBSCRIPTION_LEVEL_LABELS,
  type AdminUser, type Feature, type FeatureAccessMode, type SubscriptionLevel,
} from '../api/types'
import { IconUser, IconUsers } from '../components/icons'
import { EmptyState, SkeletonList } from '../components/Placeholders'
import { useConfirm } from '../hooks/useConfirm'
import { FeatureStatusBadge } from './FeaturesPage'

/**
 * Admin-only screen: list users, promote/demote the App Admin role, and administer feature tags
 * (subscription tier plus per-user grants and denials).
 */
export function UsersPage() {
  const { profile, refresh } = useAuth()
  const navigate = useNavigate()
  const { confirm, dialog } = useConfirm()
  const [items, setItems] = useState<AdminUser[]>([])
  const [catalog, setCatalog] = useState<Feature[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  // Which user's feature panel is expanded (one at a time keeps the list scannable).
  const [openId, setOpenId] = useState<string | null>(null)

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
    Promise.all([adminApi.listUsers(), featureApi.catalog()])
      .then(([u, f]) => { if (!cancelled) { setItems(u); setCatalog(f) } })
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

  /**
   * Applies a feature-tag change and folds the updated user back into the list. When the admin edits
   * their OWN account the signed-in profile is refreshed too, so the gated UI on this session updates
   * immediately rather than at the next reload.
   */
  async function applyFeatureChange(id: string, change: () => Promise<AdminUser>) {
    setBusyId(id)
    setError(null)
    try {
      const updated = await change()
      setItems((prev) => prev.map((u) => (u.id === id ? updated : u)))
      if (updated.isSelf) await refresh()
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
        '(logs, plans, goals, weights, stats, and private ingredients/recipes/fuel sources). ' +
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
        <Link className="btn btn-sm" to="/features">Feature tags</Link>
      </div>

      {error && <div className="notice error">{error}</div>}
      {loading && items.length === 0 && <SkeletonList />}
      {!loading && items.length === 0 && !error && (
        <EmptyState icon={<IconUsers size={28} />} title="No users found" />
      )}

      <div className="list">
        {items.map((u) => (
          <div key={u.id} className="card stack" style={{ gap: '0.6rem' }}>
            <div className="ingredient-item">
              <div className="ingredient-thumb">
                {u.avatarUrl ? <img src={u.avatarUrl} alt="" /> : <IconUser size={22} />}
              </div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div className="row spread">
                  <strong>{u.displayName}</strong>
                  <div className="row" style={{ gap: '0.3rem' }}>
                    <span className="badge">{SUBSCRIPTION_LEVEL_LABELS[u.subscriptionLevel]}</span>
                    <span className={`badge ${u.isAdmin ? 'app' : 'mine'}`}>
                      {u.isAdmin ? 'Admin' : 'User'}
                    </span>
                  </div>
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

            <div className="row spread">
              <label className="field feature-tier">
                Subscription
                <select
                  className="select"
                  value={u.subscriptionLevel}
                  disabled={busyId === u.id}
                  onChange={(e) => applyFeatureChange(u.id, () =>
                    adminApi.setSubscriptionLevel(u.id, e.target.value as SubscriptionLevel))}
                >
                  {SUBSCRIPTION_LEVELS.map((level) => (
                    <option key={level} value={level}>{SUBSCRIPTION_LEVEL_LABELS[level]}</option>
                  ))}
                </select>
              </label>
              <button
                type="button"
                className="btn btn-sm"
                aria-expanded={openId === u.id ? 'true' : 'false'}
                onClick={() => setOpenId(openId === u.id ? null : u.id)}
              >
                {openId === u.id ? 'Hide features' : `Features (${u.effectiveFeatureKeys.length})`}
              </button>
            </div>

            {openId === u.id && (
              <FeatureAccessPanel
                user={u}
                catalog={catalog}
                busy={busyId === u.id}
                onSet={(key, mode) => applyFeatureChange(u.id, () =>
                  adminApi.setFeatureAccess(u.id, key, mode))}
              />
            )}
          </div>
        ))}
      </div>
    </div>
  )
}

/**
 * Per-user feature grid: one row per catalog feature showing whether the user currently HAS it and a
 * tri-state override (tier default / granted / denied). The override is what's stored; "has it" is the
 * resolved result the server sends back after every change.
 */
function FeatureAccessPanel({
  user, catalog, busy, onSet,
}: {
  user: AdminUser
  catalog: Feature[]
  busy: boolean
  onSet: (key: string, mode: FeatureAccessMode) => void
}) {
  const modeFor = (key: string): FeatureAccessMode =>
    user.deniedFeatureKeys.includes(key) ? 'Denied'
      : user.grantedFeatureKeys.includes(key) ? 'Granted'
        : 'Inherit'

  return (
    <div className="feature-panel">
      <p className="small muted feature-panel-hint">
        Tier default follows the subscription above. Granted adds a feature the tier doesn't include;
        denied removes one it does. Planned and disabled features stay off either way.
      </p>
      {catalog.map((f) => {
        const has = user.effectiveFeatureKeys.includes(f.key)
        return (
          <div key={f.key} className="feature-row">
            <div className="feature-row-main">
              <div className="row wrap feature-row-title">
                <strong>{f.name}</strong>
                <FeatureStatusBadge status={f.status} />
                {has
                  ? <span className="badge app">On</span>
                  : <span className="badge">Off</span>}
              </div>
              <div className="small muted">
                {f.minimumSubscriptionLevel === 'Free'
                  ? 'All tiers'
                  : `${SUBSCRIPTION_LEVEL_LABELS[f.minimumSubscriptionLevel]} and above`}
              </div>
            </div>
            <select
              className="select feature-mode"
              aria-label={`${f.name} access for ${user.displayName}`}
              value={modeFor(f.key)}
              disabled={busy}
              onChange={(e) => onSet(f.key, e.target.value as FeatureAccessMode)}
            >
              {(['Inherit', 'Granted', 'Denied'] as FeatureAccessMode[]).map((mode) => (
                <option key={mode} value={mode}>{FEATURE_ACCESS_MODE_LABELS[mode]}</option>
              ))}
            </select>
          </div>
        )
      })}
    </div>
  )
}
