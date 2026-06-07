import { api } from './client'
import type {
  AdminUser, CurrentUser, GoalType, Ingredient, IngredientInput, Meal, MealInput,
  MealPlan, MealPlanInput, NutrientDefinition, Recipe, RecipeInput,
} from './types'

export const userApi = {
  me: () => api.get<CurrentUser>('/me'),
  completeOnboarding: (primaryGoal: GoalType) =>
    api.post<CurrentUser>('/me/onboarding', { primaryGoal }),
  setAdminView: (enabled: boolean) =>
    api.post<CurrentUser>('/me/admin-view', { enabled }),
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

export const nutrientApi = {
  list: () => api.get<NutrientDefinition[]>('/nutrients'),
}

export const imageApi = {
  // Uploads an image and returns its public blob URL.
  upload: (file: File) => api.upload<{ url: string }>('/images', file),
}
