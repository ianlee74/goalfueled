// TypeScript mirrors of the API contracts. Enums are serialized as strings by the API
// (JsonStringEnumConverter), so they are modeled as string unions here.

export type AppRole = 'User' | 'AppAdmin'

export type GoalType = 'WeightLoss' | 'WeightManagement' | 'WeightGain'

export type SexAtBirth = 'Male' | 'Female'

/**
 * The user's preferred unit system for body WEIGHT and HEIGHT only (never recipe/ingredient units).
 * Metric → kilograms + centimeters; UsImperial → pounds + feet/inches.
 */
export type UnitsPreference = 'Metric' | 'UsImperial'

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
  sexAtBirth?: SexAtBirth
  // Height stored canonically in centimeters; the UI converts to/from ft/in.
  heightCm?: number
  // Date of birth "yyyy-MM-dd" (drives the Kindle BMR calc); absent until provided.
  dateOfBirth?: string
  hasCompletedOnboarding: boolean
  // The meal plan the user is currently following (drives The Log); absent if none chosen.
  activeMealPlanId?: string
  // The user's recurring weight-measurement schedule; absent when none is configured.
  weightSchedule?: WeightSchedule
  // Preferred unit system for body weight + height (kg/cm vs lb/ft-in). Defaults to UsImperial server-side.
  unitsPreference: UnitsPreference
}

export type WeightScheduleCadence = 'Daily' | 'Weekly' | 'EveryNDays' | 'EveryNWeeks'

/** A user's recurring weight-measurement schedule (drives the "Log your weight" task in The Log). */
export interface WeightSchedule {
  cadence: WeightScheduleCadence
  // Anchor date "yyyy-MM-dd"; its day-of-week is the chosen start day.
  startDate: string
  // The N for the "every N" cadences (1 otherwise).
  interval: number
  startDayOfWeek: DayOfWeek
}

/** Payload for setting (cadence present) or clearing (cadence null) the weight-measurement schedule. */
export interface WeightScheduleInput {
  cadence: WeightScheduleCadence | null
  // Optional "yyyy-MM-dd"; defaults to today server-side when omitted.
  startDate?: string
  // The N for the "every N" cadences; ignored otherwise.
  interval?: number
}

/** Payload for completing the first-login welcome flow. */
export interface OnboardingInput {
  primaryGoal: GoalType
  sexAtBirth: SexAtBirth
  heightCm: number
  // Date of birth "yyyy-MM-dd" (required; drives the Kindle BMR calc).
  dateOfBirth: string
  currentWeight: number
  weightUnit: WeightUnit
  // Preferred weight/height unit system.
  unitsPreference: UnitsPreference
}

/** Payload for updating profile attributes later. Current weight is optional. */
export interface ProfileInput {
  primaryGoal: GoalType
  sexAtBirth: SexAtBirth
  heightCm: number
  // Date of birth "yyyy-MM-dd" (required; drives the Kindle BMR calc).
  dateOfBirth: string
  currentWeight?: number
  weightUnit: WeightUnit
  // Preferred weight/height unit system.
  unitsPreference: UnitsPreference
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

/** A non-English name for an ingredient, matched by the ingredient finder. */
export interface LocalizedName {
  // Lower-case ISO 639-1 language code (e.g. "es", "fr", "zh").
  language: string
  name: string
}

export interface Ingredient {
  id: string
  name: string
  description?: string
  imageUrl?: string
  // Finder search metadata; the API always returns these as (possibly empty) arrays.
  tags: string[]
  aliases: string[]
  localizedNames: LocalizedName[]
  unit: UnitOfMeasure
  servingSize: number
  nutritionPerServing: NutrientAmount[]
  scope: OwnershipScope
  ownerUserId?: string
  createdAt: string
  updatedAt: string
  canEdit: boolean
  // True when the signed-in user has marked this ingredient as a favorite (per-user).
  isFavorite: boolean
}

export interface IngredientInput {
  name: string
  description?: string
  imageUrl?: string
  unit: UnitOfMeasure
  servingSize: number
  nutritionPerServing: NutrientAmount[]
  appLevel: boolean
  // Optional finder search metadata; trimmed and de-duplicated server-side.
  tags?: string[]
  aliases?: string[]
  localizedNames?: LocalizedName[]
}

/** The favorite state of one library item (ingredient/recipe/meal) for the signed-in user. */
export interface FavoriteState {
  id: string
  isFavorite: boolean
}

/** Result of an automatic nutrition lookup; amounts are per 100 g, mapped to the app's catalog. */
export interface NutritionLookupResult {
  matchedName: string
  source: string
  basisGrams: number
  nutritionPer100g: NutrientAmount[]
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
  // True when the signed-in user has marked this recipe as a favorite (per-user).
  isFavorite: boolean
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
  // True when the signed-in user has marked this meal as a favorite (per-user).
  isFavorite: boolean
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
  // True when a nutrient goal's progress incorporates the user's actual logged consumption for at least
  // one day in the active period (un-logged days fall back to planned meal-plan intake). False for weight
  // goals and for nutrient goals projected purely from meal plans.
  basedOnLoggedConsumption: boolean
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

// 'Recipe' survives only on historical entries (logged before recipe logging was removed); the API
// rejects it for new entries — recipes define a meal, they are not consumed directly.
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
  // Correlation key when this entry completed a planned-meal task; absent for ad-hoc entries.
  plannedMealRef?: string
}

/** A planned-meal task in The Log: a meal the active plan schedules for the day, shown as checkable. */
export interface PlannedMealTask {
  // Stable correlation key for this task on this date.
  ref: string
  mealId: string
  // Resolved meal name (absent if the meal is no longer visible/exists).
  mealName?: string
  // Scheduled time of day, "HH:mm:ss"; absent for an untimed/anytime task.
  time?: string
  nutrition: NutrientAmount[]
  done: boolean
  // Id of the consumption entry that completed this task, when done.
  completedEntryId?: string
  // True when the user has skipped this planned meal: it's dropped from "still to do" and adds no nutrition.
  skipped?: boolean
}

/** The recurring "Log your weight" task, present only on days a measurement is due. */
export interface WeightTask {
  done: boolean
  // The recorded weight for the day when done; absent otherwise.
  recordedValue?: number
  recordedUnit?: WeightUnit
}

/** The active plan's tasks for a day. A null planId means no active plan is selected. */
export interface PlannedDay {
  // The day, "yyyy-MM-dd".
  date: string
  planId?: string
  planName?: string
  tasks: PlannedMealTask[]
  // The recurring weight task, present only when a measurement is due on the day.
  weightTask?: WeightTask
}

/** Payload for completing/un-completing a planned-meal task in The Log. */
export interface CompletePlannedMealInput {
  ref: string
  // Optional "yyyy-MM-dd"; defaults to today server-side when omitted.
  date?: string
  // Servings actually eaten (more/less than the planned single serving). Defaults to 1 server-side when
  // omitted. When a completion already exists, supplying a value updates it in place (no duplicate).
  servings?: number
}

export interface ConsumptionDay {
  // The day this log is for, "yyyy-MM-dd".
  date: string
  entries: ConsumptionEntry[]
  // Sum of every entry's nutrition for the day.
  totalNutrition: NutrientAmount[]
}

export interface ConsumptionEntryInput {
  // 'Recipe' is not accepted (the API rejects it); log a meal or a free-form item instead.
  source: Exclude<ConsumptionSource, 'Recipe'>
  // Required for the Meal source.
  sourceId?: string
  // Required for free-form entries; optional name override for catalog entries.
  name?: string
  servings: number
  // Optional per-serving nutrition for a free-form entry.
  nutrition?: NutrientAmount[]
  // Optional "yyyy-MM-dd"; defaults to today server-side when omitted.
  date?: string
}

/** A single logged burn activity (exercise that used calories). */
export interface BurnActivity {
  id: string
  // Calendar day, serialized "yyyy-MM-dd".
  date: string
  name: string
  // Calories burned, a positive magnitude (treated as negative in net-calorie summaries).
  caloriesBurned: number
  createdAt: string
  updatedAt: string
  // Optional user-specified time of day, serialized "HH:mm:ss"; drives timeline placement.
  // Absent for older entries (the UI then falls back to createdAt).
  time?: string
}

/** A day's burn log: its activities plus the day's total calories burned (positive; callers subtract it). */
export interface BurnDay {
  // The day this log is for, "yyyy-MM-dd".
  date: string
  activities: BurnActivity[]
  // Sum of every activity's calories for the day (positive magnitude).
  totalCaloriesBurned: number
  // The passive "Kindle" cost: daily sedentary calorie usage (Mifflin–St Jeor BMR × 1.2), a positive
  // magnitude derived from the user's profile + latest weight. Absent when the profile is incomplete
  // (the UI then prompts the user to complete it). Subtracted in the net summary: net = fuel − burn − kindle.
  kindleCost?: number
}

/** A past activity name with the calories from its most-recent entry, for the burn-name autocomplete. */
export interface BurnSuggestion {
  name: string
  // Calories from the most-recent entry that used this name (defaults the calories field on select).
  lastCalories: number
}

/** Payload for logging a burn activity. */
export interface BurnActivityInput {
  name: string
  // Calories burned, a positive magnitude (must be greater than zero).
  caloriesBurned: number
  // Optional "yyyy-MM-dd"; defaults to today server-side when omitted.
  date?: string
  // Optional time of day "HH:mm:ss"; placed/sorted in the timeline by this time when present.
  time?: string
}

// ---- Dashboard (landing page) ----

/** One upcoming planned meal on the dashboard (completable via the planned-complete endpoint). */
export interface UpcomingMeal {
  // Stable correlation key for completing this task (same form as PlannedMealTask.ref).
  ref: string
  mealId: string
  mealName?: string
  // The calendar day this occurrence falls on, "yyyy-MM-dd".
  date: string
  // Scheduled time of day, "HH:mm:ss"; absent for an untimed/"anytime" meal.
  time?: string
  calories: number
  // True when this occurrence is on a day later than today (it rolled forward).
  isLaterDay: boolean
}

/** The "weight is due today" prompt, present only when a measurement is due today. */
export interface WeightDue {
  alreadyRecorded: boolean
  recordedValue?: number
  recordedUnit?: WeightUnit
}

/** One day's point in the 7-day fuel/burn/kindle/net series. */
export interface ProgressDay {
  // The calendar day, "yyyy-MM-dd".
  date: string
  fuel: number
  // Calories burned via logged activity (positive magnitude).
  burn: number
  // Passive sedentary cost (0 when the profile is incomplete).
  kindle: number
  // Net = fuel − burn − kindle for the day.
  net: number
}

/** The 7-day progress series plus the derived weight-change estimate. */
export interface ProgressSeries {
  days: ProgressDay[]
  // Sum of every day's net (fuel − burn − kindle); negative = net deficit.
  cumulativeNet: number
  // Estimated body weight lost over the window (deficit → positive), in the user's unit.
  estimatedWeightLost: number
  weightUnit: WeightUnit
  // True when kindle could be computed (profile complete + a recorded weight).
  kindleAvailable: boolean
  // True when the window has any fuel/burn/kindle data (drives the zero-state).
  hasData: boolean
}

/** The aggregated landing-page dashboard payload. */
export interface Dashboard {
  // The day the dashboard was computed for, "yyyy-MM-dd".
  date: string
  activePlanId?: string
  activePlanName?: string
  upcomingMeals: UpcomingMeal[]
  // The "log your weight today" prompt, present only when due today.
  weightDue?: WeightDue
  progress: ProgressSeries
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

export const SEX_LABELS: Record<SexAtBirth, string> = {
  Male: 'Male',
  Female: 'Female',
}

// How the units preference is presented in the profile/onboarding control.
export const UNITS_PREFERENCE_LABELS: Record<UnitsPreference, string> = {
  Metric: 'Metric (kg, cm)',
  UsImperial: 'US (lb, ft/in)',
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

export const WEIGHT_SCHEDULE_CADENCE_LABELS: Record<WeightScheduleCadence, string> = {
  Daily: 'Daily',
  Weekly: 'Weekly',
  EveryNDays: 'Every N days',
  EveryNWeeks: 'Every N weeks',
}

// Days of week in the order .NET's System.DayOfWeek enumerates them (Sunday = 0).
export const DAYS_OF_WEEK: DayOfWeek[] = [
  'Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday',
]

// ---- Gamification (Fuel Points, levels, weekly review) ----

/** A level on the fuel-rank ladder (Ignition → Liftoff → …). */
export interface Level {
  // 1-based level number.
  number: number
  // Rank name, e.g. "Cruise".
  rank: string
  // Cumulative Fuel Points at which this level starts.
  floorPoints: number
  // Cumulative Fuel Points needed for the next level.
  nextLevelPoints: number
}

/** A completed weekly review in summary form. */
export interface CompletedReview {
  weekStart: string
  score: number
  pointsAwarded: number
  completedAt: string
}

export interface GamificationSummary {
  fuelPoints: number
  level: Level
  // Monday of the current (in-progress) week, "yyyy-MM-dd".
  currentWeekStart: string
  // Running 0–100 score for the current week so far.
  currentWeekScore: number
  // Monday of the most recent finished week when it hasn't been reviewed yet.
  pendingReviewWeekStart?: string
  lastReview?: CompletedReview
}

/** One day inside a weekly review. */
export interface ReviewDay {
  date: string
  fuel: number
  burn: number
  net: number
  fuelEntries: number
  fullyLogged: boolean
  adherent: boolean
  weighInDue: boolean
  weighInLogged: boolean
}

/** The full "your week in fuel" review. Scores reflect engagement, never deficit size. */
export interface WeeklyReview {
  weekStart: string
  weekEnd: string
  completed: boolean
  completedAt?: string
  score: number
  loggingScore: number
  adherenceScore: number
  weightScore: number
  daysFullyLogged: number
  daysAdherent: number
  weighInsDue: number
  weighInsLogged: number
  totalFuel: number
  totalBurn: number
  netCalories: number
  kindleAvailable: boolean
  days: ReviewDay[]
  bestDay?: string
  weightDelta?: number
  weightDeltaUnit?: WeightUnit
  pointsEarned: number
  completionBonus?: number
}
