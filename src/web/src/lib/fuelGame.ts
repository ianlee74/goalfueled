/**
 * The Fuel Point game rules, mirrored from the backend so the "How the game is played" page
 * can describe the whole ladder and economy without a dedicated API. The server stays the
 * source of truth for a user's actual level and points (GET /gamification); this is the static
 * reference data only. Keep in sync with DESIGN.md §4, FuelLevels.cs, and GamificationAwardService.cs.
 */

/** A named rung on the level ladder. */
export interface LadderRung {
  /** 1-based level number. */
  number: number
  /** Rank name, e.g. "Cruise". */
  rank: string
  /** Cumulative lifetime FP at which this level starts. */
  floor: number
}

/** The named levels, in order (DESIGN.md §4.1 / FuelLevels.Ladder). */
export const FUEL_LADDER: LadderRung[] = [
  { number: 1, rank: 'Ignition', floor: 0 },
  { number: 2, rank: 'Liftoff', floor: 500 },
  { number: 3, rank: 'Cruise', floor: 1_500 },
  { number: 4, rank: 'Boost', floor: 3_500 },
  { number: 5, rank: 'Afterburner', floor: 7_000 },
  { number: 6, rank: 'Orbit', floor: 12_000 },
]

/** FP per level once past the named ladder. */
export const POINTS_PER_LEVEL_BEYOND = 7_000

/** Rank name for every level past the named ladder. */
export const BEYOND_RANK = 'Escape Velocity'

/** One way Fuel Points are earned (DESIGN.md §4.2). Behavior-only — never tied to outcomes. */
export interface EarnRule {
  label: string
  points: string
  detail: string
}

/** How Fuel Points are earned, in the order the FP economy lists them. */
export const EARN_RULES: EarnRule[] = [
  { label: 'Log a fuel or burn entry', points: '+10 FP', detail: 'Up to 50 FP a day from entries.' },
  { label: 'Complete a full day', points: '+25 FP', detail: 'Two or more fuel entries logged in a day.' },
  { label: 'Log weight on schedule', points: '+50 FP', detail: 'On a day your weigh-in schedule makes it due.' },
  { label: 'Complete the weekly review', points: '+150 FP', detail: 'Plus your week score as a bonus, up to +100 FP.' },
]
