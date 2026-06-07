// Preset identities for local development. Picking one is the dev equivalent of signing in with
// Google/Microsoft/Facebook. The first user to be provisioned by the API becomes the App Admin,
// so whichever of these you sign in with first will own the admin role.

export interface DevUser {
  subject: string
  name: string
  email: string
  avatar?: string
}

export const DEV_USERS: DevUser[] = [
  { subject: 'dev-ada', name: 'Ada Lovelace', email: 'ada@dev.local' },
  { subject: 'dev-grace', name: 'Grace Hopper', email: 'grace@dev.local' },
  { subject: 'dev-alan', name: 'Alan Turing', email: 'alan@dev.local' },
]

const STORAGE_KEY = 'gf.devUser'

export function getStoredDevUser(): DevUser | null {
  const raw = localStorage.getItem(STORAGE_KEY)
  if (!raw) return null
  try {
    return JSON.parse(raw) as DevUser
  } catch {
    return null
  }
}

export function setStoredDevUser(user: DevUser | null): void {
  if (user) localStorage.setItem(STORAGE_KEY, JSON.stringify(user))
  else localStorage.removeItem(STORAGE_KEY)
}
