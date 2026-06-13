// Favorites are per-user marks on library items (ingredients/recipes/meals). The API lists
// favorites first; these helpers keep a loaded list consistent after an in-place toggle without
// a refetch.

/** Returns a copy with favorites first. The sort is stable, so the server's name order holds within each group. */
export function sortFavoritesFirst<T extends { isFavorite: boolean }>(items: T[]): T[] {
  return [...items].sort((a, b) => Number(b.isFavorite) - Number(a.isFavorite))
}

/** Returns a copy with one item's favorite flag replaced and favorites re-floated to the top. */
export function withFavorite<T extends { id: string; isFavorite: boolean }>(
  items: T[],
  id: string,
  isFavorite: boolean,
): T[] {
  return sortFavoritesFirst(items.map((x) => (x.id === id ? { ...x, isFavorite } : x)))
}
