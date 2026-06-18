import { useEffect, useState } from 'react'
import { sharingApi } from '../api/services'
import { useAuth } from '../auth/AuthContext'
import type { SharedAccount } from '../api/types'

/**
 * Lets a user who has access to more than one account (their own plus any shared with them) switch which
 * one the app is viewed as. Renders nothing while there is only the user's own account, so it stays out of
 * the way for everyone who isn't part of a sharing relationship. The active account is the one matching the
 * loaded profile id (which is the shared owner's id while acting via a share).
 */
export function AccountSwitcher() {
  const { profile, switchAccount } = useAuth()
  const [accounts, setAccounts] = useState<SharedAccount[]>([])
  const [busy, setBusy] = useState(false)

  useEffect(() => {
    let cancelled = false
    sharingApi.accounts()
      .then((a) => { if (!cancelled) setAccounts(a) })
      .catch(() => {})
    return () => { cancelled = true }
    // Reload whenever the active account changes so the list reflects newly accepted/revoked shares.
  }, [profile?.id])

  // Only worth showing once there's a second account to switch into.
  if (accounts.length < 2) return null

  // The id of the account currently shown: the profile id (the shared owner's id when acting via a share).
  const activeId = profile?.id
  const selfId = accounts.find((a) => a.isSelf)?.userId ?? null

  async function onChange(userId: string) {
    setBusy(true)
    try {
      // Switching to one's own account clears the act-as header (null), otherwise act as the chosen owner.
      await switchAccount(userId === selfId ? null : userId)
    } finally {
      setBusy(false)
    }
  }

  return (
    <label className="stack" style={{ gap: '0.25rem' }}>
      <span className="small muted">Viewing account</span>
      <select
        className="input"
        value={activeId ?? ''}
        disabled={busy}
        onChange={(e) => onChange(e.target.value)}
      >
        {accounts.map((a) => (
          <option key={a.userId} value={a.userId}>
            {a.displayName}{a.isSelf ? ' (you)' : ''}
          </option>
        ))}
      </select>
    </label>
  )
}
