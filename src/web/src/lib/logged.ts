/**
 * A one-line notification that something was logged from outside the screen showing it.
 *
 * The assistant sheet lives in the app chrome (`Layout`), so when it logs fuel or burn the Today screen
 * underneath is already mounted and would otherwise keep showing stale totals. Rather than lifting the
 * whole day's state into a provider for this single case, the writer announces and Today re-reads.
 */
const EVENT = 'goalfueled:logged'

/** Announce that fuel/burn data changed. Safe to call from anywhere. */
export function notifyLogged(): void {
  if (typeof window !== 'undefined') window.dispatchEvent(new CustomEvent(EVENT))
}

/** Subscribe to those announcements. Returns the unsubscribe function. */
export function onLogged(handler: () => void): () => void {
  if (typeof window === 'undefined') return () => {}
  window.addEventListener(EVENT, handler)
  return () => window.removeEventListener(EVENT, handler)
}
