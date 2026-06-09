// Theme model for GoalFueled. A user picks a *preference* (which may be "system",
// i.e. follow the OS), and we resolve that to a concrete theme that drives the
// [data-theme] attribute on <html> and the CSS custom properties in index.css.

/** A concrete, paintable theme (maps 1:1 to a [data-theme] CSS block). */
export type Theme = 'light' | 'dark' | 'high-contrast'

/** What the user chose. "system" tracks the OS light/dark setting. */
export type ThemePreference = Theme | 'system'

export const THEME_STORAGE_KEY = 'gf.theme'

/** Options surfaced in the theme picker, in display order. */
export const THEME_OPTIONS: { value: ThemePreference; label: string }[] = [
  { value: 'system', label: '🖥️ System' },
  { value: 'light', label: '☀️ Light' },
  { value: 'dark', label: '🌙 Dark' },
  { value: 'high-contrast', label: '◐ High contrast' },
]

export function isThemePreference(value: unknown): value is ThemePreference {
  return value === 'system' || value === 'light' || value === 'dark' || value === 'high-contrast'
}

/** Read the stored preference, defaulting to "system" when unset/invalid. */
export function getStoredPreference(): ThemePreference {
  const raw = localStorage.getItem(THEME_STORAGE_KEY)
  return isThemePreference(raw) ? raw : 'system'
}

export function setStoredPreference(pref: ThemePreference): void {
  localStorage.setItem(THEME_STORAGE_KEY, pref)
}

/** Resolve a preference to a concrete theme, consulting the OS for "system". */
export function resolveTheme(pref: ThemePreference): Theme {
  if (pref !== 'system') return pref
  const prefersDark =
    typeof window !== 'undefined' &&
    window.matchMedia?.('(prefers-color-scheme: dark)').matches
  return prefersDark ? 'dark' : 'light'
}

/** Apply a concrete theme to the document so the CSS variables take effect. */
export function applyTheme(theme: Theme): void {
  document.documentElement.setAttribute('data-theme', theme)
}
