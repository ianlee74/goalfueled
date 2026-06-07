import { useEffect, useMemo, useState } from 'react'
import { nutrientApi } from '../api/services'
import type { NutrientDefinition } from '../api/types'

// The nutrient catalog is static reference data, so cache it once per page load.
let cache: NutrientDefinition[] | null = null
let inflight: Promise<NutrientDefinition[]> | null = null

function loadCatalog(): Promise<NutrientDefinition[]> {
  if (cache) return Promise.resolve(cache)
  if (!inflight) {
    inflight = nutrientApi.list().then((list) => {
      cache = list
      return list
    })
  }
  return inflight
}

/** Provides the nutrient catalog plus handy lookups (by id, and catalog-ordered). */
export function useNutrients() {
  const [list, setList] = useState<NutrientDefinition[]>(cache ?? [])

  useEffect(() => {
    let cancelled = false
    loadCatalog().then((l) => { if (!cancelled) setList(l) }).catch(() => {})
    return () => { cancelled = true }
  }, [])

  const byId = useMemo(() => new Map(list.map((n) => [n.id, n])), [list])

  return { ordered: list, byId, loading: list.length === 0 }
}
