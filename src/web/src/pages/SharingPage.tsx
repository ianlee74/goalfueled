import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { sharingApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import {
  SHARE_PERMISSION_LABELS, SHARE_STATUS_LABELS,
  type ShareInvitation, type SharePermission,
} from '../api/types'
import { IconUsers } from '../components/icons'
import { EmptyState, SkeletonList } from '../components/Placeholders'
import { useConfirm } from '../hooks/useConfirm'

/**
 * "Sharing" (/sharing): the owner's view to create invitation links for a doctor/trainer/etc., see the
 * status of each (pending/accepted/rejected/deleted), copy a still-pending link, and revoke any of them.
 * Reached from the You tab. Only meaningful on the user's OWN account — a guest acting via a share is sent
 * back (the API blocks the writes anyway).
 */
export function SharingPage() {
  const { profile } = useAuth()
  const { confirm, dialog } = useConfirm()
  const [items, setItems] = useState<ShareInvitation[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)
  const [busyId, setBusyId] = useState<string | null>(null)
  const [copiedId, setCopiedId] = useState<string | null>(null)

  // New-invitation form state.
  const [label, setLabel] = useState('')
  const [canManagePlans, setCanManagePlans] = useState(false)
  const [creating, setCreating] = useState(false)

  const actingViaShare = !!profile?.isActingViaShare

  async function load() {
    setError(null)
    try {
      setItems(await sharingApi.listInvitations())
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load your invitations.')
    } finally {
      setLoading(false)
    }
  }

  useEffect(() => {
    let cancelled = false
    // Acting via a share has no own-invitation list to load; resolve loading without a fetch.
    if (actingViaShare) {
      Promise.resolve().then(() => { if (!cancelled) setLoading(false) })
      return () => { cancelled = true }
    }
    sharingApi.listInvitations()
      .then((i) => { if (!cancelled) setItems(i) })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load your invitations.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [actingViaShare])

  async function create() {
    setCreating(true)
    setError(null)
    try {
      const permissions: SharePermission[] = ['ViewLog']
      if (canManagePlans) permissions.push('ManageMealPlans')
      await sharingApi.createInvitation({ label: label.trim() || undefined, permissions })
      setLabel('')
      setCanManagePlans(false)
      await load()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create the invitation.')
    } finally {
      setCreating(false)
    }
  }

  async function copyLink(inv: ShareInvitation) {
    if (!inv.invitePath) return
    const url = `${window.location.origin}${inv.invitePath}`
    try {
      await navigator.clipboard.writeText(url)
      setCopiedId(inv.id)
      setTimeout(() => setCopiedId((c) => (c === inv.id ? null : c)), 2000)
    } catch {
      // Clipboard may be unavailable (insecure context); surface the URL so the user can copy it manually.
      window.prompt('Copy this invitation link:', url)
    }
  }

  function askRevoke(inv: ShareInvitation) {
    const active = inv.status === 'Accepted'
    confirm({
      title: active ? 'Revoke access?' : 'Delete invitation?',
      message: active
        ? `${inv.guestDisplayName ?? 'This person'} will lose access to your account.`
        : 'This invitation link will stop working and cannot be used.',
      confirmLabel: active ? 'Revoke access' : 'Delete',
      pendingLabel: 'Working…',
      onConfirm: async () => {
        setBusyId(inv.id)
        setError(null)
        try {
          await sharingApi.deleteInvitation(inv.id)
          await load()
        } catch (e) {
          setError(e instanceof Error ? e.message : 'Action failed.')
          throw e
        } finally {
          setBusyId(null)
        }
      },
    })
  }

  if (actingViaShare) {
    return (
      <div className="stack">
        <h1>Sharing</h1>
        <div className="notice info">
          Switch back to your own account to manage sharing.{' '}
          <Link to="/you">Go to You</Link>
        </div>
      </div>
    )
  }

  return (
    <div className="stack">
      {dialog}
      <div className="row spread">
        <h1>Sharing</h1>
        <Link className="btn btn-sm" to="/you">← Back</Link>
      </div>

      <div className="card stack">
        <p style={{ margin: 0 }}>
          Create a link to share your log with someone — a doctor, trainer, or family member. Send them the
          link; when they open it they can accept or reject. Each link can be used once.
        </p>
      </div>

      {/* Create a new invitation. */}
      <div className="card stack">
        <h2>New invitation</h2>
        <label className="stack" style={{ gap: '0.25rem' }}>
          <span className="small muted">Label (optional)</span>
          <input
            className="input"
            placeholder="e.g. Dr. Smith"
            value={label}
            onChange={(e) => setLabel(e.target.value)}
            disabled={creating}
          />
        </label>
        <div className="stack" style={{ gap: '0.35rem' }}>
          <span className="small muted">This person will be able to:</span>
          <label className="toggle" title="Always granted">
            <input type="checkbox" checked disabled />
            {SHARE_PERMISSION_LABELS.ViewLog}
          </label>
          <label className="toggle">
            <input
              type="checkbox"
              checked={canManagePlans}
              disabled={creating}
              onChange={(e) => setCanManagePlans(e.target.checked)}
            />
            {SHARE_PERMISSION_LABELS.ManageMealPlans}
          </label>
        </div>
        <button className="btn btn-primary" disabled={creating} onClick={create}>
          {creating ? 'Creating…' : 'Create link'}
        </button>
      </div>

      {error && <div className="notice error">{error}</div>}
      {loading && items.length === 0 && <SkeletonList />}
      {!loading && items.length === 0 && !error && (
        <EmptyState icon={<IconUsers size={28} />} title="No invitations yet">
          Create one above to share your log.
        </EmptyState>
      )}

      <div className="list">
        {items.map((inv) => (
          <div key={inv.id} className="card stack" style={{ gap: '0.5rem' }}>
            <div className="row spread">
              <strong>{inv.label || 'Shared invitation'}</strong>
              <span className={`badge ${inv.status === 'Accepted' ? 'app' : 'mine'}`}>
                {SHARE_STATUS_LABELS[inv.status]}
              </span>
            </div>
            <div className="small muted">
              {inv.permissions.map((p) => SHARE_PERMISSION_LABELS[p]).join(' · ')}
              {inv.guestDisplayName && ` · accepted by ${inv.guestDisplayName}`}
            </div>
            <div className="row wrap" style={{ gap: '0.5rem' }}>
              {inv.status === 'Pending' && inv.invitePath && (
                <button type="button" className="btn btn-sm" onClick={() => copyLink(inv)}>
                  {copiedId === inv.id ? 'Copied!' : 'Copy link'}
                </button>
              )}
              {(inv.status === 'Pending' || inv.status === 'Accepted') && (
                <button type="button" className="btn btn-danger btn-sm" disabled={busyId === inv.id}
                  onClick={() => askRevoke(inv)}>
                  {inv.status === 'Accepted' ? 'Revoke access' : 'Delete'}
                </button>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
