<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import { problemMessage } from '@/api/client'

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const ui = useUiStore()

const email = ref('')
const password = ref('')
const submitting = ref(false)
const error = ref<string | null>(null)

async function submit() {
  if (submitting.value) return

  error.value = null
  submitting.value = true

  try {
    await auth.login(email.value.trim(), password.value)
    const redirect = route.query.redirect as string | undefined
    await router.push(redirect ?? { name: 'dashboard' })
  } catch (e) {
    // A 403 here always means bad credentials; anything else is worth surfacing verbatim.
    error.value = problemMessage(e, t('auth.invalidCredentials'))
  } finally {
    submitting.value = false
  }
}
</script>

<template>
  <div class="flex min-h-screen items-center justify-center bg-surface-100 p-4 dark:bg-surface-950">
    <div class="w-full max-w-md">
      <div class="mb-6 flex items-center justify-between">
        <div class="flex items-center gap-2">
          <i class="pi pi-headphones text-2xl text-primary" />
          <span class="text-xl font-semibold">{{ t('app.name') }}</span>
        </div>

        <div class="flex items-center gap-1">
          <Button
            text
            rounded
            size="small"
            :label="ui.isArabic ? 'EN' : 'ع'"
            class="font-semibold"
            :aria-label="t('app.language')"
            @click="ui.toggleLocale()"
          />
          <Button
            text
            rounded
            size="small"
            :icon="ui.theme === 'dark' ? 'pi pi-sun' : 'pi pi-moon'"
            :aria-label="t('app.theme')"
            @click="ui.toggleTheme()"
          />
        </div>
      </div>

      <div
        class="rounded-xl border border-surface-200 bg-surface-0 p-6 shadow-sm dark:border-surface-800 dark:bg-surface-900"
      >
        <h1 class="text-lg font-semibold">{{ t('auth.signIn') }}</h1>
        <p class="mt-1 text-sm text-surface-500 dark:text-surface-400">
          {{ t('auth.signInSubtitle') }}
        </p>

        <form class="mt-6 flex flex-col gap-4" @submit.prevent="submit">
          <div class="flex flex-col gap-1.5">
            <label for="email" class="text-sm font-medium">{{ t('auth.email') }}</label>
            <InputText
              id="email"
              v-model="email"
              type="email"
              autocomplete="username"
              required
              dir="ltr"
              class="w-full"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label for="password" class="text-sm font-medium">{{ t('auth.password') }}</label>
            <Password
              id="password"
              v-model="password"
              :feedback="false"
              toggle-mask
              autocomplete="current-password"
              required
              input-class="w-full"
              class="w-full"
              :input-props="{ dir: 'ltr' }"
            />
          </div>

          <Message v-if="error" severity="error" :closable="false" class="text-sm">
            {{ error }}
          </Message>

          <Button
            type="submit"
            :label="submitting ? t('auth.signingIn') : t('auth.signIn')"
            :loading="submitting"
            :disabled="!email || !password"
            class="w-full"
          />
        </form>
      </div>
    </div>
  </div>
</template>
