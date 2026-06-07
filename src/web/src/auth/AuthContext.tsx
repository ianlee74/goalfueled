import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { userApi } from '../api/services'
import type { CurrentUser } from '../api/types'
import { getStoredDevUser, setStoredDevUser, type DevUser } from './devUsers'

interface AuthState {
  /** The selected dev identity (null = signed out). */
  devUser: DevUser | null
  /** The persisted profile returned by the API for the current dev identity. */
  profile: CurrentUser | null
  loading: boolean
  signIn: (user: DevUser) => Promise<void>
  signOut: () => void
  /** Re-fetch the profile (after onboarding or admin-view toggle). */
  refresh: () => Promise<void>
}

const AuthCtx = createContext<AuthState | undefined>(undefined)

export function AuthProvider({ children }: { children: ReactNode }) {
  const [devUser, setDevUser] = useState<DevUser | null>(getStoredDevUser())
  const [profile, setProfile] = useState<CurrentUser | null>(null)
  const [loading, setLoading] = useState<boolean>(!!getStoredDevUser())

  const loadProfile = useCallback(async () => {
    if (!getStoredDevUser()) {
      setProfile(null)
      return
    }
    const me = await userApi.me()
    setProfile(me)
  }, [])

  // On first mount, if a dev user is already stored, hydrate the profile.
  // `loading` is initialized to reflect whether a stored user exists, so no synchronous
  // setState is needed here — we only clear/replace it once the async load settles.
  useEffect(() => {
    if (!devUser) return
    let cancelled = false
    loadProfile()
      .catch(() => { if (!cancelled) setProfile(null) })
      .finally(() => { if (!cancelled) setLoading(false) })
    return () => { cancelled = true }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const signIn = useCallback(async (user: DevUser) => {
    setStoredDevUser(user)
    setDevUser(user)
    setLoading(true)
    try {
      await loadProfile()
    } finally {
      setLoading(false)
    }
  }, [loadProfile])

  const signOut = useCallback(() => {
    setStoredDevUser(null)
    setDevUser(null)
    setProfile(null)
  }, [])

  const refresh = useCallback(async () => {
    await loadProfile()
  }, [loadProfile])

  return (
    <AuthCtx.Provider value={{ devUser, profile, loading, signIn, signOut, refresh }}>
      {children}
    </AuthCtx.Provider>
  )
}

// eslint-disable-next-line react-refresh/only-export-components
export function useAuth(): AuthState {
  const ctx = useContext(AuthCtx)
  if (!ctx) throw new Error('useAuth must be used within an AuthProvider')
  return ctx
}
