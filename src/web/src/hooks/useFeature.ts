import { useMemo } from 'react'
import { useAuth } from '../auth/AuthContext'
import type { FeatureStatus, UserFeature } from '../api/types'

/**
 * The feature-tag keys the UI gates on. Mirrors `FeatureKeys` in
 * `src/GoalFueled.Domain/Features/FeatureDefinition.cs` — keep the two in sync; the server is the
 * authority, this is only so the client can hide what the server would refuse anyway.
 */
export const FEATURES = {
  barcodeScanning: 'barcode-scanning',
  nutritionLabelOcr: 'nutrition-label-ocr',
  accountSharing: 'account-sharing',
  mealPlans: 'meal-plans',
  weeklyReview: 'weekly-review',
  aiVoiceLogging: 'ai-voice-logging',
} as const

export type FeatureKey = (typeof FEATURES)[keyof typeof FEATURES]

/**
 * The current account's feature tags, keyed for lookup. `/me` returns ONLY the features the account
 * actually has (tier ∪ individual grants − individual denials), so presence is permission.
 */
export function useFeatures(): Map<string, UserFeature> {
  const { profile } = useAuth()
  const features = profile?.features
  return useMemo(() => new Map((features ?? []).map((f) => [f.key, f])), [features])
}

/**
 * Whether the current account has a feature. Gate UI with it:
 *
 * ```tsx
 * const canScan = useFeature(FEATURES.barcodeScanning)
 * {canScan && <Link to="/ingredients/scan">Scan</Link>}
 * ```
 *
 * Fails closed: while the profile is still loading, or for a key the server doesn't know, this is false.
 * Hiding a control is a courtesy — the endpoint behind it enforces the same gate and returns 403.
 */
export function useFeature(key: string): boolean {
  return useFeatures().has(key)
}

/**
 * The lifecycle status of a feature the account has (`'Beta'`, `'Released'`, …), or null when the account
 * doesn't have it. Use it to badge a gated surface, e.g. show "Beta" next to a still-settling feature.
 */
export function useFeatureStatus(key: string): FeatureStatus | null {
  return useFeatures().get(key)?.status ?? null
}
