<script setup lang="ts">
import { computed, ref } from 'vue'
import { RouterView, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import Button from 'primevue/button'
import Menu from 'primevue/menu'
import Avatar from 'primevue/avatar'
import NotificationBell from '@/components/NotificationBell.vue'
import Drawer from 'primevue/drawer'
import { useAuthStore } from '@/stores/auth'
import { PERMISSIONS } from '@/types/api'
import { useUiStore } from '@/stores/ui'

const { t } = useI18n()
const router = useRouter()
const auth = useAuthStore()
const ui = useUiStore()

const mobileNavOpen = ref(false)
const userMenu = ref<InstanceType<typeof Menu> | null>(null)

const navItems = computed(() => {
  const items = [
    { label: t('nav.dashboard'), icon: 'pi pi-home', to: { name: 'dashboard' } },
    { label: t('nav.tickets'), icon: 'pi pi-ticket', to: { name: 'tickets' } },
    { label: t('nav.customers'), icon: 'pi pi-users', to: { name: 'customers' } },
  ]

  // Hidden when the permission is absent. Usability only — the route guard and the API
  // enforce the same permission independently.
  if (auth.hasPermission(PERMISSIONS.usersView)) {
    items.push({ label: t('nav.users'), icon: 'pi pi-user-edit', to: { name: 'users' } })
  }

  if (auth.hasPermission(PERMISSIONS.lookupsManage)) {
    items.push({ label: t('nav.categories'), icon: 'pi pi-sitemap', to: { name: 'ticket-categories' } })
  }

  if (auth.hasPermission(PERMISSIONS.slaView)) {
    items.push({ label: t('nav.sla'), icon: 'pi pi-stopwatch', to: { name: 'sla-admin' } })
  }

  if (auth.hasPermission(PERMISSIONS.auditLogsView)) {
    items.push({ label: t('nav.auditLogs'), icon: 'pi pi-history', to: { name: 'audit-logs' } })
  }

  if (auth.hasPermission(PERMISSIONS.systemConfigView)) {
    items.push({ label: t('nav.settings'), icon: 'pi pi-cog', to: { name: 'system-config' } })
  }

  return items
})

const displayName = computed(() => ui.localizedFullName(auth.user))

const initials = computed(() => {
  const source = auth.user?.fullNameEn || auth.user?.email || '?'
  return source
    .split(/\s+/)
    .slice(0, 2)
    .map((part) => part.charAt(0).toUpperCase())
    .join('')
})

const userMenuItems = computed(() => [
  {
    label: ui.isArabic ? t('app.english') : t('app.arabic'),
    icon: 'pi pi-globe',
    command: () => ui.toggleLocale(),
  },
  {
    label: ui.theme === 'dark' ? t('app.light') : t('app.dark'),
    icon: ui.theme === 'dark' ? 'pi pi-sun' : 'pi pi-moon',
    command: () => ui.toggleTheme(),
  },
  { separator: true },
  {
    label: t('account.changePassword'),
    icon: 'pi pi-key',
    command: () => router.push({ name: 'change-password' }),
  },
  {
    label: t('app.logout'),
    icon: 'pi pi-sign-out',
    command: () => {
      auth.logout()
      router.push({ name: 'login' })
    },
  },
])

function navigate(to: { name: string }) {
  mobileNavOpen.value = false
  router.push(to)
}
</script>

<template>
  <div class="flex h-full flex-col bg-surface-50 dark:bg-surface-950">
    <!-- Top bar -->
    <header
      class="flex shrink-0 items-center gap-3 border-b border-surface-200 bg-surface-0 px-4 dark:border-surface-800 dark:bg-surface-900"
      :style="{ height: 'var(--app-topbar-height)' }"
    >
      <Button
        icon="pi pi-bars"
        text
        rounded
        class="lg:hidden"
        :aria-label="t('nav.dashboard')"
        @click="mobileNavOpen = true"
      />

      <div class="flex items-center gap-2">
        <i class="pi pi-headphones text-xl text-primary" />
        <span class="text-lg font-semibold">{{ t('app.name') }}</span>
      </div>

      <div class="ms-auto flex items-center gap-2">
        <NotificationBell />

        <Button
          text
          rounded
          :label="ui.isArabic ? 'EN' : 'ع'"
          class="font-semibold"
          :aria-label="t('app.language')"
          @click="ui.toggleLocale()"
        />
        <Button
          text
          rounded
          :icon="ui.theme === 'dark' ? 'pi pi-sun' : 'pi pi-moon'"
          :aria-label="t('app.theme')"
          @click="ui.toggleTheme()"
        />

        <button
          type="button"
          class="flex items-center gap-2 rounded-full p-1 transition hover:bg-surface-100 dark:hover:bg-surface-800"
          @click="userMenu?.toggle($event)"
        >
          <Avatar :label="initials" shape="circle" class="bg-primary text-primary-contrast" />
          <span class="hidden text-sm font-medium sm:inline">{{ displayName }}</span>
        </button>
        <Menu ref="userMenu" :model="userMenuItems" :popup="true" />
      </div>
    </header>

    <div class="flex min-h-0 flex-1">
      <!-- Sidebar: permanent on desktop, drawer on mobile -->
      <nav
        class="hidden shrink-0 border-e border-surface-200 bg-surface-0 p-3 lg:block dark:border-surface-800 dark:bg-surface-900"
        :style="{ width: 'var(--app-sidebar-width)' }"
      >
        <ul class="flex flex-col gap-1">
          <li v-for="item in navItems" :key="item.to.name">
            <RouterLink
              v-slot="{ isActive, navigate: go }"
              :to="item.to"
              custom
            >
              <button
                type="button"
                class="flex w-full items-center gap-3 rounded-lg px-3 py-2 text-start text-sm transition"
                :class="
                  isActive
                    ? 'bg-primary/10 font-semibold text-primary'
                    : 'text-surface-700 hover:bg-surface-100 dark:text-surface-300 dark:hover:bg-surface-800'
                "
                @click="go"
              >
                <i :class="item.icon" />
                <span>{{ item.label }}</span>
              </button>
            </RouterLink>
          </li>
        </ul>
      </nav>

      <Drawer
        v-model:visible="mobileNavOpen"
        :position="ui.isArabic ? 'right' : 'left'"
        :header="t('app.name')"
        class="lg:hidden"
      >
        <ul class="flex flex-col gap-1">
          <li v-for="item in navItems" :key="item.to.name">
            <button
              type="button"
              class="flex w-full items-center gap-3 rounded-lg px-3 py-2 text-start text-sm hover:bg-surface-100 dark:hover:bg-surface-800"
              @click="navigate(item.to)"
            >
              <i :class="item.icon" />
              <span>{{ item.label }}</span>
            </button>
          </li>
        </ul>
      </Drawer>

      <main class="app-scroll min-w-0 flex-1 p-4 lg:p-6">
        <RouterView />
      </main>
    </div>
  </div>
</template>
