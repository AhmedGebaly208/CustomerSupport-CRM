<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import DataTable, { type DataTablePageEvent, type DataTableSortEvent } from 'primevue/datatable'
import Column from 'primevue/column'
import InputText from 'primevue/inputtext'
import Button from 'primevue/button'
import Select from 'primevue/select'
import Tag from 'primevue/tag'
import PageHeader from '@/components/PageHeader.vue'
import { lookupsApi, usersApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { useAuthStore } from '@/stores/auth'
import { useFormat } from '@/composables/useFormat'
import { ROLE_NAMES, type Lookup, type PagedResult, type UserAdmin } from '@/types/api'

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const confirm = useConfirm()
const ui = useUiStore()
const auth = useAuthStore()
const { formatDate, formatDateTime } = useFormat()

const result = ref<PagedResult<UserAdmin> | null>(null)
const departments = ref<Lookup[]>([])
const loading = ref(true)

const search = ref('')
const role = ref<string | null>(null)
const departmentId = ref<string | null>(null)
const isActive = ref<boolean | null>(null)
const page = ref(1)
const pageSize = ref(20)
const sortBy = ref<string | undefined>(undefined)
const sortDescending = ref(false)

const roleOptions = ROLE_NAMES.map((name) => ({ label: t(`role.${name}`), value: name }))

const activeOptions = [
  { label: t('customer.active'), value: true },
  { label: t('customer.inactive'), value: false },
]

let searchTimer: ReturnType<typeof setTimeout> | undefined

async function load() {
  loading.value = true
  try {
    result.value = await usersApi.list({
      page: page.value,
      pageSize: pageSize.value,
      search: search.value.trim() || undefined,
      sortBy: sortBy.value,
      sortDescending: sortDescending.value,
      role: role.value,
      departmentId: departmentId.value,
      isActive: isActive.value,
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

watch([role, departmentId, isActive], () => {
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

function confirmDeactivate(user: UserAdmin) {
  confirm.require({
    // The message spells out the sign-out consequence, which is not obvious from
    // the word "deactivate" alone.
    message: t('user.deactivateConfirm', { name: ui.localizedFullName(user) }),
    header: t('user.deactivate'),
    icon: 'pi pi-exclamation-triangle',
    acceptProps: { label: t('user.deactivate'), severity: 'danger' },
    rejectProps: { label: t('app.cancel'), outlined: true },
    accept: async () => {
      try {
        await usersApi.deactivate(user.id)
        toast.add({ severity: 'success', summary: t('user.deactivated'), life: 3000 })
        await load()
      } catch (e) {
        // The API refuses self-deactivation and last-admin removal; show its reason.
        toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 7000 })
      }
    },
  })
}

async function reactivate(user: UserAdmin) {
  try {
    await usersApi.reactivate(user.id)
    toast.add({ severity: 'success', summary: t('user.reactivated'), life: 3000 })
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
  }
}

onMounted(async () => {
  try {
    departments.value = await lookupsApi.departments()
  } catch {
    // Losing the lookup only costs the filter dropdown.
  }
  await load()
})
</script>

<template>
  <div>
    <PageHeader :title="t('user.title')" :subtitle="t('user.subtitle')">
      <template #actions>
        <Button
          icon="pi pi-user-plus"
          :label="t('user.new')"
          size="small"
          @click="router.push({ name: 'user-new' })"
        />
      </template>
    </PageHeader>

    <div class="mb-4 flex flex-wrap gap-2">
      <span class="relative min-w-[15rem] flex-1">
        <i
          class="pi pi-search absolute top-1/2 -translate-y-1/2 text-surface-400"
          :class="ui.isArabic ? 'right-3' : 'left-3'"
        />
        <InputText
          v-model="search"
          :placeholder="t('user.searchPlaceholder')"
          class="w-full"
          :class="ui.isArabic ? 'pr-10' : 'pl-10'"
        />
      </span>

      <Select
        v-model="role"
        :options="roleOptions"
        option-label="label"
        option-value="value"
        :placeholder="t('user.role')"
        show-clear
        class="min-w-[11rem]"
      />

      <Select
        v-model="departmentId"
        :options="departments"
        :option-label="(d: Lookup) => ui.localized(d)"
        option-value="id"
        :placeholder="t('customer.department')"
        show-clear
        class="min-w-[12rem]"
      />

      <Select
        v-model="isActive"
        :options="activeOptions"
        option-label="label"
        option-value="value"
        :placeholder="t('customer.status')"
        show-clear
        class="min-w-[10rem]"
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
        @page="onPage"
        @sort="onSort"
      >
        <template #empty>
          <div class="p-6 text-center text-surface-500 dark:text-surface-400">{{ t('app.noData') }}</div>
        </template>

        <Column :field="ui.isArabic ? 'nameAr' : 'nameEn'" :header="t('user.name')" sortable>
          <template #body="{ data }">
            <div class="font-medium">{{ ui.localizedFullName(data) }}</div>
            <div class="ltr-nums text-xs text-surface-500 dark:text-surface-400">{{ data.email }}</div>
          </template>
        </Column>

        <Column :header="t('user.roles')">
          <template #body="{ data }">
            <div class="flex flex-wrap gap-1">
              <Tag
                v-for="r in data.roles"
                :key="r"
                :severity="r === 'Admin' ? 'danger' : r === 'Manager' ? 'warn' : 'info'"
                :value="t(`role.${r}`)"
                rounded
              />
            </div>
          </template>
        </Column>

        <Column :header="t('customer.department')">
          <template #body="{ data }">
            {{ (ui.isArabic ? data.departmentNameAr : data.departmentNameEn) ?? '—' }}
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

        <Column :header="t('user.lastLogin')">
          <template #body="{ data }">
            <span class="text-sm">{{ formatDateTime(data.lastLoginAt) }}</span>
          </template>
        </Column>

        <Column field="createdAt" :header="t('customer.createdAt')" sortable>
          <template #body="{ data }"><span class="text-sm">{{ formatDate(data.createdAt) }}</span></template>
        </Column>

        <Column :header="t('app.actions')">
          <template #body="{ data }">
            <div class="flex items-center gap-1">
              <Button
                icon="pi pi-pencil"
                text
                rounded
                size="small"
                :aria-label="t('app.edit')"
                @click="router.push({ name: 'user-edit', params: { id: data.id } })"
              />
              <Button
                v-if="data.isActive"
                icon="pi pi-ban"
                severity="danger"
                text
                rounded
                size="small"
                :disabled="data.id === auth.user?.id"
                :aria-label="t('user.deactivate')"
                @click="confirmDeactivate(data)"
              />
              <Button
                v-else
                icon="pi pi-check-circle"
                severity="success"
                text
                rounded
                size="small"
                :aria-label="t('user.reactivate')"
                @click="reactivate(data)"
              />
            </div>
          </template>
        </Column>
      </DataTable>
    </div>
  </div>
</template>
