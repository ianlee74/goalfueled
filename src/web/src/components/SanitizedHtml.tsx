import { useMemo } from 'react'
import DOMPurify from 'dompurify'

/**
 * Renders user-supplied HTML (e.g. an ingredient/recipe description, which "may contain HTML")
 * safely. The markup is run through DOMPurify before it reaches the DOM, stripping scripts, event
 * handlers, and dangerous URIs, so the `dangerouslySetInnerHTML` below only ever sees sanitized
 * content. Use this anywhere a description is shown as formatted text rather than plain text.
 */
export function SanitizedHtml({ html, className }: { html: string; className?: string }) {
  // Memoize so we only sanitize when the input changes, not on every render.
  const clean = useMemo(() => DOMPurify.sanitize(html), [html])

  // Content is DOMPurify-sanitized immediately above; this is the intended, safe usage.
  return <div className={className} dangerouslySetInnerHTML={{ __html: clean }} />
}
