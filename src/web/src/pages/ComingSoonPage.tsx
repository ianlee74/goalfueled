interface Props {
  title: string
  description: string
  planned: string[]
}

/** Placeholder screen for features scaffolded in this MVP but not yet implemented. */
export function ComingSoonPage({ title, description, planned }: Props) {
  return (
    <div className="stack">
      <h1>{title}</h1>
      <div className="card stack">
        <span className="badge mine">Coming soon</span>
        <p style={{ margin: 0 }}>{description}</p>
        <div>
          <strong className="small">Planned for this feature:</strong>
          <ul className="small muted">
            {planned.map((p) => <li key={p}>{p}</li>)}
          </ul>
        </div>
      </div>
    </div>
  )
}
