<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import Menu from 'primevue/menu'
import { ref } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import { useBrandingStore } from '@/stores/branding'

const { t } = useI18n()
const router = useRouter()
const auth = useAuthStore()
const ui = useUiStore()
const branding = useBrandingStore()

const userMenu = ref()

/**
 * Deliberately sparse next to the staff layout. A customer needs their requests, help, and a
 * way out — every extra affordance here is a surface that has to be proven safe.
 */
const nav = computed(() => [
  { label: t('portal.myRequests'), icon: 'pi pi-inbox', to: { name: 'portal-tickets' } },
  { label: t('portal.help'), icon: 'pi pi-question-circle', to: { name: 'portal-help' } },
])

const userItems = computed(() => [
  {
    label: t('auth.logout'),
    icon: 'pi pi-sign-out',
    command: async () => {
      await auth.logout()
      await router.push({ name: 'login' })
    },
  },
])

const displayName = computed(() => ui.localizedFullName(auth.user))
</script>

<template>
  <div class="min-h-screen bg-surface-50 dark:bg-surface-950">
    <header class="border-b border-surface-200 bg-surface-0 dark:border-surface-700 dark:bg-surface-900">
      <div class="mx-auto flex max-w-5xl flex-wrap items-center gap-3 px-4 py-3">
        <RouterLink :to="{ name: 'portal-tickets' }" class="flex items-center gap-2">
          <i class="pi pi-headphones text-xl text-primary" />
          <span class="text-lg font-semibold">
            {{ ui.isArabic ? branding.branding?.companyNameAr : branding.branding?.companyNameEn }}
          </span>
        </RouterLink>

        <nav class="flex items-center gap-1">
          <RouterLink
            v-for="item in nav"
            :key="item.label"
            :to="item.to"
            class="rounded px-3 py-2 text-sm hover:bg-surface-100 dark:hover:bg-surface-800"
            active-class="text-primary font-medium"
          >
            <i :class="item.icon" class="me-1" />
            {{ item.label }}
          </RouterLink>
        </nav>

        <div class="ms-auto flex items-center gap-1">
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

          <Button text rounded icon="pi pi-user" :label="displayName" @click="userMenu?.toggle($event)" />
          <Menu ref="userMenu" :model="userItems" popup />
        </div>
      </div>
    </header>

    <main class="mx-auto max-w-5xl px-4 py-6">
      <RouterView />
    </main>
  </div>
</template>
