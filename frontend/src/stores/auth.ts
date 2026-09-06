import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { authApi } from '@/api/services'
import { readStoredAuth, setAuthLostHandler, writeStoredAuth } from '@/api/client'
import type { CurrentUser } from '@/types/api'

export const ROLES = {
  admin: 'Admin',
  manager: 'Manager',
  agent: 'Agent',
  customer: 'Customer',
} as const

export const useAuthStore = defineStore('auth', () => {
  const user = ref<CurrentUser | null>(null)
  const initialising = ref(false)

  const isAuthenticated = computed(() => user.value !== null)
  const roles = computed(() => user.value?.roles ?? [])

  const permissions = computed(() => user.value?.permissions ?? [])

  const isStaff = computed(() =>
    roles.value.some((r) => r === ROLES.admin || r === ROLES.manager || r === ROLES.agent),
  )
  const isSupervisor = computed(() => roles.value.some((r) => r === ROLES.admin || r === ROLES.manager))

  function hasRole(...wanted: string[]): boolean {
    return wanted.some((r) => roles.value.includes(r))
  }

  /**
   * The authorization check the UI should use. Hiding a control the caller lacks permission
   * for is a usability measure only: the API enforces the same permission independently, so
   * a hidden button is never the security boundary.
   */
  function hasPermission(...wanted: string[]): boolean {
    return wanted.every((p) => permissions.value.includes(p))
  }

  function hasAnyPermission(...wanted: string[]): boolean {
    return wanted.some((p) => permissions.value.includes(p))
  }

  async function login(email: string, password: string) {
    const response = await authApi.login(email, password)

    writeStoredAuth({
      accessToken: response.accessToken,
      refreshToken: response.refreshToken,
      expiresAt: response.expiresAt,
    })

    user.value = response.user
    return response.user
  }

  function logout() {
    writeStoredAuth(null)
    user.value = null
  }

  /**
   * Rehydrates the session on a hard reload. The stored token is only a hint — the
   * server has the final say, so an expired or revoked token resolves to signed-out.
   */
  async function restore(): Promise<boolean> {
    if (user.value) return true
    if (!readStoredAuth()) return false

    initialising.value = true
    try {
      user.value = await authApi.me()
      return true
    } catch {
      logout()
      return false
    } finally {
      initialising.value = false
    }
  }

  // The axios layer clears storage on a failed refresh; mirror that into the store so
  // the router guard sees a signed-out state on the next navigation.
  setAuthLostHandler(() => {
    user.value = null
  })

  return {
    user,
    initialising,
    isAuthenticated,
    roles,
    permissions,
    isStaff,
    isSupervisor,
    hasRole,
    hasPermission,
    hasAnyPermission,
    login,
    logout,
    restore,
  }
})
