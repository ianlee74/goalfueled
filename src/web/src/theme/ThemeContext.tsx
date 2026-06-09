import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import {
  applyTheme,
  getStoredPreference,
  resolveTheme,
  setStoredPreference,
  type Theme,
  type ThemePreference,
} from './theme'

interface ThemeState {
  /** The user's choice (may be "system"). */
  preference: ThemePreference
  /** The concrete theme currently painted (what "system" resolved to). */
  theme: Theme
  setPreference: (pref: ThemePreference) => void
}

const ThemeCtx = createContext<ThemeState | undefined>(undefined)

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [preference, setPreferenceState] = useState<ThemePreference>(getStoredPreference)
  // Track the OS light/dark setting so "system" can follow it live.
  const [systemTheme, setSystemTheme] = useState<Theme>(() => resolveTheme('system'))

  // The concrete theme is derived from the preference (no setState-in-effect needed).
  const theme: Theme = preference === 'system' ? systemTheme : preference

  // Subscribe to OS theme changes (matters only while preference is "system",
  // but the subscription is cheap and keeps systemTheme always accurate).
  useEffect(() => {
    const media = window.matchMedia('(prefers-color-scheme: dark)')
    const onChange = () => setSystemTheme(media.matches ? 'dark' : 'light')
    media.addEventListener('change', onChange)
    return () => media.removeEventListener('change', onChange)
  }, [])

  // Paint the resolved theme to the document whenever it changes.
  useEffect(() => {
    applyTheme(theme)
  }, [theme])

  const setPreference = useCallback((pref: ThemePreference) => {
    setStoredPreference(pref)
    setPreferenceState(pref)
  }, [])

  return (
    <ThemeCtx.Provider value={{ preference, theme, setPreference }}>
      {children}
    </ThemeCtx.Provider>
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export function useTheme(): ThemeState {
  const ctx = useContext(ThemeCtx)
  if (!ctx) throw new Error('useTheme must be used within a ThemeProvider')
  return ctx
}
