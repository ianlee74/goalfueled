// A controlled time picker that restricts minutes to 15-minute intervals
// (:00, :15, :30, :45). Renders a single <select> with 96 valid times so the
// "only 15-minute options" rule is guaranteed across all browsers, rather than
// relying on the browser-dependent dropdown of a native <input type="time">.
//
// It accepts and emits the SAME "HH:mm" string format (24-hour) used by the
// model, with the empty string "" representing an untimed / "Anytime" meal.

const STEP_MINUTES = 15

function pad2(n: number): string {
  return n.toString().padStart(2, '0')
}

// All valid "HH:mm" values in 15-minute steps: 00:00, 00:15, … 23:45.
const QUARTER_HOUR_TIMES: string[] = (() => {
  const out: string[] = []
  for (let m = 0; m < 24 * 60; m += STEP_MINUTES) {
    out.push(`${pad2(Math.floor(m / 60))}:${pad2(m % 60)}`)
  }
  return out
})()

// Format an "HH:mm" string as a friendly 12-hour label (e.g. "8:30 AM").
function formatLabel(hhmm: string): string {
  const [hStr, mStr] = hhmm.split(':')
  const h = Number(hStr)
  const m = Number(mStr)
  const period = h < 12 ? 'AM' : 'PM'
  const h12 = h % 12 === 0 ? 12 : h % 12
  return `${h12}:${pad2(m)} ${period}`
}

export function TimePicker({
  value,
  onChange,
  disabled,
  title,
  className = 'select',
  blankLabel = 'Anytime',
}: {
  value: string // "HH:mm" or "" for untimed
  onChange: (value: string) => void
  disabled?: boolean
  title?: string
  className?: string
  blankLabel?: string
}) {
  // Preserve legacy / off-grid stored times (e.g. "08:20" from older data):
  // if the current value isn't on a 15-minute boundary, surface it as its own
  // selectable option so editing an old plan never silently drops the time.
  const offGrid = value !== '' && !QUARTER_HOUR_TIMES.includes(value)

  return (
    <select
      className={className}
      value={value}
      disabled={disabled}
      title={title}
      onChange={(e) => onChange(e.target.value)}
    >
      <option value="">{blankLabel}</option>
      {offGrid && <option value={value}>{formatLabel(value)} (custom)</option>}
      {QUARTER_HOUR_TIMES.map((t) => (
        <option key={t} value={t}>
          {formatLabel(t)}
        </option>
      ))}
    </select>
  )
}
