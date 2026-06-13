import type { Ingredient } from '../api/types'
import { fold } from './fold'

// Shared ingredient type-ahead matching, used by the IngredientFinder combobox and the
// new-ingredient form's "similar ingredients" lookup so both classify and label matches the
// same way (mirroring the server's SearchText folding rules).

export interface FinderHit {
  ingredient: Ingredient
  /** Why a non-name match surfaced (e.g. "aka garbanzo beans", "es: garbanzos", "#legume"); null when the name itself matched. */
  hint: string | null
}

/**
 * Classifies why an ingredient matches a folded search term, surfacing the matching alias /
 * localized name / tag as a hint. Returns null when nothing matches.
 */
export function findHit(i: Ingredient, foldedTerm: string): FinderHit | null {
  if (fold(i.name).includes(foldedTerm)) return { ingredient: i, hint: null }
  const alias = i.aliases.find((a) => fold(a).includes(foldedTerm))
  if (alias) return { ingredient: i, hint: `aka ${alias}` }
  const localized = i.localizedNames.find((l) => fold(l.name).includes(foldedTerm))
  if (localized) return { ingredient: i, hint: `${localized.language}: ${localized.name}` }
  const tag = i.tags.find((t) => fold(t).includes(foldedTerm))
  if (tag) return { ingredient: i, hint: `#${tag}` }
  return null
}

/** The ingredient's calories per serving, rounded, or null when it tracks none. */
export function caloriesOf(i: Ingredient): number | null {
  const c = i.nutritionPerServing.find((n) => n.nutrientId === 'calories')
  return c ? Math.round(c.value) : null
}
