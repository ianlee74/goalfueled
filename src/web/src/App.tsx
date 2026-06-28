import { Navigate, Route, Routes, useLocation, useParams } from 'react-router-dom'
import { useAuth } from './auth/AuthContext'
import { Layout } from './components/Layout'
import { TodayPage } from './pages/TodayPage'
import { LoginPage } from './pages/LoginPage'
import { WelcomePage } from './pages/WelcomePage'
import { IngredientsPage } from './pages/IngredientsPage'
import { IngredientFormPage } from './pages/IngredientFormPage'
import { IngredientReviewQueuePage } from './pages/IngredientReviewQueuePage'
import { RecipesPage } from './pages/RecipesPage'
import { RecipeFormPage } from './pages/RecipeFormPage'
import { RecipeReviewQueuePage } from './pages/RecipeReviewQueuePage'
import { FuelSourcesPage } from './pages/FuelSourcesPage'
import { FuelSourceFormPage } from './pages/FuelSourceFormPage'
import { PlansPage } from './pages/PlansPage'
import { PlanFormPage } from './pages/PlanFormPage'
import { WeightPage } from './pages/WeightPage'
import { GoalsPage } from './pages/GoalsPage'
import { ReviewPage } from './pages/ReviewPage'
import { UsersPage } from './pages/UsersPage'
import { LibraryPage } from './pages/LibraryPage'
import { YouPage } from './pages/YouPage'
import { AdminPage } from './pages/AdminPage'
import { AboutPage } from './pages/AboutPage'
import { HowItWorksPage } from './pages/HowItWorksPage'
import { SharingPage } from './pages/SharingPage'
import { InvitePage } from './pages/InvitePage'

export default function App() {
  const { signedIn, profile, loading } = useAuth()

  // While we're still determining whether a session exists (e.g. MSAL hydrating on load), show a spinner
  // rather than flashing the login page.
  if (loading && !signedIn) {
    return <div className="center-screen"><p className="muted">Loading…</p></div>
  }

  // Not signed in → login. A shared-invitation link is still reachable so a recipient who isn't signed in
  // yet lands on the invitation (where they can sign in, create an account, or decline) instead of a bare
  // login page that has lost the link.
  if (!signedIn) {
    return (
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route path="/invite/:token" element={<InvitePage />} />
        <Route path="*" element={<Navigate to="/login" replace />} />
      </Routes>
    )
  }

  // Signed in but profile still loading.
  if (loading || !profile) {
    return <div className="center-screen"><p className="muted">Loading…</p></div>
  }

  // Signed in but hasn't finished the welcome flow → onboarding. A shared-invitation link still works here
  // so a brand-new recipient can accept/reject it before (or instead of) completing their own onboarding.
  if (!profile.hasCompletedOnboarding) {
    return (
      <Routes>
        <Route path="/invite/:token" element={<InvitePage />} />
        <Route path="/welcome" element={<WelcomePage />} />
        <Route path="*" element={<Navigate to="/welcome" replace />} />
      </Routes>
    )
  }

  // Authenticated app.
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<TodayPage />} />
        <Route path="/ingredients" element={<IngredientsPage />} />
        <Route path="/ingredients/review-queue" element={<IngredientReviewQueuePage />} />
        <Route path="/ingredients/:id" element={<IngredientFormPage />} />
        <Route path="/recipes" element={<RecipesPage />} />
        <Route path="/recipes/review-queue" element={<RecipeReviewQueuePage />} />
        <Route path="/recipes/:id" element={<RecipeFormPage />} />
        <Route path="/fuel-sources" element={<FuelSourcesPage />} />
        <Route path="/fuel-sources/:id" element={<FuelSourceFormPage />} />
        {/* Old client-side routes renamed to /fuel-sources; keep redirects so existing links work. */}
        <Route path="/meals" element={<Navigate to="/fuel-sources" replace />} />
        <Route path="/meals/:id" element={<RedirectMealToFuelSource />} />
        <Route path="/plans" element={<PlansPage />} />
        <Route path="/plans/:id" element={<PlanFormPage />} />
        {/* The Log merged into Today; keep old links working (incl. the ?add deep link). */}
        <Route path="/log" element={<RedirectHome />} />
        <Route path="/weight" element={<WeightPage />} />
        <Route path="/goals" element={<GoalsPage />} />
        <Route path="/review" element={<ReviewPage />} />
        <Route path="/library" element={<LibraryPage />} />
        <Route path="/you" element={<YouPage />} />
        <Route path="/sharing" element={<SharingPage />} />
        <Route path="/how-it-works" element={<HowItWorksPage />} />
        <Route path="/about" element={<AboutPage />} />
        {/* The old profile route lives inside the You tab now. */}
        <Route path="/profile" element={<Navigate to="/you" replace />} />
        <Route path="/users" element={<UsersPage />} />
        <Route path="/admin" element={<AdminPage />} />
        {/* Opening a shared-invitation link as an already-onboarded user. */}
        <Route path="/invite/:token" element={<InvitePage />} />
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  )
}

/** Redirect to Today, preserving the query string (the LOG sheet's ?add=… deep link). */
function RedirectHome() {
  const { search } = useLocation()
  return <Navigate to={{ pathname: '/', search }} replace />
}

/** Redirect the old /meals/:id route to its renamed /fuel-sources/:id equivalent. */
function RedirectMealToFuelSource() {
  const { id } = useParams<{ id: string }>()
  return <Navigate to={`/fuel-sources/${id}`} replace />
}
