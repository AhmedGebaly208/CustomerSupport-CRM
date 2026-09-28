<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Checkbox from 'primevue/checkbox'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import DatePicker from 'primevue/datepicker'
import ToggleSwitch from 'primevue/toggleswitch'
import Tag from 'primevue/tag'
import { workspaceApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useFormat } from '@/composables/useFormat'
import type { AgentTask, SaveAgentTaskRequest } from '@/types/api'

const props = defineProps<{ tasks: AgentTask[]; overdueCount: number }>()
const emit = defineEmits<{ changed: [] }>()

const { t } = useI18n()
const toast = useToast()
const { formatDateTime } = useFormat()

const dialogOpen = ref(false)
const editingId = ref<string | null>(null)
const saving = ref(false)

const form = ref<SaveAgentTaskRequest & { dueDate: Date | null }>(empty())

function empty() {
  return {
    title: '',
    notes: null as string | null,
    dueAt: null as string | null,
    dueDate: null as Date | null,
    isReminder: false,
    ticketId: null as string | null,
    customerId: null as string | null,
  }
}

/** A reminder with no time to fire at is meaningless, so the form says so rather than
 *  letting the server reject it. */
const formError = computed(() => {
  if (!form.value.title.trim()) return t('task.error.titleRequired')
  if (form.value.isReminder && !form.value.dueDate) return t('task.error.reminderNeedsDate')
  return null
})

function openCreate() {
  editingId.value = null
  form.value = empty()
  dialogOpen.value = true
}

function openEdit(task: AgentTask) {
  editingId.value = task.id
  form.value = {
    title: task.title,
    notes: task.notes,
    dueAt: task.dueAt,
    dueDate: task.dueAt ? new Date(task.dueAt) : null,
    isReminder: task.isReminder,
    ticketId: task.ticketId,
    customerId: task.customerId,
  }
  dialogOpen.value = true
}

async function save() {
  if (formError.value || saving.value) return

  saving.value = true
  try {
    const request: SaveAgentTaskRequest = {
      title: form.value.title.trim(),
      notes: form.value.notes?.trim() || null,
      dueAt: form.value.dueDate ? form.value.dueDate.toISOString() : null,
      isReminder: form.value.isReminder,
      ticketId: form.value.ticketId,
      customerId: form.value.customerId,
    }

    if (editingId.value) await workspaceApi.updateTask(editingId.value, request)
    else await workspaceApi.createTask(request)

    dialogOpen.value = false
    emit('changed')
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 6000 })
  } finally {
    saving.value = false
  }
}

async function toggleDone(task: AgentTask) {
  try {
    await workspaceApi.setTaskDone(task.id, !task.isDone)
    emit('changed')
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
  }
}

async function remove(task: AgentTask) {
  try {
    await workspaceApi.deleteTask(task.id)
    emit('changed')
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
  }
}

const isOverdue = (task: AgentTask) => !!task.dueAt && !task.isDone && new Date(task.dueAt) < new Date()
</script>

<template>
  <div class="rounded-lg border border-surface-200 bg-surface-0 p-4 dark:border-surface-700 dark:bg-surface-900">
    <div class="mb-3 flex items-center justify-between gap-2">
      <div class="flex items-center gap-2">
        <h2 class="font-semibold">{{ t('task.title') }}</h2>
        <Tag
          v-if="props.overdueCount > 0"
          severity="danger"
          rounded
          :value="t('task.overdueCount', { n: props.overdueCount })"
        />
      </div>

      <Button icon="pi pi-plus" text rounded size="small" :aria-label="t('task.new')" @click="openCreate" />
    </div>

    <p v-if="props.tasks.length === 0" class="py-6 text-center text-sm text-surface-500 dark:text-surface-400">
      {{ t('task.empty') }}
    </p>

    <ul v-else class="divide-y divide-surface-200 dark:divide-surface-700">
      <li v-for="task in props.tasks" :key="task.id" class="group flex items-start gap-3 py-2">
        <Checkbox :model-value="task.isDone" binary class="mt-1" @update:model-value="toggleDone(task)" />

        <div class="min-w-0 flex-1">
          <div class="flex items-center gap-2">
            <span class="truncate text-sm" :class="task.isDone ? 'text-surface-400 line-through' : ''">
              {{ task.title }}
            </span>
            <i v-if="task.isReminder" class="pi pi-bell text-xs text-amber-500" />
          </div>

          <div class="flex flex-wrap items-center gap-2 text-xs text-surface-500 dark:text-surface-400">
            <span v-if="task.dueAt" :class="isOverdue(task) ? 'font-medium text-red-500' : ''">
              {{ formatDateTime(task.dueAt) }}
            </span>
            <RouterLink
              v-if="task.ticketId"
              :to="{ name: 'ticket-detail', params: { id: task.ticketId } }"
              class="ltr-nums text-primary hover:underline"
            >
              {{ task.ticketNumber }}
            </RouterLink>
          </div>
        </div>

        <div class="flex shrink-0 items-center gap-1 opacity-0 transition group-hover:opacity-100">
          <Button icon="pi pi-pencil" text rounded size="small" @click="openEdit(task)" />
          <Button icon="pi pi-trash" severity="danger" text rounded size="small" @click="remove(task)" />
        </div>
      </li>
    </ul>

    <Dialog
      v-model:visible="dialogOpen"
      modal
      :header="editingId ? t('task.edit') : t('task.new')"
      :style="{ width: '30rem' }"
      :breakpoints="{ '960px': '95vw' }"
    >
      <div class="space-y-4">
        <div>
          <label class="mb-1 block text-sm font-medium">{{ t('task.titleField') }} *</label>
          <InputText v-model="form.title" class="w-full" />
        </div>

        <div>
          <label class="mb-1 block text-sm font-medium">{{ t('task.notes') }}</label>
          <Textarea v-model="form.notes" rows="3" class="w-full" auto-resize />
        </div>

        <div>
          <label class="mb-1 block text-sm font-medium">{{ t('task.dueAt') }}</label>
          <DatePicker v-model="form.dueDate" show-time hour-format="24" show-clear class="w-full" />
        </div>

        <div class="flex items-center gap-2">
          <ToggleSwitch v-model="form.isReminder" input-id="task-reminder" />
          <label for="task-reminder" class="text-sm">{{ t('task.remindMe') }}</label>
        </div>

        <p v-if="formError" class="text-sm text-red-500">{{ formError }}</p>
      </div>

      <template #footer>
        <Button :label="t('app.cancel')" text @click="dialogOpen = false" />
        <Button :label="t('app.save')" :disabled="!!formError || saving" :loading="saving" @click="save" />
      </template>
    </Dialog>
  </div>
</template>
