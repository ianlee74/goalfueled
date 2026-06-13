import { useCallback, useEffect, useId, useRef, useState } from 'react'

/**
 * App-styled confirmation dialog (replaces the browser-native window.confirm, which
 * doesn't match our "athletic instrument" look). Follows the same inline-backdrop
 * pattern as Sheet/LevelUpOverlay: a fixed backdrop + a raised surface, closes on
 * backdrop click or Escape, with proper dialog semantics and focus management.
 *
 * The confirm handler may be async; the dialog disables its buttons and shows a
 * pending label while it runs, and stays open until the action settles (so the
 * action's own navigation/error side effects decide what happens next).
 *
 * Usually driven via the `useConfirm` hook (src/hooks/useConfirm), which manages the
 * open/close state and renders this for you.
 */

export interface ConfirmOptions {
  title: string
  message?: string
  /** Confirm button label (default "Confirm"). */
  confirmLabel?: string
  /** Label shown while the async confirm action runs (default "Working…"). */
  pendingLabel?: string
  /** Cancel button label (default "Cancel"). */
  cancelLabel?: string
  /** Style the confirm button as destructive (danger-tinted). Defaults to true. */
  danger?: boolean
  /** The action to run when confirmed. May be async; the dialog awaits it. */
  onConfirm: () => void | Promise<void>
}

export function ConfirmDialog({
  title,
  message,
  confirmLabel = 'Confirm',
  pendingLabel = 'Working…',
  cancelLabel = 'Cancel',
  danger = true,
  onConfirm,
  onClose,
}: ConfirmOptions & { onClose: () => void }) {
  const baseId = useId()
  const titleId = `${baseId}-title`
  const messageId = `${baseId}-msg`
  const confirmRef = useRef<HTMLButtonElement>(null)
  const dialogRef = useRef<HTMLDivElement>(null)
  const [pending, setPending] = useState(false)

  // Move focus into the dialog on open (the confirm action is the primary target).
  useEffect(() => {
    confirmRef.current?.focus()
  }, [])

  const cancel = useCallback(() => {
    if (!pending) onClose()
  }, [pending, onClose])

  async function handleConfirm() {
    if (pending) return // guard against double-submit
    setPending(true)
    try {
      await onConfirm()
      onClose()
    } catch {
      // The action surfaces its own error (e.g. an inline notice); reopen for retry.
      setPending(false)
    }
  }

  // Escape cancels; Tab is trapped within the dialog's buttons.
  useEffect(() => {
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        e.preventDefault()
        if (!pending) onClose()
        return
      }
      if (e.key === 'Tab') {
        const focusable = dialogRef.current?.querySelectorAll<HTMLElement>('button:not([disabled])')
        if (!focusable || focusable.length === 0) return
        const first = focusable[0]
        const last = focusable[focusable.length - 1]
        const active = document.activeElement
        if (e.shiftKey && active === first) {
          e.preventDefault()
          last.focus()
        } else if (!e.shiftKey && active === last) {
          e.preventDefault()
          first.focus()
        }
      }
    }
    document.addEventListener('keydown', onKey)
    return () => document.removeEventListener('keydown', onKey)
  }, [pending, onClose])

  return (
    <div className="confirm-backdrop" onClick={cancel}>
      <div
        ref={dialogRef}
        className="confirm-dialog"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={message ? messageId : undefined}
        onClick={(e) => e.stopPropagation()}
      >
        <h2 id={titleId} className="confirm-title">{title}</h2>
        {message && <p id={messageId} className="confirm-message">{message}</p>}
        <div className="confirm-actions">
          <button type="button" className="btn btn-caps" onClick={cancel} disabled={pending}>
            {cancelLabel}
          </button>
          <button
            ref={confirmRef}
            type="button"
            className={danger ? 'btn btn-caps btn-danger' : 'btn btn-primary'}
            onClick={handleConfirm}
            disabled={pending}
          >
            {pending ? pendingLabel : confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
