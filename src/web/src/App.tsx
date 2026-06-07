import { Navigate, Route, Routes } from 'react-router-dom'
import { useAuth } from './auth/AuthContext'
import { Layout } from './components/Layout'
import { LoginPage } from './pages/LoginPage'
import { WelcomePage } from './pages/WelcomePage'
import { IngredientsPage } from './pages/IngredientsPage'
import { IngredientFormPage } from './pages/IngredientFormPage'
import { RecipesPage } from './pages/RecipesPage'
import { RecipeFormPage } from './pages/RecipeFormPage'
import { RecipeReviewQueuePage } from './pages/RecipeReviewQueuePage'
import { MealsPage } from './pages/MealsPage'
import { MealFormPage } from './pages/MealFormPage'
import { ComingSoonPage } from './pages/ComingSoonPage'

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
        <Route index element={<Navigate to="/ingredients" replace />} />
        <Route path="/ingredients" element={<IngredientsPage />} />
        <Route path="/ingredients/:id" element={<IngredientFormPage />} />
        <Route path="/recipes" element={<RecipesPage />} />
        <Route path="/recipes/review-queue" element={<RecipeReviewQueuePage />} />
        <Route path="/recipes/:id" element={<RecipeFormPage />} />
        <Route path="/meals" element={<MealsPage />} />
        <Route path="/meals/:id" element={<MealFormPage />} />
        <Route
          path="/plans"
          element={
            <ComingSoonPage
              title="Meal Plans"
              description="Plan when to consume daily meals — daily or weekly — including untimed items like water, and see a nutritional summary of your day."
              planned={[
                'Daily & weekly schedules',
                'Timed and untimed items',
                'Daily nutrition summary toward your goal',
              ]}
            />
          }
        />
        <Route path="*" element={<Navigate to="/ingredients" replace />} />
      </Route>
    </Routes>
  )
}
