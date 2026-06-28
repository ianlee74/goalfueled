import { createContext, useCallback, useContext, useEffect, useState, type ReactNode } from 'react'
import { userApi } from '../api/services'
import type { CurrentUser } from '../api/types'
import { setActiveAccountId } from '../lib/activeAccount'
import { authMode } from './authConfig'
import { getStoredDevUser, setStoredDevUser, type DevUser } from './devUsers'
import { getAccount, initMsal, login as entraLogin, logout as entraLogout } from './msal'

interface AuthState {
  /** Whether a user is currently signed in (a dev identity in "dev" mode, an Entra account in "entra" mode). */
  signedIn: boolean
  /** The active sign-in mode for this build. */
  mode: typeof authMode
  /** The persisted profile returned by the API for the current identity (or the account being acted as). */
  profile: CurrentUser | null
  loading: boolean
  /** Dev-mode sign-in: "become" a preset user. (Only meaningful when mode === 'dev'.) */
  signIn: (user: DevUser) => Promise<void>
  /** Entra-mode sign-in: open the Entra External ID user flow (Google / email+password). */
  signInWithEntra: () => Promise<void>
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

/** Whether a session already exists synchronously (only knowable up-front in dev mode). */
const devSessionPresent = authMode === 'dev' && !!getStoredDevUser()

export function AuthProvider({ children }: { children: ReactNode }) {
  const [signedIn, setSignedIn] = useState<boolean>(devSessionPresent)
  const [profile, setProfile] = useState<CurrentUser | null>(null)
  // Entra mode must wait for MSAL to hydrate before we know if there's a session, so it starts loading.
  const [loading, setLoading] = useState<boolean>(authMode === 'entra' || devSessionPresent)

  const loadProfile = useCallback(async () => {
    const me = await userApi.me()
    setProfile(me)
  }, [])

  // On first mount, hydrate any existing session (Entra account or stored dev user) and its profile.
  useEffect(() => {
    let cancelled = false
    async function bootstrap() {
      try {
        if (authMode === 'entra') {
          await initMsal()
          if (cancelled) return
          if (!getAccount()) return
          setSignedIn(true)
        } else if (!getStoredDevUser()) {
          return
        }
        await loadProfile()
      } catch {
        if (!cancelled) setProfile(null)
      } finally {
        if (!cancelled) setLoading(false)
      }
    }
    bootstrap()
    return () => { cancelled = true }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  const finishSignIn = useCallback(async () => {
    setSignedIn(true)
    setLoading(true)
    try {
      await loadProfile()
    } finally {
      setLoading(false)
    }
  }, [loadProfile])

  const signIn = useCallback(async (user: DevUser) => {
    // A fresh sign-in always starts on the user's own account (clear any prior account-switch).
    setActiveAccountId(null)
    setStoredDevUser(user)
    await finishSignIn()
  }, [finishSignIn])

  const signInWithEntra = useCallback(async () => {
    setActiveAccountId(null)
    await entraLogin()
    await finishSignIn()
  }, [finishSignIn])

  const signOut = useCallback(() => {
    // Signing out also drops any account-switch so the next sign-in starts on their own account.
    setActiveAccountId(null)
    setProfile(null)
    setSignedIn(false)
    if (authMode === 'entra') {
      void entraLogout().catch(() => { /* best-effort; local state is already cleared */ })
    } else {
      setStoredDevUser(null)
    }
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
    <AuthCtx.Provider
      value={{ signedIn, mode: authMode, profile, loading, signIn, signInWithEntra, signOut, refresh, switchAccount }}
    >
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
