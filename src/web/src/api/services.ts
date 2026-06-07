import { api } from './client'
import type {
  CurrentUser, GoalType, Ingredient, IngredientInput, NutrientDefinition,
  Recipe, RecipeInput,
} from './types'

export const userApi = {
  me: () => api.get<CurrentUser>('/me'),
  completeOnboarding: (primaryGoal: GoalType) =>
    api.post<CurrentUser>('/me/onboarding', { primaryGoal }),
  setAdminView: (enabled: boolean) =>
    api.post<CurrentUser>('/me/admin-view', { enabled }),
}

export const ingredientApi = {
  list: (search?: string) =>
    api.get<Ingredient[]>(`/ingredients${search ? `?search=${encodeURIComponent(search)}` : ''}`),
  get: (id: string) => api.get<Ingredient>(`/ingredients/${id}`),
  create: (input: IngredientInput) => api.post<Ingredient>('/ingredients', input),
  update: (id: string, input: IngredientInput) => api.put<Ingredient>(`/ingredients/${id}`, input),
  remove: (id: string) => api.del(`/ingredients/${id}`),
}

export const recipeApi = {
  list: (search?: string) =>
    api.get<Recipe[]>(`/recipes${search ? `?search=${encodeURIComponent(search)}` : ''}`),
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

export const nutrientApi = {
  list: () => api.get<NutrientDefinition[]>('/nutrients'),
}
