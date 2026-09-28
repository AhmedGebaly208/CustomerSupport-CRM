<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import TreeTable from 'primevue/treetable'
import Column from 'primevue/column'
import Button from 'primevue/button'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import ToggleSwitch from 'primevue/toggleswitch'
import Tag from 'primevue/tag'
import Message from 'primevue/message'
import type { TreeNode } from 'primevue/treenode'
import PageHeader from '@/components/PageHeader.vue'
import { lookupsApi, ticketCategoriesApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import {
  type CategoryLookup,
  type CategoryReorderItem,
  type CategoryUpsertRequest,
  type Lookup,
} from '@/types/api'

const { t } = useI18n()
const toast = useToast()
const confirm = useConfirm()
const ui = useUiStore()

const tree = ref<CategoryLookup[]>([])
const departments = ref<Lookup[]>([])
const loading = ref(true)
const saving = ref(false)

const dialog = ref(false)
const editingId = ref<string | null>(null)
const error = ref<string | null>(null)

const form = ref<CategoryUpsertRequest>({
  nameAr: '',
  nameEn: '',
  parentId: null,
  departmentId: null,
  sortOrder: 0,
  isActive: true,
})

/** Flattened, for the parent picker and for sibling lookups during a move. */
interface FlatCategory {
  id: string
  nameAr: string
  nameEn: string
  parentId: string | null
  departmentId: string | null
  sortOrder: number
  depth: number
  isActive: boolean
}

const flat = computed<FlatCategory[]>(() => {
  const out: FlatCategory[] = []

  const walk = (nodes: CategoryLookup[], depth: number) => {
    for (const node of nodes) {
      out.push({
        id: node.id,
        nameAr: node.nameAr,
        nameEn: node.nameEn,
        parentId: node.parentId,
        departmentId: node.departmentId,
        sortOrder: node.sortOrder,
        depth,
        // The tree endpoint returns inactive nodes too; activeIds tracks which is which.
        isActive: activeIds.value.has(node.id),
      })
      walk(node.children, depth + 1)
    }
  }

  walk(tree.value, 0)
  return out
})

/**
 * The category DTO carries no IsActive flag, so the active set is derived by diffing the
 * full tree against the active-only tree. Cheaper than widening the DTO for one screen.
 */
const activeIds = ref<Set<string>>(new Set())

/**
 * Parent options for the dialog, excluding the category being edited and its descendants —
 * the server rejects a cycle, but offering an impossible choice is a poor form.
 */
const parentOptions = computed(() => {
  const banned = new Set<string>()

  if (editingId.value) {
    banned.add(editingId.value)

    let grew = true
    while (grew) {
      grew = false
      for (const c of flat.value) {
        if (c.parentId && banned.has(c.parentId) && !banned.has(c.id)) {
          banned.add(c.id)
          grew = true
        }
      }
    }
  }

  return flat.value
    .filter((c) => !banned.has(c.id))
    .map((c) => ({
      label: `${'— '.repeat(c.depth)}${ui.isArabic ? c.nameAr : c.nameEn}`,
      value: c.id,
    }))
})

/** PrimeVue TreeTable wants { key, data, children }. */
function toTreeNodes(nodes: CategoryLookup[]): TreeNode[] {
  return nodes.map((node) => ({
    key: node.id,
    data: node,
    children: node.children.length ? toTreeNodes(node.children) : undefined,
  }))
}

const treeNodes = computed(() => toTreeNodes(tree.value))

async function load() {
  loading.value = true
  try {
    const [all, activeOnly, deps] = await Promise.all([
      ticketCategoriesApi.tree(true),
      ticketCategoriesApi.tree(false),
      lookupsApi.departments().catch(() => [] as Lookup[]),
    ])

    tree.value = all
    departments.value = deps

    const ids = new Set<string>()
    const collect = (nodes: CategoryLookup[]) => {
      for (const n of nodes) {
        ids.add(n.id)
        collect(n.children)
      }
    }
    collect(activeOnly)
    activeIds.value = ids
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 5000 })
  } finally {
    loading.value = false
  }
}

function openCreate(parentId: string | null = null) {
  editingId.value = null
  error.value = null

  // New siblings go to the end, so creating one never reshuffles the existing order.
  const siblings = flat.value.filter((c) => c.parentId === parentId)
  const nextOrder = siblings.length ? Math.max(...siblings.map((s) => s.sortOrder)) + 1 : 0

  form.value = {
    nameAr: '',
    nameEn: '',
    parentId,
    departmentId: null,
    sortOrder: nextOrder,
    isActive: true,
  }
  dialog.value = true
}

function openEdit(category: CategoryLookup) {
  editingId.value = category.id
  error.value = null
  form.value = {
    nameAr: category.nameAr,
    nameEn: category.nameEn,
    parentId: category.parentId,
    departmentId: category.departmentId,
    sortOrder: category.sortOrder,
    isActive: activeIds.value.has(category.id),
  }
  dialog.value = true
}

async function save() {
  if (saving.value) return

  error.value = null
  saving.value = true

  try {
    if (editingId.value) {
      await ticketCategoriesApi.update(editingId.value, form.value)
    } else {
      await ticketCategoriesApi.create(form.value)
    }

    dialog.value = false
    toast.add({ severity: 'success', summary: t('category.saved'), life: 3000 })
    await load()
  } catch (e) {
    // Surfaces the server's own reasons: cycle, missing parent, duplicate.
    error.value = problemMessage(e, t('error.saveFailed'))
  } finally {
    saving.value = false
  }
}

async function toggleActive(category: CategoryLookup) {
  const nowActive = activeIds.value.has(category.id)

  try {
    await ticketCategoriesApi.setActive(category.id, !nowActive)
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
  }
}

function confirmDelete(category: CategoryLookup) {
  confirm.require({
    message: t('category.deleteConfirm', { name: ui.localized(category) }),
    header: t('app.confirm'),
    icon: 'pi pi-exclamation-triangle',
    acceptProps: { label: t('app.delete'), severity: 'danger' },
    rejectProps: { label: t('app.cancel'), outlined: true },
    accept: async () => {
      try {
        await ticketCategoriesApi.remove(category.id)
        await load()
      } catch (e) {
        // Refused while active tickets or sub-categories still reference it.
        toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 7000 })
      }
    },
  })
}

/**
 * Moves a category among its siblings. The whole sibling list is sent with fresh
 * sortOrder values so the server validates and persists one coherent shape rather than a
 * sequence of partial swaps.
 */
async function move(category: CategoryLookup, direction: -1 | 1) {
  const siblings = flat.value
    .filter((c) => c.parentId === category.parentId)
    .sort((a, b) => a.sortOrder - b.sortOrder)

  const index = siblings.findIndex((c) => c.id === category.id)
  const target = index + direction

  if (index < 0 || target < 0 || target >= siblings.length) return

  const reordered = [...siblings]
  ;[reordered[index], reordered[target]] = [reordered[target], reordered[index]]

  const items: CategoryReorderItem[] = reordered.map((c, i) => ({
    id: c.id,
    parentId: c.parentId,
    sortOrder: i,
  }))

  try {
    await ticketCategoriesApi.reorder(items)
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 5000 })
  }
}

function canMove(category: CategoryLookup, direction: -1 | 1): boolean {
  const siblings = flat.value
    .filter((c) => c.parentId === category.parentId)
    .sort((a, b) => a.sortOrder - b.sortOrder)

  const index = siblings.findIndex((c) => c.id === category.id)
  const target = index + direction
  return index >= 0 && target >= 0 && target < siblings.length
}

function departmentName(departmentId: string | null): string {
  if (!departmentId) return '—'
  const match = departments.value.find((d) => d.id === departmentId)
  return match ? ui.localized(match) : '—'
}

onMounted(load)
</script>

<template>
  <div>
    <PageHeader :title="t('category.title')" :subtitle="t('category.subtitle')">
      <template #actions>
        <Button icon="pi pi-refresh" outlined size="small" :aria-label="t('app.search')" @click="load" />
        <Button icon="pi pi-plus" :label="t('category.new')" size="small" @click="openCreate(null)" />
      </template>
    </PageHeader>

    <Message severity="info" :closable="false" class="mb-4 text-sm">
      {{ t('category.deactivateHint') }}
    </Message>

    <div class="rounded-xl border border-surface-200 bg-surface-0 dark:border-surface-800 dark:bg-surface-900">
      <TreeTable :value="treeNodes" :loading="loading" size="small" class="cursor-default">
        <template #empty>
          <div class="p-6 text-center text-surface-500 dark:text-surface-400">{{ t('app.noData') }}</div>
        </template>

        <Column field="nameEn" :header="t('category.title')" expander>
          <template #body="{ node }">
            <span :class="activeIds.has(node.data.id) ? '' : 'text-surface-400 line-through'">
              {{ ui.localized(node.data) }}
            </span>
          </template>
        </Column>

        <Column :header="t('customer.department')">
          <template #body="{ node }">
            <span class="text-sm">{{ departmentName(node.data.departmentId) }}</span>
          </template>
        </Column>

        <Column :header="t('category.sortOrder')" style="width: 6rem">
          <template #body="{ node }">
            <span class="ltr-nums text-sm">{{ node.data.sortOrder }}</span>
          </template>
        </Column>

        <Column :header="t('customer.status')" style="width: 8rem">
          <template #body="{ node }">
            <Tag
              :severity="activeIds.has(node.data.id) ? 'success' : 'secondary'"
              :value="activeIds.has(node.data.id) ? t('customer.active') : t('customer.inactive')"
              rounded
            />
          </template>
        </Column>

        <Column :header="t('app.actions')" style="width: 14rem">
          <template #body="{ node }">
            <div class="flex items-center gap-1">
              <Button
                icon="pi pi-arrow-up"
                text
                rounded
                size="small"
                :disabled="!canMove(node.data, -1)"
                :aria-label="t('category.moveUp')"
                @click="move(node.data, -1)"
              />
              <Button
                icon="pi pi-arrow-down"
                text
                rounded
                size="small"
                :disabled="!canMove(node.data, 1)"
                :aria-label="t('category.moveDown')"
                @click="move(node.data, 1)"
              />
              <Button
                icon="pi pi-plus"
                text
                rounded
                size="small"
                :aria-label="t('category.addChild')"
                @click="openCreate(node.data.id)"
              />
              <Button
                icon="pi pi-pencil"
                text
                rounded
                size="small"
                :aria-label="t('app.edit')"
                @click="openEdit(node.data)"
              />
              <Button
                :icon="activeIds.has(node.data.id) ? 'pi pi-eye-slash' : 'pi pi-eye'"
                text
                rounded
                size="small"
                :aria-label="activeIds.has(node.data.id) ? t('category.deactivate') : t('category.activate')"
                @click="toggleActive(node.data)"
              />
              <Button
                icon="pi pi-trash"
                severity="danger"
                text
                rounded
                size="small"
                :aria-label="t('app.delete')"
                @click="confirmDelete(node.data)"
              />
            </div>
          </template>
        </Column>
      </TreeTable>
    </div>

    <!-- Create / edit -->
    <Dialog
      v-model:visible="dialog"
      modal
      :header="editingId ? t('app.edit') : t('category.new')"
      :style="{ width: '32rem' }"
    >
      <div class="flex flex-col gap-4">
        <Message v-if="error" severity="error" :closable="false" class="text-sm">{{ error }}</Message>

        <div class="grid gap-4 sm:grid-cols-2">
          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.nameAr') }} *</label>
            <InputText v-model="form.nameAr" dir="rtl" class="w-full" />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.nameEn') }} *</label>
            <InputText v-model="form.nameEn" dir="ltr" class="w-full" />
          </div>

          <div class="flex flex-col gap-1.5 sm:col-span-2">
            <label class="text-sm font-medium">{{ t('category.parent') }}</label>
            <Select
              v-model="form.parentId"
              :options="parentOptions"
              option-label="label"
              option-value="value"
              show-clear
              :placeholder="t('app.none')"
              class="w-full"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('customer.department') }}</label>
            <Select
              v-model="form.departmentId"
              :options="departments"
              :option-label="(d: Lookup) => ui.localized(d)"
              option-value="id"
              show-clear
              class="w-full"
            />
          </div>

          <div class="flex flex-col gap-1.5">
            <label class="text-sm font-medium">{{ t('category.sortOrder') }}</label>
            <InputNumber v-model="form.sortOrder" :min="0" show-buttons class="w-full" />
          </div>

          <div class="flex items-center gap-2 sm:col-span-2">
            <ToggleSwitch v-model="form.isActive" />
            <span class="text-sm">
              {{ form.isActive ? t('customer.active') : t('customer.inactive') }}
            </span>
          </div>
        </div>
      </div>

      <template #footer>
        <Button :label="t('app.cancel')" outlined @click="dialog = false" />
        <Button
          :label="t('app.save')"
          :loading="saving"
          :disabled="!form.nameAr.trim() || !form.nameEn.trim()"
          @click="save"
        />
      </template>
    </Dialog>
  </div>
</template>
