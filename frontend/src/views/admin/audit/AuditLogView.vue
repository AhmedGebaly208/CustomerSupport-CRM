<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import DataTable, { type DataTablePageEvent, type DataTableSortEvent } from 'primevue/datatable'
import Column from 'primevue/column'
import InputText from 'primevue/inputtext'
import Select from 'primevue/select'
import DatePicker from 'primevue/datepicker'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Dialog from 'primevue/dialog'
import PageHeader from '@/components/PageHeader.vue'
import { auditLogsApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { useFormat } from '@/composables/useFormat'
import { AuditAction, type AuditLogEntry, type PagedResult } from '@/types/api'

const { t } = useI18n()
const toast = useToast()
const ui = useUiStore()
const { formatDateTime } = useFormat()

const result = ref<PagedResult<AuditLogEntry> | null>(null)
const entityNames = ref<string[]>([])
const loading = ref(true)

const search = ref('')
const entityName = ref<string | null>(null)
const action = ref<AuditAction | null>(null)
const dateRange = ref<Date[] | null>(null)
const page = ref(1)
const pageSize = ref(20)
const sortBy = ref<string | undefined>(undefined)
const sortDescending = ref(true)

const selected = ref<AuditLogEntry | null>(null)

const actionOptions = [
  { label: t('audit.action.Created'), value: AuditAction.Created },
  { label: t('audit.action.Updated'), value: AuditAction.Updated },
  { label: t('audit.action.Deleted'), value: AuditAction.Deleted },
]

const ACTION_SEVERITY: Record<AuditAction, string> = {
  [AuditAction.Created]: 'success',
  [AuditAction.Updated]: 'info',
  [AuditAction.Deleted]: 'danger',
}

/** Turns the stored JSON diff into rows the table can render. */
const selectedChanges = computed(() => {
  if (!selected.value?.changes) return []

  try {
    const parsed = JSON.parse(selected.value.changes) as Record<
      string,
      { old?: string | null; new?: string | null }
    >
    return Object.entries(parsed).map(([field, value]) => ({
      field,
      oldValue: value.old ?? '—',
      newValue: value.new ?? '—',
    }))
  } catch {
    // A malformed payload must not blank the dialog — show it raw instead.
    return [{ field: '—', oldValue: '', newValue: selected.value.changes }]
  }
})

let searchTimer: ReturnType<typeof setTimeout> | undefined

async function load() {
  loading.value = true
  try {
    result.value = await auditLogsApi.list({
      page: page.value,
      pageSize: pageSize.value,
      search: search.value.trim() || undefined,
      sortBy: sortBy.value,
      sortDescending: sortDescending.value,
      entityName: entityName.value,
      action: action.value,
      dateFrom: dateRange.value?.[0]?.toISOString() ?? null,
      dateTo: dateRange.value?.[1]?.toISOString() ?? null,
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

watch([entityName, action, dateRange], () => {
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

function reset() {
  search.value = ''
  entityName.value = null
  action.value = null
  dateRange.value = null
}

onMounted(async () => {
  try {
    entityNames.value = (await auditLogsApi.facets()).entityNames
  } catch {
    // Losing the facet list only costs the entity dropdown.
  }
  await load()
})
</script>

<template>
  <div>
    <PageHeader :title="t('audit.title')" :subtitle="t('audit.subtitle')">
      <template #actions>
        <Button icon="pi pi-filter-slash" :label="t('audit.clearFilters')" outlined size="small" @click="reset" />
        <Button icon="pi pi-refresh" outlined size="small" :aria-label="t('app.search')" @click="load" />
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
          :placeholder="t('audit.searchPlaceholder')"
          class="w-full"
          :class="ui.isArabic ? 'pr-10' : 'pl-10'"
        />
      </span>

      <Select
        v-model="entityName"
        :options="entityNames"
        :placeholder="t('audit.entity')"
        show-clear
        class="min-w-[12rem]"
      />

      <Select
        v-model="action"
        :options="actionOptions"
        option-label="label"
        option-value="value"
        :placeholder="t('audit.actionLabel')"
        show-clear
        class="min-w-[11rem]"
      />

      <DatePicker
        v-model="dateRange"
        selection-mode="range"
        :manual-input="false"
        show-icon
        icon-display="input"
        :placeholder="t('audit.dateRange')"
        class="min-w-[15rem]"
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
        :rows-per-page-options="[20, 50, 100]"
        class="cursor-pointer"
        @page="onPage"
        @sort="onSort"
        @row-click="(e: { data: AuditLogEntry }) => (selected = e.data)"
      >
        <template #empty>
          <div class="p-6 text-center text-surface-500 dark:text-surface-400">{{ t('app.noData') }}</div>
        </template>

        <Column field="occurredAt" :header="t('audit.when')" sortable>
          <template #body="{ data }">
            <span class="text-sm">{{ formatDateTime(data.occurredAt) }}</span>
          </template>
        </Column>

        <Column field="entityName" :header="t('audit.entity')" sortable>
          <template #body="{ data }">
            <div class="font-medium">{{ data.entityName }}</div>
            <div class="ltr-nums truncate text-xs text-surface-500 dark:text-surface-400">
              {{ data.entityId }}
            </div>
          </template>
        </Column>

        <Column field="action" :header="t('audit.actionLabel')" sortable>
          <template #body="{ data }">
            <Tag
              :severity="ACTION_SEVERITY[data.action as AuditAction]"
              :value="t(`audit.action.${AuditAction[data.action as AuditAction]}`)"
              rounded
            />
          </template>
        </Column>

        <Column :header="t('audit.who')">
          <template #body="{ data }">{{ data.userName ?? t('audit.system') }}</template>
        </Column>

        <Column :header="t('audit.changes')">
          <template #body="{ data }">
            <span v-if="!data.changes" class="text-surface-400">—</span>
            <span v-else class="text-xs text-surface-600 dark:text-surface-300">
              {{ Object.keys(JSON.parse(data.changes)).join(', ') }}
            </span>
          </template>
        </Column>
      </DataTable>
    </div>

    <!-- Detail -->
    <Dialog
      :visible="selected !== null"
      modal
      :header="t('audit.detail')"
      :style="{ width: '40rem' }"
      @update:visible="(v: boolean) => { if (!v) selected = null }"
    >
      <div v-if="selected" class="flex flex-col gap-4">
        <div class="grid gap-3 sm:grid-cols-2">
          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('audit.when') }}</div>
            <div>{{ formatDateTime(selected.occurredAt) }}</div>
          </div>
          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('audit.who') }}</div>
            <div>{{ selected.userName ?? t('audit.system') }}</div>
          </div>
          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('audit.entity') }}</div>
            <div>{{ selected.entityName }}</div>
          </div>
          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('audit.recordId') }}</div>
            <div class="ltr-nums break-all text-sm">{{ selected.entityId }}</div>
          </div>
          <div v-if="selected.ipAddress">
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('audit.ipAddress') }}</div>
            <div class="ltr-nums">{{ selected.ipAddress }}</div>
          </div>
        </div>

        <div v-if="selectedChanges.length" class="border-t border-surface-200 pt-3 dark:border-surface-800">
          <div class="mb-2 text-xs text-surface-500 dark:text-surface-400">{{ t('audit.changes') }}</div>
          <DataTable :value="selectedChanges" size="small" striped-rows>
            <Column field="field" :header="t('audit.field')" />
            <Column field="oldValue" :header="t('audit.oldValue')">
              <template #body="{ data }">
                <span class="text-sm text-surface-600 dark:text-surface-300">{{ data.oldValue }}</span>
              </template>
            </Column>
            <Column field="newValue" :header="t('audit.newValue')">
              <template #body="{ data }"><span class="text-sm font-medium">{{ data.newValue }}</span></template>
            </Column>
          </DataTable>
        </div>

        <p v-else class="text-sm text-surface-500 dark:text-surface-400">{{ t('audit.noChanges') }}</p>
      </div>

      <template #footer>
        <Button :label="t('app.close')" outlined @click="selected = null" />
      </template>
    </Dialog>
  </div>
</template>
