import { NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useEffect, useState } from 'react'
import type { ComponentType } from 'react'
import { useAuth } from '../auth/AuthContext'
import { FEATURES, useFeature } from '../hooks/useFeature'
import { AssistantSheet } from './AssistantSheet'
import { FuelBurnIcon } from './FuelBurnIcon'
import { Sheet } from './Sheet'
import {
  IconGauge, IconLibrary, IconLogPlus, IconRocket, IconSpark, IconTarget, IconUser, IconUsers, IconWeight,
} from './icons'

/**
 * App chrome (DESIGN.md §2): a slim header with the brand, a bottom tab bar with a raised
 * center LOG action on mobile, and a persistent sidebar on desktop (≥1024px). The LOG action
 * opens a bottom sheet offering the three quick-log destinations (fuel / burn / weight).
 */

interface Destination {
  to: string
  label: string
  Ico: ComponentType<{ size?: number }>
  /** Route prefixes that light this destination up (besides `to` itself). */
  match: string[]
}

const DESTINATIONS: Destination[] = [
  { to: '/', label: 'Today', Ico: IconGauge, match: ['/log'] },
  { to: '/goals', label: 'Goals', Ico: IconTarget, match: ['/weight'] },
  { to: '/library', label: 'Library', Ico: IconLibrary, match: ['/ingredients', '/recipes', '/fuel-sources', '/plans'] },
  { to: '/you', label: 'You', Ico: IconUser, match: ['/profile'] },
]

/** Admin destination, surfaced in the sidebar only while acting as an admin. */
const ADMIN_DESTINATION: Destination = {
  to: '/admin', label: 'Admin', Ico: IconUsers, match: ['/users', '/ingredients/review-queue', '/recipes/review-queue'],
}

function isDestinationActive(d: Destination, pathname: string): boolean {
  if (d.to === '/') return pathname === '/' || d.match.some((m) => pathname === m || pathname.startsWith(m + '/'))
  return (
    pathname === d.to ||
    pathname.startsWith(d.to + '/') ||
    d.match.some((m) => pathname === m || pathname.startsWith(m + '/'))
  )
}

export function Layout() {
  const [logOpen, setLogOpen] = useState(false)
  const [assistantOpen, setAssistantOpen] = useState(false)
  const { pathname } = useLocation()
  const { profile, switchAccount } = useAuth()
  // Feature-tagged. Without the tag the entry point simply isn't offered; the endpoint enforces the same
  // gate server-side and returns 403 either way.
  const canUseAssistant = useFeature(FEATURES.aiVoiceLogging)
  // Surface the Admin destination in the sidebar only while acting as an admin.
  const sidebarDestinations = profile?.isActingAsAdmin
    ? [...DESTINATIONS, ADMIN_DESTINATION]
    : DESTINATIONS

  // Close the sheet whenever the route changes (a pick navigated away).
  useEffect(() => { setLogOpen(false) }, [pathname])

  return (
    <div className="app-shell">
      <header className="app-header">
        <NavLink to="/" end className="brand" aria-label="Go to Today">
          <span className="brand-mark" aria-hidden="true"><IconRocket size={22} /></span>
          <span className="brand-name">GoalFueled</span>
        </NavLink>
      </header>

      {/* Desktop sidebar: same destinations plus the LOG action as a regular entry. */}
      <aside className="sidebar" aria-label="Primary">
        {sidebarDestinations.map((d) => (
          <NavLink
            key={d.to}
            to={d.to}
            end={d.to === '/'}
            className={isDestinationActive(d, pathname) ? 'active' : ''}
          >
            <span className="ico"><d.Ico size={19} /></span>
            {d.label}
          </NavLink>
        ))}
        <button type="button" className="sidebar-log" onClick={() => setLogOpen(true)}>
          <span className="ico"><IconLogPlus size={19} /></span>
          Log
        </button>
      </aside>

      <main className="app-main">
        {/* "Acting as" banner: when viewing an account someone shared with us, make whose account this is
            obvious and offer a one-tap way back to our own. */}
        {profile?.isActingViaShare && (
          <div className="notice info row spread" style={{ alignItems: 'center' }}>
            <span>Viewing <strong>{profile.displayName}</strong>'s account</span>
            <button type="button" className="btn btn-sm" onClick={() => switchAccount(null)}>
              Switch back
            </button>
          </div>
        )}
        <Outlet />
      </main>

      {/* Mobile bottom tab bar with the raised center LOG action. */}
      <nav className="tabbar" aria-label="Primary">
        {DESTINATIONS.slice(0, 2).map((d) => <TabLink key={d.to} d={d} pathname={pathname} />)}
        <div className="tab-log">
          <button type="button" aria-label="Log fuel, burn, or weight" onClick={() => setLogOpen(true)}>
            <IconLogPlus size={26} />
          </button>
          <span>Log</span>
        </div>
        {DESTINATIONS.slice(2).map((d) => <TabLink key={d.to} d={d} pathname={pathname} />)}
      </nav>

      {logOpen && (
        <LogSheet
          onClose={() => setLogOpen(false)}
          canUseAssistant={canUseAssistant}
          onOpenAssistant={() => { setLogOpen(false); setAssistantOpen(true) }}
        />
      )}
      {assistantOpen && <AssistantSheet onClose={() => setAssistantOpen(false)} />}
    </div>
  )
}

function TabLink({ d, pathname }: { d: Destination; pathname: string }) {
  return (
    <NavLink to={d.to} end={d.to === '/'} className={isDestinationActive(d, pathname) ? 'active' : ''}>
      <d.Ico size={21} />
      <span>{d.label}</span>
    </NavLink>
  )
}

/** Bottom sheet listing the quick-log destinations (plus the assistant, when the account has it). */
function LogSheet({ onClose, canUseAssistant, onOpenAssistant }: {
  onClose: () => void
  canUseAssistant: boolean
  onOpenAssistant: () => void
}) {
  const navigate = useNavigate()

  function go(path: string) {
    onClose()
    navigate(path)
  }

  return (
    <Sheet title="Log" onClose={onClose}>
      <div className="sheet-actions">
        {canUseAssistant && (
          <button type="button" onClick={onOpenAssistant}>
            <span className="fb-icon" style={{ background: 'var(--surface-muted)', color: 'var(--primary)' }}>
              <IconSpark size={18} />
            </span>
            <span className="sheet-action-text">
              <strong>Assistant <span className="badge beta">Beta</span></strong>
              <span className="small muted">Say or type it and I'll log it</span>
            </span>
          </button>
        )}
        <button type="button" onClick={() => go('/?add=fuel')}>
          <FuelBurnIcon kind="fuel" />
          <span className="sheet-action-text">
            <strong>Fuel</strong>
            <span className="small muted">Food you ate</span>
          </span>
        </button>
        <button type="button" onClick={() => go('/?add=burn')}>
          <FuelBurnIcon kind="burn" />
          <span className="sheet-action-text">
            <strong>Burn</strong>
            <span className="small muted">Exercise that used calories</span>
          </span>
        </button>
        <button type="button" onClick={() => go('/weight')}>
          <span className="fb-icon" style={{ background: 'var(--surface-muted)', color: 'var(--text)' }}>
            <IconWeight size={18} />
          </span>
          <span className="sheet-action-text">
            <strong>Weight</strong>
            <span className="small muted">Record a weigh-in</span>
          </span>
        </button>
      </div>
    </Sheet>
  )
}
