import { Navigate, Route, Routes } from 'react-router-dom'
import { useAuth } from './auth/AuthContext'
import { Layout } from './components/Layout'
import { DashboardPage } from './pages/DashboardPage'
import { LoginPage } from './pages/LoginPage'
import { WelcomePage } from './pages/WelcomePage'
import { IngredientsPage } from './pages/IngredientsPage'
import { IngredientFormPage } from './pages/IngredientFormPage'
import { RecipesPage } from './pages/RecipesPage'
import { RecipeFormPage } from './pages/RecipeFormPage'
import { RecipeReviewQueuePage } from './pages/RecipeReviewQueuePage'
import { MealsPage } from './pages/MealsPage'
import { MealFormPage } from './pages/MealFormPage'
import { PlansPage } from './pages/PlansPage'
import { PlanFormPage } from './pages/PlanFormPage'
import { WeightPage } from './pages/WeightPage'
import { GoalsPage } from './pages/GoalsPage'
import { LogPage } from './pages/LogPage'
import { UsersPage } from './pages/UsersPage'
import { ProfilePage } from './pages/ProfilePage'

export default function App() {
  const { devUser, profile, loading } = useAuth()

  // Not signed in → login.
  if (!devUser) {
    return (
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="*" element={<Navigate to="/login" replace />} />
      </Routes>
    )
  }

  // Signed in but profile still loading.
  if (loading || !profile) {
    return <div className="center-screen"><p className="muted">Loading…</p></div>
  }

  // Signed in but hasn't finished the welcome flow → onboarding.
  if (!profile.hasCompletedOnboarding) {
    return (
      <Routes>
        <Route path="/welcome" element={<WelcomePage />} />
        <Route path="*" element={<Navigate to="/welcome" replace />} />
      </Routes>
    )
  }

  // Authenticated app.
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<DashboardPage />} />
        <Route path="/ingredients" element={<IngredientsPage />} />
        <Route path="/ingredients/:id" element={<IngredientFormPage />} />
        <Route path="/recipes" element={<RecipesPage />} />
        <Route path="/recipes/review-queue" element={<RecipeReviewQueuePage />} />
        <Route path="/recipes/:id" element={<RecipeFormPage />} />
        <Route path="/meals" element={<MealsPage />} />
        <Route path="/meals/:id" element={<MealFormPage />} />
        <Route path="/plans" element={<PlansPage />} />
        <Route path="/plans/:id" element={<PlanFormPage />} />
        <Route path="/log" element={<LogPage />} />
        <Route path="/weight" element={<WeightPage />} />
        <Route path="/goals" element={<GoalsPage />} />
        <Route path="/profile" element={<ProfilePage />} />
        <Route path="/users" element={<UsersPage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  )
}
