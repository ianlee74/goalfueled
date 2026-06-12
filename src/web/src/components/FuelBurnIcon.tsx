import type { ComponentType, ReactNode } from 'react'
import { IconBurn, IconGauge } from './icons'

/**
 * The app's two core calorie concepts:
 *  - `fuel` — an activity that ADDS calories to the body (consuming food).
 *  - `burn` — an activity that USES calories (exercise).
 *
 * These are surfaced consistently across the UI (currently The Log) so the vocabulary reads
 * coherently. Each kind pairs a monoline icon with an accent color drawn from the theme
 * tokens (emerald = fuel, ember = burn — mirroring the brand's rocket + exhaust).
 */
export type FuelBurnKind = 'fuel' | 'burn'

interface KindMeta {
  /** The monoline icon for the kind. */
  Icon: ComponentType<{ size?: number }>
  /** Capitalized noun used in headings/labels ("Fuel" / "Burn"). */
  label: string
}

const FUEL_BURN_META: Record<FuelBurnKind, KindMeta> = {
  fuel: { Icon: IconGauge, label: 'Fuel' },
  burn: { Icon: IconBurn, label: 'Burn' },
}

/** A small square chip showing the fuel/burn icon, themed to the kind's accent. */
export function FuelBurnIcon({ kind, title }: { kind: FuelBurnKind; title?: string }) {
  const meta = FUEL_BURN_META[kind]
  return (
    <span
      className={`fb-icon fb-${kind}`}
      role="img"
      aria-label={title ?? meta.label}
      title={title ?? meta.label}
    >
      <meta.Icon size={18} />
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
