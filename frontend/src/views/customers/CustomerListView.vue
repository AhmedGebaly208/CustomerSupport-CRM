<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import DataTable, { type DataTablePageEvent, type DataTableSortEvent } from 'primevue/datatable'
import Column from 'primevue/column'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Select from 'primevue/select'
import PageHeader from '@/components/PageHeader.vue'
import { customersApi, lookupsApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { useFormat } from '@/composables/useFormat'
import type { CustomerListItem, Lookup, PagedResult } from '@/types/api'

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const ui = useUiStore()
const { formatDate, formatNumber } = useFormat()

const result = ref<PagedResult<CustomerListItem> | null>(null)
const departments = ref<Lookup[]>([])
const loading = ref(true)

const search = ref('')
const departmentId = ref<string | null>(null)
const page = ref(1)
const pageSize = ref(20)
const sortBy = ref<string | undefined>(undefined)
const sortDescending = ref(false)

let searchTimer: ReturnType<typeof setTimeout> | undefined

async function load() {
  loading.value = true
  try {
    result.value = await customersApi.search({
      page: page.value,
      pageSize: pageSize.value,
      search: search.value.trim() || undefined,
      sortBy: sortBy.value,
      sortDescending: sortDescending.value,
      departmentId: departmentId.value,
    })
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 5000 })
  } finally {
    loading.value = false
  }
}

// Debounced so a fast typist does not fire a request per keystroke.
watch(search, () => {
  clearTimeout(searchTimer)
  searchTimer = setTimeout(() => {
    page.value = 1
    load()
  }, 350)
})

watch(departmentId, () => {
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

onMounted(async () => {
  try {
    departments.value = await lookupsApi.departments()
  } catch {
    // A failed lookup only costs the filter dropdown; the list itself still works.
  }
  await load()
})
</script>

<template>
  <div>
    <PageHeader :title="t('customer.title')">
      <template #actions>
        <Button
          icon="pi pi-plus"
          :label="t('customer.new')"
          size="small"
          @click="router.push({ name: 'customer-new' })"
        />
      </template>
    </PageHeader>

    <div class="mb-4 flex flex-wrap gap-2">
      <span class="relative flex-1 min-w-[16rem]">
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

      <Select
        v-model="departmentId"
        :options="departments"
        :option-label="(d: Lookup) => ui.localized(d)"
        option-value="id"
        :placeholder="t('customer.department')"
        show-clear
        class="min-w-[12rem]"
      />
    </div>

    <div
      class="rounded-xl border border-surface-200 bg-surface-0 dark:border-surface-800 dark:bg-surface-900"
    >
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
        @row-click="(e: { data: CustomerListItem }) => router.push({ name: 'customer-detail', params: { id: e.data.id } })"
      >
        <template #empty>
          <div class="p-6 text-center text-surface-500 dark:text-surface-400">{{ t('app.noData') }}</div>
        </template>

        <Column field="code" :header="t('customer.code')" sortable>
          <template #body="{ data }"><span class="ltr-nums font-medium">{{ data.code }}</span></template>
        </Column>

        <Column :field="ui.isArabic ? 'nameAr' : 'nameEn'" :header="t('customer.one')" sortable>
          <template #body="{ data }">
            <div class="font-medium">{{ ui.isArabic ? data.fullNameAr : data.fullNameEn }}</div>
            <div v-if="data.companyName" class="text-xs text-surface-500 dark:text-surface-400">
              {{ data.companyName }}
            </div>
          </template>
        </Column>

        <Column field="email" :header="t('customer.email')" sortable>
          <template #body="{ data }"><span class="ltr-nums text-sm">{{ data.email ?? '—' }}</span></template>
        </Column>

        <Column :header="t('customer.phone')">
          <template #body="{ data }"><span class="ltr-nums text-sm">{{ data.phone ?? '—' }}</span></template>
        </Column>

        <Column :header="t('customer.department')">
          <template #body="{ data }">
            {{ (ui.isArabic ? data.departmentNameAr : data.departmentNameEn) ?? '—' }}
          </template>
        </Column>

        <Column :header="t('customer.openTickets')">
          <template #body="{ data }">
            <Tag
              :severity="data.openTicketCount > 0 ? 'warn' : 'secondary'"
              :value="formatNumber(data.openTicketCount)"
              rounded
            />
          </template>
        </Column>

        <Column :header="t('customer.status')">
          <template #body="{ data }">
            <Tag
              :severity="data.isActive ? 'success' : 'secondary'"
              :value="data.isActive ? t('customer.active') : t('customer.inactive')"
              rounded
            />
          </template>
        </Column>

        <Column field="createdAt" :header="t('customer.createdAt')" sortable>
          <template #body="{ data }"><span class="text-sm">{{ formatDate(data.createdAt) }}</span></template>
        </Column>
      </DataTable>
    </div>
  </div>
</template>
