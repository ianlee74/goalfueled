import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { gamificationApi } from '../api/services'
import type { GamificationSummary } from '../api/types'
import { IconLevel, IconReview } from '../components/icons'
import { safeReturnTo } from '../lib/returnTo'
import {
  FUEL_LADDER,
  BEYOND_RANK,
  POINTS_PER_LEVEL_BEYOND,
  EARN_RULES,
  type LadderRung,
} from '../lib/fuelGame'

/**
 * "How the game is played" (/how-it-works): the rules behind the level on the You tab.
 * Shows the player's current progress, the full level ladder (with their place on it),
 * how Fuel Points are earned, and how the weekly review pays out (DESIGN.md §4).
 */
export function HowItWorksPage() {
  const [summary, setSummary] = useState<GamificationSummary | null>(null)
  const [searchParams] = useSearchParams()
  // Where to send the user "back": the page that linked here (Today or You), falling back to
  // the You tab where the level lives when arrived at directly.
  const backTo = safeReturnTo(searchParams.get('returnTo')) ?? '/you'

  useEffect(() => {
    let cancelled = false
    gamificationApi.summary()
      .then((s) => { if (!cancelled) setSummary(s) })
      .catch(() => {})
    return () => { cancelled = true }
  }, [])

  return (
    <div className="stack">
      <div className="row spread">
        <h1>How the game is played</h1>
        <Link className="btn btn-sm" to={backTo}>← Back</Link>
      </div>

      <div className="card stack">
        <p style={{ margin: 0 }}>
          GoalFueled rewards <strong>consistency, not outcomes</strong>. You earn Fuel Points (FP)
          for the behaviors that build a good day — logging your fuel and burn, weighing in on
          schedule, and closing out each week. No points are ever tied to weight lost or eating
          less. Stack up FP and you climb the level ladder.
        </p>
      </div>

      {summary && <ProgressCard summary={summary} />}

      <LadderCard currentLevel={summary?.level.number ?? null} />

      <EarnCard />

      <WeeklyReviewCard />
    </div>
  )
}

/** Where the player stands right now: rank, lifetime FP, and the meter to the next level. */
function ProgressCard({ summary }: { summary: GamificationSummary }) {
  const { level, fuelPoints } = summary
  const span = Math.max(1, level.nextLevelPoints - level.floorPoints)
  const pct = Math.max(0, Math.min(100, (100 * (fuelPoints - level.floorPoints)) / span))
  const toNext = Math.max(0, level.nextLevelPoints - fuelPoints)

  return (
    <div className="card stack" style={{ gap: '0.5rem' }}>
      <h2>Your progress</h2>
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
      </span>
    </div>
  )
}

/** The full level ladder, with the player's current rung called out. */
function LadderCard({ currentLevel }: { currentLevel: number | null }) {
  return (
    <div className="card stack">
      <h2>The levels</h2>
      <p className="small muted" style={{ margin: 0 }}>
        Each level is reached at a cumulative lifetime FP total. Your rank is the ladder rung
        you've climbed to.
      </p>
      <div className="stack" style={{ gap: '0.4rem' }}>
        {FUEL_LADDER.map((rung) => (
          <LadderRow key={rung.number} rung={rung} currentLevel={currentLevel} />
        ))}
        <BeyondRow currentLevel={currentLevel} />
      </div>
    </div>
  )
}

/** One named rung. The current level gets an accent border + a "You're here" marker. */
function LadderRow({ rung, currentLevel }: { rung: LadderRung; currentLevel: number | null }) {
  const isCurrent = currentLevel === rung.number
  const isCleared = currentLevel != null && currentLevel > rung.number

  return (
    <div
      className="row spread"
      style={{
        alignItems: 'center',
        gap: '0.6rem',
        padding: '0.55rem 0.7rem',
        border: '1px solid var(--border)',
        borderColor: isCurrent ? 'var(--primary)' : 'var(--border)',
        background: isCurrent ? 'var(--primary-soft)' : 'transparent',
        borderRadius: 8,
      }}
    >
      <div className="row" style={{ gap: '0.6rem', alignItems: 'center', minWidth: 0 }}>
        <span className="chip">LV{rung.number}</span>
        <strong style={{ opacity: isCleared || isCurrent ? 1 : 0.7 }}>{rung.rank}</strong>
      </div>
      <span className="small muted" style={{ whiteSpace: 'nowrap' }}>
        {isCurrent
          ? "You're here"
          : `${rung.floor.toLocaleString()} FP`}
      </span>
    </div>
  )
}

/** The "Escape Velocity and beyond" rung that summarizes the repeating levels past Orbit. */
function BeyondRow({ currentLevel }: { currentLevel: number | null }) {
  const isBeyond = currentLevel != null && currentLevel > FUEL_LADDER.length
  return (
    <div
      className="row spread"
      style={{
        alignItems: 'center',
        gap: '0.6rem',
        padding: '0.55rem 0.7rem',
        border: '1px solid var(--border)',
        borderColor: isBeyond ? 'var(--primary)' : 'var(--border)',
        background: isBeyond ? 'var(--primary-soft)' : 'transparent',
        borderRadius: 8,
      }}
    >
      <div className="row" style={{ gap: '0.6rem', alignItems: 'center', minWidth: 0 }}>
        <span className="chip">LV{FUEL_LADDER.length + 1}+</span>
        <strong style={{ opacity: isBeyond ? 1 : 0.7 }}>{BEYOND_RANK}</strong>
      </div>
      <span className="small muted" style={{ whiteSpace: 'nowrap' }}>
        {isBeyond ? "You're here" : `+${POINTS_PER_LEVEL_BEYOND.toLocaleString()} FP / level`}
      </span>
    </div>
  )
}

/** How Fuel Points are earned — the behavior-only economy. */
function EarnCard() {
  return (
    <div className="card stack">
      <h2>Earning Fuel Points</h2>
      <div className="stack" style={{ gap: '0.6rem' }}>
        {EARN_RULES.map((rule) => (
          <div key={rule.label} className="row spread" style={{ gap: '0.75rem', alignItems: 'baseline' }}>
            <div style={{ minWidth: 0 }}>
              <strong>{rule.label}</strong>
              <div className="small muted">{rule.detail}</div>
            </div>
            <span className="stat-label" style={{ color: 'var(--primary)', whiteSpace: 'nowrap' }}>
              {rule.points}
            </span>
          </div>
        ))}
      </div>
    </div>
  )
}

/** What the weekly review is and how its score feeds the bonus. */
function WeeklyReviewCard() {
  return (
    <div className="card stack">
      <div className="row" style={{ gap: '0.6rem', alignItems: 'center' }}>
        <span style={{ color: 'var(--primary)', display: 'inline-flex' }}><IconReview size={22} /></span>
        <h2 style={{ margin: 0 }}>The weekly review</h2>
      </div>
      <p style={{ margin: 0 }}>
        Weeks run Monday to Sunday. Once a week finishes, you can review it for a one-time payout of
        150 FP plus your <strong>week score</strong> as a bonus (up to 100 FP). The score measures
        engagement, not results — out of 100:
      </p>
      <ul className="stack" style={{ margin: 0, paddingLeft: '1.2rem', gap: '0.35rem' }}>
        <li><strong>50</strong> — logging: how many days you fully logged.</li>
        <li><strong>30</strong> — adherence: days you stayed within your budget.</li>
        <li><strong>20</strong> — weigh-ins: scheduled weigh-ins you logged.</li>
      </ul>
    </div>
  )
}
