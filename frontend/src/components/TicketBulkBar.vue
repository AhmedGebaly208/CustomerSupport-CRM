<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Select from 'primevue/select'
import Dialog from 'primevue/dialog'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Tag from 'primevue/tag'
import { authApi, ticketsApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import {
  TicketPriority,
  TicketStatus,
  type Agent,
  type BulkOperationResult,
  type TicketListItem,
} from '@/types/api'

const props = defineProps<{ selection: TicketListItem[] }>()
const emit = defineEmits<{ (e: 'applied'): void; (e: 'clear'): void }>()

const { t } = useI18n()
const toast = useToast()
const ui = useUiStore()

const agents = ref<Agent[]>([])
const agentId = ref<string | null>(null)
const priority = ref<TicketPriority | null>(null)
const status = ref<TicketStatus | null>(null)
const busy = ref(false)

/** Shown only when at least one item failed, so partial success is never silent. */
const result = ref<BulkOperationResult | null>(null)

const priorityOptions = Object.entries(TicketPriority)
  .filter(([, v]) => typeof v === 'number')
  .map(([name, value]) => ({ label: t(`priority.${name}`), value: value as TicketPriority }))

const statusOptions = Object.entries(TicketStatus)
  .filter(([, v]) => typeof v === 'number')
  .map(([name, value]) => ({ label: t(`status.${name}`), value: value as TicketStatus }))

async function loadAgents() {
  if (agents.value.length) return
  try {
    agents.value = await authApi.agents()
  } catch {
    // The picker degrades to unassign-only rather than blocking the bar.
  }
}

async function run(operation: () => Promise<BulkOperationResult>) {
  if (busy.value) return

  busy.value = true
  try {
    const outcome = await operation()

    if (outcome.failedCount === 0) {
      toast.add({
        severity: 'success',
        summary: t('ticket.bulk.allSucceeded', { count: outcome.succeededCount }),
        life: 3000,
      })
    } else {
      // Partial success is the interesting case: the successes stand, and the failures
      // need to be shown per ticket rather than as one vague error.
      result.value = outcome
    }

    emit('applied')
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 6000 })
  } finally {
    busy.value = false
  }
}

const ids = () => props.selection.map((t) => t.id)

const applyAssign = () => run(() => ticketsApi.bulkAssign(ids(), agentId.value))
const applyPriority = () => run(() => ticketsApi.bulkPriority(ids(), priority.value!))
const applyStatus = () => run(() => ticketsApi.bulkStatus(ids(), status.value!))

function numberFor(id: string) {
  return props.selection.find((t) => t.id === id)?.number ?? id
}
</script>

<template>
  <div
    v-if="selection.length"
    class="mb-3 flex flex-wrap items-center gap-2 rounded-xl border border-primary/30 bg-primary/5 p-3"
  >
    <span class="font-medium">
      {{ t('ticket.bulk.selected', { count: selection.length }) }}
    </span>

    <div class="flex flex-wrap items-center gap-2 ms-auto">
      <Select
        v-model="agentId"
        :options="agents"
        :option-label="(a: Agent) => `${ui.localizedFullName(a)} (${a.openTicketCount})`"
        option-value="id"
        show-clear
        :placeholder="t('ticket.assignTo')"
        class="min-w-[12rem]"
        @before-show="loadAgents"
      />
      <Button
        :label="t('ticket.assign')"
        size="small"
        outlined
        :loading="busy"
        @click="applyAssign"
      />

      <Select
        v-model="priority"
        :options="priorityOptions"
        option-label="label"
        option-value="value"
        :placeholder="t('ticket.priority')"
        class="min-w-[10rem]"
      />
      <Button
        :label="t('app.save')"
        size="small"
        outlined
        :disabled="priority === null"
        :loading="busy"
        @click="applyPriority"
      />

      <Select
        v-model="status"
        :options="statusOptions"
        option-label="label"
        option-value="value"
        :placeholder="t('ticket.status')"
        class="min-w-[10rem]"
      />
      <Button
        :label="t('ticket.changeStatus')"
        size="small"
        outlined
        :disabled="status === null"
        :loading="busy"
        @click="applyStatus"
      />

      <Button
        icon="pi pi-times"
        text
        rounded
        size="small"
        :aria-label="t('app.cancel')"
        @click="emit('clear')"
      />
    </div>
  </div>

  <!-- Partial-failure report -->
  <Dialog
    :visible="result !== null"
    modal
    :header="t('ticket.bulk.partialTitle')"
    :style="{ width: '34rem' }"
    @update:visible="(v: boolean) => { if (!v) result = null }"
  >
    <div v-if="result" class="flex flex-col gap-3">
      <p class="text-sm">
        {{ t('ticket.bulk.partialBody', { ok: result.succeededCount, failed: result.failedCount }) }}
      </p>

      <DataTable :value="result.items.filter((i) => !i.succeeded)" size="small" striped-rows>
        <Column :header="t('ticket.number')">
          <template #body="{ data }">
            <span class="ltr-nums">{{ numberFor(data.ticketId) }}</span>
          </template>
        </Column>
        <Column :header="t('ticket.bulk.reason')">
          <template #body="{ data }">
            <Tag severity="danger" :value="data.errorMessage" rounded />
          </template>
        </Column>
      </DataTable>
    </div>

    <template #footer>
      <Button :label="t('app.close')" outlined @click="result = null" />
    </template>
  </Dialog>
</template>
