<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Select from 'primevue/select'
import TreeSelect from 'primevue/treeselect'
import Button from 'primevue/button'
import Message from 'primevue/message'
import PageHeader from '@/components/PageHeader.vue'
import { customersApi, lookupsApi, ticketsApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import type { TreeNode } from 'primevue/treenode'
import {
  CommunicationChannel,
  TicketPriority,
  type Agent,
  type CategoryLookup,
  type CustomerListItem,
  type Lookup,
} from '@/types/api'
import { authApi } from '@/api/services'

const props = defineProps<{ id?: string }>()

const { t } = useI18n()
const route = useRoute()
const router = useRouter()
const toast = useToast()
const ui = useUiStore()

const isEdit = !!props.id

const customerId = ref<string | null>((route.query.customerId as string) ?? null)
const subject = ref('')
const description = ref('')
const priority = ref<TicketPriority>(TicketPriority.Normal)
const channel = ref<CommunicationChannel>(CommunicationChannel.WebForm)
const categoryKey = ref<Record<string, boolean> | null>(null)
const departmentId = ref<string | null>(null)
const branchId = ref<string | null>(null)
const assignedAgentId = ref<string | null>(null)

const customers = ref<CustomerListItem[]>([])
const customerSearch = ref('')
const categories = ref<CategoryLookup[]>([])
const departments = ref<Lookup[]>([])
const branches = ref<Lookup[]>([])
const agents = ref<Agent[]>([])

const loading = ref(isEdit)
const saving = ref(false)
const error = ref<string | null>(null)

const priorityOptions = Object.entries(TicketPriority)
  .filter(([, v]) => typeof v === 'number')
  .map(([name, value]) => ({ label: t(`priority.${name}`), value: value as TicketPriority }))

const channelOptions = Object.entries(CommunicationChannel)
  .filter(([, v]) => typeof v === 'number')
  .map(([name, value]) => ({ label: t(`channel.${name}`), value: value as CommunicationChannel }))

/** TreeSelect wants { key, label, children }; the API returns the raw category tree. */
function toTreeNodes(nodes: CategoryLookup[]): TreeNode[] {
  return nodes.map((node) => ({
    key: node.id,
    label: ui.localized(node),
    // Parents are selectable: "Technical Issue" is a valid categorisation on its own.
    selectable: true,
    children: node.children.length ? toTreeNodes(node.children) : undefined,
  }))
}

/** TreeSelect models selection as a key map; the API wants a single id. */
function selectedCategoryId(): string | null {
  const keys = Object.keys(categoryKey.value ?? {})
  return keys.length ? keys[0] : null
}

let customerTimer: ReturnType<typeof setTimeout> | undefined

async function searchCustomers(term: string) {
  try {
    const page = await customersApi.search({ search: term || undefined, pageSize: 25 })
    customers.value = page.items
  } catch {
    // The picker degrades to whatever was already loaded.
  }
}

watch(customerSearch, (term) => {
  clearTimeout(customerTimer)
  customerTimer = setTimeout(() => searchCustomers(term), 300)
})

// Narrowing the department should narrow the categories and agents offered with it.
watch(departmentId, async (value) => {
  const [cats, ags] = await Promise.allSettled([lookupsApi.categories(value), authApi.agents(value)])
  if (cats.status === 'fulfilled') categories.value = cats.value
  if (ags.status === 'fulfilled') agents.value = ags.value
})

async function save() {
  if (saving.value) return

  error.value = null

  if (!customerId.value) {
    error.value = t('ticket.customer') + ' — ' + t('app.required')
    return
  }

  saving.value = true
  try {
    if (isEdit) {
      const updated = await ticketsApi.update(props.id!, {
        subject: subject.value.trim(),
        description: description.value.trim(),
        priority: priority.value,
        categoryId: selectedCategoryId(),
        departmentId: departmentId.value,
        branchId: branchId.value,
      })
      toast.add({ severity: 'success', summary: t('ticket.updated'), life: 3000 })
      await router.push({ name: 'ticket-detail', params: { id: updated.id } })
    } else {
      const created = await ticketsApi.create({
        customerId: customerId.value,
        subject: subject.value.trim(),
        description: description.value.trim(),
        priority: priority.value,
        channel: channel.value,
        categoryId: selectedCategoryId(),
        departmentId: departmentId.value,
        branchId: branchId.value,
        assignedAgentId: assignedAgentId.value,
      })
      toast.add({
        severity: 'success',
        summary: t('ticket.created', { number: created.number }),
        life: 4000,
      })
      await router.push({ name: 'ticket-detail', params: { id: created.id } })
    }
  } catch (e) {
    error.value = problemMessage(e, t('error.saveFailed'))
  } finally {
    saving.value = false
  }
}

onMounted(async () => {
  const [cats, deps, brs, ags] = await Promise.allSettled([
    lookupsApi.categories(),
    lookupsApi.departments(),
    lookupsApi.branches(),
    authApi.agents(),
  ])
  if (cats.status === 'fulfilled') categories.value = cats.value
  if (deps.status === 'fulfilled') departments.value = deps.value
  if (brs.status === 'fulfilled') branches.value = brs.value
  if (ags.status === 'fulfilled') agents.value = ags.value

  await searchCustomers('')

  if (isEdit) {
    try {
      const existing = await ticketsApi.getById(props.id!)
      customerId.value = existing.customerId
      subject.value = existing.subject
      description.value = existing.description
      priority.value = existing.priority
      channel.value = existing.channel
      categoryKey.value = existing.categoryId ? { [existing.categoryId]: true } : null
      departmentId.value = existing.departmentId
      branchId.value = existing.branchId
      assignedAgentId.value = existing.assignedAgentId
    } catch (e) {
      error.value = problemMessage(e, t('error.loadFailed'))
    } finally {
      loading.value = false
    }
  } else if (customerId.value) {
    // Arrived from a customer page: make sure that customer is in the picker's options.
    try {
      const customer = await customersApi.getById(customerId.value)
      if (!customers.value.some((c) => c.id === customer.id)) {
        customers.value = [
          {
            id: customer.id,
            code: customer.code,
            fullNameAr: customer.fullNameAr,
            fullNameEn: customer.fullNameEn,
            email: customer.email,
            phone: customer.phone,
            companyName: customer.companyName,
            departmentNameAr: customer.departmentNameAr,
            departmentNameEn: customer.departmentNameEn,
            branchNameAr: customer.branchNameAr,
            branchNameEn: customer.branchNameEn,
            isActive: customer.isActive,
            openTicketCount: 0,
            createdAt: customer.createdAt,
          },
          ...customers.value,
        ]
      }
      departmentId.value ??= customer.departmentId
      branchId.value ??= customer.branchId
    } catch {
      customerId.value = null
    }
  }
})
</script>

<template>
  <div class="mx-auto max-w-4xl">
    <PageHeader :title="isEdit ? t('ticket.edit') : t('ticket.new')">
      <template #actions>
        <Button :label="t('app.cancel')" outlined size="small" @click="router.back()" />
        <Button :label="t('app.save')" :loading="saving" size="small" @click="save" />
      </template>
    </PageHeader>

    <Message v-if="error" severity="error" :closable="false" class="mb-4">{{ error }}</Message>

    <form v-if="!loading" class="flex flex-col gap-6" @submit.prevent="save">
      <section
        class="rounded-xl border border-surface-200 bg-surface-0 p-4 dark:border-surface-800 dark:bg-surface-900"
      >
        <div class="grid gap-4 sm:grid-cols-2">
          <div class="flex flex-col gap-1.5 sm:col-span-2">
            <label class="text-sm font-medium">{{ t('ticket.customer') }} *</label>
            <Select
              v-model="customerId"
              :options="customers"
              :option-label="(c: CustomerListItem) => `${c.code} — ${ui.isArabic ? c.fullNameAr : c.fullNameEn}`"
              option-value="id"
              filter
              :filter-placeholder="t('app.searchPlaceholder')"
              :disabled="isEdit"
              class="w-full"
              @filter="(e: { value: string }) => (customerSearch = e.value)"
            />
          </div>

          <div class="flex flex-col gap-1.5 sm:col-span-2">
            <label class="text-sm font-medium">{{ t('ticket.subject') }} *</label>
            <InputText v-model="subject" required maxlength="300" class="w-full" />
          </div>

          <div class="flex flex-col gap-1.5 sm:col-span-2">
            <label class="text-sm font-medium">{{ t('ticket.description') }} *</label>
            <Textarea v-model="description" rows="5" auto-resize required class="w-full" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('ticket.priority') }}</label>
            <Select
              v-model="priority"
              :options="priorityOptions"
              option-label="label"
              option-value="value"
              class="w-full"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('ticket.channel') }}</label>
            <Select
              v-model="channel"
              :options="channelOptions"
              option-label="label"
              option-value="value"
              :disabled="isEdit"
              class="w-full"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('ticket.category') }}</label>
            <TreeSelect
              v-model="categoryKey"
              :options="toTreeNodes(categories)"
              selection-mode="single"
              :placeholder="t('app.none')"
              class="w-full"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('ticket.department') }}</label>
            <Select
              v-model="departmentId"
              :options="departments"
              :option-label="(d: Lookup) => ui.localized(d)"
              option-value="id"
              show-clear
              class="w-full"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('ticket.branch') }}</label>
            <Select
              v-model="branchId"
              :options="branches"
              :option-label="(b: Lookup) => ui.localized(b)"
              option-value="id"
              show-clear
              class="w-full"
            />
          </div>

          <div v-if="!isEdit" class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('ticket.assignTo') }}</label>
            <Select
              v-model="assignedAgentId"
              :options="agents"
              :option-label="(a: Agent) => `${ui.localizedFullName(a)} (${a.openTicketCount})`"
              option-value="id"
              show-clear
              :placeholder="t('ticket.unassigned')"
              class="w-full"
            />
          </div>
        </div>
      </section>
    </form>
  </div>
</template>
