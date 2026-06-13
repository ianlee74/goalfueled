import { IconStar } from './icons'

/**
 * The favorite toggle star shown on Library list cards. Rendered inside a card that is itself a
 * <Link>, so the click is swallowed (preventDefault/stopPropagation) before invoking the toggle.
 */
export function FavoriteStar({ isFavorite, onToggle }: { isFavorite: boolean; onToggle: () => void }) {
  const label = isFavorite ? 'Remove from favorites' : 'Add to favorites'
  return (
    <button
      type="button"
      className={`fav-btn${isFavorite ? ' on' : ''}`}
      aria-pressed={isFavorite}
      aria-label={label}
      title={label}
      onClick={(e) => {
        e.preventDefault()
        e.stopPropagation()
        onToggle()
      }}
    >
      <IconStar filled={isFavorite} />
    </button>
  )
}
