import { useEffect } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { IconUsers, IconRecipe, IconIngredient } from '../components/icons'

/**
 * Admin tools, split out of the You tab into a dedicated top-level page.
 * The "Admin view" toggle stays on /you; the actual tools live here and are
 * only reachable while acting as an admin.
 */
export function AdminPage() {
  const { profile } = useAuth()
  const navigate = useNavigate()

  // Non-admins (or admins with admin view off) shouldn't be here; bounce them.
  useEffect(() => {
    if (profile && !profile.isActingAsAdmin) navigate('/ingredients', { replace: true })
  }, [profile, navigate])

  return (
    <div className="stack">
      <h1>Admin</h1>

      <div className="list">
        <AdminTool
          to="/users"
          icon={<IconUsers size={22} />}
          title="Manage users"
          description="View members and promote or demote the App Admin role."
        />
        <AdminTool
          to="/recipes/review-queue"
          icon={<IconRecipe size={22} />}
          title="Recipe review queue"
          description="Approve or reject recipes submitted for the public library."
        />
        <AdminTool
          to="/ingredients/review-queue"
          icon={<IconIngredient size={22} />}
          title="Ingredient review queue"
          description="Approve or reject ingredients submitted for the public library."
        />
      </div>
    </div>
  )
}

function AdminTool({
  to, icon, title, description,
}: {
  to: string
  icon: React.ReactNode
  title: string
  description: string
}) {
  return (
    <Link to={to} className="card ingredient-item" style={{ textDecoration: 'none', color: 'inherit' }}>
      <div className="ingredient-thumb">{icon}</div>
      <div style={{ flex: 1, minWidth: 0 }}>
        <strong>{title}</strong>
        <div className="small muted">{description}</div>
      </div>
    </Link>
  )
}
