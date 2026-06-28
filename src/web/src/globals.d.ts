/// <reference types="vite/client" />

/** ISO 8601 build timestamp injected at build time (see `define` in vite.config.ts). */
declare const __BUILD_DATE__: string

/**
 * Build-time config for Entra External ID sign-in. When all three required vars are set the app runs
 * in "entra" auth mode; otherwise it falls back to local dev logins. See src/web/.env.example and
 * src/web/src/auth/authConfig.ts.
 */
interface ImportMetaEnv {
  /** SPA app registration client id. */
  readonly VITE_ENTRA_CLIENT_ID?: string
  /** Authority URL, e.g. https://<tenant>.ciamlogin.com/<tenant-guid>/v2.0 */
  readonly VITE_ENTRA_AUTHORITY?: string
  /** API scope requested for the access token, e.g. api://<api-client-id>/access_as_user */
  readonly VITE_ENTRA_API_SCOPE?: string
  /** Optional explicit authority host to trust; derived from VITE_ENTRA_AUTHORITY when omitted. */
  readonly VITE_ENTRA_KNOWN_AUTHORITY?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
