import axios, {
  AxiosError,
  type AxiosInstance,
  type InternalAxiosRequestConfig,
} from 'axios'
import type { AuthResponse, ProblemDetails } from '@/types/api'
import { i18n } from '@/i18n'

const STORAGE_KEY = 'crm.auth'

export interface StoredAuth {
  accessToken: string
  refreshToken: string
  expiresAt: string
}

export function readStoredAuth(): StoredAuth | null {
  const raw = localStorage.getItem(STORAGE_KEY)
  if (!raw) return null

  try {
    return JSON.parse(raw) as StoredAuth
  } catch {
    // A corrupt entry would otherwise wedge every future request.
    localStorage.removeItem(STORAGE_KEY)
    return null
  }
}

export function writeStoredAuth(auth: StoredAuth | null): void {
  if (auth) localStorage.setItem(STORAGE_KEY, JSON.stringify(auth))
  else localStorage.removeItem(STORAGE_KEY)
}

export const http: AxiosInstance = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '/api',
  headers: { 'Content-Type': 'application/json' },
})

http.interceptors.request.use((config: InternalAxiosRequestConfig) => {
  const auth = readStoredAuth()
  if (auth?.accessToken) {
    config.headers.Authorization = `Bearer ${auth.accessToken}`
  }

  // Lets the API pick the response culture (area 12).
  const locale = localStorage.getItem('crm.locale') ?? 'ar'
  config.headers['Accept-Language'] = locale === 'ar' ? 'ar-SA' : 'en-US'

  return config
})

/** Callback the auth store registers so a failed refresh can clear session state. */
let onAuthLost: (() => void) | null = null
export function setAuthLostHandler(handler: () => void): void {
  onAuthLost = handler
}

// A single in-flight refresh shared by every request that 401s at the same moment;
// without this, a page issuing five parallel calls would fire five refreshes and
// invalidate its own rotating refresh token.
let refreshInFlight: Promise<string> | null = null

async function refreshAccessToken(): Promise<string> {
  const stored = readStoredAuth()
  if (!stored?.refreshToken) throw new Error('No refresh token available.')

  // A bare axios call, so this request does not recurse through the interceptors.
  const { data } = await axios.post<AuthResponse>(
    `${http.defaults.baseURL}/auth/refresh`,
    { refreshToken: stored.refreshToken },
    { headers: { 'Content-Type': 'application/json' } },
  )

  writeStoredAuth({
    accessToken: data.accessToken,
    refreshToken: data.refreshToken,
    expiresAt: data.expiresAt,
  })

  return data.accessToken
}

http.interceptors.response.use(
  (response) => response,
  async (error: AxiosError<ProblemDetails>) => {
    const original = error.config as (InternalAxiosRequestConfig & { _retried?: boolean }) | undefined

    const isAuthEndpoint = original?.url?.includes('/auth/login') || original?.url?.includes('/auth/refresh')

    if (error.response?.status === 401 && original && !original._retried && !isAuthEndpoint) {
      original._retried = true

      try {
        refreshInFlight ??= refreshAccessToken().finally(() => {
          refreshInFlight = null
        })

        const token = await refreshInFlight
        original.headers.Authorization = `Bearer ${token}`
        return http(original)
      } catch {
        writeStoredAuth(null)
        onAuthLost?.()
      }
    }

    return Promise.reject(error)
  },
)

/** Extracts a display message from an RFC 7807 response, falling back sensibly. */
/**
 * Turns a failed request into a message for the user.
 *
 * The server sends a language-neutral `errorCode` alongside its English `detail`. When we
 * have a translation for the code we use it, so the message follows the user's locale
 * rather than the server's. Codes we have not translated yet fall back to `detail`, which
 * is readable but English — that is the intended interim state while codes are added.
 */
export function problemMessage(error: unknown, fallback: string): string {
  if (!axios.isAxiosError<ProblemDetails>(error)) return fallback

  const problem = error.response?.data
  const translated = translateErrorCode(problem?.errorCode)
  if (translated) return translated

  if (problem?.errors) {
    const first = Object.values(problem.errors).flat()[0]
    if (first) return first
  }

  return problem?.detail ?? problem?.title ?? error.message ?? fallback
}

function translateErrorCode(code: string | undefined): string | null {
  if (!code) return null

  const key = `apiError.${code}`
  // te() is checked against the active locale so a key present only in the fallback file
  // does not silently render English inside an Arabic screen.
  if (!i18n.global.te(key, i18n.global.locale.value)) return null

  return i18n.global.t(key)
}
