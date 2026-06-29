// Thin wrapper around MSAL for the "entra" auth mode. Uses popup flows so it slots into the existing
// AuthContext without redirect-bootstrapping: sign-in opens the Entra user flow (Google + email/password
// local accounts, depending on tenant config) in a popup, and access tokens are acquired silently from
// the cached account on every API call (with a popup fallback when interaction is required).
//
// Everything here throws if Entra is not configured; callers must guard on `authMode === 'entra'`.

import {
  PublicClientApplication,
  InteractionRequiredAuthError,
  type AccountInfo,
} from '@azure/msal-browser'
import { entraConfig } from './authConfig'

let pca: PublicClientApplication | null = null
let initPromise: Promise<void> | null = null

function app(): PublicClientApplication {
  if (!entraConfig) throw new Error('Entra is not configured')
  if (!pca) {
    pca = new PublicClientApplication({
      auth: {
        clientId: entraConfig.clientId,
        authority: entraConfig.authority,
        knownAuthorities: entraConfig.knownAuthority ? [entraConfig.knownAuthority] : [],
        // A blank page (not the SPA root) so the popup's auth response lands somewhere that doesn't
        // re-boot the app — booting the SPA in the popup breaks the popup handshake. Must be registered
        // as a SPA redirect URI in the app registration.
        redirectUri: `${window.location.origin}/blank.html`,
      },
      // localStorage so the session survives a full page reload (the SPA hydrates from it on mount).
      cache: { cacheLocation: 'localStorage' },
    })
  }
  return pca
}

/** Idempotently initialize MSAL (required by v3+ before any other call). */
export async function initMsal(): Promise<void> {
  initPromise ??= (async () => {
    await app().initialize()
    // Completes any pending redirect response and, crucially, clears a stale "interaction_in_progress"
    // flag that an interrupted popup/redirect can leave in localStorage — which would otherwise block
    // every subsequent sign-in until storage is manually cleared.
    await app().handleRedirectPromise()
  })()
  await initPromise
}

/** The signed-in account, or null. Call after {@link initMsal}. */
export function getAccount(): AccountInfo | null {
  return app().getAllAccounts()[0] ?? null
}

/** Interactive sign-in via popup. Resolves to the signed-in account. */
export async function login(): Promise<AccountInfo> {
  await initMsal()
  const result = await app().loginPopup({ scopes: [entraConfig!.scope] })
  app().setActiveAccount(result.account)
  return result.account
}

/** Sign out and clear the cached account. */
export async function logout(): Promise<void> {
  await initMsal()
  const account = getAccount()
  await app().logoutPopup({ account: account ?? undefined })
}

/**
 * Acquire an access token for the API scope, or null if no account is signed in. Tries silent first
 * and falls back to an interactive popup only when the cached token can't be refreshed.
 */
export async function acquireToken(): Promise<string | null> {
  await initMsal()
  const account = getAccount()
  if (!account) return null

  const request = { scopes: [entraConfig!.scope], account }
  try {
    const result = await app().acquireTokenSilent(request)
    return result.accessToken
  } catch (e) {
    if (e instanceof InteractionRequiredAuthError) {
      const result = await app().acquireTokenPopup(request)
      return result.accessToken
    }
    throw e
  }
}
