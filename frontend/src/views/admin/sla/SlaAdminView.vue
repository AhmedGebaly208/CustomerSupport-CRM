<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import Select from 'primevue/select'
import MultiSelect from 'primevue/multiselect'
import ToggleSwitch from 'primevue/toggleswitch'
import Tag from 'primevue/tag'
import DatePicker from 'primevue/datepicker'
import Message from 'primevue/message'
import { slaApi, lookupsApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { useAuthStore } from '@/stores/auth'
import { useFormat } from '@/composables/useFormat'
import {
  AutoAssignmentStrategy,
  PERMISSIONS,
  SlaTargetKind,
  TicketPriority,
  TicketStatus,
  type CategoryLookup,
  type Lookup,
  type SaveSlaEscalationRuleRequest,
  type SaveSlaPolicyRequest,
  type SlaEscalationRule,
  type SlaPolicy,
  type SlaPreview,
  type SlaTarget,
} from '@/types/api'

const { t } = useI18n()
const toast = useToast()
const confirm = useConfirm()
const ui = useUiStore()
const auth = useAuthStore()
const { formatDateTime } = useFormat()

const policies = ref<SlaPolicy[]>([])
const departments = ref<Lookup[]>([])
const branches = ref<Lookup[]>([])
const categories = ref<CategoryLookup[]>([])
const loading = ref(true)
const saving = ref(false)

const canManage = computed(() => auth.hasPermission(PERMISSIONS.slaManage))

// ---- option lists ----

const priorities = [TicketPriority.Low, TicketPriority.Normal, TicketPriority.High, TicketPriority.Urgent]

const statusOptions = computed(() =>
  Object.values(TicketStatus)
    .filter((v): v is TicketStatus => typeof v === 'number')
    .map((value) => ({ value, label: t(`ticket.status.${TicketStatus[value]}`) })),
)

const strategyOptions = computed(() =>
  [AutoAssignmentStrategy.None, AutoAssignmentStrategy.LeastBusy, AutoAssignmentStrategy.RoundRobin].map(
    (value) => ({ value, label: t(`sla.strategy.${AutoAssignmentStrategy[value]}`) }),
  ),
)

const targetKindOptions = computed(() =>
  [SlaTargetKind.FirstResponse, SlaTargetKind.Resolution].map((value) => ({
    value,
    label: t(`sla.targetKind.${SlaTargetKind[value]}`),
  })),
)

// ---- policy dialog ----

const dialogOpen = ref(false)
const editingId = ref<string | null>(null)

const form = reactive<SaveSlaPolicyRequest>(emptyForm())

function emptyForm(): SaveSlaPolicyRequest {
  return {
    nameAr: '',
    nameEn: '',
    isActive: true,
    rank: 0,
    departmentId: null,
    branchId: null,
    categoryId: null,
    countsBusinessHoursOnly: true,
    pausedStatuses: [TicketStatus.Pending],
    assignmentStrategy: AutoAssignmentStrategy.None,
    // Every priority is listed so the form cannot be saved with a gap that would silently
    // leave tickets of that priority without a target.
    targets: priorities.map((priority) => ({
      priority,
      firstResponseMinutes: 240,
      resolutionMinutes: 1440,
    })),
  }
}

function openCreate() {
  editingId.value = null
  Object.assign(form, emptyForm())
  dialogOpen.value = true
}

function openEdit(policy: SlaPolicy) {
  editingId.value = policy.id
  Object.assign(form, {
    nameAr: policy.nameAr,
    nameEn: policy.nameEn,
    isActive: policy.isActive,
    rank: policy.rank,
    departmentId: policy.departmentId,
    branchId: policy.branchId,
    categoryId: policy.categoryId,
    countsBusinessHoursOnly: policy.countsBusinessHoursOnly,
    pausedStatuses: [...policy.pausedStatuses],
    assignmentStrategy: policy.assignmentStrategy,
    targets: priorities.map<SlaTarget>((priority) => {
      const existing = policy.targets.find((x) => x.priority === priority)
      return existing
        ? { ...existing }
        : { priority, firstResponseMinutes: 240, resolutionMinutes: 1440 }
    }),
  })
  dialogOpen.value = true
}

/** Mirrors the server guard, so the obvious mistake is caught before a round trip. */
const formError = computed(() => {
  if (!form.nameAr.trim() || !form.nameEn.trim()) return t('sla.error.nameRequired')

  for (const target of form.targets) {
    if (target.firstResponseMinutes <= 0 || target.resolutionMinutes <= 0) {
      return t('sla.error.positiveTargets')
    }
    if (target.resolutionMinutes < target.firstResponseMinutes) {
      return t('sla.error.resolutionShorter', { priority: t(`ticket.priority.${TicketPriority[target.priority]}`) })
    }
  }

  return null
})

async function save() {
  if (formError.value || saving.value) return

  saving.value = true
  try {
    if (editingId.value) await slaApi.updatePolicy(editingId.value, { ...form })
    else await slaApi.createPolicy({ ...form })

    toast.add({ severity: 'success', summary: t('app.saved'), life: 3000 })
    dialogOpen.value = false
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 7000 })
  } finally {
    saving.value = false
  }
}

function confirmDelete(policy: SlaPolicy) {
  confirm.require({
    message: t('sla.deleteConfirm', { name: ui.localized({ nameAr: policy.nameAr, nameEn: policy.nameEn }) }),
    header: t('app.confirm'),
    icon: 'pi pi-exclamation-triangle',
    acceptProps: { label: t('app.delete'), severity: 'danger' },
    rejectProps: { label: t('app.cancel'), outlined: true },
    accept: async () => {
      try {
        await slaApi.deletePolicy(policy.id)
        await load()
      } catch (e) {
        // The API refuses while tickets still reference the policy and says to deactivate
        // it instead; that message is worth showing for a while.
        toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 8000 })
      }
    },
  })
}

// ---- escalation rules ----

const rulesFor = ref<SlaPolicy | null>(null)
const ruleDialogOpen = ref(false)
const editingRuleId = ref<string | null>(null)
const ruleForm = reactive<SaveSlaEscalationRuleRequest>(emptyRule())

function emptyRule(): SaveSlaEscalationRuleRequest {
  return {
    nameAr: '',
    nameEn: '',
    isActive: true,
    target: SlaTargetKind.FirstResponse,
    thresholdPercent: 80,
    raiseLevelBy: 1,
    reassignToUserId: null,
    notifyRole: null,
  }
}

function openRules(policy: SlaPolicy) {
  rulesFor.value = policy
}

function openRuleCreate() {
  editingRuleId.value = null
  Object.assign(ruleForm, emptyRule())
  ruleDialogOpen.value = true
}

function openRuleEdit(rule: SlaEscalationRule) {
  editingRuleId.value = rule.id
  Object.assign(ruleForm, {
    nameAr: rule.nameAr,
    nameEn: rule.nameEn,
    isActive: rule.isActive,
    target: rule.target,
    thresholdPercent: rule.thresholdPercent,
    raiseLevelBy: rule.raiseLevelBy,
    reassignToUserId: rule.reassignToUserId,
    notifyRole: rule.notifyRole,
  })
  ruleDialogOpen.value = true
}

async function saveRule() {
  const policy = rulesFor.value
  if (!policy || saving.value) return

  saving.value = true
  try {
    if (editingRuleId.value) await slaApi.updateRule(policy.id, editingRuleId.value, { ...ruleForm })
    else await slaApi.addRule(policy.id, { ...ruleForm })

    ruleDialogOpen.value = false
    await load()
    rulesFor.value = policies.value.find((p) => p.id === policy.id) ?? null
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 7000 })
  } finally {
    saving.value = false
  }
}

async function deleteRule(rule: SlaEscalationRule) {
  const policy = rulesFor.value
  if (!policy) return

  try {
    await slaApi.deleteRule(policy.id, rule.id)
    await load()
    rulesFor.value = policies.value.find((p) => p.id === policy.id) ?? null
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
  }
}

// ---- preview ----

const previewOpen = ref(false)
const previewPolicy = ref<SlaPolicy | null>(null)
const previewPriority = ref<TicketPriority>(TicketPriority.Normal)
const previewStart = ref<Date | null>(null)
const previewResult = ref<SlaPreview | null>(null)

function openPreview(policy: SlaPolicy) {
  previewPolicy.value = policy
  previewPriority.value = TicketPriority.Normal
  previewStart.value = new Date()
  previewResult.value = null
  previewOpen.value = true
  void runPreview()
}

async function runPreview() {
  const policy = previewPolicy.value
  if (!policy) return

  try {
    previewResult.value = await slaApi.preview({
      policyId: policy.id,
      priority: previewPriority.value,
      startAt: previewStart.value ? previewStart.value.toISOString() : null,
    })
  } catch (e) {
    previewResult.value = null
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
  }
}

// ---- data ----

/** Empty criteria mean "any", which is what makes a policy the fallback — worth saying in
 *  the table rather than showing a blank cell. */
function scopeLabel(policy: SlaPolicy): string {
  const parts: string[] = []

  if (policy.categoryId) {
    parts.push(ui.localized({ nameAr: policy.categoryNameAr, nameEn: policy.categoryNameEn }))
  }
  if (policy.departmentId) {
    parts.push(ui.localized({ nameAr: policy.departmentNameAr, nameEn: policy.departmentNameEn }))
  }
  if (policy.branchId) {
    parts.push(ui.localized({ nameAr: policy.branchNameAr, nameEn: policy.branchNameEn }))
  }

  return parts.length > 0 ? parts.join(' · ') : t('sla.appliesToAll')
}

async function load() {
  loading.value = true
  try {
    policies.value = await slaApi.policies()
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 5000 })
  } finally {
    loading.value = false
  }
}

onMounted(async () => {
  await Promise.all([
    load(),
    lookupsApi.departments().then((d) => (departments.value = d)),
    lookupsApi.branches().then((b) => (branches.value = b)),
    lookupsApi.categories().then((c) => (categories.value = c)),
  ])
})
</script>

<template>
  <div class="space-y-4">
    <div class="flex flex-wrap items-center justify-between gap-3">
      <div>
        <h1 class="text-2xl font-semibold">{{ t('sla.title') }}</h1>
        <p class="text-sm text-surface-500 dark:text-surface-400">{{ t('sla.subtitle') }}</p>
      </div>

      <Button v-if="canManage" :label="t('sla.newPolicy')" icon="pi pi-plus" @click="openCreate" />
    </div>

    <Message severity="info" :closable="false" class="text-sm">
      {{ t('sla.resolutionHint') }}
    </Message>

    <DataTable :value="policies" :loading="loading" size="small" striped-rows>
      <template #empty>
        <div class="p-6 text-center text-surface-500 dark:text-surface-400">{{ t('sla.none') }}</div>
      </template>

      <Column :header="t('sla.policyName')">
        <template #body="{ data }">
          <div class="flex items-center gap-2">
            <span class="font-medium">{{ ui.localized({ nameAr: data.nameAr, nameEn: data.nameEn }) }}</span>
            <Tag v-if="!data.isActive" severity="secondary" :value="t('app.inactive')" rounded />
          </div>
        </template>
      </Column>

      <Column :header="t('sla.scope')">
        <template #body="{ data }">
          <span class="text-sm">{{ scopeLabel(data) }}</span>
        </template>
      </Column>

      <Column :header="t('sla.rank')" style="width: 6rem">
        <template #body="{ data }"><span class="ltr-nums text-sm">{{ data.rank }}</span></template>
      </Column>

      <Column :header="t('sla.clockType')">
        <template #body="{ data }">
          <Tag
            :severity="data.countsBusinessHoursOnly ? 'info' : 'warn'"
            :value="data.countsBusinessHoursOnly ? t('sla.businessHours') : t('sla.roundTheClock')"
            rounded
          />
        </template>
      </Column>

      <Column :header="t('sla.assignment')">
        <template #body="{ data }">
          <span class="text-sm">{{ t(`sla.strategy.${AutoAssignmentStrategy[data.assignmentStrategy]}`) }}</span>
        </template>
      </Column>

      <Column :header="t('sla.rules')" style="width: 7rem">
        <template #body="{ data }">
          <Button
            :label="String(data.escalationRules.length)"
            icon="pi pi-bolt"
            text
            size="small"
            @click="openRules(data)"
          />
        </template>
      </Column>

      <Column :header="t('app.actions')" style="width: 10rem">
        <template #body="{ data }">
          <div class="flex items-center gap-1">
            <Button
              icon="pi pi-calculator"
              text
              rounded
              size="small"
              :aria-label="t('sla.preview')"
              @click="openPreview(data)"
            />
            <Button
              v-if="canManage"
              icon="pi pi-pencil"
              text
              rounded
              size="small"
              :aria-label="t('app.edit')"
              @click="openEdit(data)"
            />
            <Button
              v-if="canManage"
              icon="pi pi-trash"
              severity="danger"
              text
              rounded
              size="small"
              :aria-label="t('app.delete')"
              @click="confirmDelete(data)"
            />
          </div>
        </template>
      </Column>
    </DataTable>

    <!-- Policy editor -->
    <Dialog
      v-model:visible="dialogOpen"
      modal
      :header="editingId ? t('sla.editPolicy') : t('sla.newPolicy')"
      :style="{ width: '46rem' }"
      :breakpoints="{ '960px': '95vw' }"
    >
      <div class="space-y-4">
        <div class="grid gap-4 md:grid-cols-2">
          <div>
            <label class="mb-1 block text-sm font-medium">{{ t('sla.nameAr') }} *</label>
            <InputText v-model="form.nameAr" class="w-full" />
          </div>
          <div>
            <label class="mb-1 block text-sm font-medium">{{ t('sla.nameEn') }} *</label>
            <InputText v-model="form.nameEn" class="w-full" />
          </div>
        </div>

        <div class="grid gap-4 md:grid-cols-3">
          <div>
            <label class="mb-1 block text-sm font-medium">{{ t('ticket.department') }}</label>
            <Select
              v-model="form.departmentId"
              :options="departments"
              option-label="nameEn"
              option-value="id"
              show-clear
              class="w-full"
              :placeholder="t('sla.anyValue')"
            >
              <template #option="{ option }">{{ ui.localized(option) }}</template>
              <template #value="{ value }">
                {{ value ? ui.localized(departments.find((d) => d.id === value)) : t('sla.anyValue') }}
              </template>
            </Select>
          </div>

          <div>
            <label class="mb-1 block text-sm font-medium">{{ t('ticket.branch') }}</label>
            <Select
              v-model="form.branchId"
              :options="branches"
              option-label="nameEn"
              option-value="id"
              show-clear
              class="w-full"
              :placeholder="t('sla.anyValue')"
            >
              <template #option="{ option }">{{ ui.localized(option) }}</template>
              <template #value="{ value }">
                {{ value ? ui.localized(branches.find((b) => b.id === value)) : t('sla.anyValue') }}
              </template>
            </Select>
          </div>

          <div>
            <label class="mb-1 block text-sm font-medium">{{ t('ticket.category') }}</label>
            <Select
              v-model="form.categoryId"
              :options="categories"
              option-label="nameEn"
              option-value="id"
              show-clear
              class="w-full"
              :placeholder="t('sla.anyValue')"
            >
              <template #option="{ option }">{{ ui.localized(option) }}</template>
              <template #value="{ value }">
                {{ value ? ui.localized(categories.find((c) => c.id === value)) : t('sla.anyValue') }}
              </template>
            </Select>
          </div>
        </div>

        <p class="text-xs text-surface-500 dark:text-surface-400">{{ t('sla.scopeHint') }}</p>

        <div class="grid gap-4 md:grid-cols-3">
          <div>
            <label class="mb-1 block text-sm font-medium">{{ t('sla.rank') }}</label>
            <InputNumber v-model="form.rank" :min="0" :max="1000" class="w-full" show-buttons />
          </div>

          <div>
            <label class="mb-1 block text-sm font-medium">{{ t('sla.assignment') }}</label>
            <Select
              v-model="form.assignmentStrategy"
              :options="strategyOptions"
              option-label="label"
              option-value="value"
              class="w-full"
            />
          </div>

          <div>
            <label class="mb-1 block text-sm font-medium">{{ t('sla.pausedStatuses') }}</label>
            <MultiSelect
              v-model="form.pausedStatuses"
              :options="statusOptions"
              option-label="label"
              option-value="value"
              display="chip"
              class="w-full"
              :placeholder="t('sla.noPause')"
            />
          </div>
        </div>

        <div class="flex items-center gap-6">
          <div class="flex items-center gap-2">
            <ToggleSwitch v-model="form.isActive" input-id="sla-active" />
            <label for="sla-active" class="text-sm">{{ t('app.active') }}</label>
          </div>

          <div class="flex items-center gap-2">
            <ToggleSwitch v-model="form.countsBusinessHoursOnly" input-id="sla-hours" />
            <label for="sla-hours" class="text-sm">{{ t('sla.businessHoursOnly') }}</label>
          </div>
        </div>

        <div>
          <h3 class="mb-2 font-medium">{{ t('sla.targets') }}</h3>

          <DataTable :value="form.targets" size="small">
            <Column :header="t('ticket.priority')" style="width: 9rem">
              <template #body="{ data }">
                {{ t(`ticket.priority.${TicketPriority[data.priority]}`) }}
              </template>
            </Column>

            <Column :header="t('sla.firstResponseMinutes')">
              <template #body="{ data }">
                <InputNumber v-model="data.firstResponseMinutes" :min="1" :max="100000" class="w-full" />
              </template>
            </Column>

            <Column :header="t('sla.resolutionMinutes')">
              <template #body="{ data }">
                <InputNumber v-model="data.resolutionMinutes" :min="1" :max="100000" class="w-full" />
              </template>
            </Column>
          </DataTable>

          <p class="mt-1 text-xs text-surface-500 dark:text-surface-400">{{ t('sla.minutesHint') }}</p>
        </div>

        <Message v-if="formError" severity="error" :closable="false" class="text-sm">
          {{ formError }}
        </Message>
      </div>

      <template #footer>
        <Button :label="t('app.cancel')" text @click="dialogOpen = false" />
        <Button :label="t('app.save')" :disabled="!!formError || saving" :loading="saving" @click="save" />
      </template>
    </Dialog>

    <!-- Escalation rules -->
    <Dialog
      v-model:visible="ruleDialogOpen"
      modal
      :header="editingRuleId ? t('sla.editRule') : t('sla.newRule')"
      :style="{ width: '34rem' }"
      :breakpoints="{ '960px': '95vw' }"
    >
      <div class="space-y-4">
        <div class="grid gap-4 md:grid-cols-2">
          <div>
            <label class="mb-1 block text-sm font-medium">{{ t('sla.nameAr') }} *</label>
            <InputText v-model="ruleForm.nameAr" class="w-full" />
          </div>
          <div>
            <label class="mb-1 block text-sm font-medium">{{ t('sla.nameEn') }} *</label>
            <InputText v-model="ruleForm.nameEn" class="w-full" />
          </div>
        </div>

        <div class="grid gap-4 md:grid-cols-2">
          <div>
            <label class="mb-1 block text-sm font-medium">{{ t('sla.ruleTarget') }}</label>
            <Select
              v-model="ruleForm.target"
              :options="targetKindOptions"
              option-label="label"
              option-value="value"
              class="w-full"
            />
          </div>

          <div>
            <label class="mb-1 block text-sm font-medium">{{ t('sla.threshold') }}</label>
            <InputNumber v-model="ruleForm.thresholdPercent" :min="1" :max="500" suffix=" %" class="w-full" />
          </div>
        </div>

        <div class="grid gap-4 md:grid-cols-2">
          <div>
            <label class="mb-1 block text-sm font-medium">{{ t('sla.raiseLevelBy') }}</label>
            <InputNumber v-model="ruleForm.raiseLevelBy" :min="0" :max="10" class="w-full" show-buttons />
          </div>

          <div>
            <label class="mb-1 block text-sm font-medium">{{ t('sla.notifyRole') }}</label>
            <InputText v-model="ruleForm.notifyRole" class="w-full" :placeholder="t('sla.notifyRoleHint')" />
          </div>
        </div>

        <div class="flex items-center gap-2">
          <ToggleSwitch v-model="ruleForm.isActive" input-id="rule-active" />
          <label for="rule-active" class="text-sm">{{ t('app.active') }}</label>
        </div>

        <p class="text-xs text-surface-500 dark:text-surface-400">{{ t('sla.thresholdHint') }}</p>
      </div>

      <template #footer>
        <Button :label="t('app.cancel')" text @click="ruleDialogOpen = false" />
        <Button :label="t('app.save')" :loading="saving" @click="saveRule" />
      </template>
    </Dialog>

    <Dialog
      :visible="rulesFor !== null"
      modal
      :header="t('sla.rulesFor', { name: rulesFor ? ui.localized({ nameAr: rulesFor.nameAr, nameEn: rulesFor.nameEn }) : '' })"
      :style="{ width: '46rem' }"
      :breakpoints="{ '960px': '95vw' }"
      @update:visible="(v) => { if (!v) rulesFor = null }"
    >
      <div class="mb-3 flex justify-end">
        <Button v-if="canManage" :label="t('sla.newRule')" icon="pi pi-plus" size="small" @click="openRuleCreate" />
      </div>

      <DataTable :value="rulesFor?.escalationRules ?? []" size="small" striped-rows>
        <template #empty>
          <div class="p-4 text-center text-sm text-surface-500 dark:text-surface-400">{{ t('sla.noRules') }}</div>
        </template>

        <Column :header="t('sla.ruleName')">
          <template #body="{ data }">
            <div class="flex items-center gap-2">
              <span>{{ ui.localized({ nameAr: data.nameAr, nameEn: data.nameEn }) }}</span>
              <Tag v-if="!data.isActive" severity="secondary" :value="t('app.inactive')" rounded />
            </div>
          </template>
        </Column>

        <Column :header="t('sla.ruleTarget')">
          <template #body="{ data }">{{ t(`sla.targetKind.${SlaTargetKind[data.target]}`) }}</template>
        </Column>

        <Column :header="t('sla.threshold')" style="width: 7rem">
          <template #body="{ data }"><span class="ltr-nums">{{ data.thresholdPercent }}%</span></template>
        </Column>

        <Column :header="t('sla.raiseLevelBy')" style="width: 6rem">
          <template #body="{ data }"><span class="ltr-nums">+{{ data.raiseLevelBy }}</span></template>
        </Column>

        <Column :header="t('sla.notifyRole')">
          <template #body="{ data }">{{ data.notifyRole ?? '—' }}</template>
        </Column>

        <Column v-if="canManage" :header="t('app.actions')" style="width: 7rem">
          <template #body="{ data }">
            <div class="flex items-center gap-1">
              <Button icon="pi pi-pencil" text rounded size="small" @click="openRuleEdit(data)" />
              <Button icon="pi pi-trash" severity="danger" text rounded size="small" @click="deleteRule(data)" />
            </div>
          </template>
        </Column>
      </DataTable>
    </Dialog>

    <!-- Preview -->
    <Dialog v-model:visible="previewOpen" modal :header="t('sla.preview')" :style="{ width: '32rem' }">
      <p class="mb-4 text-sm text-surface-500 dark:text-surface-400">{{ t('sla.previewHint') }}</p>

      <div class="grid gap-4 md:grid-cols-2">
        <div>
          <label class="mb-1 block text-sm font-medium">{{ t('ticket.priority') }}</label>
          <Select
            v-model="previewPriority"
            :options="priorities.map((p) => ({ value: p, label: t(`ticket.priority.${TicketPriority[p]}`) }))"
            option-label="label"
            option-value="value"
            class="w-full"
            @change="runPreview"
          />
        </div>

        <div>
          <label class="mb-1 block text-sm font-medium">{{ t('sla.startAt') }}</label>
          <DatePicker v-model="previewStart" show-time hour-format="24" class="w-full" @date-select="runPreview" />
        </div>
      </div>

      <div v-if="previewResult" class="mt-4 space-y-2 rounded border border-surface-200 p-3 dark:border-surface-700">
        <div class="flex justify-between gap-3 text-sm">
          <span class="text-surface-500 dark:text-surface-400">{{ t('sla.firstResponseDue') }}</span>
          <span class="font-medium">{{ formatDateTime(previewResult.firstResponseDueAt) }}</span>
        </div>
        <div class="flex justify-between gap-3 text-sm">
          <span class="text-surface-500 dark:text-surface-400">{{ t('sla.resolutionDue') }}</span>
          <span class="font-medium">{{ formatDateTime(previewResult.resolutionDueAt) }}</span>
        </div>
        <div class="pt-1 text-xs text-surface-500 dark:text-surface-400">
          {{ previewResult.countsBusinessHoursOnly ? t('sla.businessHours') : t('sla.roundTheClock') }}
        </div>
      </div>

      <template #footer>
        <Button :label="t('app.close')" text @click="previewOpen = false" />
      </template>
    </Dialog>
  </div>
</template>
