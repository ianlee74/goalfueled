import { api } from './client'
import type {
  AdminUser, BurnActivity, BurnActivityInput, BurnDay, BurnSuggestion,
  CompletePlannedMealInput, ConsumptionDay, ConsumptionEntry, ConsumptionEntryInput,
  CurrentUser, Dashboard, Goal, GoalInput, Ingredient, IngredientInput, Meal, MealInput,
  MealPlan, MealPlanInput, NutrientDefinition, NutritionLookupResult, OnboardingInput,
  PlannedDay, ProfileInput, Recipe, RecipeInput, WeightEntry, WeightEntryInput,
  WeightScheduleInput,
} from './types'

export const userApi = {
  me: () => api.get<CurrentUser>('/me'),
  completeOnboarding: (input: OnboardingInput) =>
    api.post<CurrentUser>('/me/onboarding', input),
  updateProfile: (input: ProfileInput) =>
    api.put<CurrentUser>('/me/profile', input),
  setAdminView: (enabled: boolean) =>
    api.post<CurrentUser>('/me/admin-view', { enabled }),
  // Choose (or clear, with null) the meal plan the user is currently following.
  setActivePlan: (mealPlanId: string | null) =>
    api.put<CurrentUser>('/me/active-plan', { mealPlanId }),
  // Set (cadence present) or clear (cadence null) the recurring weight-measurement schedule.
  setWeightSchedule: (input: WeightScheduleInput) =>
    api.put<CurrentUser>('/me/weight-schedule', input),
}

// Admin-only user management. Endpoints return 403 unless the caller is acting as an admin.
export const adminApi = {
  listUsers: () => api.get<AdminUser[]>('/admin/users'),
  setAdminRole: (id: string, isAdmin: boolean) =>
    api.post<AdminUser>(`/admin/users/${id}/admin-role`, { isAdmin }),
}

// Builds a `?search=…&all=…` query string. `all` is honored server-side only for acting admins.
function listQuery(search?: string, all?: boolean): string {
  const params = new URLSearchParams()
  if (search) params.set('search', search)
  if (all) params.set('all', 'true')
  const qs = params.toString()
  return qs ? `?${qs}` : ''
}

export const ingredientApi = {
  list: (search?: string, all?: boolean) =>
    api.get<Ingredient[]>(`/ingredients${listQuery(search, all)}`),
  get: (id: string) => api.get<Ingredient>(`/ingredients/${id}`),
  // Auto-fill nutrition from an external source (USDA), per 100 g, mapped to the app's catalog.
  nutritionLookup: (query: string) =>
    api.get<NutritionLookupResult>(`/ingredients/nutrition-lookup?query=${encodeURIComponent(query)}`),
  create: (input: IngredientInput) => api.post<Ingredient>('/ingredients', input),
  update: (id: string, input: IngredientInput) => api.put<Ingredient>(`/ingredients/${id}`, input),
  remove: (id: string) => api.del(`/ingredients/${id}`),
}

export const recipeApi = {
  list: (search?: string, all?: boolean) =>
    api.get<Recipe[]>(`/recipes${listQuery(search, all)}`),
  get: (id: string) => api.get<Recipe>(`/recipes/${id}`),
  create: (input: RecipeInput) => api.post<Recipe>('/recipes', input),
  update: (id: string, input: RecipeInput) => api.put<Recipe>(`/recipes/${id}`, input),
  remove: (id: string) => api.del(`/recipes/${id}`),
  submitForReview: (id: string) => api.post<Recipe>(`/recipes/${id}/submit-for-review`),
  // Admin review queue + actions.
  reviewQueue: () => api.get<Recipe[]>('/recipes/review-queue'),
  approve: (id: string) => api.post<Recipe>(`/recipes/${id}/approve`),
  reject: (id: string) => api.post<Recipe>(`/recipes/${id}/reject`),
}

export const mealApi = {
  list: (search?: string, all?: boolean) =>
    api.get<Meal[]>(`/meals${listQuery(search, all)}`),
  get: (id: string) => api.get<Meal>(`/meals/${id}`),
  create: (input: MealInput) => api.post<Meal>('/meals', input),
  update: (id: string, input: MealInput) => api.put<Meal>(`/meals/${id}`, input),
  remove: (id: string) => api.del(`/meals/${id}`),
}

export const mealPlanApi = {
  list: (search?: string, all?: boolean) =>
    api.get<MealPlan[]>(`/mealplans${listQuery(search, all)}`),
  get: (id: string) => api.get<MealPlan>(`/mealplans/${id}`),
  create: (input: MealPlanInput) => api.post<MealPlan>('/mealplans', input),
  update: (id: string, input: MealPlanInput) => api.put<MealPlan>(`/mealplans/${id}`, input),
  remove: (id: string) => api.del(`/mealplans/${id}`),
}

export const weightApi = {
  // The caller's own weight history, most recent first.
  list: () => api.get<WeightEntry[]>('/weights'),
  // Records (or, for an existing day, updates) a dated weight entry.
  record: (input: WeightEntryInput) => api.post<WeightEntry>('/weights', input),
}

export const goalApi = {
  // The caller's own goals, most recently created first, each with a computed progress snapshot.
  list: () => api.get<Goal[]>('/goals'),
  get: (id: string) => api.get<Goal>(`/goals/${id}`),
  create: (input: GoalInput) => api.post<Goal>('/goals', input),
  update: (id: string, input: GoalInput) => api.put<Goal>(`/goals/${id}`, input),
  remove: (id: string) => api.del(`/goals/${id}`),
}

export const consumptionApi = {
  // The caller's consumption log for a day (defaults to today server-side when date is omitted).
  day: (date?: string) => api.get<ConsumptionDay>(`/consumption${date ? `?date=${date}` : ''}`),
  // Logs a consumed item (catalog meal/recipe or free-form).
  log: (input: ConsumptionEntryInput) => api.post<ConsumptionEntry>('/consumption', input),
  remove: (id: string) => api.del(`/consumption/${id}`),
  // The active plan's planned-meal tasks for a day (each flagged done when completed).
  plannedDay: (date?: string) =>
    api.get<PlannedDay>(`/consumption/planned${date ? `?date=${date}` : ''}`),
  // Completes a planned-meal task (records a consumption entry tagged with its ref).
  completePlanned: (input: CompletePlannedMealInput) =>
    api.post<ConsumptionEntry>('/consumption/planned/complete', input),
  // Un-completes a planned-meal task (removes its completion entry).
  uncompletePlanned: (input: CompletePlannedMealInput) =>
    api.post<void>('/consumption/planned/uncomplete', input),
}

export const burnApi = {
  // The caller's burn log for a day (defaults to today server-side when date is omitted).
  day: (date?: string) => api.get<BurnDay>(`/burn${date ? `?date=${date}` : ''}`),
  // Logs a burn activity (name + calories burned).
  log: (input: BurnActivityInput) => api.post<BurnActivity>('/burn', input),
  remove: (id: string) => api.del(`/burn/${id}`),
  // Past activity names with their most-recent calories, for the name autocomplete.
  suggestions: () => api.get<BurnSuggestion[]>('/burn/suggestions'),
}

export const nutrientApi = {
  list: () => api.get<NutrientDefinition[]>('/nutrients'),
}

export const dashboardApi = {
  // The aggregated landing-page dashboard (defaults to today server-side when date is omitted).
  get: (date?: string) => api.get<Dashboard>(`/dashboard${date ? `?date=${date}` : ''}`),
}

export const imageApi = {
  // Uploads an image and returns its public blob URL.
  upload: (file: File) => api.upload<{ url: string }>('/images', file),
}
