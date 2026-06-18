import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { DEV_USERS, type DevUser } from '../auth/devUsers'
import { safeReturnTo } from '../lib/returnTo'

/**
 * Local dev sign-in. In production this screen would offer Google / Microsoft / Facebook buttons
 * (via Entra External ID). For now it lets you "become" one of a few preset users so the whole app —
 * including the first-user-becomes-admin rule — works end to end.
 *
 * Honors `?returnTo=` (an app-internal path, guarded against open redirects) so flows that route through
 * sign-in — e.g. opening a shared-invitation link while signed out — land back where they started. The
 * `?mode=signup` hint only adjusts the copy; dev sign-in and sign-up are the same action (production maps
 * them to the social sign-up vs sign-in entry points).
 */
export function LoginPage() {
  const { signIn } = useAuth()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const returnTo = safeReturnTo(searchParams.get('returnTo'))
  const isSignup = searchParams.get('mode') === 'signup'
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function pick(user: DevUser) {
    setBusy(user.subject)
    setError(null)
    try {
      await signIn(user)
      // After sign-in, return to the originating path (e.g. the invitation) when one was passed.
      if (returnTo) navigate(returnTo, { replace: true })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Sign in failed. Is the API running?')
      setBusy(null)
    }
  }

  return (
    <div className="center-screen">
      <div className="card auth-card stack">
        <div>
          <h1><span className="brand-mark">●</span> GoalFueled</h1>
          <p className="muted">Define and track plans to reach your health goals.</p>
        </div>

        {error && <div className="notice error">{error}</div>}

        <div className="stack">
          <p className="small muted" style={{ margin: 0 }}>
            {isSignup
              ? 'Create your account (development users — real social sign-up coming later):'
              : 'Sign in (development users — real social logins coming later):'}
          </p>
          {DEV_USERS.map((u) => (
            <button
              key={u.subject}
              className="btn btn-block"
              disabled={busy !== null}
              onClick={() => pick(u)}
            >
              {busy === u.subject ? 'Signing in…' : `Continue as ${u.name}`}
            </button>
          ))}
        </div>

        <p className="small muted" style={{ margin: 0 }}>
          The first account to sign in becomes the App Admin.
        </p>
      </div>
    </div>
  )
}
