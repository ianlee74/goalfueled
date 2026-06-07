import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { userApi } from '../api/services'
import { GOAL_LABELS, type GoalType } from '../api/types'

const GOAL_BLURBS: Record<GoalType, string> = {
  WeightLoss: 'Plan meals that keep you in a calorie deficit.',
  WeightManagement: 'Maintain your current weight with balanced plans.',
  WeightGain: 'Build plans to add weight and muscle steadily.',
}

/** First-login welcome page that asks the user for their first goal (spec: Feature 1). */
export function WelcomePage() {
  const { profile, refresh } = useAuth()
  const navigate = useNavigate()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function choose(goal: GoalType) {
    setBusy(true)
    setError(null)
    try {
      await userApi.completeOnboarding(goal)
      await refresh()
      navigate('/ingredients', { replace: true })
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not save your goal.')
      setBusy(false)
    }
  }

  return (
    <div className="center-screen">
      <div className="card auth-card stack" style={{ maxWidth: 520 }}>
        <div>
          <h1>Welcome{profile ? `, ${profile.displayName.split(' ')[0]}` : ''} 👋</h1>
          <p className="muted">To get started, what's your first goal?</p>
        </div>

        {error && <div className="notice error">{error}</div>}

        <div className="goal-grid">
          {(Object.keys(GOAL_LABELS) as GoalType[]).map((g) => (
            <button key={g} className="goal-card" disabled={busy} onClick={() => choose(g)}>
              {GOAL_LABELS[g]}
              <div className="sub">{GOAL_BLURBS[g]}</div>
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}
