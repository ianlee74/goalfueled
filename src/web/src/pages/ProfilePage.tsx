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
    weight: '',
    weightUnit: 'Pound',
    goal: profile?.primaryGoal ?? 'WeightLoss',
  })

  async function save() {
    setError(null)
    setSaved(false)
    if (!fields.sex) { setError('Please select your sex at birth.'); return }
    if (!fields.dob) { setError('Please enter your date of birth.'); return }
    if (!(fields.heightCm && fields.heightCm > 0)) { setError('Please enter your height.'); return }
    // Weight is optional on this page; only validate it when provided.
    let weight: number | undefined
    if (fields.weight.trim() !== '') {
      weight = Number(fields.weight)
      if (!(weight > 0)) { setError('Enter a weight greater than zero, or leave it blank.'); return }
    }

    setSaving(true)
    try {
      await userApi.updateProfile({
        primaryGoal: fields.goal,
        sexAtBirth: fields.sex,
        heightCm: fields.heightCm,
        dateOfBirth: fields.dob,
        currentWeight: weight,
        weightUnit: fields.weightUnit,
      })
      await refresh()
      setFields((f) => ({ ...f, weight: '' }))
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
        <ProfileFields value={fields} onChange={setFields} disabled={saving} weightRequired={false} />
        {profile?.heightCm != null && (
          <p className="muted small" style={{ margin: 0 }}>
            Current saved height: {formatHeight(profile.heightCm)}
          </p>
        )}
        <p className="muted small" style={{ margin: 0 }}>
          Leave current weight blank to keep your last recorded weight. Entering a value records today's weight.
        </p>
        <button className="btn btn-primary" disabled={saving} onClick={save}>
          {saving ? 'Saving…' : 'Save changes'}
        </button>
      </div>

      <WeightScheduleEditor />
    </div>
  )
}
