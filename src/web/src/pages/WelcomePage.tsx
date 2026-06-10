import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { userApi } from '../api/services'
import { ProfileFields, type ProfileFieldsValue } from '../components/ProfileFields'

/**
 * First-login welcome page. Captured one time: sex at birth, height, current weight, and primary goal.
 * The current weight is recorded as today's weight entry server-side so the meal-plan weight gate and
 * weight goals are immediately satisfied. Onboarding completes only once all fields are saved.
 */
export function WelcomePage() {
  const { profile, refresh } = useAuth()
  const navigate = useNavigate()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [fields, setFields] = useState<ProfileFieldsValue>({
    sex: '',
    dob: '',
    heightCm: null,
    heightUnit: 'ftin',
    weight: '',
    weightUnit: 'Pound',
    goal: 'WeightLoss',
    // New users default to US/Imperial (matching the lb + ft/in defaults above); they can switch here.
    unitsPreference: 'UsImperial',
  })

  async function submit() {
    setError(null)
    if (!fields.sex) { setError('Please select your sex at birth.'); return }
    if (!fields.dob) { setError('Please enter your year of birth.'); return }
    if (!(fields.heightCm && fields.heightCm > 0)) { setError('Please enter your height.'); return }
    const weight = Number(fields.weight)
    if (!(weight > 0)) { setError('Please enter your current weight.'); return }

    setBusy(true)
    try {
      await userApi.completeOnboarding({
        primaryGoal: fields.goal,
        sexAtBirth: fields.sex,
        heightCm: fields.heightCm,
        dateOfBirth: fields.dob,
        currentWeight: weight,
        weightUnit: fields.weightUnit,
        unitsPreference: fields.unitsPreference,
      })
      await refresh()
      navigate('/ingredients', { replace: true })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save your details.')
      setBusy(false)
    }
  }

  return (
    <div className="center-screen">
      <div className="card auth-card stack" style={{ maxWidth: 520 }}>
        <div>
          <h1>Welcome{profile ? `, ${profile.displayName.split(' ')[0]}` : ''} 👋</h1>
          <p className="muted">Tell us a little about yourself to get started. You can change any of this later.</p>
        </div>

        {error && <div className="notice error">{error}</div>}

        <ProfileFields value={fields} onChange={setFields} disabled={busy} />

        <button className="btn btn-primary" disabled={busy} onClick={submit}>
          {busy ? 'Saving…' : 'Get started'}
        </button>
      </div>
    </div>
  )
}
