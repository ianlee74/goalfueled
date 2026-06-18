import { useEffect, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { sharingApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import {
  SHARE_PERMISSION_LABELS, SHARE_STATUS_LABELS,
  type ShareInvitationPreview,
} from '../api/types'
import { IconUsers } from '../components/icons'

/**
 * The invitation landing page (/invite/:token). A signed-in user who opens a shared link decides here
 * whether to accept (gaining access to the owner's account) or reject it. Either choice spends the link;
 * an already-used link shows its resolved state. On accept we switch straight into the shared account.
 */
export function InvitePage() {
  const { token } = useParams<{ token: string }>()
  const navigate = useNavigate()
  const { switchAccount } = useAuth()
  const [preview, setPreview] = useState<ShareInvitationPreview | null>(null)
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  // Set once the user accepts/rejects, so we can show the outcome instead of the decision buttons.
  const [resolved, setResolved] = useState<ShareInvitationPreview | null>(null)

  useEffect(() => {
    if (!token) return
    let cancelled = false
    sharingApi.preview(token)
      .then((p) => { if (!cancelled) setPreview(p) })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'This invitation could not be found.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [token])

  async function respond(accept: boolean) {
    if (!token) return
    setBusy(true)
    setError(null)
    try {
      const result = await sharingApi.respond(token, accept)
      setResolved(result)
      // On accept, drop the user straight into the account they were granted.
      if (accept) {
        await switchAccount(result.ownerUserId)
        navigate('/', { replace: true })
        return
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not record your choice.')
    } finally {
      setBusy(false)
    }
  }

  if (loading) {
    return <div className="center-screen"><p className="muted">Loading invitation…</p></div>
  }

  // The resolved (post-decision) or already-spent state.
  const shown = resolved ?? preview
  const spent = shown != null && !shown.canRespond

  return (
    <div className="center-screen">
      <div className="card auth-card stack">
        <div className="row" style={{ gap: '0.6rem', alignItems: 'center' }}>
          <span style={{ color: 'var(--primary)', display: 'inline-flex' }}><IconUsers size={26} /></span>
          <h1 style={{ margin: 0 }}>Shared account</h1>
        </div>

        {error && <div className="notice error">{error}</div>}

        {shown && (
          <>
            <p style={{ margin: 0 }}>
              <strong>{shown.ownerDisplayName}</strong> wants to share their GoalFueled account with you.
            </p>
            <div className="stack" style={{ gap: '0.3rem' }}>
              <span className="small muted">You would be able to:</span>
              <ul className="stack" style={{ margin: 0, paddingLeft: '1.2rem', gap: '0.2rem' }}>
                {shown.permissions.map((p) => (
                  <li key={p}>{SHARE_PERMISSION_LABELS[p]}</li>
                ))}
              </ul>
            </div>

            {resolved ? (
              <div className="notice info">
                {resolved.status === 'Accepted'
                  ? 'Invitation accepted.'
                  : 'Invitation declined.'}
              </div>
            ) : shown.isOwner ? (
              <div className="notice info">This is your own invitation — send the link to someone else.</div>
            ) : spent ? (
              <div className="notice info">
                This invitation is no longer available ({SHARE_STATUS_LABELS[shown.status].toLowerCase()}).
              </div>
            ) : (
              <div className="row" style={{ gap: '0.5rem' }}>
                <button type="button" className="btn btn-primary" disabled={busy}
                  onClick={() => respond(true)}>
                  {busy ? 'Working…' : 'Accept'}
                </button>
                <button type="button" className="btn btn-danger" disabled={busy}
                  onClick={() => respond(false)}>
                  Reject
                </button>
              </div>
            )}
          </>
        )}

        <button type="button" className="btn btn-sm btn-block" onClick={() => navigate('/', { replace: true })}>
          Go to the app
        </button>
      </div>
    </div>
  )
}
