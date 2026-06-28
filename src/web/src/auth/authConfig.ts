// Picks the sign-in mode from build-time config.
//
// - "entra": real sign-in via Microsoft Entra External ID (which federates Google and, if enabled in
//   the tenant user flow, email+password local accounts). Used in deployed environments.
// - "dev":   the preset X-Dev-* header logins for local development (see ./devUsers).
//
// Mode is "entra" only when all three VITE_ENTRA_* vars are present at build time; otherwise the app
// falls back to dev logins. This keeps `npm run dev` working with zero config while letting a deployed
// build opt into Entra by setting the vars (see src/web/.env.example).

export interface EntraConfig {
  /** The SPA app registration's client id. */
  clientId: string
  /** Authority URL, e.g. https://<tenant>.ciamlogin.com/<tenant-guid>/v2.0 */
  authority: string
  /** API scope the SPA requests an access token for, e.g. api://<api-client-id>/access_as_user */
  scope: string
  /** Authority host that MSAL should trust, e.g. <tenant>.ciamlogin.com (required for CIAM/custom authorities). */
  knownAuthority?: string
}

function readEntraConfig(): EntraConfig | null {
  const clientId = import.meta.env.VITE_ENTRA_CLIENT_ID?.trim()
  const authority = import.meta.env.VITE_ENTRA_AUTHORITY?.trim()
  const scope = import.meta.env.VITE_ENTRA_API_SCOPE?.trim()
  if (!clientId || !authority || !scope) return null

  let knownAuthority = import.meta.env.VITE_ENTRA_KNOWN_AUTHORITY?.trim()
  if (!knownAuthority) {
    // Derive the host from the authority URL so callers usually don't have to set it explicitly.
    try {
      knownAuthority = new URL(authority).host
    } catch {
      knownAuthority = undefined
    }
  }

  return { clientId, authority, scope, knownAuthority }
}

export const entraConfig = readEntraConfig()
export const authMode: 'entra' | 'dev' = entraConfig ? 'entra' : 'dev'
