/**
 * Shared guard for `?returnTo=` navigation params. Only honor app-internal relative paths to
 * avoid open redirects: a single leading slash, but not "//host" (protocol-relative) or anything
 * carrying a scheme. Returns the safe path, or null when the value can't be trusted.
 */
export function safeReturnTo(raw: string | null): string | null {
  if (!raw) return null
  if (!raw.startsWith('/') || raw.startsWith('//')) return null
  return raw
}
