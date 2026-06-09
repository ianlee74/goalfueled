// TypeScript mirrors of the API contracts. Enums are serialized as strings by the API
// (JsonStringEnumConverter), so they are modeled as string unions here.

export type AppRole = 'User' | 'AppAdmin'

export type GoalType = 'WeightLoss' | 'WeightManagement' | 'WeightGain'

export type OwnershipScope = 'App' | 'User'

export type UnitOfMeasure =
  | 'Gram' | 'Milligram' | 'Kilogram' | 'Ounce' | 'Pound'
  | 'Milliliter' | 'Liter' | 'Teaspoon' | 'Tablespoon' | 'FluidOunce' | 'Cup'
  | 'Piece' | 'Serving'

export type NutrientUnit = 'Kcal' | 'Gram' | 'Milligram' | 'Microgram' | 'IU'

export type NutrientCategory =
  | 'Energy' | 'Macronutrient' | 'Fat' | 'Carbohydrate' | 'Sterol'
  | 'Mineral' | 'Vitamin' | 'Other'

export interface NutrientDefinition {
  id: string
  name: string
  unit: NutrientUnit
  category: NutrientCategory
  dailyValue?: number
  isCoreLabelItem: boolean
}

export interface NutrientAmount {
  nutrientId: string
  value: number
}

export interface CurrentUser {
  id: string
  email: string
  displayName: string
  avatarUrl?: string
  roles: AppRole[]
  isAdmin: boolean
  adminViewEnabled: boolean
  isActingAsAdmin: boolean
  primaryGoal?: GoalType
  hasCompletedOnboarding: boolean
}

/** A user as shown on the admin user-management screen. */
export interface AdminUser {
  id: string
  email: string
  displayName: string
  avatarUrl?: string
  roles: AppRole[]
  isAdmin: boolean
  isSelf: boolean
}

export interface Ingredient {
  id: string
  name: string
  description?: string
  imageUrl?: string
  unit: UnitOfMeasure
  servingSize: number
  nutritionPerServing: NutrientAmount[]
  scope: OwnershipScope
  ownerUserId?: string
  createdAt: string
  updatedAt: string
  canEdit: boolean
}

export interface IngredientInput {
  name: string
  description?: string
  imageUrl?: string
  unit: UnitOfMeasure
  servingSize: number
  nutritionPerServing: NutrientAmount[]
  appLevel: boolean
}

export type RecipeVisibility = 'Private' | 'PendingReview' | 'Public' | 'Rejected'

export interface RecipeItem {
  ingredientId: string
  quantity: number
  unit: UnitOfMeasure
  ingredientName?: string
}

export interface Recipe {
  id: string
  name: string
  description?: string
  imageUrl?: string
  items: RecipeItem[]
  servings: number
  scope: OwnershipScope
  ownerUserId?: string
  originalCreatorUserId?: string
  visibility: RecipeVisibility
  totalNutrition: NutrientAmount[]
  nutritionPerServing: NutrientAmount[]
  createdAt: string
  updatedAt: string
  canEdit: boolean
  canSubmitForReview: boolean
  canReview: boolean
}

export interface RecipeItemInput {
  ingredientId: string
  quantity: number
  unit: UnitOfMeasure
}

export interface RecipeInput {
  name: string
  description?: string
  imageUrl?: string
  items: RecipeItemInput[]
  servings: number
  appLevel: boolean
}

export interface MealPortion {
  recipeId: string
  servings: number
  recipeName?: string
}

export interface Meal {
  id: string
  name: string
  portions: MealPortion[]
  scope: OwnershipScope
  ownerUserId?: string
  totalNutrition: NutrientAmount[]
  createdAt: string
  updatedAt: string
  canEdit: boolean
}

export interface MealPortionInput {
  recipeId: string
  servings: number
}

export interface MealInput {
  name: string
  portions: MealPortionInput[]
  appLevel: boolean
}

export type MealPlanCadence = 'Daily' | 'Weekly'

export type DayOfWeek =
  | 'Sunday' | 'Monday' | 'Tuesday' | 'Wednesday' | 'Thursday' | 'Friday' | 'Saturday'

export interface ScheduledMeal {
  mealId: string
  dayOfWeek?: DayOfWeek
  // Serialized as "HH:mm:ss" by the API.
  time?: string
  mealName?: string
}

export interface UntimedItem {
  mealId?: string
  ingredientId?: string
  quantity?: number
  note?: string
  // Resolved name of the referenced meal or ingredient.
  name?: string
}

export interface DailyNutrition {
  dayOfWeek?: DayOfWeek
  nutrition: NutrientAmount[]
}

export interface NutritionSummary {
  days: DailyNutrition[]
  weeklyTotal: NutrientAmount[]
  dailyAverage: NutrientAmount[]
}

export interface MealPlan {
  id: string
  name: string
  cadence: MealPlanCadence
  ownerUserId?: string
  scheduledMeals: ScheduledMeal[]
  untimedItems: UntimedItem[]
  summary: NutritionSummary
  createdAt: string
  updatedAt: string
  canEdit: boolean
}

export interface ScheduledMealInput {
  mealId: string
  dayOfWeek?: DayOfWeek
  time?: string
}

export interface UntimedItemInput {
  mealId?: string
  ingredientId?: string
  quantity?: number
  note?: string
}

export interface MealPlanInput {
  name: string
  cadence: MealPlanCadence
  scheduledMeals: ScheduledMealInput[]
  untimedItems: UntimedItemInput[]
}

export type WeightUnit = 'Pound' | 'Kilogram'

export interface WeightEntry {
  id: string
  // Calendar day, serialized "yyyy-MM-dd".
  date: string
  value: number
  unit: WeightUnit
  createdAt: string
  updatedAt: string
}

export interface WeightEntryInput {
  value: number
  unit: WeightUnit
  // Optional "yyyy-MM-dd"; defaults to today server-side when omitted.
  date?: string
}

export type GoalMetricType = 'Weight' | 'Nutrient'

export type GoalCadence = 'OneTime' | 'Recurring'

export type GoalStatus = 'InProgress' | 'Achieved' | 'Missed' | 'NoData'

export interface GoalProgress {
  status: GoalStatus
  baselineValue: number
  currentValue: number
  targetValue: number
  // Progress toward the target, clamped 0..100, for the progress bar.
  percent: number
  exceededTarget: boolean
  // Whether the current standing is good (drives celebrate vs. warn in the visual).
  outcomePositive: boolean
  periodLabel: string
  message: string
}

export interface Goal {
  id: string
  name: string
  metricType: GoalMetricType
  nutrientId?: string
  nutrientName?: string
  // Unit label for the metric (e.g. "lb/kg", "kcal", "g", "mg").
  metricUnit: string
  targetValue: number
  cadence: GoalCadence
  // Calendar dates, serialized "yyyy-MM-dd".
  startDate: string
  targetDate?: string
  recurrenceDays?: number
  exceedingIsPositive: boolean
  progress: GoalProgress
  createdAt: string
  updatedAt: string
}

export interface GoalInput {
  name: string
  metricType: GoalMetricType
  targetValue: number
  cadence: GoalCadence
  exceedingIsPositive: boolean
  nutrientId?: string
  // Optional "yyyy-MM-dd"; defaults to today server-side when omitted.
  startDate?: string
  targetDate?: string
  recurrenceDays?: number
}

export type ConsumptionSource = 'Meal' | 'Recipe' | 'FreeForm'

export interface ConsumptionEntry {
  id: string
  // Calendar day, serialized "yyyy-MM-dd".
  date: string
  source: ConsumptionSource
  // Referenced meal/recipe id for catalog entries; absent for free-form items.
  sourceId?: string
  name: string
  servings: number
  // Nutrition for this logged item (source per-serving × servings, or inline free-form × servings).
  nutrition: NutrientAmount[]
  // True when the referenced catalog item is no longer visible (live nutrition couldn't be resolved).
  sourceMissing: boolean
  createdAt: string
  updatedAt: string
}

export interface ConsumptionDay {
  // The day this log is for, "yyyy-MM-dd".
  date: string
  entries: ConsumptionEntry[]
  // Sum of every entry's nutrition for the day.
  totalNutrition: NutrientAmount[]
}

export interface ConsumptionEntryInput {
  source: ConsumptionSource
  // Required for Meal/Recipe sources.
  sourceId?: string
  // Required for free-form entries; optional name override for catalog entries.
  name?: string
  servings: number
  // Optional per-serving nutrition for a free-form entry.
  nutrition?: NutrientAmount[]
  // Optional "yyyy-MM-dd"; defaults to today server-side when omitted.
  date?: string
}

export const CONSUMPTION_SOURCE_LABELS: Record<ConsumptionSource, string> = {
  Meal: 'Meal', Recipe: 'Recipe', FreeForm: 'Free-form',
}

export const GOAL_METRIC_LABELS: Record<GoalMetricType, string> = {
  Weight: 'Weight', Nutrient: 'Nutrient',
}

export const GOAL_CADENCE_LABELS: Record<GoalCadence, string> = {
  OneTime: 'One-time (by a date)', Recurring: 'Recurring (every N days)',
}

// Friendly labels for the recipe visibility lifecycle.
export const RECIPE_VISIBILITY_LABELS: Record<RecipeVisibility, string> = {
  Private: 'Private',
  PendingReview: 'Pending review',
  Public: 'Public',
  Rejected: 'Rejected',
}

export const GOAL_LABELS: Record<GoalType, string> = {
  WeightLoss: 'Lose weight',
  WeightManagement: 'Maintain weight',
  WeightGain: 'Gain weight',
}

// Display abbreviations + grouping for the unit selector.
export const UNIT_LABELS: Record<UnitOfMeasure, string> = {
  Gram: 'grams (g)', Milligram: 'milligrams (mg)', Kilogram: 'kilograms (kg)',
  Ounce: 'ounces (oz)', Pound: 'pounds (lb)',
  Milliliter: 'milliliters (ml)', Liter: 'liters (l)', Teaspoon: 'teaspoons (tsp)',
  Tablespoon: 'tablespoons (tbsp)', FluidOunce: 'fluid ounces (fl oz)', Cup: 'cups',
  Piece: 'pieces', Serving: 'servings',
}

export const NUTRIENT_UNIT_ABBR: Record<NutrientUnit, string> = {
  Kcal: 'kcal', Gram: 'g', Milligram: 'mg', Microgram: 'mcg', IU: 'IU',
}

export const MEAL_PLAN_CADENCE_LABELS: Record<MealPlanCadence, string> = {
  Daily: 'Daily', Weekly: 'Weekly',
}

export const WEIGHT_UNIT_LABELS: Record<WeightUnit, string> = {
  Pound: 'lb', Kilogram: 'kg',
}

// Days of week in the order .NET's System.DayOfWeek enumerates them (Sunday = 0).
export const DAYS_OF_WEEK: DayOfWeek[] = [
  'Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday',
]
