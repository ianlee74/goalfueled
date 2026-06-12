import type { ReactNode } from 'react'

/** Shimmering placeholder rows shown while a list loads (DESIGN.md §6 — no bare "Loading…"). */
export function SkeletonList({ rows = 3 }: { rows?: number }) {
  return (
    <div className="list" aria-hidden="true">
      {Array.from({ length: rows }, (_, i) => (
        <div key={i} className="card ingredient-item">
          <div className="skeleton" style={{ width: 48, height: 48 }} />
          <div className="stack" style={{ flex: 1, gap: '0.45rem' }}>
            <div className="skeleton" style={{ height: 14, width: '55%' }} />
            <div className="skeleton" style={{ height: 11, width: '35%' }} />
          </div>
        </div>
      ))}
    </div>
  )
}

/** A friendly centered empty state: monoline icon, headline, optional guidance. */
export function EmptyState({ icon, title, children }: { icon: ReactNode; title: string; children?: ReactNode }) {
  return (
    <div className="card empty-state">
      <span className="empty-state-icon" aria-hidden="true">{icon}</span>
      <strong>{title}</strong>
      {children && <div className="muted small">{children}</div>}
    </div>
  )
}
