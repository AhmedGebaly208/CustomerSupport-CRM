<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Skeleton from 'primevue/skeleton'
import Button from 'primevue/button'
import PageHeader from '@/components/PageHeader.vue'
import StatusTag from '@/components/StatusTag.vue'
import { workspaceApi } from '@/api/services'
import AgentTasksPanel from '@/components/AgentTasksPanel.vue'
import TicketMiniList from '@/components/TicketMiniList.vue'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { useAuthStore } from '@/stores/auth'
import { useFormat } from '@/composables/useFormat'
import type { AgentWorkspace } from '@/types/api'

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const ui = useUiStore()
const auth = useAuthStore()
const { formatDateTime, formatNumber, isOverdue } = useFormat()

const workspace = ref<AgentWorkspace | null>(null)
const loading = ref(true)

/** The board's ticket counters. Kept as a computed rather than a second request so the
 *  whole page renders from one payload and cannot show panels from different moments. */
const board = computed(() => workspace.value?.tickets ?? null)

const tiles = () => [
  { key: 'assignedActive', icon: 'pi pi-inbox', value: board.value?.assignedActive ?? 0, tone: 'text-primary' },
  { key: 'assignedNew', icon: 'pi pi-sparkles', value: board.value?.assignedNew ?? 0, tone: 'text-blue-500' },
  { key: 'assignedOverdue', icon: 'pi pi-exclamation-triangle', value: board.value?.assignedOverdue ?? 0, tone: 'text-red-500' },
  { key: 'unassigned', icon: 'pi pi-users', value: board.value?.unassignedInDepartment ?? 0, tone: 'text-amber-500' },
  { key: 'resolvedToday', icon: 'pi pi-check-circle', value: board.value?.resolvedToday ?? 0, tone: 'text-emerald-500' },
]

async function load() {
  loading.value = true
  try {
    workspace.value = await workspaceApi.mine()
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 5000 })
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>

<template>
  <div>
    <PageHeader
      :title="t('dashboard.title')"
      :subtitle="auth.user ? t('auth.welcome', { name: ui.localizedFullName(auth.user) }) : undefined"
    >
      <template #actions>
        <Button icon="pi pi-refresh" :label="t('app.search')" outlined size="small" @click="load" />
        <Button
          icon="pi pi-plus"
          :label="t('ticket.new')"
          size="small"
          @click="router.push({ name: 'ticket-new' })"
        />
      </template>
    </PageHeader>

    <!-- Stat tiles -->
    <div class="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-5">
      <div
        v-for="tile in tiles()"
        :key="tile.key"
        class="rounded-xl border border-surface-200 bg-surface-0 p-4 dark:border-surface-800 dark:bg-surface-900"
      >
        <div class="flex items-center gap-2 text-sm text-surface-500 dark:text-surface-400">
          <i :class="[tile.icon, tile.tone]" />
          <span class="truncate">{{ t(`dashboard.${tile.key}`) }}</span>
        </div>
        <Skeleton v-if="loading" height="2rem" class="mt-2" />
        <div v-else class="ltr-nums mt-1 text-2xl font-semibold">{{ formatNumber(tile.value) }}</div>
      </div>
    </div>

    <!-- My tickets -->
    <div
      class="mt-6 rounded-xl border border-surface-200 bg-surface-0 dark:border-surface-800 dark:bg-surface-900"
    >
      <div class="border-b border-surface-200 px-4 py-3 dark:border-surface-800">
        <h2 class="font-semibold">{{ t('dashboard.myTickets') }}</h2>
      </div>

      <div v-if="loading" class="flex flex-col gap-2 p-4">
        <Skeleton v-for="n in 4" :key="n" height="2.5rem" />
      </div>

      <DataTable
        v-else
        :value="board?.recentAssigned ?? []"
        data-key="id"
        size="small"
        striped-rows
        row-hover
        class="cursor-pointer"
        @row-click="(e: { data: { id: string } }) => router.push({ name: 'ticket-detail', params: { id: e.data.id } })"
      >
        <template #empty>
          <div class="p-6 text-center text-surface-500 dark:text-surface-400">
            {{ t('dashboard.empty') }}
          </div>
        </template>

        <Column field="number" :header="t('ticket.number')">
          <template #body="{ data }">
            <span class="ltr-nums font-medium">{{ data.number }}</span>
          </template>
        </Column>

        <Column field="subject" :header="t('ticket.subject')">
          <template #body="{ data }">
            <span class="line-clamp-1">{{ data.subject }}</span>
          </template>
        </Column>

        <Column :header="t('ticket.customer')">
          <template #body="{ data }">
            {{ ui.isArabic ? data.customerNameAr : data.customerNameEn }}
          </template>
        </Column>

        <Column :header="t('ticket.status')">
          <template #body="{ data }"><StatusTag :status="data.status" /></template>
        </Column>

        <Column :header="t('ticket.priority')">
          <template #body="{ data }"><StatusTag :priority="data.priority" /></template>
        </Column>

        <Column :header="t('ticket.resolutionDue')">
          <template #body="{ data }">
            <span :class="isOverdue(data.resolutionDueAt) ? 'font-medium text-red-500' : ''">
              {{ formatDateTime(data.resolutionDueAt) }}
            </span>
          </template>
        </Column>
      </DataTable>
    </div>

    <div class="mt-6 grid gap-4 lg:grid-cols-3">
      <AgentTasksPanel
        :tasks="workspace?.openTasks ?? []"
        :overdue-count="workspace?.overdueTaskCount ?? 0"
        @changed="load"
      />

      <TicketMiniList
        :title="t('dashboard.mentions')"
        icon="pi pi-at"
        :tickets="workspace?.mentionedTickets ?? []"
        :empty-text="t('dashboard.noMentions')"
      />

      <TicketMiniList
        :title="t('dashboard.watching')"
        icon="pi pi-eye"
        :tickets="workspace?.watchedTickets ?? []"
        :empty-text="t('dashboard.notWatching')"
      />
    </div>
  </div>
</template>
