// Thin wrapper around MSAL for the "entra" auth mode. Uses the full-page REDIRECT flow (not popups):
// sign-in navigates the whole tab to the Entra user flow (Google + email/password) and returns to the
// app, where initMsal()'s handleRedirectPromise() completes the sign-in. Redirect avoids the popup
// handshake failure modes (interaction_in_progress / no_token_request_cache_error / blocked or
// orphaned popups) and needs no separate redirect page — the app origin is the redirect target.
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
        // The app origin (already registered as a SPA redirect URI). The redirect response lands here
        // and is processed by handleRedirectPromise() in initMsal().
        redirectUri: window.location.origin,
      },
      // localStorage so the session survives the full-page redirect and later reloads.
      cache: { cacheLocation: 'localStorage' },
    })
  }
  return pca
}

/**
 * Idempotently initialize MSAL and process a returning redirect response. Calling handleRedirectPromise()
 * here both completes a redirect sign-in (establishing the cached account) and clears any stale
 * interaction state. Safe (returns null) on a normal page load.
 */
export async function initMsal(): Promise<void> {
  initPromise ??= (async () => {
    await app().initialize()
    await app().handleRedirectPromise()
  })()
  await initPromise
}

/** The signed-in account, or null. Call after {@link initMsal}. */
export function getAccount(): AccountInfo | null {
  return app().getAllAccounts()[0] ?? null
}

/** Interactive sign-in via full-page redirect. Navigates away; the session is established on return. */
export async function login(): Promise<void> {
  await initMsal()
  await app().loginRedirect({ scopes: [entraConfig!.scope] })
}

/** Sign out via full-page redirect, clearing the cached account. */
export async function logout(): Promise<void> {
  await initMsal()
  await app().logoutRedirect({ account: getAccount() ?? undefined })
}

/**
 * Acquire an access token for the API scope, or null if no account is signed in. Tries silent first;
 * if interaction is required it falls back to a redirect (which navigates away and resolves on return).
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
      await app().acquireTokenRedirect(request)
      return null // the page navigates away; the token is available after the redirect completes
    }
    throw e
  }
}
