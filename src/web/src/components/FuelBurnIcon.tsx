import type { ReactNode } from 'react'

/**
 * The app's two core calorie concepts:
 *  - `fuel` — an activity that ADDS calories to the body (consuming food).
 *  - `burn` — an activity that USES calories (exercise).
 *
 * These are surfaced consistently across the UI (currently The Log) so the vocabulary reads
 * coherently. Iconography follows the app's emoji convention; each kind also has an accent color
 * drawn from the theme tokens (emerald = fuel, flame-orange = burn — mirroring the brand's
 * green check-rocket with a flame-orange exhaust).
 */
export type FuelBurnKind = 'fuel' | 'burn'

interface KindMeta {
  /** The emoji icon for the kind. */
  icon: string
  /** Capitalized noun used in headings/labels ("Fuel" / "Burn"). */
  label: string
  /** Accent color CSS variable for the kind. */
  accent: string
  /** Soft background CSS variable for the kind's icon chip. */
  soft: string
}

const FUEL_BURN_META: Record<FuelBurnKind, KindMeta> = {
  fuel: { icon: '🍽️', label: 'Fuel', accent: 'var(--primary)', soft: 'var(--primary-soft)' },
  burn: { icon: '🔥', label: 'Burn', accent: 'var(--accent)', soft: 'var(--accent-soft)' },
}

/** A small square chip showing the fuel/burn emoji, themed to the kind's accent. */
export function FuelBurnIcon({ kind, title }: { kind: FuelBurnKind; title?: string }) {
  const meta = FUEL_BURN_META[kind]
  return (
    <span
      className={`fb-icon fb-${kind}`}
      role="img"
      aria-label={title ?? meta.label}
      title={title ?? meta.label}
    >
      {meta.icon}
    </span>
  )
}

/**
 * A section heading that pairs the kind's icon with its label (and optional trailing content,
 * e.g. a count). Use at the top of a Fuel or Burn section in The Log.
 */
export function FuelBurnHeading(
  { kind, children }: { kind: FuelBurnKind; children?: ReactNode },
) {
  const meta = FUEL_BURN_META[kind]
  return (
    <div className="row spread fb-heading">
      <h2 className={`fb-title fb-${kind}`}>
        <FuelBurnIcon kind={kind} />
        {meta.label}
      </h2>
      {children}
    </div>
  )
}
