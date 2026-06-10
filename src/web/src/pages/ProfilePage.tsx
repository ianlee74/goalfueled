import { useState } from 'react'
import { useAuth } from '../auth/AuthContext'
import { userApi } from '../api/services'
import { ProfileFields, type ProfileFieldsValue } from '../components/ProfileFields'
import { WeightScheduleEditor } from '../components/WeightScheduleEditor'
import { formatHeight } from '../lib/height'

/** Profile page where the user updates the basic info captured at first login. */
export function ProfilePage() {
  const { profile, refresh } = useAuth()
  const [saving, setSaving] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  // The authenticated app only mounts this page once `profile` is loaded, so it's safe to seed the
  // form directly from it on first render (no effect needed). `refresh()` after a save flows back
  // through ProfileFields' controlled inputs, and our own writes keep the form in sync.
  const [fields, setFields] = useState<ProfileFieldsValue>({
    sex: profile?.sexAtBirth ?? '',
    dob: profile?.dateOfBirth ?? '',
    heightCm: profile?.heightCm ?? null,
    heightUnit: 'ftin',
    // Weight is not editable here (it's recorded through The Log); these seed the shared
    // ProfileFields value but the weight field is hidden via showWeight={false}.
    weight: '',
    weightUnit: 'Pound',
    goal: profile?.primaryGoal ?? 'WeightLoss',
  })

  async function save() {
    setError(null)
    setSaved(false)
    if (!fields.sex) { setError('Please select your sex at birth.'); return }
    if (!fields.dob) { setError('Please enter your year of birth.'); return }
    if (!(fields.heightCm && fields.heightCm > 0)) { setError('Please enter your height.'); return }

    setSaving(true)
    try {
      // Current weight is not recorded here (it's logged via The Log); omit currentWeight so the
      // update never upserts a weight entry. weightUnit is sent only to satisfy the contract.
      await userApi.updateProfile({
        primaryGoal: fields.goal,
        sexAtBirth: fields.sex,
        heightCm: fields.heightCm,
        dateOfBirth: fields.dob,
        weightUnit: fields.weightUnit,
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
    <div className="stack">
      <div className="row spread">
        <h1>Profile</h1>
      </div>

      {error && <div className="notice error">{error}</div>}
      {saved && <div className="notice info">Profile updated.</div>}

      <div className="card stack" style={{ maxWidth: 520 }}>
        <ProfileFields value={fields} onChange={setFields} disabled={saving} showWeight={false} />
        {profile?.heightCm != null && (
          <p className="muted small" style={{ margin: 0 }}>
            Current saved height: {formatHeight(profile.heightCm)}
          </p>
        )}
        <button className="btn btn-primary" disabled={saving} onClick={save}>
          {saving ? 'Saving…' : 'Save changes'}
        </button>
      </div>

      <WeightScheduleEditor />
    </div>
  )
}
