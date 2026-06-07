import { useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { DEV_USERS, type DevUser } from '../auth/devUsers'

/**
 * Local dev sign-in. In production this screen would offer Google / Microsoft / Facebook buttons
 * (via Entra External ID). For now it lets you "become" one of a few preset users so the whole app —
 * including the first-user-becomes-admin rule — works end to end.
 */
export function LoginPage() {
  const { signIn } = useAuth()
  const [busy, setBusy] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  async function pick(user: DevUser) {
    setBusy(user.subject)
    setError(null)
    try {
      await signIn(user)
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
            Sign in (development users — real social logins coming later):
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
