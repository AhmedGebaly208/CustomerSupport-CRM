<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import Tag from 'primevue/tag'
import Textarea from 'primevue/textarea'
import Checkbox from 'primevue/checkbox'
import Skeleton from 'primevue/skeleton'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import PageHeader from '@/components/PageHeader.vue'
import StatusTag from '@/components/StatusTag.vue'
import CustomerActivity from '@/components/CustomerActivity.vue'
import CustomerAttachments from '@/components/CustomerAttachments.vue'
import { customersApi, ticketsApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { useAuthStore } from '@/stores/auth'
import { useFormat } from '@/composables/useFormat'
import {
  CommunicationChannel,
  ContactType,
  InteractionDirection,
  type CustomerDetail,
  type CustomerNote,
  type Interaction,
  type TicketListItem,
} from '@/types/api'

const props = defineProps<{ id: string }>()

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const confirm = useConfirm()
const ui = useUiStore()
const auth = useAuthStore()
const { formatDate, formatDateTime } = useFormat()

const customer = ref<CustomerDetail | null>(null)
const notes = ref<CustomerNote[]>([])
const interactions = ref<Interaction[]>([])
const tickets = ref<TicketListItem[]>([])
const loading = ref(true)

const newNote = ref('')
const newNoteInternal = ref(true)
const savingNote = ref(false)

async function load() {
  loading.value = true
  try {
    customer.value = await customersApi.getById(props.id)

    // Independent panels: one failing should not blank the whole page.
    const [n, i, tk] = await Promise.allSettled([
      customersApi.notes(props.id),
      customersApi.interactions(props.id, 1, 50),
      ticketsApi.search({ customerId: props.id, pageSize: 50 }),
    ])

    if (n.status === 'fulfilled') notes.value = n.value
    if (i.status === 'fulfilled') interactions.value = i.value.items
    if (tk.status === 'fulfilled') tickets.value = tk.value.items
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 5000 })
  } finally {
    loading.value = false
  }
}

async function addNote() {
  if (!newNote.value.trim() || savingNote.value) return

  savingNote.value = true
  try {
    const note = await customersApi.addNote(props.id, newNote.value.trim(), newNoteInternal.value)
    notes.value = [note, ...notes.value]
    newNote.value = ''
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 5000 })
  } finally {
    savingNote.value = false
  }
}

function confirmDelete() {
  confirm.require({
    message: t('customer.deleteConfirm'),
    header: t('app.confirm'),
    icon: 'pi pi-exclamation-triangle',
    acceptProps: { label: t('app.delete'), severity: 'danger' },
    rejectProps: { label: t('app.cancel'), outlined: true },
    accept: async () => {
      try {
        await customersApi.remove(props.id)
        toast.add({ severity: 'success', summary: t('customer.deleted'), life: 3000 })
        await router.push({ name: 'customers' })
      } catch (e) {
        // The API refuses deletion while active tickets remain; show that reason verbatim.
        toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 6000 })
      }
    },
  })
}

onMounted(load)
</script>

<template>
  <div>
    <template v-if="loading">
      <Skeleton height="2.5rem" class="mb-4 max-w-sm" />
      <Skeleton height="12rem" />
    </template>

    <template v-else-if="customer">
      <PageHeader
        :title="ui.isArabic ? customer.fullNameAr : customer.fullNameEn"
        :subtitle="customer.companyName ?? undefined"
      >
        <template #actions>
          <Button
            icon="pi pi-ticket"
            :label="t('ticket.new')"
            outlined
            size="small"
            @click="router.push({ name: 'ticket-new', query: { customerId: customer!.id } })"
          />
          <Button
            icon="pi pi-pencil"
            :label="t('app.edit')"
            size="small"
            @click="router.push({ name: 'customer-edit', params: { id: customer!.id } })"
          />
          <Button
            v-if="auth.isSupervisor"
            icon="pi pi-trash"
            severity="danger"
            outlined
            size="small"
            :aria-label="t('app.delete')"
            @click="confirmDelete"
          />
        </template>
      </PageHeader>

      <!-- Profile summary -->
      <section
        class="mb-5 rounded-xl border border-surface-200 bg-surface-0 p-4 dark:border-surface-800 dark:bg-surface-900"
      >
        <div class="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('customer.code') }}</div>
            <div class="ltr-nums font-medium">{{ customer.code }}</div>
          </div>
          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('customer.email') }}</div>
            <div class="ltr-nums truncate">{{ customer.email ?? '—' }}</div>
          </div>
          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('customer.phone') }}</div>
            <div class="ltr-nums">{{ customer.phone ?? '—' }}</div>
          </div>
          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('customer.whatsapp') }}</div>
            <div class="ltr-nums">{{ customer.whatsAppNumber ?? '—' }}</div>
          </div>
          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('customer.department') }}</div>
            <div>{{ (ui.isArabic ? customer.departmentNameAr : customer.departmentNameEn) ?? '—' }}</div>
          </div>
          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('customer.branch') }}</div>
            <div>{{ (ui.isArabic ? customer.branchNameAr : customer.branchNameEn) ?? '—' }}</div>
          </div>
          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('customer.createdAt') }}</div>
            <div>{{ formatDate(customer.createdAt) }}</div>
          </div>
          <div>
            <div class="text-xs text-surface-500 dark:text-surface-400">{{ t('customer.status') }}</div>
            <Tag
              :severity="customer.isActive ? 'success' : 'secondary'"
              :value="customer.isActive ? t('customer.active') : t('customer.inactive')"
              rounded
            />
          </div>
        </div>

        <div v-if="customer.contacts.length" class="mt-4 border-t border-surface-200 pt-3 dark:border-surface-800">
          <div class="mb-2 text-xs text-surface-500 dark:text-surface-400">{{ t('customer.contacts') }}</div>
          <div class="flex flex-wrap gap-2">
            <Tag
              v-for="c in customer.contacts"
              :key="c.id"
              :severity="c.isPrimary ? 'info' : 'secondary'"
              rounded
            >
              <span class="text-xs">{{ t(`contactType.${ContactType[c.type]}`) }}:</span>
              <span class="ltr-nums ms-1">{{ c.value }}</span>
            </Tag>
          </div>
        </div>
      </section>

      <Tabs value="activity">
        <TabList>
          <Tab value="activity">{{ t('activity.title') }}</Tab>
          <Tab value="tickets">{{ t('customer.tickets') }}</Tab>
          <Tab value="attachments">{{ t('attachment.title') }}</Tab>
          <Tab value="interactions">{{ t('customer.interactions') }}</Tab>
          <Tab value="notes">{{ t('customer.notes') }}</Tab>
        </TabList>

        <TabPanels>
          <!-- Consolidated timeline -->
          <TabPanel value="activity">
            <CustomerActivity :customer-id="id" />
          </TabPanel>

          <!-- Attachments -->
          <TabPanel value="attachments">
            <CustomerAttachments :customer-id="id" />
          </TabPanel>

          <!-- Tickets -->
          <TabPanel value="tickets">
            <DataTable
              :value="tickets"
              data-key="id"
              size="small"
              striped-rows
              row-hover
              class="cursor-pointer"
              @row-click="(e: { data: TicketListItem }) => router.push({ name: 'ticket-detail', params: { id: e.data.id } })"
            >
              <template #empty>
                <div class="p-6 text-center text-surface-500 dark:text-surface-400">{{ t('app.noData') }}</div>
              </template>
              <Column field="number" :header="t('ticket.number')">
                <template #body="{ data }"><span class="ltr-nums font-medium">{{ data.number }}</span></template>
              </Column>
              <Column field="subject" :header="t('ticket.subject')" />
              <Column :header="t('ticket.status')">
                <template #body="{ data }"><StatusTag :status="data.status" /></template>
              </Column>
              <Column :header="t('ticket.priority')">
                <template #body="{ data }"><StatusTag :priority="data.priority" /></template>
              </Column>
              <Column :header="t('ticket.createdAt')">
                <template #body="{ data }">{{ formatDate(data.createdAt) }}</template>
              </Column>
            </DataTable>
          </TabPanel>

          <!-- Interaction history -->
          <TabPanel value="interactions">
            <div v-if="!interactions.length" class="p-6 text-center text-surface-500 dark:text-surface-400">
              {{ t('customer.noInteractions') }}
            </div>

            <ul v-else class="flex flex-col gap-3 py-2">
              <li
                v-for="item in interactions"
                :key="item.id"
                class="rounded-lg border border-surface-200 p-3 dark:border-surface-800"
              >
                <div class="flex flex-wrap items-center gap-2 text-sm">
                  <Tag
                    :severity="item.direction === InteractionDirection.Inbound ? 'info' : 'success'"
                    :value="t(`direction.${InteractionDirection[item.direction]}`)"
                    rounded
                  />
                  <span class="font-medium">{{ t(`channel.${CommunicationChannel[item.channel]}`) }}</span>
                  <span v-if="item.subject" class="text-surface-600 dark:text-surface-300">— {{ item.subject }}</span>
                  <span class="ms-auto text-xs text-surface-500 dark:text-surface-400">
                    {{ formatDateTime(item.occurredAt) }}
                  </span>
                </div>
                <p class="mt-2 whitespace-pre-wrap text-sm">{{ item.body }}</p>
                <div
                  v-if="item.ticketNumber || item.agentName"
                  class="mt-2 flex gap-3 text-xs text-surface-500 dark:text-surface-400"
                >
                  <span v-if="item.ticketNumber" class="ltr-nums">{{ item.ticketNumber }}</span>
                  <span v-if="item.agentName">{{ item.agentName }}</span>
                </div>
              </li>
            </ul>
          </TabPanel>

          <!-- Notes -->
          <TabPanel value="notes">
            <div class="flex flex-col gap-2 py-2">
              <Textarea
                v-model="newNote"
                rows="3"
                auto-resize
                :placeholder="t('customer.notePlaceholder')"
                class="w-full"
              />
              <div class="flex flex-wrap items-center gap-3">
                <label class="flex items-center gap-2 text-sm">
                  <Checkbox v-model="newNoteInternal" binary />
                  {{ t('customer.internalNote') }}
                </label>
                <Button
                  :label="t('customer.addNote')"
                  size="small"
                  class="ms-auto"
                  :loading="savingNote"
                  :disabled="!newNote.trim()"
                  @click="addNote"
                />
              </div>
            </div>

            <div v-if="!notes.length" class="p-6 text-center text-surface-500 dark:text-surface-400">
              {{ t('customer.noNotes') }}
            </div>

            <ul v-else class="mt-2 flex flex-col gap-2">
              <li
                v-for="note in notes"
                :key="note.id"
                class="rounded-lg border border-surface-200 p-3 dark:border-surface-800"
              >
                <p class="whitespace-pre-wrap text-sm">{{ note.body }}</p>
                <div class="mt-2 flex flex-wrap items-center gap-2 text-xs text-surface-500 dark:text-surface-400">
                  <Tag v-if="note.isInternal" severity="warn" :value="t('customer.internalNote')" rounded />
                  <span>{{ note.createdByName ?? '—' }}</span>
                  <span class="ms-auto">{{ formatDateTime(note.createdAt) }}</span>
                </div>
              </li>
            </ul>
          </TabPanel>
        </TabPanels>
      </Tabs>
    </template>
  </div>
</template>
