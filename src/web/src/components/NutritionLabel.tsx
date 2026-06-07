import { useNutrients } from '../hooks/useNutrients'
import { NUTRIENT_UNIT_ABBR, type NutrientAmount } from '../api/types'

/** A named column of nutrient amounts, e.g. "Whole recipe" or "Per serving". */
export interface NutritionColumn {
  label: string
  amounts: NutrientAmount[]
}

/**
 * Renders a compact, label-style table of nutrient amounts using the catalog for names/units/order.
 * Accepts either a single `amounts` list (one value column) or multiple named `columns`. When more than
 * one column is present, a header row of column labels is shown.
 */
export function NutritionLabel(
  props: { amounts: NutrientAmount[] } | { columns: NutritionColumn[] },
) {
  const { byId, ordered } = useNutrients()

  // Normalize both prop shapes into a single list of columns.
  const columns: NutritionColumn[] = 'columns' in props
    ? props.columns
    : [{ label: '', amounts: props.amounts }]

  // Per-column lookup of nutrientId -> value.
  const valueMaps = columns.map((c) => new Map(c.amounts.map((a) => [a.nutrientId, a.value])))

  if (valueMaps.every((m) => m.size === 0)) {
    return <p className="muted small">No nutritional info recorded.</p>
  }

  // Show in catalog order, only nutrients that have a value in at least one column.
  const rows = ordered.filter((def) => valueMaps.some((m) => m.has(def.id)))
  const showHeader = columns.length > 1

  return (
    <table className="nutri-table">
      {showHeader && (
        <thead>
          <tr>
            <th>{' '}</th>
            {columns.map((c) => <th key={c.label} className="val">{c.label}</th>)}
          </tr>
        </thead>
      )}
      <tbody>
        {rows.map((def) => {
          const meta = byId.get(def.id)
          const unit = meta ? NUTRIENT_UNIT_ABBR[meta.unit] : ''
          return (
            <tr key={def.id} className={meta?.isCoreLabelItem ? 'core' : ''}>
              <td>{meta?.name ?? def.id}</td>
              {valueMaps.map((m, i) => (
                <td key={i} className="val">
                  {m.has(def.id) ? <>{round(m.get(def.id) ?? 0)} {unit}</> : <span className="muted">—</span>}
                </td>
              ))}
            </tr>
          )
        })}
      </tbody>
    </table>
  )
}

function round(n: number): string {
  return Number.isInteger(n) ? String(n) : n.toFixed(1)
}
