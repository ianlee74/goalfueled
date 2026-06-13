/**
 * Mirrors SearchText.Fold on the server: lower-case + strip combining diacritics, so
 * "jalapeno" matches "jalapeño" in either direction. Shared by the type-ahead finders
 * (ingredients, burn activities) so they all match the same way.
 */
export function fold(value: string): string {
  return value.normalize('NFD').replace(/[̀-ͯ]/g, '').toLowerCase()
}
