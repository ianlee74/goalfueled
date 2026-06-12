import { useEffect } from 'react'
import type { Level } from '../api/types'
import { IconLevel } from './icons'

/** Full-screen rank card with a subtle ember sweep; auto-dismisses, honors reduced motion via CSS. */
export function LevelUpOverlay({ level, onClose }: { level: Level; onClose: () => void }) {
  useEffect(() => {
    const t = setTimeout(onClose, 2400)
    return () => clearTimeout(t)
  }, [onClose])

  return (
    <div className="levelup-backdrop" role="status" onClick={onClose}>
      <div className="levelup-card">
        <span style={{ color: 'var(--primary)' }}><IconLevel size={34} /></span>
        <span className="stat-label">Level {level.number}</span>
        <span className="levelup-rank">{level.rank}</span>
        <span className="small muted">Fuel Points keep climbing — next rank at {level.nextLevelPoints.toLocaleString()} FP</span>
      </div>
    </div>
  )
}
