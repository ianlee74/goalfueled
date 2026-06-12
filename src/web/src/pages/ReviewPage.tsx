import { useEffect, useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { gamificationApi } from '../api/services'
import { WEIGHT_UNIT_LABELS, type WeeklyReview } from '../api/types'

/**
 * "Your week in fuel" (DESIGN.md §3.3): the weekly review — score ring, component breakdown,
 * day strip, totals, weight delta — ending in the one-time completion bonus. The score reflects
 * engagement (logging, adherence, weigh-ins), never deficit size.
 */
export function ReviewPage() {
  const [searchParams] = useSearchParams()
  const weekStart = searchParams.get('weekStart') ?? undefined
  const [review, setReview] = useState<WeeklyReview | null>(null)
  const [loading, setLoading] = useState(true)
  const [claiming, setClaiming] = useState(false)
  const [claimed, setClaimed] = useState<number | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    setLoading(true)
    gamificationApi.review(weekStart)
      .then((r) => { if (!cancelled) setReview(r) })
      .catch((e) => { if (!cancelled) setError(e instanceof Error ? e.message : 'Failed to load the review.') })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
  }, [weekStart])

  async function complete() {
    if (!review) return
    setError(null)
    setClaiming(true)
    try {
      const updated = await gamificationApi.completeReview(review.weekStart)
      setReview(updated)
      setClaimed(updated.completionBonus ?? null)
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to complete the review.')
    } finally {
      setClaiming(false)
    }
  }

  const today = new Date().toISOString().slice(0, 10)
  const weekFinished = review != null && review.weekEnd < today

  return (
    <div className="stack">
      <div className="row spread">
        <h1>Week in fuel</h1>
        {review && (
          <span className="stat-label">{fmtDate(review.weekStart)} – {fmtDate(review.weekEnd)}</span>
        )}
      </div>

      {error && <div className="notice error">{error}</div>}
      {loading && <p className="muted">Loading…</p>}

      {review && (
        <>
          {/* Score + components. */}
          <div className="card stack">
            <ScoreRing score={review.score} />
            <ScoreMeter label="Logging consistency" value={review.loggingScore} max={50}
              sub={`${review.daysFullyLogged} day${review.daysFullyLogged === 1 ? '' : 's'} fully logged`} />
            <ScoreMeter label="Budget adherence" value={review.adherenceScore} max={30}
              sub={`${review.daysAdherent} day${review.daysAdherent === 1 ? '' : 's'} on budget`} />
            <ScoreMeter label="Weigh-ins" value={review.weightScore} max={20}
              sub={review.weighInsDue === 0 ? 'none scheduled' : `${review.weighInsLogged} of ${review.weighInsDue} on schedule`} />
          </div>

          {/* Day-by-day strip. */}
          <div className="card stack">
            <h2>Day by day</h2>
            <div className="review-days">
              {review.days.map((d) => (
                <div key={d.date} className="review-day" title={`${d.date}: ${Math.round(d.fuel)} kcal fuel, ${d.fuelEntries} entries`}>
                  <span className="stat-label">{weekdayInitial(d.date)}</span>
                  <span className={`dot ${d.fullyLogged ? 'on' : d.fuelEntries > 0 ? '' : 'off'}`} aria-hidden="true" />
                  <span className="small muted">{d.fuel > 0 ? compactKcal(d.fuel) : '–'}</span>
                </div>
              ))}
            </div>
            <span className="small muted">Dot: green = fully logged (2+ meals), red = nothing logged.</span>
          </div>

          {/* Totals. */}
          <div className="card stack">
            <h2>Totals</h2>
            <div className="log-totals">
              <div className="stat">
                <span className="stat-label">Fuel</span>
                <span className="stat-num">{Math.round(review.totalFuel).toLocaleString()}</span>
              </div>
              <div className="stat">
                <span className="stat-label">Burn</span>
                <span className="stat-num">−{Math.round(review.totalBurn).toLocaleString()}</span>
              </div>
              <div className="stat">
                <span className="stat-label">Net</span>
                <span className="stat-num">{Math.round(review.netCalories).toLocaleString()}</span>
              </div>
            </div>
            <div className="small muted">
              {review.bestDay && <>Best day: {fmtDate(review.bestDay)} · </>}
              {review.weightDelta != null && review.weightDeltaUnit && (
                <>
                  Weight {review.weightDelta > 0 ? '+' : ''}{review.weightDelta}{' '}
                  {WEIGHT_UNIT_LABELS[review.weightDeltaUnit]} this week ·{' '}
                </>
              )}
              {review.pointsEarned.toLocaleString()} FP earned from logging
              {!review.kindleAvailable && <> · net excludes kindle (complete your <Link to="/you">profile</Link>)</>}
            </div>
          </div>

          {/* The one-time completion bonus. */}
          {review.completed ? (
            <div className="notice info">
              Review complete{claimed != null && <> — <strong>+{claimed} FP</strong> banked</>}. Next one unlocks after Sunday.
            </div>
          ) : weekFinished ? (
            <button type="button" className="btn btn-primary btn-block" disabled={claiming} onClick={complete}>
              {claiming ? 'Claiming…' : `Complete review · +${150 + review.score} FP`}
            </button>
          ) : (
            <p className="muted small" style={{ margin: 0 }}>
              This week is still in progress — the review can be completed after Sunday.
            </p>
          )}
        </>
      )}
    </div>
  )
}

/** The week score as the app's single ring meter. */
function ScoreRing({ score }: { score: number }) {
  const R = 56
  const C = 2 * Math.PI * R
  const dash = (Math.max(0, Math.min(100, score)) / 100) * C
  return (
    <div className="score-ring">
      <svg viewBox="0 0 132 132" role="img" aria-label={`Week score ${score} out of 100`}>
        <circle cx={66} cy={66} r={R} fill="none" stroke="var(--surface-muted)" strokeWidth={11} />
        <circle
          cx={66} cy={66} r={R} fill="none" stroke="var(--primary)" strokeWidth={11}
          strokeDasharray={`${dash} ${C}`} transform="rotate(-90 66 66)"
          style={{ transition: 'stroke-dasharray 0.6s ease' }}
        />
      </svg>
      <div className="score-ring-center">
        <span className="stat-num" style={{ fontSize: '2.2rem' }}>{score}</span>
        <span className="stat-label">Week score</span>
      </div>
    </div>
  )
}

function ScoreMeter({ label, value, max, sub }: { label: string; value: number; max: number; sub: string }) {
  const pct = Math.max(0, Math.min(100, (100 * value) / max))
  return (
    <div className="stack" style={{ gap: '0.25rem' }}>
      <div className="row spread">
        <span className="stat-label">{label}</span>
        <span className="small" style={{ fontWeight: 700 }}>{value} / {max}</span>
      </div>
      <div className="meter" role="progressbar" aria-valuenow={value} aria-valuemin={0} aria-valuemax={max} aria-label={label}>
        <div className="meter-fill" style={{ width: `${pct}%` }} />
      </div>
      <span className="small muted">{sub}</span>
    </div>
  )
}

// Day cells are narrow; thousands render as "4.9k" so they never wrap.
function compactKcal(kcal: number): string {
  const n = Math.round(kcal)
  return n >= 1000 ? `${(n / 1000).toFixed(1)}k` : String(n)
}

function fmtDate(iso: string): string {
  const d = new Date(iso + 'T00:00:00')
  return `${d.getMonth() + 1}/${d.getDate()}`
}

function weekdayInitial(iso: string): string {
  return new Date(iso + 'T00:00:00').toLocaleDateString(undefined, { weekday: 'narrow' })
}

