import { useCallback, useState } from 'react'
import { ConfirmDialog, type ConfirmOptions } from '../components/ConfirmDialog'

/**
 * useConfirm — an app-styled replacement for the browser-native `window.confirm`.
 * Returns `confirm(options)` to open the dialog, and a `dialog` element to render
 * once near the top of the consuming component's tree:
 *
 *   const { confirm, dialog } = useConfirm()
 *   ...
 *   confirm({ title: 'Delete this ingredient?', confirmLabel: 'Delete', onConfirm: doDelete })
 *   ...
 *   return <>{dialog}{rest}</>
 *
 * The `onConfirm` action may be async; the dialog disables its buttons and shows a
 * pending label while it runs (no double-submit), and only closes once it resolves.
 */
export function useConfirm() {
  const [options, setOptions] = useState<ConfirmOptions | null>(null)

  const confirm = useCallback((opts: ConfirmOptions) => setOptions(opts), [])
  const close = useCallback(() => setOptions(null), [])

  const dialog = options ? <ConfirmDialog {...options} onClose={close} /> : null

  return { confirm, dialog }
}
