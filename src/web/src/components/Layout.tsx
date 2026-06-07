import { NavLink, Outlet } from 'react-router-dom'
import { useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { userApi } from '../api/services'

const NAV = [
  { to: '/ingredients', label: 'Ingredients', ico: '🥕' },
  { to: '/recipes', label: 'Recipes', ico: '🍳' },
  { to: '/meals', label: 'Meals', ico: '🍽️' },
  { to: '/plans', label: 'Plans', ico: '📅' },
]

export function Layout() {
  const { profile, signOut, refresh } = useAuth()
  const [busy, setBusy] = useState(false)

  async function toggleAdminView(enabled: boolean) {
    setBusy(true)
    try {
      await userApi.setAdminView(enabled)
      await refresh()
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="app-shell">
      <header className="app-header">
        <div className="brand">
          <span className="brand-mark">●</span> GoalFueled
        </div>
        <div className="row" style={{ gap: '0.75rem' }}>
          {profile?.isAdmin && (
            <label className="toggle" title="Toggle whether you see admin features">
              <input
                type="checkbox"
                checked={profile.adminViewEnabled}
                disabled={busy}
                onChange={(e) => toggleAdminView(e.target.checked)}
              />
              Admin
            </label>
          )}
          <button className="btn btn-sm" onClick={signOut}>Sign out</button>
        </div>
      </header>

      <main className="app-main">
        <Outlet />
      </main>

      <nav className="bottom-nav">
        {NAV.map((n) => (
          <NavLink key={n.to} to={n.to} className={({ isActive }) => (isActive ? 'active' : '')}>
            <span className="ico">{n.ico}</span>
            {n.label}
          </NavLink>
        ))}
      </nav>
    </div>
  )
}
