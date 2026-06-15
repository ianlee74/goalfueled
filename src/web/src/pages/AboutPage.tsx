/**
 * The About page (/about): a concise, on-brand explanation of what GoalFueled is,
 * plus a build-time "last updated" date so it stays accurate without hand-editing.
 */

// __BUILD_DATE__ is an ISO string injected by Vite's `define` at build time
// (see vite.config.ts / globals.d.ts). Parse + format it once at module load.
const LAST_UPDATED = new Date(__BUILD_DATE__).toLocaleDateString(undefined, {
  year: 'numeric',
  month: 'long',
  day: 'numeric',
})

export function AboutPage() {
  return (
    <div className="stack">
      <h1>About</h1>

      <div className="card stack">
        <p style={{ margin: 0 }}>
          GoalFueled is your athletic instrument for fuel. Log what you put in and what you
          burn off, watch your weight and goals trend in real time, and treat every day as a
          training session — not a diet.
        </p>
        <p style={{ margin: 0 }}>
          The daily loop lives on Today: read the gauge, log your fuel and burn, and stay on
          budget. The work you do once — building fuel sources, recipes, and weekly plans —
          makes that loop fast. Consistency earns Fuel Points and levels you up.
        </p>
      </div>

      <div className="card stack">
        <h2>What you can do</h2>
        <ul className="stack" style={{ margin: 0, paddingLeft: '1.2rem', gap: '0.4rem' }}>
          <li>Log <strong>fuel</strong> (food, hydration, supplements) and <strong>burn</strong> (activity).</li>
          <li>Track your <strong>weight</strong> and progress toward your <strong>goals</strong>.</li>
          <li>Build reusable <strong>fuel sources</strong>, <strong>recipes</strong>, and weekly <strong>plans</strong>.</li>
          <li>Earn <strong>Fuel Points</strong>, climb levels, and close out each week with a review.</li>
        </ul>
      </div>

      <p className="small muted" style={{ margin: 0 }}>Last updated: {LAST_UPDATED}</p>
    </div>
  )
}
