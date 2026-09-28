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
import Dialog from 'primevue/dialog'
import FileUpload, { type FileUploadUploaderEvent } from 'primevue/fileupload'
import Textarea from 'primevue/textarea'
import DataTableResults from 'primevue/datatable'
import ColumnResult from 'primevue/column'
import PageHeader from '@/components/PageHeader.vue'
import { customersApi, lookupsApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { useFormat } from '@/composables/useFormat'
import { useAuthStore } from '@/stores/auth'
import {
  PERMISSIONS,
  type CustomerImportResult,
  type CustomerListItem,
  type Lookup,
  type PagedResult,
} from '@/types/api'

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const ui = useUiStore()
const { formatDate, formatNumber } = useFormat()

const auth = useAuthStore()

const result = ref<PagedResult<CustomerListItem> | null>(null)
const departments = ref<Lookup[]>([])
const loading = ref(true)

const canMerge = auth.hasPermission(PERMISSIONS.customersMerge)
const canImport = auth.hasPermission(PERMISSIONS.customersImport)

/** Rows ticked in the table; merge needs exactly two. */
const selection = ref<CustomerListItem[]>([])

const importDialog = ref(false)
const importing = ref(false)
const importResult = ref<CustomerImportResult | null>(null)

const mergeDialog = ref(false)
const mergeSurvivorId = ref<string | null>(null)
const mergeReason = ref('')
const merging = ref(false)

async function runImport(event: FileUploadUploaderEvent) {
  // PrimeVue hands over File | File[] depending on multiple mode.
  const file = Array.isArray(event.files) ? event.files[0] : event.files
  if (!file || importing.value) return

  importing.value = true
  try {
    importResult.value = await customersApi.import(file)
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 7000 })
  } finally {
    importing.value = false
  }
}

function openMerge() {
  if (selection.value.length !== 2) return
  // Default the survivor to the older record: it usually carries more history.
  const [a, b] = selection.value
  mergeSurvivorId.value = a.createdAt <= b.createdAt ? a.id : b.id
  mergeReason.value = ''
  mergeDialog.value = true
}

async function confirmMerge() {
  if (!mergeSurvivorId.value || merging.value) return

  const loser = selection.value.find((c) => c.id !== mergeSurvivorId.value)
  if (!loser) return

  merging.value = true
  try {
    const outcome = await customersApi.merge(mergeSurvivorId.value, loser.id, mergeReason.value || null)
    mergeDialog.value = false
    selection.value = []

    toast.add({
      severity: 'success',
      summary: t('merge.done', { tickets: outcome.ticketsMoved }),
      life: 6000,
    })
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 8000 })
  } finally {
    merging.value = false
  }
}

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
          v-if="canMerge"
          icon="pi pi-sign-in"
          :label="t('merge.action')"
          outlined
          size="small"
          :disabled="selection.length !== 2"
          @click="openMerge"
        />
        <Button
          v-if="canImport"
          icon="pi pi-upload"
          :label="t('import.action')"
          outlined
          size="small"
          @click="importDialog = true"
        />
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
        v-model:selection="selection"
        @row-click="(e: { data: CustomerListItem }) => router.push({ name: 'customer-detail', params: { id: e.data.id } })"
      >
        <Column v-if="canMerge" selection-mode="multiple" header-style="width: 3rem" />
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

    <p v-if="canMerge" class="mt-2 text-xs text-surface-500 dark:text-surface-400">
      {{ t('merge.selectHint') }}
    </p>

    <!-- Import -->
    <Dialog v-model:visible="importDialog" modal :header="t('import.action')" :style="{ width: '40rem' }">
      <div class="flex flex-col gap-3">
        <p class="text-sm text-surface-600 dark:text-surface-300">{{ t('import.explain') }}</p>

        <FileUpload
          mode="basic"
          :auto="true"
          :custom-upload="true"
          accept=".csv,.xlsx"
          :choose-label="t('import.choose')"
          :disabled="importing"
          @uploader="runImport"
        />

        <div v-if="importResult" class="flex flex-col gap-2">
          <div class="flex flex-wrap gap-2">
            <Tag severity="success" :value="t('import.succeeded', { count: importResult.succeededCount })" rounded />
            <Tag
              v-if="importResult.failedCount"
              severity="danger"
              :value="t('import.failed', { count: importResult.failedCount })"
              rounded
            />
          </div>

          <DataTableResults
            v-if="importResult.failedCount"
            :value="importResult.rows.filter((r) => !r.succeeded)"
            size="small"
            striped-rows
            scrollable
            scroll-height="16rem"
          >
            <ColumnResult :header="t('import.row')" style="width: 5rem">
              <template #body="{ data }"><span class="ltr-nums">{{ data.rowNumber }}</span></template>
            </ColumnResult>
            <ColumnResult :header="t('ticket.bulk.reason')">
              <template #body="{ data }">
                <span class="text-sm text-red-500">{{ data.errors.join(' · ') }}</span>
              </template>
            </ColumnResult>
          </DataTableResults>
        </div>
      </div>

      <template #footer>
        <Button :label="t('app.close')" outlined @click="importDialog = false; importResult = null" />
      </template>
    </Dialog>

    <!-- Merge -->
    <Dialog v-model:visible="mergeDialog" modal :header="t('merge.action')" :style="{ width: '34rem' }">
      <div class="flex flex-col gap-3">
        <p class="text-sm text-surface-600 dark:text-surface-300">{{ t('merge.explain') }}</p>

        <label class="text-sm font-medium">{{ t('merge.keep') }}</label>
        <div class="flex flex-col gap-2">
          <label
            v-for="candidate in selection"
            :key="candidate.id"
            class="flex cursor-pointer items-center gap-2 rounded-lg border p-2"
            :class="mergeSurvivorId === candidate.id
              ? 'border-primary bg-primary/5'
              : 'border-surface-200 dark:border-surface-800'"
          >
            <input v-model="mergeSurvivorId" type="radio" :value="candidate.id" />
            <span class="ltr-nums text-xs text-surface-500">{{ candidate.code }}</span>
            <span class="font-medium">{{ ui.isArabic ? candidate.fullNameAr : candidate.fullNameEn }}</span>
            <span class="ms-auto text-xs text-surface-500">
              {{ t('customer.openTickets') }}: {{ candidate.openTicketCount }}
            </span>
          </label>
        </div>

        <Textarea v-model="mergeReason" rows="2" auto-resize :placeholder="t('merge.reason')" class="w-full" />
      </div>

      <template #footer>
        <Button :label="t('app.cancel')" outlined @click="mergeDialog = false" />
        <Button
          :label="t('merge.confirm')"
          severity="danger"
          :loading="merging"
          :disabled="!mergeSurvivorId"
          @click="confirmMerge"
        />
      </template>
    </Dialog>
  </div>
</template>
