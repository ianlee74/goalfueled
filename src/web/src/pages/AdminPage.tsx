import { useEffect, useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { visionModelApi } from '../api/services'
import type { VisionModelSettings } from '../api/types'
import { IconUsers, IconRecipe, IconIngredient } from '../components/icons'

/**
 * Admin tools, split out of the You tab into a dedicated top-level page.
 * The "Admin view" toggle stays on /you; the actual tools live here and are
 * only reachable while acting as an admin.
 */
export function AdminPage() {
  const { profile } = useAuth()
  const navigate = useNavigate()

  // Non-admins (or admins with admin view off) shouldn't be here; bounce them.
  useEffect(() => {
    if (profile && !profile.isActingAsAdmin) navigate('/ingredients', { replace: true })
  }, [profile, navigate])

  return (
    <div className="stack">
      <h1>Admin</h1>

      <div className="list">
        <AdminTool
          to="/users"
          icon={<IconUsers size={22} />}
          title="Manage users"
          description="View members and promote or demote the App Admin role."
        />
        <AdminTool
          to="/recipes/review-queue"
          icon={<IconRecipe size={22} />}
          title="Recipe review queue"
          description="Approve or reject recipes submitted for the public library."
        />
        <AdminTool
          to="/ingredients/review-queue"
          icon={<IconIngredient size={22} />}
          title="Ingredient review queue"
          description="Approve or reject ingredients submitted for the public library."
        />
      </div>

      <VisionModelCard />
    </div>
  )
}

/**
 * Runtime picker for the nutrition-label OCR vision model. The override is persisted and wins over
 * configuration, so an admin can A/B test models before baking a choice into config.
 */
function VisionModelCard() {
  const [settings, setSettings] = useState<VisionModelSettings | null>(null)
  const [choice, setChoice] = useState('')
  const [loading, setLoading] = useState(true)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    visionModelApi.get()
      .then((s) => { if (!cancelled) { setSettings(s); setChoice(s.effectiveModel) } })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [])

  async function apply() {
    setBusy(true); setError(null)
    try {
      setSettings(await visionModelApi.setOverride(choice))
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to set model.')
    } finally { setBusy(false) }
  }

  async function clear() {
    setBusy(true); setError(null)
    try {
      await visionModelApi.clearOverride()
      const s = await visionModelApi.get()
      setSettings(s); setChoice(s.effectiveModel)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to clear override.')
    } finally { setBusy(false) }
  }

  if (loading) return null

  return (
    <div className="card stack">
      <h2>Nutrition label OCR model <span className="muted small">testing override</span></h2>

      {!settings?.isApiKeyConfigured && (
        <div className="notice info">
          No Anthropic API key is configured, so label OCR is currently disabled. You can still pre-select a
          model here; it takes effect once a key is set.
        </div>
      )}

      {settings && (
        <>
          <p className="small muted" style={{ margin: 0 }}>
            Currently using <strong>{settings.effectiveModel}</strong>
            {settings.overrideModel
              ? ' (runtime override).'
              : settings.isModelConfigured
                ? ' (from configuration).'
                : ' (built-in default).'}
          </p>

          <label className="field">
            Model
            <select className="select" value={choice} disabled={busy}
              onChange={(e) => setChoice(e.target.value)}>
              {settings.availableModels.map((m) => (
                <option key={m} value={m}>{m}</option>
              ))}
            </select>
          </label>

          <div className="row" style={{ gap: '0.5rem' }}>
            <button type="button" className="btn btn-primary btn-sm" disabled={busy || choice === settings.effectiveModel}
              onClick={apply}>
              {busy ? 'Saving…' : 'Use this model'}
            </button>
            {settings.overrideModel && (
              <button type="button" className="btn btn-sm" disabled={busy} onClick={clear}
                title="Revert to the configured model">
                Clear override
              </button>
            )}
          </div>

          <p className="small muted" style={{ margin: 0 }}>
            The override is temporary and persists across restarts. Once you settle on a model, set
            <code> GoalFueled:Anthropic:VisionModel </code> in configuration and clear the override.
          </p>
        </>
      )}

      {error && <div className="notice error">{error}</div>}
    </div>
  )
}

function AdminTool({
  to, icon, title, description,
}: {
  to: string
  icon: React.ReactNode
  title: string
  description: string
}) {
  return (
    <Link to={to} className="card ingredient-item" style={{ textDecoration: 'none', color: 'inherit' }}>
      <div className="ingredient-thumb">{icon}</div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <strong>{title}</strong>
        <div className="small muted">{description}</div>
      </div>
    </Link>
  )
}
