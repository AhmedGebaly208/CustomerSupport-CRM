<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Select from 'primevue/select'
import Button from 'primevue/button'
import ToggleSwitch from 'primevue/toggleswitch'
import Checkbox from 'primevue/checkbox'
import Message from 'primevue/message'
import PageHeader from '@/components/PageHeader.vue'
import { customersApi, lookupsApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { ContactType, type Lookup, type SaveCustomerContact, type SaveCustomerRequest } from '@/types/api'

const props = defineProps<{ id?: string }>()

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const ui = useUiStore()

const isEdit = !!props.id

const form = ref<SaveCustomerRequest>({
  fullNameAr: '',
  fullNameEn: '',
  email: null,
  phone: null,
  whatsAppNumber: null,
  companyName: null,
  nationalId: null,
  address: null,
  preferredLanguage: 'ar',
  departmentId: null,
  branchId: null,
  isActive: true,
  contacts: [],
})

const departments = ref<Lookup[]>([])
const branches = ref<Lookup[]>([])
const loading = ref(isEdit)
const saving = ref(false)
const error = ref<string | null>(null)

const contactTypeOptions = Object.entries(ContactType)
  .filter(([, v]) => typeof v === 'number')
  .map(([name, value]) => ({ label: t(`contactType.${name}`), value: value as ContactType }))

const languageOptions = [
  { label: t('app.arabic'), value: 'ar' },
  { label: t('app.english'), value: 'en' },
]

function addContact() {
  form.value.contacts = [
    ...(form.value.contacts ?? []),
    { type: ContactType.Mobile, value: '', label: null, isPrimary: false } satisfies SaveCustomerContact,
  ]
}

function removeContact(index: number) {
  form.value.contacts = (form.value.contacts ?? []).filter((_, i) => i !== index)
}

async function save() {
  if (saving.value) return

  error.value = null
  saving.value = true

  try {
    const payload: SaveCustomerRequest = {
      ...form.value,
      // Empty strings are not "no value" to the API's validators; normalise them here.
      email: form.value.email?.trim() || null,
      phone: form.value.phone?.trim() || null,
      whatsAppNumber: form.value.whatsAppNumber?.trim() || null,
      companyName: form.value.companyName?.trim() || null,
      nationalId: form.value.nationalId?.trim() || null,
      address: form.value.address?.trim() || null,
      contacts: (form.value.contacts ?? []).filter((c) => c.value.trim().length > 0),
    }

    const saved = isEdit
      ? await customersApi.update(props.id!, payload)
      : await customersApi.create(payload)

    toast.add({
      severity: 'success',
      summary: isEdit ? t('customer.updated') : t('customer.created'),
      life: 3000,
    })

    await router.push({ name: 'customer-detail', params: { id: saved.id } })
  } catch (e) {
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
    const existing = await customersApi.getById(props.id!)
    form.value = {
      fullNameAr: existing.fullNameAr,
      fullNameEn: existing.fullNameEn,
      email: existing.email,
      phone: existing.phone,
      whatsAppNumber: existing.whatsAppNumber,
      companyName: existing.companyName,
      nationalId: existing.nationalId,
      address: existing.address,
      preferredLanguage: existing.preferredLanguage,
      departmentId: existing.departmentId,
      branchId: existing.branchId,
      isActive: existing.isActive,
      contacts: existing.contacts.map((c) => ({
        type: c.type,
        value: c.value,
        label: c.label,
        isPrimary: c.isPrimary,
      })),
    }
  } catch (e) {
    error.value = problemMessage(e, t('error.loadFailed'))
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div class="mx-auto max-w-4xl">
    <PageHeader :title="isEdit ? t('customer.edit') : t('customer.new')">
      <template #actions>
        <Button :label="t('app.cancel')" outlined size="small" @click="router.back()" />
        <Button :label="t('app.save')" :loading="saving" size="small" @click="save" />
      </template>
    </PageHeader>

    <Message v-if="error" severity="error" :closable="false" class="mb-4">{{ error }}</Message>

    <form v-if="!loading" class="flex flex-col gap-6" @submit.prevent="save">
      <!-- Identity -->
      <section
        class="rounded-xl border border-surface-200 bg-surface-0 p-4 dark:border-surface-800 dark:bg-surface-900"
      >
        <h2 class="mb-4 font-semibold">{{ t('customer.details') }}</h2>

        <div class="grid gap-4 sm:grid-cols-2">
          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.nameAr') }} *</label>
            <InputText v-model="form.fullNameAr" required dir="rtl" class="w-full" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.nameEn') }} *</label>
            <InputText v-model="form.fullNameEn" required dir="ltr" class="w-full" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.email') }}</label>
            <InputText v-model="form.email" type="email" dir="ltr" class="w-full" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.phone') }}</label>
            <InputText v-model="form.phone" dir="ltr" class="w-full" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.whatsapp') }}</label>
            <InputText v-model="form.whatsAppNumber" dir="ltr" class="w-full" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.company') }}</label>
            <InputText v-model="form.companyName" class="w-full" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.nationalId') }}</label>
            <InputText v-model="form.nationalId" dir="ltr" class="w-full" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.preferredLanguage') }}</label>
            <Select
              v-model="form.preferredLanguage"
              :options="languageOptions"
              option-label="label"
              option-value="value"
              class="w-full"
            />
          </div>

          <div class="flex flex-col gap-1.5 sm:col-span-2">
            <label class="text-sm font-medium">{{ t('customer.address') }}</label>
            <Textarea v-model="form.address" rows="2" auto-resize class="w-full" />
          </div>
        </div>

        <p class="mt-3 text-xs text-surface-500 dark:text-surface-400">
          {{ t('customer.contactRequired') }}
        </p>
      </section>

      <!-- Routing -->
      <section
        class="rounded-xl border border-surface-200 bg-surface-0 p-4 dark:border-surface-800 dark:bg-surface-900"
      >
        <div class="grid gap-4 sm:grid-cols-3">
          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.department') }}</label>
            <Select
              v-model="form.departmentId"
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
              v-model="form.branchId"
              :options="branches"
              :option-label="(b: Lookup) => ui.localized(b)"
              option-value="id"
              show-clear
              class="w-full"
            />
          </div>

          <div v-if="isEdit" class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.status') }}</label>
            <div class="flex items-center gap-2 pt-2">
              <ToggleSwitch v-model="form.isActive" />
              <span class="text-sm">{{ form.isActive ? t('customer.active') : t('customer.inactive') }}</span>
            </div>
          </div>
        </div>
      </section>

      <!-- Extra contact details -->
      <section
        class="rounded-xl border border-surface-200 bg-surface-0 p-4 dark:border-surface-800 dark:bg-surface-900"
      >
        <div class="mb-3 flex items-center justify-between">
          <h2 class="font-semibold">{{ t('customer.contacts') }}</h2>
          <Button
            icon="pi pi-plus"
            :label="t('customer.addContact')"
            text
            size="small"
            @click="addContact"
          />
        </div>

        <div v-if="!form.contacts?.length" class="py-3 text-sm text-surface-500 dark:text-surface-400">
          {{ t('app.noData') }}
        </div>

        <div
          v-for="(contact, index) in form.contacts ?? []"
          :key="index"
          class="mb-2 grid items-end gap-2 sm:grid-cols-[10rem_1fr_10rem_auto_auto]"
        >
          <Select
            v-model="contact.type"
            :options="contactTypeOptions"
            option-label="label"
            option-value="value"
            class="w-full"
          />
          <InputText v-model="contact.value" :placeholder="t('customer.contactValue')" dir="ltr" class="w-full" />
          <InputText v-model="contact.label" :placeholder="t('customer.contactLabel')" class="w-full" />
          <label class="flex items-center gap-2 whitespace-nowrap text-sm">
            <Checkbox v-model="contact.isPrimary" binary />
            {{ t('customer.isPrimary') }}
          </label>
          <Button icon="pi pi-trash" severity="danger" text @click="removeContact(index)" />
        </div>
      </section>
    </form>
  </div>
</template>
