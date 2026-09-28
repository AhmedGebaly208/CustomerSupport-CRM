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
import Chip from 'primevue/chip'
import Dialog from 'primevue/dialog'
import MultiSelectTags from 'primevue/multiselect'
import InputTextName from 'primevue/inputtext'
import PageHeader from '@/components/PageHeader.vue'
import StatusTag from '@/components/StatusTag.vue'
import TicketBulkBar from '@/components/TicketBulkBar.vue'
import { savedViewsApi, slaApi, ticketsApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import SlaBadge from '@/components/SlaBadge.vue'
import { useUiStore } from '@/stores/ui'
import { useFormat } from '@/composables/useFormat'
import { useAuthStore } from '@/stores/auth'
import {
  TicketPriority,
  TicketStatus,
  type PagedResult,
  type SavedView,
  type Tag,
  type TicketListItem,
  type TicketSlaStatus,
} from '@/types/api'

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const ui = useUiStore()
const { formatDateTime, isOverdue } = useFormat()

const auth = useAuthStore()

const result = ref<PagedResult<TicketListItem> | null>(null)
const loading = ref(true)

/** Rows ticked in the table; drives the bulk bar. */
const selection = ref<TicketListItem[]>([])

const tagOptions = ref<Tag[]>([])
const tagIds = ref<string[]>([])
const watchedByMe = ref(false)

const savedViews = ref<SavedView[]>([])
const savedViewName = ref('')
const showSaveView = ref(false)

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

/** SLA state for the rows currently on screen, keyed by ticket id. Fetched in one call
 *  after the page loads rather than per row. */
const slaStatuses = ref<Record<string, TicketSlaStatus>>({})

async function loadSlaStatuses() {
  const ids = result.value?.items.map((x) => x.id) ?? []
  if (ids.length === 0) {
    slaStatuses.value = {}
    return
  }

  try {
    slaStatuses.value = await slaApi.ticketStatuses(ids)
  } catch {
    // The list is still usable without badges, so a failure here is silent rather than a
    // toast on top of the tickets the agent actually came for.
    slaStatuses.value = {}
  }
}

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
      tagIds: tagIds.value.length ? tagIds.value : undefined,
      watchedBy: watchedByMe.value ? auth.user?.id : undefined,
    })
    await loadSlaStatuses()
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

watch([statuses, priorities, onlyActive, onlyUnassigned, tagIds, watchedByMe], () => {
  page.value = 1
  load()
})

/**
 * Saved views round-trip the filter state as an opaque JSON blob. The server stores it
 * without interpreting it, so adding a filter here needs no backend change.
 */
function currentFilters(): string {
  return JSON.stringify({
    search: search.value,
    statuses: statuses.value,
    priorities: priorities.value,
    onlyActive: onlyActive.value,
    onlyUnassigned: onlyUnassigned.value,
    tagIds: tagIds.value,
    watchedByMe: watchedByMe.value,
  })
}

function applySavedView(view: SavedView) {
  try {
    const f = JSON.parse(view.filtersJson)
    search.value = f.search ?? ''
    statuses.value = f.statuses ?? []
    priorities.value = f.priorities ?? []
    onlyActive.value = f.onlyActive ?? false
    onlyUnassigned.value = f.onlyUnassigned ?? false
    tagIds.value = f.tagIds ?? []
    watchedByMe.value = f.watchedByMe ?? false
    page.value = 1
    load()
  } catch {
    toast.add({ severity: 'error', summary: t('ticket.savedViews.corrupt'), life: 5000 })
  }
}

async function saveCurrentView() {
  const name = savedViewName.value.trim()
  if (!name) return

  try {
    await savedViewsApi.upsert(name, currentFilters())
    savedViews.value = await savedViewsApi.list()
    savedViewName.value = ''
    showSaveView.value = false
    toast.add({ severity: 'success', summary: t('ticket.savedViews.saved'), life: 3000 })
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 5000 })
  }
}

async function deleteSavedView(view: SavedView) {
  try {
    await savedViewsApi.remove(view.id)
    savedViews.value = savedViews.value.filter((v) => v.id !== view.id)
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
  }
}

function onBulkApplied() {
  selection.value = []
  load()
}

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

onMounted(async () => {
  const [views, tags] = await Promise.allSettled([savedViewsApi.list(), ticketsApi.searchTags()])
  if (views.status === 'fulfilled') savedViews.value = views.value
  if (tags.status === 'fulfilled') tagOptions.value = tags.value
  await load()
})
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

      <ToggleButton
        v-model="watchedByMe"
        :on-label="t('ticket.watching')"
        :off-label="t('ticket.watching')"
        on-icon="pi pi-eye"
        off-icon="pi pi-eye-slash"
      />

      <MultiSelectTags
        v-model="tagIds"
        :options="tagOptions"
        option-label="name"
        option-value="id"
        :placeholder="t('ticket.tags')"
        :max-selected-labels="2"
        filter
        class="min-w-[11rem]"
      />
    </div>

    <!-- Saved views -->
    <div class="mb-4 flex flex-wrap items-center gap-2">
      <span class="text-sm text-surface-500 dark:text-surface-400">{{ t('ticket.savedViews.title') }}</span>

      <Chip
        v-for="view in savedViews"
        :key="view.id"
        :label="view.name"
        removable
        class="cursor-pointer"
        @click="applySavedView(view)"
        @remove="deleteSavedView(view)"
      />

      <Button
        icon="pi pi-bookmark"
        :label="t('ticket.savedViews.save')"
        text
        size="small"
        @click="showSaveView = true"
      />
    </div>

    <TicketBulkBar
      :selection="selection"
      @applied="onBulkApplied"
      @clear="selection = []"
    />

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
        v-model:selection="selection"
        @row-click="(e: { data: TicketListItem }) => router.push({ name: 'ticket-detail', params: { id: e.data.id } })"
      >
        <Column selection-mode="multiple" header-style="width: 3rem" />
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
            <SlaBadge
              v-if="slaStatuses[data.id]"
              :clock="slaStatuses[data.id].resolution"
              show-remaining
            />
            <span
              v-else
              class="text-sm"
              :class="isOverdue(data.resolutionDueAt) ? 'font-medium text-red-500' : ''"
            >
              {{ formatDateTime(data.resolutionDueAt) }}
            </span>
          </template>
        </Column>
      </DataTable>
    </div>

    <Dialog v-model:visible="showSaveView" modal :header="t('ticket.savedViews.save')" :style="{ width: '26rem' }">
      <div class="flex flex-col gap-2">
        <label class="text-sm font-medium">{{ t('ticket.savedViews.name') }}</label>
        <InputTextName v-model="savedViewName" class="w-full" @keyup.enter="saveCurrentView" />
        <small class="text-surface-500 dark:text-surface-400">{{ t('ticket.savedViews.hint') }}</small>
      </div>
      <template #footer>
        <Button :label="t('app.cancel')" outlined @click="showSaveView = false" />
        <Button :label="t('app.save')" :disabled="!savedViewName.trim()" @click="saveCurrentView" />
      </template>
    </Dialog>
  </div>
</template>
