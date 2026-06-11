import { NavLink, Outlet } from 'react-router-dom'
import { useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { userApi } from '../api/services'
import { ThemePicker } from '../theme/ThemePicker'

const NAV = [
  { to: '/', label: 'Dashboard', ico: '🏠' },
  { to: '/ingredients', label: 'Ingredients', ico: '🥕' },
  { to: '/recipes', label: 'Recipes', ico: '🍳' },
  { to: '/meals', label: 'Meals', ico: '🍽️' },
  { to: '/plans', label: 'Plans', ico: '📅' },
  { to: '/log', label: 'The Log', ico: '🍪' },
  { to: '/weight', label: 'Weight', ico: '⚖️' },
  { to: '/goals', label: 'Goals', ico: '🎯' },
  { to: '/profile', label: 'Profile', ico: '👤' },
]

// Admin-only nav entries, surfaced only while the admin view is enabled.
const ADMIN_NAV = [
  { to: '/users', label: 'Users', ico: '👥' },
]

export function Layout() {
  const { profile, signOut, refresh } = useAuth()
  const [busy, setBusy] = useState(false)
  // Hamburger menu open/closed state. The nav lives in a drawer until the final UI is decided.
  const [menuOpen, setMenuOpen] = useState(false)

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
        <div className="row" style={{ gap: '0.6rem' }}>
          <button
            className="hamburger"
            aria-label="Toggle navigation menu"
            aria-expanded={menuOpen ? 'true' : 'false'}
            onClick={() => setMenuOpen((o) => !o)}
          >
            ☰
          </button>
          <div className="brand">
            <span className="brand-mark" aria-hidden="true">🚀</span>
            <span className="brand-name">GoalFueled</span>
          </div>
        </div>
        <div className="row" style={{ gap: '0.75rem' }}>
          <ThemePicker />
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

      {/* Navigation drawer toggled by the hamburger button. */}
      {menuOpen && (
        <nav className="nav-drawer">
          {/* Admin nav entries appear only while the admin view is enabled. */}
          {(profile?.isActingAsAdmin ? [...NAV, ...ADMIN_NAV] : NAV).map((n) => (
            <NavLink
              key={n.to}
              to={n.to}
              // The Dashboard link points at the index route ("/"), which otherwise matches every path; `end`
              // keeps it highlighted only on the dashboard itself.
              end={n.to === '/'}
              className={({ isActive }) => (isActive ? 'active' : '')}
              onClick={() => setMenuOpen(false)}
            >
              <span className="ico">{n.ico}</span>
              {n.label}
            </NavLink>
          ))}
        </nav>
      )}

      <main className="app-main">
        <Outlet />
      </main>
    </div>
  )
}
