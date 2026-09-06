<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import InputText from 'primevue/inputtext'
import Password from 'primevue/password'
import Select from 'primevue/select'
import MultiSelect from 'primevue/multiselect'
import Button from 'primevue/button'
import Message from 'primevue/message'
import PageHeader from '@/components/PageHeader.vue'
import { lookupsApi, usersApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { ROLE_NAMES, type Lookup } from '@/types/api'

const props = defineProps<{ id?: string }>()

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const ui = useUiStore()

const isEdit = !!props.id

const email = ref('')
const password = ref('')
const fullNameAr = ref('')
const fullNameEn = ref('')
const preferredLanguage = ref('ar')
const departmentId = ref<string | null>(null)
const branchId = ref<string | null>(null)
const roles = ref<string[]>(['Agent'])

const departments = ref<Lookup[]>([])
const branches = ref<Lookup[]>([])

const loading = ref(isEdit)
const saving = ref(false)
const error = ref<string | null>(null)

const roleOptions = ROLE_NAMES.map((name) => ({ label: t(`role.${name}`), value: name }))

const languageOptions = [
  { label: t('app.arabic'), value: 'ar' },
  { label: t('app.english'), value: 'en' },
]

async function save() {
  if (saving.value) return

  error.value = null
  saving.value = true

  try {
    if (isEdit) {
      await usersApi.update(props.id!, {
        fullNameAr: fullNameAr.value.trim(),
        fullNameEn: fullNameEn.value.trim(),
        preferredLanguage: preferredLanguage.value,
        departmentId: departmentId.value,
        branchId: branchId.value,
        roles: roles.value,
      })
      toast.add({ severity: 'success', summary: t('user.updated'), life: 3000 })
    } else {
      await usersApi.create({
        email: email.value.trim(),
        password: password.value,
        fullNameAr: fullNameAr.value.trim(),
        fullNameEn: fullNameEn.value.trim(),
        preferredLanguage: preferredLanguage.value,
        departmentId: departmentId.value,
        branchId: branchId.value,
        roles: roles.value,
      })
      toast.add({ severity: 'success', summary: t('user.created'), life: 3000 })
    }

    await router.push({ name: 'users' })
  } catch (e) {
    // Surfaces the API's own reasons: duplicate email, password policy, last-admin guard.
    error.value = problemMessage(e, t('error.saveFailed'))
  } finally {
    saving.value = false
  }
}

onMounted(async () => {
  const [deps, brs] = await Promise.allSettled([lookupsApi.departments(), lookupsApi.branches()])
  if (deps.status === 'fulfilled') departments.value = deps.value
  if (brs.status === 'fulfilled') branches.value = brs.value

  if (!isEdit) return

  try {
    const user = await usersApi.get(props.id!)
    email.value = user.email
    fullNameAr.value = user.fullNameAr
    fullNameEn.value = user.fullNameEn
    preferredLanguage.value = user.preferredLanguage
    departmentId.value = user.departmentId
    branchId.value = user.branchId
    roles.value = [...user.roles]
  } catch (e) {
    error.value = problemMessage(e, t('error.loadFailed'))
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="mx-auto max-w-3xl">
    <PageHeader :title="isEdit ? t('user.edit') : t('user.new')">
      <template #actions>
        <Button :label="t('app.cancel')" outlined size="small" @click="router.back()" />
        <Button
          :label="t('app.save')"
          :loading="saving"
          :disabled="!fullNameAr || !fullNameEn || !roles.length || (!isEdit && (!email || !password))"
          size="small"
          @click="save"
        />
      </template>
    </PageHeader>

    <Message v-if="error" severity="error" :closable="false" class="mb-4">{{ error }}</Message>

    <form v-if="!loading" class="flex flex-col gap-6" @submit.prevent="save">
      <section
        class="rounded-xl border border-surface-200 bg-surface-0 p-4 dark:border-surface-800 dark:bg-surface-900"
      >
        <div class="grid gap-4 sm:grid-cols-2">
          <div class="flex flex-col gap-1.5 sm:col-span-2">
            <label class="text-sm font-medium">{{ t('auth.email') }} *</label>
            <InputText v-model="email" type="email" dir="ltr" :disabled="isEdit" required class="w-full" />
            <small v-if="isEdit" class="text-surface-500 dark:text-surface-400">
              {{ t('user.emailImmutable') }}
            </small>
          </div>

          <div v-if="!isEdit" class="flex flex-col gap-1.5 sm:col-span-2">
            <label class="text-sm font-medium">{{ t('auth.password') }} *</label>
            <Password
              v-model="password"
              toggle-mask
              :feedback="true"
              class="w-full"
              input-class="w-full"
              :input-props="{ dir: 'ltr', autocomplete: 'new-password' }"
            />
            <small class="text-surface-500 dark:text-surface-400">{{ t('user.passwordPolicy') }}</small>
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.nameAr') }} *</label>
            <InputText v-model="fullNameAr" dir="rtl" required class="w-full" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.nameEn') }} *</label>
            <InputText v-model="fullNameEn" dir="ltr" required class="w-full" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('user.roles') }} *</label>
            <MultiSelect
              v-model="roles"
              :options="roleOptions"
              option-label="label"
              option-value="value"
              display="chip"
              class="w-full"
            />
            <small class="text-surface-500 dark:text-surface-400">{{ t('user.rolesHint') }}</small>
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.preferredLanguage') }}</label>
            <Select
              v-model="preferredLanguage"
              :options="languageOptions"
              option-label="label"
              option-value="value"
              class="w-full"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.department') }}</label>
            <Select
              v-model="departmentId"
              :options="departments"
              :option-label="(d: Lookup) => ui.localized(d)"
              option-value="id"
              show-clear
              class="w-full"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.branch') }}</label>
            <Select
              v-model="branchId"
              :options="branches"
              :option-label="(b: Lookup) => ui.localized(b)"
              option-value="id"
              show-clear
              class="w-full"
            />
          </div>
        </div>
      </section>
    </form>
  </div>
</template>
