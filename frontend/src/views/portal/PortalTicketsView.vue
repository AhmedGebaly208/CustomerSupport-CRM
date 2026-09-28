<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Skeleton from 'primevue/skeleton'
import Paginator from 'primevue/paginator'
import ToggleSwitch from 'primevue/toggleswitch'
import StatusTag from '@/components/StatusTag.vue'
import { portalApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useFormat } from '@/composables/useFormat'
import type { PortalTicketListItem } from '@/types/api'

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const { formatDateTime } = useFormat()

const items = ref<PortalTicketListItem[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = ref(20)
const openOnly = ref(false)
const loading = ref(true)

async function load() {
  loading.value = true
  try {
    const result = await portalApi.tickets(openOnly.value, page.value, pageSize.value)
    items.value = result.items
    total.value = result.totalCount
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 6000 })
  } finally {
    loading.value = false
  }
}

watch([openOnly], () => {
  page.value = 1
  void load()
})

onMounted(load)
</script>

<template>
  <div>
    <div class="mb-4 flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold">{{ t('portal.myRequests') }}</h1>
        <p class="text-sm text-surface-500 dark:text-surface-400">{{ t('portal.requestsSubtitle') }}</p>
      </div>

      <Button
        :label="t('portal.newRequest')"
        icon="pi pi-plus"
        @click="router.push({ name: 'portal-new-ticket' })"
      />
    </div>

    <label class="mb-4 flex items-center gap-2 text-sm">
      <ToggleSwitch v-model="openOnly" input-id="portal-open-only" />
      <span>{{ t('portal.openOnly') }}</span>
    </label>

    <div v-if="loading" class="grid gap-3">
      <Skeleton v-for="n in 4" :key="n" height="5rem" />
    </div>

    <div
      v-else-if="items.length === 0"
      class="rounded-lg border border-dashed border-surface-300 py-16 text-center dark:border-surface-600"
    >
      <i class="pi pi-inbox mb-3 block text-3xl text-surface-400" />
      <p class="text-surface-500 dark:text-surface-400">{{ t('portal.noRequests') }}</p>
      <Button
        :label="t('portal.newRequest')"
        icon="pi pi-plus"
        class="mt-4"
        @click="router.push({ name: 'portal-new-ticket' })"
      />
    </div>

    <ul v-else class="grid gap-3">
      <li
        v-for="ticket in items"
        :key="ticket.id"
        class="cursor-pointer rounded-lg border border-surface-200 bg-surface-0 p-4 transition hover:border-primary dark:border-surface-700 dark:bg-surface-900"
        @click="router.push({ name: 'portal-ticket', params: { id: ticket.id } })"
      >
        <div class="mb-1 flex flex-wrap items-center gap-2">
          <span class="ltr-nums text-sm font-medium text-primary">{{ ticket.number }}</span>
          <StatusTag :status="ticket.status" />
        </div>

        <p class="mb-1 font-medium">{{ ticket.subject }}</p>

        <p class="text-xs text-surface-500 dark:text-surface-400">
          {{ t('portal.lastActivity') }}: {{ formatDateTime(ticket.lastActivityAt) }}
        </p>
      </li>
    </ul>

    <Paginator
      v-if="total > pageSize"
      :rows="pageSize"
      :total-records="total"
      :first="(page - 1) * pageSize"
      class="mt-4"
      @page="(e: { page: number }) => { page = e.page + 1; load() }"
    />
  </div>
</template>
