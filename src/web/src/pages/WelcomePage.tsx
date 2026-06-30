import { useState, type ReactNode } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { userApi } from '../api/services'
import { ProfileFields, type ProfileFieldsValue } from '../components/ProfileFields'
import { IconBurn, IconGauge, IconSpark } from '../components/icons'

/**
 * First-login flow, shown once while the account is still un-onboarded. Two steps:
 *  1. A brief "how it works" intro that explains the core vocabulary (Fuel / Burn / Kindle / Net).
 *  2. The profile capture: sex at birth, height, current weight, and primary goal.
 * The current weight is recorded as today's weight entry server-side so the meal-plan weight gate and
 * weight goals are immediately satisfied. Onboarding completes only once all fields are saved, so this
 * whole flow — intro included — is only ever seen once, right after the login is first created.
 */
export function WelcomePage() {
  const { profile, refresh } = useAuth()
  const navigate = useNavigate()
  const [step, setStep] = useState<'intro' | 'profile'>('intro')
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

  const firstName = profile ? profile.displayName.split(' ')[0] : ''

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

  if (step === 'intro') {
    return <WelcomeIntro firstName={firstName} onNext={() => setStep('profile')} />
  }

  return (
    <div className="center-screen">
      <div className="card auth-card stack" style={{ maxWidth: 520 }}>
        <div>
          <h1>A little about you</h1>
          <p className="muted">This sets up your daily numbers. You can change any of it later.</p>
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

/**
 * Step 1 of onboarding: the one-time orientation. Explains the three calorie concepts the whole app
 * is built around so a brand-new user understands the vocabulary before they ever see it in context.
 */
function WelcomeIntro({ firstName, onNext }: { firstName: string; onNext: () => void }) {
  return (
    <div className="center-screen">
      <div className="card auth-card stack" style={{ maxWidth: 560 }}>
        <div>
          <h1>Welcome{firstName ? `, ${firstName}` : ''} 👋</h1>
          <p className="muted">
            GoalFueled tracks the energy moving through your day. Three numbers tell the story:
          </p>
        </div>

        <div className="stack" style={{ gap: '0.6rem' }}>
          <Term kind="fuel" icon={<IconGauge size={18} />} name="Fuel">
            The calories you take in — the food and drink you log.
          </Term>
          <Term kind="burn" icon={<IconBurn size={18} />} name="Burn">
            The calories you spend on purpose — workouts and activity you log.
          </Term>
          <Term kind="kindle" icon={<IconSpark size={18} />} name="Kindle">
            Your body's passive burn — the energy it uses just to keep you running, worked out from
            your profile. You don't log it; we do the math.
          </Term>
        </div>

        <div
          className="notice"
          style={{ textAlign: 'center', fontSize: '1.05rem' }}
        >
          <strong>Net</strong> = Fuel − Burn − Kindle
          <div className="small muted" style={{ marginTop: '0.25rem' }}>
            what's left over at the end of the day
          </div>
        </div>

        <p className="muted" style={{ margin: 0 }}>
          Each day, log your fuel and burn and weigh in on schedule. Staying consistent earns
          Fuel Points and levels you up — rewards are for showing up, never for eating less.
        </p>

        <button className="btn btn-primary" onClick={onNext}>
          Next
        </button>
      </div>
    </div>
  )
}

/** One vocabulary row: a themed icon chip, the term, and its plain-language definition. */
function Term(
  { kind, icon, name, children }: { kind: 'fuel' | 'burn' | 'kindle'; icon: ReactNode; name: string; children: ReactNode },
) {
  return (
    <div className="row" style={{ gap: '0.7rem', alignItems: 'flex-start' }}>
      <span className={`fb-icon fb-${kind}`}>{icon}</span>
      <div style={{ minWidth: 0 }}>
        <strong>{name}</strong>
        <div className="small muted">{children}</div>
      </div>
    </div>
  )
}
