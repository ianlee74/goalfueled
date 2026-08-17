import { useEffect, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { featureApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import {
  FEATURE_STATUS_LABELS, SUBSCRIPTION_LEVELS, SUBSCRIPTION_LEVEL_LABELS,
  type Feature, type FeatureStatus,
} from '../api/types'
import { IconLevel } from '../components/icons'
import { EmptyState, SkeletonList } from '../components/Placeholders'

/**
 * Admin-only screen: the feature-tag catalog. Read-only — definitions (name, status, minimum tier) ship
 * with the build, so this is the reference an admin consults before granting or denying a feature to
 * someone on the Users screen.
 */
export function FeaturesPage() {
  const { profile } = useAuth()
  const navigate = useNavigate()
  const [items, setItems] = useState<Feature[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  // Non-admins shouldn't be here; bounce them back to the ingredient list.
  useEffect(() => {
    if (profile && !profile.isActingAsAdmin) navigate('/ingredients', { replace: true })
  }, [profile, navigate])

  useEffect(() => {
    let cancelled = false
    featureApi.catalog()
      .then((f) => { if (!cancelled) setItems(f) })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load features.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [])

  return (
    <div className="stack">
      <div className="row spread">
        <h1>Feature tags</h1>
      </div>

      <p className="small muted feature-panel-hint">
        A user gets everything their subscription tier includes, plus anything granted to them
        individually, minus anything denied to them. Grants and denials are set per user on the{' '}
        <strong>Manage users</strong> screen.
      </p>

      {error && <div className="notice error">{error}</div>}
      {loading && items.length === 0 && <SkeletonList />}
      {!loading && items.length === 0 && !error && (
        <EmptyState icon={<IconLevel size={28} />} title="No features defined" />
      )}

      <div className="list">
        {items.map((f) => (
          <div key={f.key} className="card stack feature-card">
            <div className="row spread">
              <strong>{f.name}</strong>
              <FeatureStatusBadge status={f.status} />
            </div>
            <div className="small muted">{f.description}</div>
            <div className="row wrap feature-card-levels">
              <code className="small muted">{f.key}</code>
              {SUBSCRIPTION_LEVELS.map((level) => (
                <span key={level} className={`badge ${f.includedLevels.includes(level) ? 'app' : ''}`}>
                  {SUBSCRIPTION_LEVEL_LABELS[level]}
                </span>
              ))}
            </div>
            <div className="small muted">{tierSummary(f)}</div>
          </div>
        ))}
      </div>
    </div>
  )
}

/** One-line plain-English summary of who gets the feature by tier alone. */
function tierSummary(f: Feature): string {
  switch (f.status) {
    case 'Planned':
      return 'Not built yet — off for everyone, and an individual grant cannot turn it on.'
    case 'Disabled':
      return 'Kill switch is on — off for everyone regardless of tier or grant.'
    case 'Alpha':
      return 'Internal testing only — no tier includes it; reachable only by an individual grant.'
    case 'Deprecated':
      return `Going away. Still included with ${SUBSCRIPTION_LEVEL_LABELS[f.minimumSubscriptionLevel]} and above.`
    default:
      return f.minimumSubscriptionLevel === 'Free'
        ? 'Included with every tier.'
        : `Included with ${SUBSCRIPTION_LEVEL_LABELS[f.minimumSubscriptionLevel]} and above.`
  }
}

/** Status pill, colored by whether the feature is live, still settling, or off. */
export function FeatureStatusBadge({ status }: { status: FeatureStatus }) {
  const tone =
    status === 'Released' ? 'app'
      : status === 'Beta' || status === 'Alpha' ? 'beta'
        : status === 'Deprecated' || status === 'Disabled' ? 'off'
          : ''
  return <span className={`badge ${tone}`}>{FEATURE_STATUS_LABELS[status]}</span>
}
