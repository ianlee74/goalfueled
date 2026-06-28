import { useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { DEV_USERS, type DevUser } from '../auth/devUsers'
import { safeReturnTo } from '../lib/returnTo'

/**
 * Sign-in screen. In a deployed build (auth mode "entra") it offers a single button that opens the
 * Microsoft Entra External ID user flow — where the user picks Google or an email+password account.
 * In local dev (auth mode "dev") it lets you "become" one of a few preset users so the whole app —
 * including the first-user-becomes-admin rule — works end to end without real OAuth.
 *
 * Honors `?returnTo=` (an app-internal path, guarded against open redirects) so flows that route through
 * sign-in — e.g. opening a shared-invitation link while signed out — land back where they started. The
 * `?mode=signup` hint only adjusts the copy; sign-in and sign-up map to the same entry point here.
 */
export function LoginPage() {
  const { mode, signIn, signInWithEntra } = useAuth()
  const navigate = useNavigate()
  const [searchParams] = useSearchParams()
  const returnTo = safeReturnTo(searchParams.get('returnTo'))
  const isSignup = searchParams.get('mode') === 'signup'
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  function afterSignIn() {
    // After sign-in, return to the originating path (e.g. the invitation) when one was passed.
    if (returnTo) navigate(returnTo, { replace: true })
  }

  async function pickDevUser(user: DevUser) {
    setBusy(user.subject)
    setError(null)
    try {
      await signIn(user)
      afterSignIn()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Sign in failed. Is the API running?')
      setBusy(null)
    }
  }

  async function signInEntra() {
    setBusy('entra')
    setError(null)
    try {
      await signInWithEntra()
      afterSignIn()
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Sign in failed. Please try again.')
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

        {mode === 'entra' ? (
          <div className="stack">
            <button
              className="btn btn-block btn-primary"
              disabled={busy !== null}
              onClick={signInEntra}
            >
              {busy === 'entra'
                ? 'Opening sign-in…'
                : isSignup ? 'Create your account' : 'Sign in'}
            </button>
            <p className="small muted" style={{ margin: 0 }}>
              Continue with Google or an email and password.
            </p>
          </div>
        ) : (
          <div className="stack">
            <p className="small muted" style={{ margin: 0 }}>
              {isSignup
                ? 'Create your account (development users — real social sign-up in deployed builds):'
                : 'Sign in (development users — real social logins in deployed builds):'}
            </p>
            {DEV_USERS.map((u) => (
              <button
                key={u.subject}
                className="btn btn-block"
                disabled={busy !== null}
                onClick={() => pickDevUser(u)}
              >
                {busy === u.subject ? 'Signing in…' : `Continue as ${u.name}`}
              </button>
            ))}
          </div>
        )}

        <p className="small muted" style={{ margin: 0 }}>
          The first account to sign in becomes the App Admin.
        </p>
      </div>
    </div>
  )
}
