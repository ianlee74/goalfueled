import { getStoredDevUser } from '../auth/devUsers'

export class ApiError extends Error {
  status: number
  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

/**
 * Builds auth headers for the current request. Today this attaches the dev-login headers; when real
 * social auth is added, swap this for an `Authorization: Bearer <token>` header — nothing else in
 * the app needs to change.
 */
function authHeaders(): Record<string, string> {
  const user = getStoredDevUser()
  if (!user) return {}
  const headers: Record<string, string> = {
    'X-Dev-Subject': user.subject,
    'X-Dev-Email': user.email,
    'X-Dev-Name': user.name,
  }
  if (user.avatar) headers['X-Dev-Avatar'] = user.avatar
  return headers
}

async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(`/api${path}`, {
    method,
    headers: {
      ...authHeaders(),
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

  const res = await fetch(`/api${path}`, {
    method: 'POST',
    headers: { ...authHeaders() },
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
