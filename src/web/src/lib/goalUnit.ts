// A weight goal's server-provided metricUnit is the generic "lb/kg" (weight goals carry no stored unit).
// For display we substitute the user's preferred weight-unit label so the figure reads in their units;
// nutrient goals keep their own unit label unchanged. Affects ONLY body-weight goals.

import type { Goal, UnitsPreference } from '../api/types'
import { weightUnitLabelFor } from './weight'

/** The unit label to show for a goal's target/progress, honoring the units preference for weight goals. */
export function goalUnitLabel(goal: Goal, preference: UnitsPreference): string {
  return goal.metricType === 'Weight' ? weightUnitLabelFor(preference) : goal.metricUnit
}
