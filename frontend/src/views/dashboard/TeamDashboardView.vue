<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Select from 'primevue/select'
import Skeleton from 'primevue/skeleton'
import Tag from 'primevue/tag'
import PageHeader from '@/components/PageHeader.vue'
import TicketMiniList from '@/components/TicketMiniList.vue'
import { lookupsApi, workspaceApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import type { Lookup, TeamDashboard } from '@/types/api'

const { t } = useI18n()
const toast = useToast()
const ui = useUiStore()

const board = ref<TeamDashboard | null>(null)
const departments = ref<Lookup[]>([])
const departmentId = ref<string | null>(null)
const loading = ref(true)

const tiles = computed(() => [
  { key: 'active', icon: 'pi pi-inbox', value: board.value?.totalActive ?? 0, tone: 'text-primary' },
  { key: 'unassigned', icon: 'pi pi-users', value: board.value?.totalUnassigned ?? 0, tone: 'text-amber-500' },
  { key: 'atRisk', icon: 'pi pi-clock', value: board.value?.totalAtRisk ?? 0, tone: 'text-orange-500' },
  { key: 'overdue', icon: 'pi pi-exclamation-triangle', value: board.value?.totalOverdue ?? 0, tone: 'text-red-500' },
  { key: 'resolvedToday', icon: 'pi pi-check-circle', value: board.value?.resolvedToday ?? 0, tone: 'text-emerald-500' },
])

async function load() {
  loading.value = true
  try {
    board.value = await workspaceApi.team(departmentId.value)
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 5000 })
  } finally {
    loading.value = false
  }
}

watch(departmentId, load)

onMounted(async () => {
  await Promise.all([load(), lookupsApi.departments().then((d) => (departments.value = d))])
})
</script>

<template>
  <div>
    <PageHeader :title="t('team.title')" :subtitle="t('team.subtitle')">
      <template #actions>
        <Select
          v-model="departmentId"
          :options="departments"
          option-label="nameEn"
          option-value="id"
          show-clear
          class="w-56"
          :placeholder="t('team.allDepartments')"
        >
          <template #option="{ option }">{{ ui.localized(option) }}</template>
          <template #value="{ value }">
            {{ value ? ui.localized(departments.find((d) => d.id === value)) : t('team.allDepartments') }}
          </template>
        </Select>
      </template>
    </PageHeader>

    <div class="mb-6 grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
      <div
        v-for="tile in tiles"
        :key="tile.key"
        class="rounded-lg border border-surface-200 bg-surface-0 p-4 dark:border-surface-700 dark:bg-surface-900"
      >
        <div class="flex items-center gap-2 text-sm text-surface-500 dark:text-surface-400">
          <i :class="[tile.icon, tile.tone]" />
          <span>{{ t(`team.tile.${tile.key}`) }}</span>
        </div>
        <Skeleton v-if="loading" height="2rem" class="mt-2" />
        <div v-else class="ltr-nums mt-1 text-2xl font-semibold">{{ tile.value }}</div>
      </div>
    </div>

    <div class="mb-6 rounded-lg border border-surface-200 bg-surface-0 dark:border-surface-700 dark:bg-surface-900">
      <div class="border-b border-surface-200 p-4 dark:border-surface-700">
        <h2 class="font-semibold">{{ t('team.workload') }}</h2>
        <p class="text-sm text-surface-500 dark:text-surface-400">{{ t('team.workloadHint') }}</p>
      </div>

      <DataTable :value="board?.members ?? []" :loading="loading" size="small" striped-rows>
        <template #empty>
          <div class="p-6 text-center text-surface-500 dark:text-surface-400">{{ t('team.noAgents') }}</div>
        </template>

        <Column :header="t('team.agent')">
          <template #body="{ data }">
            {{ ui.isArabic ? data.agentNameAr : data.agentNameEn }}
          </template>
        </Column>

        <Column :header="t('team.active')" style="width: 8rem">
          <template #body="{ data }"><span class="ltr-nums">{{ data.active }}</span></template>
        </Column>

        <Column :header="t('team.atRisk')" style="width: 8rem">
          <template #body="{ data }">
            <Tag v-if="data.atRisk > 0" severity="warn" rounded :value="String(data.atRisk)" />
            <span v-else class="ltr-nums text-surface-400">0</span>
          </template>
        </Column>

        <Column :header="t('team.overdue')" style="width: 8rem">
          <template #body="{ data }">
            <Tag v-if="data.overdue > 0" severity="danger" rounded :value="String(data.overdue)" />
            <span v-else class="ltr-nums text-surface-400">0</span>
          </template>
        </Column>

        <Column :header="t('team.resolvedToday')" style="width: 9rem">
          <template #body="{ data }"><span class="ltr-nums">{{ data.resolvedToday }}</span></template>
        </Column>
      </DataTable>
    </div>

    <TicketMiniList
      :title="t('team.oldestUnassigned')"
      icon="pi pi-inbox"
      :tickets="board?.oldestUnassigned ?? []"
      :empty-text="t('team.nothingUnassigned')"
    />
  </div>
</template>
