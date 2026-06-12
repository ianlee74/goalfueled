import type { Level } from '../api/types'

const STORAGE_KEY = 'gf.lastLevel'

/**
 * Remembers the last level the user saw and returns true exactly once when it rises —
 * the trigger for the app's single celebration moment (DESIGN.md §4.2).
 */
export function detectLevelUp(level: Level): boolean {
  const stored = Number(localStorage.getItem(STORAGE_KEY) || '0')
  localStorage.setItem(STORAGE_KEY, String(level.number))
  return stored > 0 && level.number > stored
}
