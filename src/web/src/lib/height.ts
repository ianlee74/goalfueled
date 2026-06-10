// Height is stored canonically in centimeters on the server. These helpers convert between cm and a
// friendly feet/inches representation for the UI so users can enter whichever unit they prefer.

import type { UnitsPreference } from '../api/types'

export type HeightUnit = 'cm' | 'ftin'

const CM_PER_INCH = 2.54
const INCHES_PER_FOOT = 12

/** The height unit a units preference implies (Metric → cm, US → ft/in). */
export function heightUnitFor(preference: UnitsPreference): HeightUnit {
  return preference === 'Metric' ? 'cm' : 'ftin'
}

/** Splits a centimeter value into whole feet and (rounded) inches, e.g. 180 → { feet: 5, inches: 11 }. */
export function cmToFtIn(cm: number): { feet: number; inches: number } {
  const totalInches = Math.round(cm / CM_PER_INCH)
  return { feet: Math.floor(totalInches / INCHES_PER_FOOT), inches: totalInches % INCHES_PER_FOOT }
}

/** Converts feet + inches to centimeters (rounded to one decimal). */
export function ftInToCm(feet: number, inches: number): number {
  const cm = (feet * INCHES_PER_FOOT + inches) * CM_PER_INCH
  return Math.round(cm * 10) / 10
}

/**
 * A short human label for a stored cm height in the user's preferred unit:
 * Metric → "180 cm"; US/Imperial → "5′11″".
 */
export function formatHeight(cm: number, preference: UnitsPreference): string {
  if (heightUnitFor(preference) === 'cm') return `${Math.round(cm * 10) / 10} cm`
  const { feet, inches } = cmToFtIn(cm)
  return `${feet}′${inches}″`
}
