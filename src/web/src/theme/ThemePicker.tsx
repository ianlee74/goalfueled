import { useTheme } from './ThemeContext'
import { THEME_OPTIONS, isThemePreference } from './theme'

/** Compact theme selector for the app header. */
export function ThemePicker() {
  const { preference, setPreference } = useTheme()

  return (
    <label className="theme-picker" title="Choose a theme">
      <span className="sr-only">Theme</span>
      <select
        className="select"
        aria-label="Theme"
        value={preference}
        onChange={(e) => {
          if (isThemePreference(e.target.value)) setPreference(e.target.value)
        }}
      >
        {THEME_OPTIONS.map((opt) => (
          <option key={opt.value} value={opt.value}>
            {opt.label}
          </option>
        ))}
      </select>
    </label>
  )
}
