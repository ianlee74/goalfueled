import { GOAL_LABELS, SEX_LABELS, WEIGHT_UNIT_LABELS, type GoalType, type SexAtBirth, type WeightUnit } from '../api/types'
import { cmToFtIn, ftInToCm, type HeightUnit } from '../lib/height'

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
}

interface Props {
  value: ProfileFieldsValue
  onChange: (next: ProfileFieldsValue) => void
  disabled?: boolean
  /** When false (Profile page), the current-weight field is hidden; weight is recorded via The Log instead. */
  showWeight?: boolean
}

export function ProfileFields({ value, onChange, disabled, showWeight = true }: Props) {
  function set<K extends keyof ProfileFieldsValue>(key: K, v: ProfileFieldsValue[K]) {
    onChange({ ...value, [key]: v })
  }

  const ft = value.heightCm != null ? cmToFtIn(value.heightCm) : { feet: 0, inches: 0 }
  // We collect only the year of birth (no month/day) and store it as January 1 of that year. The
  // year input is bounded to 1900..current year (no future years); the stored dob is "{year}-01-01".
  const currentYear = new Date().getFullYear()
  const birthYear = value.dob ? value.dob.slice(0, 4) : ''

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
        Year of birth
        <input
          className="input"
          type="number"
          inputMode="numeric"
          min="1900"
          max={currentYear}
          step="1"
          placeholder="e.g. 1990"
          value={birthYear}
          disabled={disabled}
          onChange={(e) => setBirthYear(e.target.value)}
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

      {showWeight && (
        <div className="field">
          <span>Current weight</span>
          <div className="row" style={{ gap: '0.5rem' }}>
            <input
              className="input"
              type="number"
              min="0"
              step="any"
              placeholder="e.g. 180"
              value={value.weight}
              disabled={disabled}
              style={{ flex: 1 }}
              onChange={(e) => set('weight', e.target.value)}
            />
            <select
              className="select"
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
