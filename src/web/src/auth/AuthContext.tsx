import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { userApi } from '../api/services'
import type { CurrentUser } from '../api/types'
import { setActiveAccountId } from '../lib/activeAccount'
import { getStoredDevUser, setStoredDevUser, type DevUser } from './devUsers'

interface AuthState {
  /** The selected dev identity (null = signed out). */
  devUser: DevUser | null
  /** The persisted profile returned by the API for the current dev identity (or the account being acted as). */
  profile: CurrentUser | null
  loading: boolean
  signIn: (user: DevUser) => Promise<void>
  signOut: () => void
  /** Re-fetch the profile (after onboarding or admin-view toggle). */
  refresh: () => Promise<void>
  /**
   * Switch which account the app is viewed as. Pass an owner's user id to act as an account shared with
   * you, or null to return to your own account. Persists the choice and reloads the profile.
   */
  switchAccount: (userId: string | null) => Promise<void>
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
    // A fresh sign-in always starts on the user's own account (clear any prior account-switch).
    setActiveAccountId(null)
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
    // Signing out also drops any account-switch so the next sign-in starts on their own account.
    setActiveAccountId(null)
    setStoredDevUser(null)
    setDevUser(null)
    setProfile(null)
  }, [])

  const refresh = useCallback(async () => {
    await loadProfile()
  }, [loadProfile])

  const switchAccount = useCallback(async (userId: string | null) => {
    // null (or our own id resolved by the caller) returns to self. Persist first so the reload's profile
    // fetch already carries the act-as header.
    setActiveAccountId(userId)
    setLoading(true)
    try {
      await loadProfile()
    } finally {
      setLoading(false)
    }
  }, [loadProfile])

  return (
    <AuthCtx.Provider value={{ devUser, profile, loading, signIn, signOut, refresh, switchAccount }}>
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
