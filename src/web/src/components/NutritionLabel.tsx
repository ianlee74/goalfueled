import { useNutrients } from '../hooks/useNutrients'
import { NUTRIENT_UNIT_ABBR, type NutrientAmount } from '../api/types'

/** Renders a compact, label-style table of nutrient amounts using the catalog for names/units/order. */
export function NutritionLabel({ amounts }: { amounts: NutrientAmount[] }) {
  const { byId, ordered } = useNutrients()

  if (amounts.length === 0) {
    return <p className="muted small">No nutritional info recorded.</p>
  }

  const valueFor = new Map(amounts.map((a) => [a.nutrientId, a.value]))
  // Show in catalog order, only nutrients that have a value.
  const rows = ordered.filter((def) => valueFor.has(def.id))

  return (
    <table className="nutri-table">
      <tbody>
        {rows.map((def) => {
          const meta = byId.get(def.id)
          const unit = meta ? NUTRIENT_UNIT_ABBR[meta.unit] : ''
          const value = valueFor.get(def.id) ?? 0
          return (
            <tr key={def.id} className={meta?.isCoreLabelItem ? 'core' : ''}>
              <td>{meta?.name ?? def.id}</td>
              <td className="val">
                {round(value)} {unit}
              </td>
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
