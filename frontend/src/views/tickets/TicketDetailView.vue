<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Select from 'primevue/select'
import Textarea from 'primevue/textarea'
import Menu from 'primevue/menu'
import Checkbox from 'primevue/checkbox'
import Tag from 'primevue/tag'
import Skeleton from 'primevue/skeleton'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import Dialog from 'primevue/dialog'
import PageHeader from '@/components/PageHeader.vue'
import StatusTag from '@/components/StatusTag.vue'
import TicketSidePanels from '@/components/TicketSidePanels.vue'
import { authApi, slaApi, ticketsApi, workspaceApi } from '@/api/services'
import SlaBadge from '@/components/SlaBadge.vue'
import TicketArticles from '@/components/TicketArticles.vue'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { useFormat } from '@/composables/useFormat'
import {
  CommunicationChannel,
  TicketStatus,
  type Agent,
  type TicketComment,
  type TicketDetail,
  type TicketHistoryEntry,
  type QuickReply,
  type TicketSlaStatus,
} from '@/types/api'

const props = defineProps<{ id: string }>()

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const ui = useUiStore()
const { formatDateTime, formatRelative, isOverdue } = useFormat()

const ticket = ref<TicketDetail | null>(null)
const comments = ref<TicketComment[]>([])
const history = ref<TicketHistoryEntry[]>([])
const agents = ref<Agent[]>([])
const loading = ref(true)

const newComment = ref('')

/** Saved snippets the agent can drop into a reply. The body is inserted in the customer's
 *  language, which is the ticket's, while the title lists in the agent's own. */
const quickReplies = ref<QuickReply[]>([])
const quickReplyMenu = ref()

const quickReplyItems = computed(() =>
  quickReplies.value.map((reply) => ({
    label: ui.isArabic ? reply.titleAr : reply.titleEn,
    command: () => insertQuickReply(reply),
  })),
)

function insertQuickReply(reply: QuickReply) {
  const body = ticket.value?.customerPreferredLanguage === 'en' ? reply.bodyEn : reply.bodyAr
  // Appended rather than replacing, so a half-written reply is not thrown away.
  newComment.value = newComment.value ? `${newComment.value}

${body}` : body
}
const commentInternal = ref(false)
const savingComment = ref(false)

const statusDialog = ref(false)
const pendingStatus = ref<TicketStatus | null>(null)
const statusNote = ref('')
const changingStatus = ref(false)

const assignDialog = ref(false)
const pendingAgentId = ref<string | null>(null)
const assigning = ref(false)

/** Only the transitions the server's domain workflow permits from the current status. */
const nextStatuses = computed(() =>
  (ticket.value?.allowedNextStatuses ?? []).map((status) => ({
    status,
    label: t(`status.${TicketStatus[status]}`),
  })),
)

/** Live SLA state. Kept beside the ticket rather than on it because it is derived from the
 *  clock and changes every minute, while the ticket itself does not. */
const slaStatus = ref<TicketSlaStatus | null>(null)

async function load() {
  loading.value = true
  try {
    ticket.value = await ticketsApi.getById(props.id)

    slaApi
      .ticketStatus(props.id)
      .then((s) => (slaStatus.value = s))
      // The ticket is readable without its badges, so this failure stays quiet.
      .catch(() => (slaStatus.value = null))

    workspaceApi
      .quickReplies()
      .then((r) => (quickReplies.value = r))
      // Snippets are a convenience; the reply box works without them.
      .catch(() => (quickReplies.value = []))

    const [c, h, a] = await Promise.allSettled([
      ticketsApi.comments(props.id),
      ticketsApi.history(props.id),
      authApi.agents(ticket.value.departmentId),
    ])

    if (c.status === 'fulfilled') comments.value = c.value
    if (h.status === 'fulfilled') history.value = h.value
    if (a.status === 'fulfilled') agents.value = a.value
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 5000 })
  } finally {
    loading.value = false
  }
}

async function refreshSideData() {
  const [c, h] = await Promise.allSettled([ticketsApi.comments(props.id), ticketsApi.history(props.id)])
  if (c.status === 'fulfilled') comments.value = c.value
  if (h.status === 'fulfilled') history.value = h.value
}

async function addComment() {
  if (!newComment.value.trim() || savingComment.value) return

  savingComment.value = true
  try {
    await ticketsApi.addComment(props.id, newComment.value.trim(), commentInternal.value)
    newComment.value = ''
    // A public reply can move the ticket New -> Open and stamp the first response,
    // so reload the header too rather than only appending the comment.
    ticket.value = await ticketsApi.getById(props.id)
    await refreshSideData()
    toast.add({ severity: 'success', summary: t('ticket.commentAdded'), life: 2500 })
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 5000 })
  } finally {
    savingComment.value = false
  }
}

function openStatusDialog(status: TicketStatus) {
  pendingStatus.value = status
  statusNote.value = ''
  statusDialog.value = true
}

async function confirmStatus() {
  if (pendingStatus.value === null || changingStatus.value) return

  changingStatus.value = true
  try {
    ticket.value = await ticketsApi.changeStatus(props.id, pendingStatus.value, statusNote.value || null)
    await refreshSideData()
    statusDialog.value = false
    toast.add({ severity: 'success', summary: t('ticket.statusChanged'), life: 2500 })
  } catch (e) {
    // A 409 here means the workflow rejected the move; show the server's reason.
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 6000 })
  } finally {
    changingStatus.value = false
  }
}

function openAssignDialog() {
  pendingAgentId.value = ticket.value?.assignedAgentId ?? null
  assignDialog.value = true
}

async function confirmAssign() {
  if (assigning.value) return

  assigning.value = true
  try {
    ticket.value = await ticketsApi.assign(props.id, pendingAgentId.value)
    await refreshSideData()
    assignDialog.value = false
    toast.add({ severity: 'success', summary: t('ticket.assigned'), life: 2500 })
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 6000 })
  } finally {
    assigning.value = false
  }
}

onMounted(load)
</script>

<template>
  <div>
    <template v-if="loading">
      <Skeleton height="2.5rem" class="mb-4 max-w-md" />
      <Skeleton height="14rem" />
    </template>

    <template v-else-if="ticket">
      <PageHeader :title="ticket.subject">
        <template #actions>
          <Button
            icon="pi pi-user-edit"
            :label="t('ticket.assign')"
            outlined
            size="small"
            @click="openAssignDialog"
          />
          <Button
            v-for="option in nextStatuses"
            :key="option.status"
            :label="option.label"
            size="small"
            :severity="option.status === TicketStatus.Closed ? 'secondary' : 'primary'"
            outlined
            @click="openStatusDialog(option.status)"
          />
          <Button
            icon="pi pi-pencil"
            size="small"
            :aria-label="t('app.edit')"
            @click="router.push({ name: 'ticket-edit', params: { id: ticket!.id } })"
          />
        </template>
      </PageHeader>

      <!-- Header facts -->
      <section
        class="mb-5 rounded-xl border border-surface-200 bg-surface-0 p-4 dark:border-surface-800 dark:bg-surface-900"
      >
        <div class="mb-4 flex flex-wrap items-center gap-2">
          <span class="ltr-nums text-lg font-semibold">{{ ticket.number }}</span>
          <StatusTag :status="ticket.status" />
          <StatusTag :priority="ticket.priority" />
          <Tag severity="secondary" :value="t(`channel.${CommunicationChannel[ticket.channel]}`)" rounded />
          <Tag
            v-if="ticket.escalationLevel > 0"
            severity="danger"
            :value="`${t('ticket.escalationLevel')} ${ticket.escalationLevel}`"
            rounded
          />
        </div>

        <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('ticket.customer') }}</div>
            <RouterLink
              :to="{ name: 'customer-detail', params: { id: ticket.customerId } }"
              class="font-medium text-primary hover:underline"
            >
              {{ ui.isArabic ? ticket.customerNameAr : ticket.customerNameEn }}
            </RouterLink>
            <div class="ltr-nums text-xs text-surface-500 dark:text-surface-400">{{ ticket.customerCode }}</div>
          </div>

          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('ticket.assignedAgent') }}</div>
            <div>{{ ticket.assignedAgentName ?? t('ticket.unassigned') }}</div>
          </div>

          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('ticket.category') }}</div>
            <div>{{ (ui.isArabic ? ticket.categoryNameAr : ticket.categoryNameEn) ?? '—' }}</div>
          </div>

          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('ticket.department') }}</div>
            <div>{{ (ui.isArabic ? ticket.departmentNameAr : ticket.departmentNameEn) ?? '—' }}</div>
          </div>

          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('ticket.createdAt') }}</div>
            <div class="text-sm">{{ formatDateTime(ticket.createdAt) }}</div>
          </div>

          <div class="sm:col-span-2">
            <TicketArticles :ticket-id="props.id" @insert="(text) => (newComment = newComment ? `${newComment}
${text}` : text)" />
          </div>

          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('ticket.firstResponse') }}</div>
            <SlaBadge v-if="slaStatus" :clock="slaStatus.firstResponse" show-remaining class="mt-1" />
            <div v-else class="text-sm">{{ formatDateTime(ticket.firstRespondedAt) }}</div>
          </div>

          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('ticket.resolutionDue') }}</div>
            <SlaBadge v-if="slaStatus" :clock="slaStatus.resolution" show-remaining class="mt-1" />
            <div
              v-else
              class="text-sm"
              :class="isOverdue(ticket.resolutionDueAt) ? 'font-medium text-red-500' : ''"
            >
              {{ formatDateTime(ticket.resolutionDueAt) }}
            </div>
          </div>

          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('ticket.resolvedAt') }}</div>
            <div class="text-sm">{{ formatDateTime(ticket.resolvedAt) }}</div>
          </div>
        </div>

        <div class="mt-4 border-t border-surface-200 pt-3 dark:border-surface-800">
          <div class="mb-1 text-xs text-surface-500 dark:text-surface-400">{{ t('ticket.description') }}</div>
          <p class="whitespace-pre-wrap text-sm">{{ ticket.description }}</p>
        </div>
      </section>

      <div class="grid gap-5 lg:grid-cols-[1fr_20rem]">
      <div class="min-w-0">
      <Tabs value="comments">
        <TabList>
          <Tab value="comments">{{ t('ticket.comments') }}</Tab>
          <Tab value="history">{{ t('ticket.history') }}</Tab>
        </TabList>

        <TabPanels>
          <TabPanel value="comments">
            <div class="flex flex-col gap-2 py-2">
              <Textarea
                v-model="newComment"
                rows="3"
                auto-resize
                :placeholder="t('ticket.commentPlaceholder')"
                class="w-full"
              />
              <div class="flex flex-wrap items-center gap-3">
                <label class="flex items-center gap-2 text-sm">
                  <Checkbox v-model="commentInternal" binary />
                  {{ t('ticket.internalComment') }}
                </label>

                <Button
                  v-if="quickReplies.length > 0"
                  icon="pi pi-bolt"
                  :label="t('quickReply.insert')"
                  text
                  size="small"
                  @click="quickReplyMenu?.toggle($event)"
                />

                <Menu ref="quickReplyMenu" :model="quickReplyItems" popup />
                <span class="text-xs text-surface-500 dark:text-surface-400">
                  {{ commentInternal ? t('ticket.internalComment') : t('ticket.publicComment') }}
                </span>
                <Button
                  :label="t('ticket.addComment')"
                  size="small"
                  class="ms-auto"
                  :loading="savingComment"
                  :disabled="!newComment.trim()"
                  @click="addComment"
                />
              </div>
            </div>

            <div v-if="!comments.length" class="p-6 text-center text-surface-500 dark:text-surface-400">
              {{ t('ticket.noComments') }}
            </div>

            <ul v-else class="mt-2 flex flex-col gap-2">
              <li
                v-for="comment in comments"
                :key="comment.id"
                class="rounded-lg border p-3"
                :class="
                  comment.isInternal
                    ? 'border-amber-300 bg-amber-50 dark:border-amber-800 dark:bg-amber-950/30'
                    : 'border-surface-200 dark:border-surface-800'
                "
              >
                <p class="whitespace-pre-wrap text-sm">{{ comment.body }}</p>
                <div class="mt-2 flex flex-wrap items-center gap-2 text-xs text-surface-500 dark:text-surface-400">
                  <Tag v-if="comment.isInternal" severity="warn" :value="t('ticket.internalComment')" rounded />
                  <span>{{ comment.authorName ?? '—' }}</span>
                  <span class="ms-auto">{{ formatDateTime(comment.createdAt) }}</span>
                </div>
              </li>
            </ul>
          </TabPanel>

          <TabPanel value="history">
            <div v-if="!history.length" class="p-6 text-center text-surface-500 dark:text-surface-400">
              {{ t('ticket.noHistory') }}
            </div>

            <ol v-else class="flex flex-col gap-3 py-2">
              <li v-for="entry in history" :key="entry.id" class="flex gap-3">
                <div class="mt-1.5 h-2 w-2 shrink-0 rounded-full bg-primary" />
                <div class="min-w-0 flex-1">
                  <div class="text-sm">
                    <span class="font-medium">{{ entry.field }}</span>
                    <span class="text-surface-500 dark:text-surface-400">
                      : {{ entry.oldValue ?? '—' }} → {{ entry.newValue ?? '—' }}
                    </span>
                  </div>
                  <div v-if="entry.note" class="mt-0.5 text-sm text-surface-600 dark:text-surface-300">
                    {{ entry.note }}
                  </div>
                  <div class="mt-0.5 text-xs text-surface-500 dark:text-surface-400">
                    {{ t('ticket.changedBy') }} {{ entry.changedByName ?? '—' }} ·
                    {{ formatRelative(entry.changedAt) }}
                  </div>
                </div>
              </li>
            </ol>
          </TabPanel>
        </TabPanels>
      </Tabs>
      </div>

      <TicketSidePanels :ticket="ticket" @changed="(t) => (ticket = t)" />
      </div>

      <!-- Status change -->
      <Dialog
        v-model:visible="statusDialog"
        modal
        :header="t('ticket.changeStatus')"
        :style="{ width: '28rem' }"
      >
        <div class="flex flex-col gap-3">
          <div v-if="pendingStatus !== null" class="flex items-center gap-2">
            <StatusTag :status="ticket.status" />
            <i class="pi" :class="ui.isArabic ? 'pi-arrow-left' : 'pi-arrow-right'" />
            <StatusTag :status="pendingStatus" />
          </div>
          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('ticket.statusNote') }}</label>
            <Textarea v-model="statusNote" rows="3" auto-resize class="w-full" />
          </div>
        </div>
        <template #footer>
          <Button :label="t('app.cancel')" outlined @click="statusDialog = false" />
          <Button :label="t('app.confirm')" :loading="changingStatus" @click="confirmStatus" />
        </template>
      </Dialog>

      <!-- Assignment -->
      <Dialog v-model:visible="assignDialog" modal :header="t('ticket.assignTo')" :style="{ width: '28rem' }">
        <Select
          v-model="pendingAgentId"
          :options="agents"
          :option-label="(a: Agent) => `${ui.localizedFullName(a)} (${a.openTicketCount})`"
          option-value="id"
          show-clear
          :placeholder="t('ticket.unassigned')"
          class="w-full"
        />
        <template #footer>
          <Button :label="t('app.cancel')" outlined @click="assignDialog = false" />
          <Button :label="t('app.confirm')" :loading="assigning" @click="confirmAssign" />
        </template>
      </Dialog>
    </template>
  </div>
</template>
