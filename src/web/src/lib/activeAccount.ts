/**
 * The account the user is currently viewing the app "as". Defaults to their own account; when they switch
 * into an account someone shared with them, that owner's user id is stored here and sent on every request as
 * the `X-Act-As-User` header. The API only honors it when the signed-in user holds an active share grant
 * over that account, so a stale/forged value can never escalate access — it simply falls back to "self".
 */

const STORAGE_KEY = 'gf.activeAccount'

/** The user id of the account currently being acted as, or null when operating as oneself. */
export function getActiveAccountId(): string | null {
  return localStorage.getItem(STORAGE_KEY)
}

/** Sets (non-null) or clears (null) the account being acted as. */
export function setActiveAccountId(userId: string | null): void {
  if (userId) localStorage.setItem(STORAGE_KEY, userId)
  else localStorage.removeItem(STORAGE_KEY)
}
