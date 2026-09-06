<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import DataTable, { type DataTablePageEvent, type DataTableSortEvent } from 'primevue/datatable'
import Column from 'primevue/column'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import MultiSelect from 'primevue/multiselect'
import ToggleButton from 'primevue/togglebutton'
import PageHeader from '@/components/PageHeader.vue'
import StatusTag from '@/components/StatusTag.vue'
import { ticketsApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { useFormat } from '@/composables/useFormat'
import { TicketPriority, TicketStatus, type PagedResult, type TicketListItem } from '@/types/api'

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const ui = useUiStore()
const { formatDateTime, isOverdue } = useFormat()

const result = ref<PagedResult<TicketListItem> | null>(null)
const loading = ref(true)

const search = ref('')
const statuses = ref<TicketStatus[]>([])
const priorities = ref<TicketPriority[]>([])
const onlyActive = ref(false)
const onlyUnassigned = ref(false)
const page = ref(1)
const pageSize = ref(20)
const sortBy = ref<string | undefined>(undefined)
const sortDescending = ref(false)

const statusOptions = Object.entries(TicketStatus)
  .filter(([, v]) => typeof v === 'number')
  .map(([name, value]) => ({ label: t(`status.${name}`), value: value as TicketStatus }))

const priorityOptions = Object.entries(TicketPriority)
  .filter(([, v]) => typeof v === 'number')
  .map(([name, value]) => ({ label: t(`priority.${name}`), value: value as TicketPriority }))

let searchTimer: ReturnType<typeof setTimeout> | undefined

async function load() {
  loading.value = true
  try {
    result.value = await ticketsApi.search({
      page: page.value,
      pageSize: pageSize.value,
      search: search.value.trim() || undefined,
      sortBy: sortBy.value,
      sortDescending: sortDescending.value,
      statuses: statuses.value.length ? statuses.value : undefined,
      priorities: priorities.value.length ? priorities.value : undefined,
      onlyActive: onlyActive.value ? true : undefined,
      unassigned: onlyUnassigned.value ? true : undefined,
    })
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 5000 })
  } finally {
    loading.value = false
  }
}

watch(search, () => {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => {
    page.value = 1
    load()
  }, 350)
})

watch([statuses, priorities, onlyActive, onlyUnassigned], () => {
  page.value = 1
  load()
})

function onPage(event: DataTablePageEvent) {
  page.value = event.page + 1
  pageSize.value = event.rows
  load()
}

function onSort(event: DataTableSortEvent) {
  sortBy.value = (event.sortField as string) || undefined
  sortDescending.value = event.sortOrder === -1
  load()
}

onMounted(load)
</script>

<template>
  <div>
    <PageHeader :title="t('ticket.title')">
      <template #actions>
        <Button
          icon="pi pi-plus"
          :label="t('ticket.new')"
          size="small"
          @click="router.push({ name: 'ticket-new' })"
        />
      </template>
    </PageHeader>

    <div class="mb-4 flex flex-wrap gap-2">
      <span class="relative min-w-[14rem] flex-1">
        <i
          class="pi pi-search absolute top-1/2 -translate-y-1/2 text-surface-400"
          :class="ui.isArabic ? 'right-3' : 'left-3'"
        />
        <InputText
          v-model="search"
          :placeholder="t('app.searchPlaceholder')"
          class="w-full"
          :class="ui.isArabic ? 'pr-10' : 'pl-10'"
        />
      </span>

      <MultiSelect
        v-model="statuses"
        :options="statusOptions"
        option-label="label"
        option-value="value"
        :placeholder="t('ticket.status')"
        :max-selected-labels="2"
        class="min-w-[11rem]"
      />

      <MultiSelect
        v-model="priorities"
        :options="priorityOptions"
        option-label="label"
        option-value="value"
        :placeholder="t('ticket.priority')"
        :max-selected-labels="2"
        class="min-w-[11rem]"
      />

      <ToggleButton
        v-model="onlyActive"
        :on-label="t('ticket.onlyActive')"
        :off-label="t('ticket.onlyActive')"
        on-icon="pi pi-filter-fill"
        off-icon="pi pi-filter"
      />

      <ToggleButton
        v-model="onlyUnassigned"
        :on-label="t('ticket.onlyUnassigned')"
        :off-label="t('ticket.onlyUnassigned')"
        on-icon="pi pi-user-minus"
        off-icon="pi pi-user"
      />
    </div>

    <div class="rounded-xl border border-surface-200 bg-surface-0 dark:border-surface-800 dark:bg-surface-900">
      <DataTable
        :value="result?.items ?? []"
        :loading="loading"
        data-key="id"
        size="small"
        striped-rows
        row-hover
        lazy
        paginator
        :rows="pageSize"
        :first="(page - 1) * pageSize"
        :total-records="result?.totalCount ?? 0"
        :rows-per-page-options="[10, 20, 50, 100]"
        class="cursor-pointer"
        @page="onPage"
        @sort="onSort"
        @row-click="(e: { data: TicketListItem }) => router.push({ name: 'ticket-detail', params: { id: e.data.id } })"
      >
        <template #empty>
          <div class="p-6 text-center text-surface-500 dark:text-surface-400">{{ t('app.noData') }}</div>
        </template>

        <Column field="number" :header="t('ticket.number')" sortable>
          <template #body="{ data }"><span class="ltr-nums font-medium">{{ data.number }}</span></template>
        </Column>

        <Column field="subject" :header="t('ticket.subject')" sortable>
          <template #body="{ data }">
            <div class="line-clamp-1 font-medium">{{ data.subject }}</div>
            <div v-if="data.categoryNameEn" class="text-xs text-surface-500 dark:text-surface-400">
              {{ ui.isArabic ? data.categoryNameAr : data.categoryNameEn }}
            </div>
          </template>
        </Column>

        <Column :header="t('ticket.customer')">
          <template #body="{ data }">{{ ui.isArabic ? data.customerNameAr : data.customerNameEn }}</template>
        </Column>

        <Column field="status" :header="t('ticket.status')" sortable>
          <template #body="{ data }"><StatusTag :status="data.status" /></template>
        </Column>

        <Column field="priority" :header="t('ticket.priority')" sortable>
          <template #body="{ data }"><StatusTag :priority="data.priority" /></template>
        </Column>

        <Column :header="t('ticket.assignedAgent')">
          <template #body="{ data }">
            <span v-if="data.assignedAgentName">{{ data.assignedAgentName }}</span>
            <span v-else class="text-surface-400">{{ t('ticket.unassigned') }}</span>
          </template>
        </Column>

        <Column field="createdAt" :header="t('ticket.createdAt')" sortable>
          <template #body="{ data }">
            <span class="text-sm">{{ formatDateTime(data.createdAt) }}</span>
          </template>
        </Column>

        <Column :header="t('ticket.resolutionDue')">
          <template #body="{ data }">
            <span
              class="text-sm"
              :class="isOverdue(data.resolutionDueAt) ? 'font-medium text-red-500' : ''"
            >
              {{ formatDateTime(data.resolutionDueAt) }}
            </span>
          </template>
        </Column>
      </DataTable>
    </div>
  </div>
</template>
