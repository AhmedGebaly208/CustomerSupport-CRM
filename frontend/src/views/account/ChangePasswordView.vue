<script setup lang="ts">
import { ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Password from 'primevue/password'
import Button from 'primevue/button'
import Message from 'primevue/message'
import PageHeader from '@/components/PageHeader.vue'
import { authApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useAuthStore } from '@/stores/auth'

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const auth = useAuthStore()

const currentPassword = ref('')
const newPassword = ref('')
const confirmPassword = ref('')
const saving = ref(false)
const error = ref<string | null>(null)

async function submit() {
  if (saving.value) return

  error.value = null

  if (newPassword.value !== confirmPassword.value) {
    error.value = t('account.passwordMismatch')
    return
  }

  saving.value = true
  try {
    await authApi.changePassword({
      currentPassword: currentPassword.value,
      newPassword: newPassword.value,
    })

    // The server revoked the refresh token, so this session cannot be renewed.
    // Sign out locally and send the user back through login rather than letting
    // them hit a confusing 403 on the next refresh.
    toast.add({ severity: 'success', summary: t('account.passwordChanged'), life: 5000 })
    auth.logout()
    await router.push({ name: 'login' })
  } catch (e) {
    error.value = problemMessage(e, t('error.saveFailed'))
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="mx-auto max-w-lg">
    <PageHeader :title="t('account.changePassword')" :subtitle="t('account.changePasswordHint')" />

    <form
      class="flex flex-col gap-4 rounded-xl border border-surface-200 bg-surface-0 p-4 dark:border-surface-800 dark:bg-surface-900"
      @submit.prevent="submit"
    >
      <div class="flex flex-col gap-1.5">
        <label class="text-sm font-medium">{{ t('account.currentPassword') }}</label>
        <Password
          v-model="currentPassword"
          :feedback="false"
          toggle-mask
          required
          class="w-full"
          input-class="w-full"
          :input-props="{ dir: 'ltr', autocomplete: 'current-password' }"
        />
      </div>

      <div class="flex flex-col gap-1.5">
        <label class="text-sm font-medium">{{ t('account.newPassword') }}</label>
        <Password
          v-model="newPassword"
          toggle-mask
          required
          class="w-full"
          input-class="w-full"
          :input-props="{ dir: 'ltr', autocomplete: 'new-password' }"
        />
        <small class="text-surface-500 dark:text-surface-400">{{ t('user.passwordPolicy') }}</small>
      </div>

      <div class="flex flex-col gap-1.5">
        <label class="text-sm font-medium">{{ t('account.confirmPassword') }}</label>
        <Password
          v-model="confirmPassword"
          :feedback="false"
          toggle-mask
          required
          class="w-full"
          input-class="w-full"
          :input-props="{ dir: 'ltr', autocomplete: 'new-password' }"
        />
      </div>

      <Message v-if="error" severity="error" :closable="false" class="text-sm">{{ error }}</Message>

      <div class="flex justify-end gap-2">
        <Button :label="t('app.cancel')" outlined size="small" @click="router.back()" />
        <Button
          type="submit"
          :label="t('app.save')"
          :loading="saving"
          :disabled="!currentPassword || !newPassword || !confirmPassword"
          size="small"
        />
      </div>
    </form>
  </div>
</template>
