import type { ReactNode } from 'react'

/**
 * GoalFueled's custom technical-monoline icon set (DESIGN.md §1.3).
 * Single-weight square-capped strokes on a 24×24 grid, drawn like instrument /
 * blueprint markings — equipment, not characters. Icons inherit `currentColor`
 * so theme tokens tint them.
 */

interface IconProps {
  size?: number
  /** Accessible label; omitted = decorative (aria-hidden). */
  title?: string
}

function Svg({ size = 20, title, children }: IconProps & { children: ReactNode }) {
  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.9}
      strokeLinecap="square"
      strokeLinejoin="miter"
      role={title ? 'img' : undefined}
      aria-hidden={title ? undefined : true}
    >
      {title && <title>{title}</title>}
      {children}
    </svg>
  )
}

/** Fuel gauge with needle — the fuel/today mark. */
export function IconGauge(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M4 16a8 8 0 0 1 16 0" />
      <path d="M12 16l4.2-5.2" />
      <circle cx={12} cy={16} r={1.1} fill="currentColor" stroke="none" />
      <path d="M4 16h2M18 16h2" />
    </Svg>
  )
}

/** Faceted flame, straight segments — burn. */
export function IconBurn(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M12 3l3.5 5.5-2 2 4 3.5a6 6 0 1 1-11 0l4-4.5Z" />
    </Svg>
  )
}

/** Four-point spark — the passive "kindle" burn that runs in the background. */
export function IconSpark(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M12 3v5M12 16v5M3 12h5M16 12h5" />
      <path d="M12 9.5l1 1.5 1.5 1-1.5 1-1 1.5-1-1.5-1.5-1 1.5-1Z" />
    </Svg>
  )
}

/** Bathroom scale: plate with a dial. */
export function IconWeight(p: IconProps) {
  return (
    <Svg {...p}>
      <rect x={4.5} y={4.5} width={15} height={15} rx={1.5} />
      <path d="M9.5 10.5a2.5 2.5 0 0 1 5 0" />
      <path d="M12 10.5l1.6-1.9" />
    </Svg>
  )
}

/** Concentric target — goals. */
export function IconTarget(p: IconProps) {
  return (
    <Svg {...p}>
      <circle cx={12} cy={12} r={8.2} />
      <circle cx={12} cy={12} r={4.2} />
      <circle cx={12} cy={12} r={1} fill="currentColor" stroke="none" />
    </Svg>
  )
}

/** Hexagonal container with a fill line — ingredient. */
export function IconIngredient(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M12 2.8l7.5 4.3v9.8L12 21.2l-7.5-4.3V7.1Z" />
      <path d="M8.2 13.5h7.6" />
    </Svg>
  )
}

/** Pan with a straight handle — recipe. */
export function IconRecipe(p: IconProps) {
  return (
    <Svg {...p}>
      <circle cx={9.5} cy={13.5} r={5.5} />
      <path d="M14.7 11.7l6.3-2.3" />
    </Svg>
  )
}

/** Plate (rim + well) — meal. */
export function IconMeal(p: IconProps) {
  return (
    <Svg {...p}>
      <circle cx={12} cy={12} r={8.4} />
      <circle cx={12} cy={12} r={4} />
    </Svg>
  )
}

/** Grid calendar — plan. */
export function IconPlan(p: IconProps) {
  return (
    <Svg {...p}>
      <rect x={4} y={5.5} width={16} height={14.5} rx={1.5} />
      <path d="M8.5 3.5v4M15.5 3.5v4M4 10.5h16" />
      <path d="M8 14.5h.01M12 14.5h.01M16 14.5h.01M8 17.5h.01M12 17.5h.01" />
    </Svg>
  )
}

/** Ruled journal — the log. */
export function IconJournal(p: IconProps) {
  return (
    <Svg {...p}>
      <rect x={5.5} y={3.5} width={13.5} height={17} rx={1.5} />
      <path d="M9 3.5v17" />
      <path d="M12 9h4M12 13h4" />
    </Svg>
  )
}

/** Helmeted head + shoulders — profile/you. */
export function IconUser(p: IconProps) {
  return (
    <Svg {...p}>
      <circle cx={12} cy={8} r={3.6} />
      <path d="M5.5 20c0-3.6 2.9-5.6 6.5-5.6s6.5 2 6.5 5.6" />
    </Svg>
  )
}

/** Two figures — users (admin). */
export function IconUsers(p: IconProps) {
  return (
    <Svg {...p}>
      <circle cx={9} cy={8.5} r={3.1} />
      <path d="M3.5 19.5c0-3.1 2.5-4.9 5.5-4.9s5.5 1.8 5.5 4.9" />
      <path d="M15.4 5.9a3.1 3.1 0 0 1 0 5.2" />
      <path d="M16.8 14.9c2.3.6 3.7 2.2 3.7 4.6" />
    </Svg>
  )
}

/** Stacked crates — library. */
export function IconLibrary(p: IconProps) {
  return (
    <Svg {...p}>
      <rect x={5} y={4.5} width={14} height={6.5} />
      <rect x={5} y={13} width={14} height={6.5} />
      <path d="M10 7.75h4M10 16.25h4" />
    </Svg>
  )
}

/** Bold plus in a ring — the quick-log action. */
export function IconLogPlus(p: IconProps) {
  return (
    <Svg {...p}>
      <circle cx={12} cy={12} r={8.5} />
      <path d="M12 8.5v7M8.5 12h7" />
    </Svg>
  )
}

/** Rank chevrons — level. */
export function IconLevel(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M6 8.5l6-3.5 6 3.5" />
      <path d="M6 13l6-3.5 6 3.5" />
      <path d="M6 17.5l6-3.5 6 3.5" />
    </Svg>
  )
}

/** Partial ring meter — score. */
export function IconScore(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M12 3.5a8.5 8.5 0 1 1-8.1 5.9" />
      <circle cx={12} cy={12} r={1.1} fill="currentColor" stroke="none" />
    </Svg>
  )
}

/** Checkpoint flag — weekly review. */
export function IconReview(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M6 21V3.5" />
      <path d="M6 4.5h10.5l-2.6 3.5 2.6 3.5H6" />
    </Svg>
  )
}

/** Hamburger menu. */
export function IconMenu(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M4 6.5h16M4 12h16M4 17.5h16" />
    </Svg>
  )
}

/** Faceted five-point star — favorite marker. `filled` renders the solid (favorited) state. */
export function IconStar(p: IconProps & { filled?: boolean }) {
  const { filled, ...rest } = p
  return (
    <Svg {...rest}>
      <path
        d="M12 3.5l2.5 5.3 5.7.7-4.2 4 1.1 5.7L12 16.4l-5.1 2.8 1.1-5.7-4.2-4 5.7-.7Z"
        fill={filled ? 'currentColor' : 'none'}
      />
    </Svg>
  )
}

/** Monoline rocket — the brand mark (replaces the emoji logo). */
export function IconRocket(p: IconProps) {
  return (
    <Svg {...p}>
      <path d="M12 2.5c2.5 2 4 5 4 8.5 0 1.6-.3 3.1-.8 4.5H8.8A12.6 12.6 0 0 1 8 11c0-3.5 1.5-6.5 4-8.5Z" />
      <circle cx={12} cy={9.5} r={1.8} />
      <path d="M8.8 13.5L6 17.2h3.4M15.2 13.5l2.8 3.7h-3.4" />
      <path d="M12 18.5v3" />
    </Svg>
  )
}
