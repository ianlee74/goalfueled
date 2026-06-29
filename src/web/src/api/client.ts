import { authMode } from '../auth/authConfig'
import { getStoredDevUser } from '../auth/devUsers'
import { acquireToken } from '../auth/msal'
import { getActiveAccountId } from '../lib/activeAccount'

// Base URL for the API. Empty (the default) means same-origin `/api/...` — correct for local dev (Vite
// proxies /api to the Functions host) and for a Static Web App that proxies /api to a linked backend.
// Set VITE_API_BASE_URL to the Function App's absolute origin when the SPA must call it cross-origin
// (e.g. an SWA Free tier that can't link a backend); CORS on the API must allow the SPA origin.
const API_BASE = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

/**
 * Builds auth headers for the current request. In "entra" mode it attaches an
 * `Authorization: Bearer <token>` header (token acquired via MSAL); in local "dev" mode it attaches
 * the spoofable X-Dev-* headers. In both modes it forwards the optional act-as header.
 */
async function authHeaders(): Promise<Record<string, string>> {
  const headers: Record<string, string> = {}

  if (authMode === 'entra') {
    const token = await acquireToken()
    if (token) headers['Authorization'] = `Bearer ${token}`
  } else {
    const user = getStoredDevUser()
    if (user) {
      headers['X-Dev-Subject'] = user.subject
      headers['X-Dev-Email'] = user.email
      headers['X-Dev-Name'] = user.name
      if (user.avatar) headers['X-Dev-Avatar'] = user.avatar
    }
  }

  // Account switching: when viewing an account shared with us, ask the API to act as that owner. It only
  // honors this if we hold an active grant, so an unauthorized value safely falls back to our own account.
  const actAs = getActiveAccountId()
  if (actAs) headers['X-Act-As-User'] = actAs
  return headers
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(`${API_BASE}/api${path}`, {
    method,
    headers: {
      ...(await authHeaders()),
      ...(body !== undefined ? { 'Content-Type': 'application/json' } : {}),
    },
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })

  if (res.status === 204) return undefined as T

  const text = await res.text()
  const data = text ? JSON.parse(text) : undefined

  if (!res.ok) {
    const detail = (data && (data.detail as string)) || res.statusText
    throw new ApiError(res.status, detail)
  }
  return data as T
}

/**
 * Uploads a file via multipart/form-data. Unlike `request()` we deliberately do NOT set a
 * Content-Type header so the browser can add the multipart boundary; auth headers are still sent.
 */
async function upload<T>(path: string, file: File, field = 'file'): Promise<T> {
  const form = new FormData()
  form.append(field, file)

  const res = await fetch(`${API_BASE}/api${path}`, {
    method: 'POST',
    headers: { ...(await authHeaders()) },
    body: form,
  })

  const text = await res.text()
  const data = text ? JSON.parse(text) : undefined

  if (!res.ok) {
    const detail = (data && (data.detail as string)) || res.statusText
    throw new ApiError(res.status, detail)
  }
  return data as T
}

export const api = {
  get: <T>(path: string) => request<T>('GET', path),
  post: <T>(path: string, body?: unknown) => request<T>('POST', path, body),
  put: <T>(path: string, body?: unknown) => request<T>('PUT', path, body),
  del: (path: string) => request<void>('DELETE', path),
  upload: <T>(path: string, file: File, field?: string) => upload<T>(path, file, field),
}
