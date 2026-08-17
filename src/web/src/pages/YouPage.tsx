import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { gamificationApi, userApi } from '../api/services'
import { FEATURE_STATUS_LABELS, SUBSCRIPTION_LEVEL_LABELS, type GamificationSummary } from '../api/types'
import { ProfileFields, type ProfileFieldsValue } from '../components/ProfileFields'
import { WeightScheduleEditor } from '../components/WeightScheduleEditor'
import { AccountSwitcher } from '../components/AccountSwitcher'
import { IconLevel, IconUsers } from '../components/icons'
import { ThemePicker } from '../theme/ThemePicker'
import { formatHeight, heightUnitFor } from '../lib/height'
import { weightUnitFor } from '../lib/weight'

/**
 * The You tab (DESIGN.md §3.4): account, appearance, profile details, weight schedule,
 * and (for admins) the admin tools. The level/Fuel Points header lands here in Phase 5.
 */
export function YouPage() {
  const { profile, signOut, refresh } = useAuth()
  const [adminBusy, setAdminBusy] = useState(false)
  const [summary, setSummary] = useState<GamificationSummary | null>(null)

  useEffect(() => {
    let cancelled = false
    gamificationApi.summary()
      .then((s) => { if (!cancelled) setSummary(s) })
      .catch(() => {})
    return () => { cancelled = true }
  }, [])

  async function toggleAdminView(enabled: boolean) {
    setAdminBusy(true)
    try {
      await userApi.setAdminView(enabled)
      await refresh()
    } finally {
      setAdminBusy(false)
    }
  }

  return (
    <div className="stack">
      <h1>You</h1>

      {/* Level & Fuel Points header (DESIGN.md §3.4). */}
      {summary && <LevelCard summary={summary} />}

      {/* How the level is earned: the full ladder + FP economy. */}
      <Link className="card ingredient-item" to="/how-it-works?returnTo=/you">
        <span style={{ color: 'var(--primary)', display: 'inline-flex' }} aria-hidden="true">
          <IconLevel size={24} />
        </span>
        <div style={{ flex: 1, minWidth: 0 }}>
          <strong>How the game is played</strong>
          <div className="small muted">Your progress, the levels, and how to earn Fuel Points</div>
        </div>
        <span className="muted" aria-hidden="true">→</span>
      </Link>

      {/* Account: who's signed in + the way out. */}
      <div className="card stack">
        <h2>Account</h2>
        <div className="row spread">
          <div style={{ minWidth: 0 }}>
            <strong>{profile?.displayName}</strong>
            <div className="small muted">{profile?.email}</div>
          </div>
          <button type="button" className="btn btn-sm" onClick={signOut}>Sign out</button>
        </div>
        {/* Switch between your own account and any shared with you (renders only when there's a choice). */}
        <AccountSwitcher />
      </div>

      {/* Sharing: create/manage links that let others view your log or manage your plans. Only on your
          own account — managing sharing while acting as someone else's account makes no sense. */}
      {!profile?.isActingViaShare && (
        <Link className="card ingredient-item" to="/sharing">
          <span style={{ color: 'var(--primary)', display: 'inline-flex' }} aria-hidden="true">
            <IconUsers size={24} />
          </span>
          <div style={{ flex: 1, minWidth: 0 }}>
            <strong>Sharing</strong>
            <div className="small muted">Share your log with a doctor, trainer, or family member</div>
          </div>
          <span className="muted" aria-hidden="true">→</span>
        </Link>
      )}

      {/* Plan: the subscription tier and the feature tags it (plus any individual grants) turns on. */}
      <PlanCard />

      {/* Appearance. */}
      <div className="card stack">
        <h2>Appearance</h2>
        <div className="row spread">
          <span className="small muted">Theme</span>
          <ThemePicker />
        </div>
      </div>

      <ProfileEditorCard />

      <WeightScheduleEditor />

      {/* General info: what the app is + when it was last updated. */}
      <Link className="card ingredient-item" to="/about">
        <div style={{ flex: 1, minWidth: 0 }}>
          <strong>About</strong>
          <div className="small muted">What GoalFueled is and when it was last updated</div>
        </div>
        <span className="muted" aria-hidden="true">→</span>
      </Link>

      {/* Admin tools, only while acting as an admin (plus the toggle for admins). */}
      {profile?.isAdmin && (
        <div className="card stack">
          <h2>Admin</h2>
          <label className="toggle" title="Toggle whether you see admin features">
            <input
              type="checkbox"
              checked={profile.adminViewEnabled}
              disabled={adminBusy}
              onChange={(e) => toggleAdminView(e.target.checked)}
            />
            Admin view
          </label>
          {profile.isActingAsAdmin && (
            <div className="row wrap" style={{ gap: '0.5rem' }}>
              <Link className="btn btn-sm" to="/admin">Open Admin tools</Link>
            </div>
          )}
        </div>
      )}
    </div>
  )
}

/**
 * The account's subscription tier and the feature tags currently switched on for it. Read-only — tiers
 * and individual grants are set by an admin; this is just so a user can see what they have and which of
 * it is still in beta.
 */
function PlanCard() {
  const { profile } = useAuth()
  if (!profile) return null

  return (
    <div className="card stack">
      <h2>Plan</h2>
      <div className="row spread">
        <span className="small muted">Subscription</span>
        <span className="badge app">{SUBSCRIPTION_LEVEL_LABELS[profile.subscriptionLevel]}</span>
      </div>
      {profile.features.length === 0 ? (
        <div className="small muted">No features are switched on for this account yet.</div>
      ) : (
        <div className="row wrap feature-card-levels">
          {profile.features.map((f) => (
            <span key={f.key} className={`badge ${f.status === 'Beta' || f.status === 'Alpha' ? 'beta' : ''}`}>
              {f.name}{f.status === 'Beta' || f.status === 'Alpha' ? ` · ${FEATURE_STATUS_LABELS[f.status]}` : ''}
            </span>
          ))}
        </div>
      )}
    </div>
  )
}

/** Rank, lifetime FP, and the meter to the next level. */
function LevelCard({ summary }: { summary: GamificationSummary }) {
  const { level, fuelPoints } = summary
  const span = Math.max(1, level.nextLevelPoints - level.floorPoints)
  const pct = Math.max(0, Math.min(100, (100 * (fuelPoints - level.floorPoints)) / span))
  const toNext = Math.max(0, level.nextLevelPoints - fuelPoints)

  return (
    <div className="card stack" style={{ gap: '0.5rem' }}>
      <div className="row" style={{ gap: '0.75rem', alignItems: 'center' }}>
        <span style={{ color: 'var(--primary)', display: 'inline-flex' }}><IconLevel size={30} /></span>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div className="row spread" style={{ alignItems: 'baseline' }}>
            <span className="stat-num" style={{ fontSize: '1.5rem' }}>
              LV{level.number} {level.rank}
            </span>
            <span className="stat-label">{fuelPoints.toLocaleString()} FP</span>
          </div>
        </div>
      </div>
      <div
        className="meter"
        role="progressbar"
        aria-valuenow={Math.round(pct)}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label="Progress to the next level"
      >
        <div className="meter-fill" style={{ width: `${pct}%` }} />
      </div>
      <span className="small muted">
        {toNext.toLocaleString()} FP to level {level.number + 1}
        {' · '}earn points by logging fuel, burn, full days, scheduled weigh-ins, and weekly reviews
      </span>
    </div>
  )
}

/** The editable profile details previously on /profile. */
function ProfileEditorCard() {
  const { profile, refresh } = useAuth()
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  // The authenticated app only mounts this page once `profile` is loaded, so it's safe to seed the
  // form directly from it on first render (no effect needed). `refresh()` after a save flows back
  // through ProfileFields' controlled inputs, and our own writes keep the form in sync.
  const unitsPreference = profile?.unitsPreference ?? 'UsImperial'
  const [fields, setFields] = useState<ProfileFieldsValue>({
    sex: profile?.sexAtBirth ?? '',
    dob: profile?.dateOfBirth ?? '',
    heightCm: profile?.heightCm ?? null,
    // Default the height unit selector from the saved units preference.
    heightUnit: heightUnitFor(unitsPreference),
    // Weight is not editable here (it's recorded through the quick log); these seed the shared
    // ProfileFields value but the weight field is hidden via showWeight={false}.
    weight: '',
    weightUnit: weightUnitFor(unitsPreference),
    goal: profile?.primaryGoal ?? 'WeightLoss',
    unitsPreference,
  })

  async function save() {
    setError(null)
    setSaved(false)
    if (!fields.sex) { setError('Please select your sex at birth.'); return }
    if (!fields.dob) { setError('Please enter your year of birth.'); return }
    if (!(fields.heightCm && fields.heightCm > 0)) { setError('Please enter your height.'); return }

    setSaving(true)
    try {
      // Current weight is not recorded here (it's logged via the quick log); omit currentWeight so
      // the update never upserts a weight entry. weightUnit is sent only to satisfy the contract.
      await userApi.updateProfile({
        primaryGoal: fields.goal,
        sexAtBirth: fields.sex,
        heightCm: fields.heightCm,
        dateOfBirth: fields.dob,
        weightUnit: fields.weightUnit,
        unitsPreference: fields.unitsPreference,
      })
      await refresh()
      setSaved(true)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save your profile.')
    } finally {
      setSaving(false)
    }
  }

  return (
    <div className="card stack" style={{ maxWidth: 520 }}>
      <h2>Profile</h2>
      {error && <div className="notice error">{error}</div>}
      {saved && <div className="notice info">Profile updated.</div>}
      <ProfileFields value={fields} onChange={setFields} disabled={saving} showWeight={false} />
      {profile?.heightCm != null && (
        <p className="muted small" style={{ margin: 0 }}>
          Current saved height: {formatHeight(profile.heightCm, fields.unitsPreference)}
        </p>
      )}
      <button className="btn btn-primary" disabled={saving} onClick={save}>
        {saving ? 'Saving…' : 'Save changes'}
      </button>
    </div>
  )
}
