import {
  GOAL_LABELS, SEX_LABELS, UNITS_PREFERENCE_LABELS, WEIGHT_UNIT_LABELS,
  type GoalType, type SexAtBirth, type UnitsPreference, type WeightUnit,
} from '../api/types'
import { cmToFtIn, ftInToCm, heightUnitFor, type HeightUnit } from '../lib/height'
import { weightUnitFor } from '../lib/weight'

/**
 * The basic-info fields captured at first login and editable later on the Profile page: sex at birth,
 * height (cm or ft/in), current weight (value + unit), and primary goal. State is owned by the parent;
 * this component is purely presentational so both the Welcome and Profile pages share identical inputs.
 *
 * Height is stored canonically in centimeters (`heightCm`). When the user picks the ft/in unit, the
 * feet/inches inputs are derived from `heightCm` and edits are converted straight back to cm.
 */
export interface ProfileFieldsValue {
  sex: SexAtBirth | ''
  /**
   * Date of birth as "yyyy-MM-dd"; '' when unset. We only ever collect the user's *year* of birth
   * (to avoid storing a precise birth date), so this is always "{year}-01-01" — January 1 of the
   * entered year, which is what age calculations use. See {@link ProfileFields} for the year input.
   */
  dob: string
  heightCm: number | null
  heightUnit: HeightUnit
  weight: string
  weightUnit: WeightUnit
  goal: GoalType
  /**
   * Preferred unit system for weight + height. Switching it re-defaults the height and weight unit
   * selectors (Metric → cm + kg, US → ft/in + lb); the user may still override each per-entry.
   */
  unitsPreference: UnitsPreference
}

interface Props {
  value: ProfileFieldsValue
  onChange: (next: ProfileFieldsValue) => void
  disabled?: boolean
  /** When false (Profile page), the current-weight field is hidden; weight is recorded via The Log instead. */
  showWeight?: boolean
}

/** Keep only digits and a single decimal point (drops any further dots), for filtered numeric text entry. */
function sanitizeDecimal(text: string): string {
  const cleaned = text.replace(/[^\d.]/g, '')
  const firstDot = cleaned.indexOf('.')
  if (firstDot === -1) return cleaned
  return cleaned.slice(0, firstDot + 1) + cleaned.slice(firstDot + 1).replace(/\./g, '')
}

export function ProfileFields({ value, onChange, disabled, showWeight = true }: Props) {
  function set<K extends keyof ProfileFieldsValue>(key: K, v: ProfileFieldsValue[K]) {
    onChange({ ...value, [key]: v })
  }

  // Changing the units preference re-defaults both the height and weight unit selectors to match it.
  function setUnitsPreference(pref: UnitsPreference) {
    onChange({
      ...value,
      unitsPreference: pref,
      heightUnit: heightUnitFor(pref),
      weightUnit: weightUnitFor(pref),
    })
  }

  const ft = value.heightCm != null ? cmToFtIn(value.heightCm) : { feet: 0, inches: 0 }
  // We collect only the year of birth (no month/day) and store it as January 1 of that year
  // (stored dob is "{year}-01-01"); the input keeps just digits, capped at 4.
  // Read back the year segment, not a fixed-width slice: while typing, dob is "{partial}-01-01"
  // (e.g. "1-01-01"), and slice(0,4) would yield "1-01" — a non-numeric value that a number input
  // silently rejects, making the field impossible to type into one digit at a time.
  const birthYear = value.dob ? value.dob.split('-')[0] : ''

  function setBirthYear(yearText: string) {
    set('dob', yearText ? `${yearText}-01-01` : '')
  }

  function setFtIn(feet: number, inches: number) {
    set('heightCm', ftInToCm(feet, inches))
  }

  return (
    <div className="stack">
      <label className="field">
        Sex at birth
        <select
          className="select"
          value={value.sex}
          disabled={disabled}
          onChange={(e) => set('sex', e.target.value as SexAtBirth)}
        >
          <option value="" disabled>Select…</option>
          <option value="Male">{SEX_LABELS.Male}</option>
          <option value="Female">{SEX_LABELS.Female}</option>
        </select>
      </label>

      <label className="field">
        Units
        <select
          className="select"
          value={value.unitsPreference}
          disabled={disabled}
          onChange={(e) => setUnitsPreference(e.target.value as UnitsPreference)}
        >
          {(Object.keys(UNITS_PREFERENCE_LABELS) as UnitsPreference[]).map((p) => (
            <option key={p} value={p}>{UNITS_PREFERENCE_LABELS[p]}</option>
          ))}
        </select>
      </label>

      <label className="field">
        Year of birth
        {/* A filtered text input (not type="number"): a controlled number input blanks any invalid
            intermediate value, which made this field impossible to type into. We keep only digits and
            cap at 4. Range is validated on submit. */}
        <input
          className="input"
          type="text"
          inputMode="numeric"
          maxLength={4}
          placeholder="e.g. 1990"
          value={birthYear}
          disabled={disabled}
          onChange={(e) => setBirthYear(e.target.value.replace(/\D/g, '').slice(0, 4))}
        />
      </label>

      <div className="field">
        <div className="row spread">
          <span>Height</span>
          <select
            className="select"
            style={{ width: 'auto' }}
            value={value.heightUnit}
            disabled={disabled}
            onChange={(e) => set('heightUnit', e.target.value as HeightUnit)}
            aria-label="Height unit"
          >
            <option value="ftin">ft / in</option>
            <option value="cm">cm</option>
          </select>
        </div>
        {value.heightUnit === 'cm' ? (
          <input
            className="input"
            type="number"
            min="0"
            step="any"
            placeholder="e.g. 180"
            value={value.heightCm ?? ''}
            disabled={disabled}
            onChange={(e) => set('heightCm', e.target.value === '' ? null : Number(e.target.value))}
          />
        ) : (
          <div className="row" style={{ gap: '0.5rem' }}>
            <input
              className="input"
              type="number"
              min="0"
              step="1"
              placeholder="ft"
              value={value.heightCm != null ? ft.feet : ''}
              disabled={disabled}
              onChange={(e) => setFtIn(Number(e.target.value || 0), ft.inches)}
              aria-label="Feet"
            />
            <input
              className="input"
              type="number"
              min="0"
              max="11"
              step="1"
              placeholder="in"
              value={value.heightCm != null ? ft.inches : ''}
              disabled={disabled}
              onChange={(e) => setFtIn(ft.feet, Number(e.target.value || 0))}
              aria-label="Inches"
            />
          </div>
        )}
      </div>

      <p className="muted small" style={{ margin: 0 }}>
        We use your sex, year of birth, and height to work out your Kindle — the calories your body
        burns just by being alive, even at rest. We&apos;ll never share this with anyone without your
        permission.
      </p>

      {showWeight && (
        <div className="field">
          <span>Current weight</span>
          <div className="row" style={{ gap: '0.5rem' }}>
            {/* Filtered text input (not type="number") for the same reason as the year field: a
                controlled number input blanks invalid intermediate values, which can make the field
                feel impossible to type into. Keep digits and a single decimal point. */}
            <input
              className="input"
              type="text"
              inputMode="decimal"
              placeholder="e.g. 180"
              value={value.weight}
              disabled={disabled}
              style={{ flex: 1 }}
              onChange={(e) => set('weight', sanitizeDecimal(e.target.value))}
            />
            <select
              className="select"
              style={{ width: 'auto' }}
              value={value.weightUnit}
              disabled={disabled}
              onChange={(e) => set('weightUnit', e.target.value as WeightUnit)}
              aria-label="Weight unit"
            >
              <option value="Pound">{WEIGHT_UNIT_LABELS.Pound}</option>
              <option value="Kilogram">{WEIGHT_UNIT_LABELS.Kilogram}</option>
            </select>
          </div>
        </div>
      )}

      <label className="field">
        Goal
        <select
          className="select"
          value={value.goal}
          disabled={disabled}
          onChange={(e) => set('goal', e.target.value as GoalType)}
        >
          {(Object.keys(GOAL_LABELS) as GoalType[]).map((g) => (
            <option key={g} value={g}>{GOAL_LABELS[g]}</option>
          ))}
        </select>
      </label>
    </div>
  )
}
