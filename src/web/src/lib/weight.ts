// Weight values are stored on each WeightEntry with their own unit (Pound | Kilogram). These helpers
// convert between kg and lb so any stored value can be displayed in the user's preferred unit. This
// affects ONLY body weight — never recipe/ingredient measurement units.

import type { UnitsPreference, WeightUnit } from '../api/types'

// Mirrors the server-side KindleCostCalculator.KilogramsPerPound conversion constant.
const KILOGRAMS_PER_POUND = 0.45359237

/** The weight unit a units preference implies (Metric → kg, US → lb). */
export function weightUnitFor(preference: UnitsPreference): WeightUnit {
  return preference === 'Metric' ? 'Kilogram' : 'Pound'
}

/** The short weight-unit label for a units preference (Metric → "kg", US → "lb"). */
export function weightUnitLabelFor(preference: UnitsPreference): string {
  return preference === 'Metric' ? 'kg' : 'lb'
}

/** Converts a weight value from one unit to another. A no-op when the units already match. */
export function convertWeight(value: number, from: WeightUnit, to: WeightUnit): number {
  if (from === to) return value
  return to === 'Kilogram' ? value * KILOGRAMS_PER_POUND : value / KILOGRAMS_PER_POUND
}

/** Rounds a weight to one decimal place (e.g. 81.6467 → 81.6). */
export function roundWeight(value: number): number {
  return Math.round(value * 10) / 10
}

/**
 * Formats a stored weight (value + its own unit) in the user's preferred unit, e.g. "180 lb" or
 * "81.6 kg". Converts when the stored unit differs from the preference.
 */
export function formatWeight(value: number, unit: WeightUnit, preference: UnitsPreference): string {
  const target = weightUnitFor(preference)
  const converted = roundWeight(convertWeight(value, unit, target))
  return `${converted} ${target === 'Kilogram' ? 'kg' : 'lb'}`
}
