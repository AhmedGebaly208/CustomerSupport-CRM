<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Select from 'primevue/select'
import Message from 'primevue/message'
import { lookupsApi, portalApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { TicketPriority, type CategoryLookup, type CreatePortalTicketRequest } from '@/types/api'

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const ui = useUiStore()

const categories = ref<CategoryLookup[]>([])
const saving = ref(false)

const form = reactive<CreatePortalTicketRequest>({
  subject: '',
  description: '',
  priority: TicketPriority.Normal,
  categoryId: null,
})

/** Urgent is not offered. Priority is a desk judgement, and a picker that lets everyone
 *  choose the top of the queue stops meaning anything — the server clamps it too. */
const priorityOptions = computed(() =>
  [TicketPriority.Low, TicketPriority.Normal, TicketPriority.High].map((value) => ({
    value,
    label: t(`ticket.priority.${TicketPriority[value]}`),
  })),
)

const formError = computed(() => {
  if (!form.subject.trim()) return t('portal.error.subjectRequired')
  if (!form.description.trim()) return t('portal.error.descriptionRequired')
  return null
})

async function submit() {
  if (formError.value || saving.value) return

  saving.value = true
  try {
    const created = await portalApi.create({
      ...form,
      subject: form.subject.trim(),
      description: form.description.trim(),
    })

    toast.add({ severity: 'success', summary: t('portal.requestCreated'), life: 4000 })
    await router.push({ name: 'portal-ticket', params: { id: created.id } })
  } catch (e) {
    // The server refuses past an open-request cap; that message is worth reading in full.
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 8000 })
  } finally {
    saving.value = false
  }
}

onMounted(async () => {
  try {
    categories.value = await lookupsApi.categories()
  } catch {
    // Category is optional, so the form still works without the list.
    categories.value = []
  }
})
</script>

<template>
  <div class="mx-auto max-w-2xl">
    <h1 class="mb-1 text-2xl font-semibold">{{ t('portal.newRequest') }}</h1>
    <p class="mb-6 text-sm text-surface-500 dark:text-surface-400">{{ t('portal.newRequestHint') }}</p>

    <div class="space-y-4">
      <div>
        <label class="mb-1 block text-sm font-medium">{{ t('portal.subject') }} *</label>
        <InputText v-model="form.subject" class="w-full" :placeholder="t('portal.subjectPlaceholder')" />
      </div>

      <div>
        <label class="mb-1 block text-sm font-medium">{{ t('portal.description') }} *</label>
        <Textarea
          v-model="form.description"
          rows="7"
          auto-resize
          class="w-full"
          :placeholder="t('portal.descriptionPlaceholder')"
        />
      </div>

      <div class="grid gap-4 sm:grid-cols-2">
        <div>
          <label class="mb-1 block text-sm font-medium">{{ t('portal.category') }}</label>
          <Select
            v-model="form.categoryId"
            :options="categories"
            option-label="nameEn"
            option-value="id"
            show-clear
            class="w-full"
            :placeholder="t('portal.anyCategory')"
          >
            <template #option="{ option }">{{ ui.localized(option) }}</template>
            <template #value="{ value }">
              {{ value ? ui.localized(categories.find((c) => c.id === value)) : t('portal.anyCategory') }}
            </template>
          </Select>
        </div>

        <div>
          <label class="mb-1 block text-sm font-medium">{{ t('portal.priority') }}</label>
          <Select
            v-model="form.priority"
            :options="priorityOptions"
            option-label="label"
            option-value="value"
            class="w-full"
          />
        </div>
      </div>

      <Message v-if="formError" severity="error" :closable="false" class="text-sm">
        {{ formError }}
      </Message>

      <div class="flex justify-end gap-2">
        <Button :label="t('app.cancel')" text @click="router.back()" />
        <Button
          :label="t('portal.submit')"
          icon="pi pi-send"
          :disabled="!!formError || saving"
          :loading="saving"
          @click="submit"
        />
      </div>
    </div>
  </div>
</template>
