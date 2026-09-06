<script setup lang="ts">
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Chip from 'primevue/chip'
import Select from 'primevue/select'
import AutoComplete from 'primevue/autocomplete'
import Dialog from 'primevue/dialog'
import Textarea from 'primevue/textarea'
import InputText from 'primevue/inputtext'
import Tag from 'primevue/tag'
import { authApi, ticketsApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useAuthStore } from '@/stores/auth'
import { useUiStore } from '@/stores/ui'
import {
  PERMISSIONS,
  TicketLinkType,
  type Agent,
  type TicketDetail,
  type Tag as TagModel,
  type TicketListItem,
} from '@/types/api'

const props = defineProps<{ ticket: TicketDetail }>()
const emit = defineEmits<{ (e: 'changed', ticket: TicketDetail): void }>()

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const auth = useAuthStore()
const ui = useUiStore()

const canEdit = computed(() => auth.hasPermission(PERMISSIONS.ticketsEdit))
const canMerge = computed(() => auth.hasPermission(PERMISSIONS.ticketsClose))
const isSupervisor = computed(() => auth.hasPermission(PERMISSIONS.dashboardViewTeam))

const tags = ref<TagModel[]>([...props.ticket.tags])
const watchers = ref([...props.ticket.watchers])
const links = ref([...props.ticket.links])

// ---- Tags ----

const tagQuery = ref('')
const tagSuggestions = ref<TagModel[]>([])

async function suggestTags(event: { query: string }) {
  try {
    tagSuggestions.value = await ticketsApi.searchTags(event.query)
  } catch {
    tagSuggestions.value = []
  }
}

async function addTag() {
  // AutoComplete yields either a picked Tag object or the raw string the agent typed;
  // both are valid because tags are created lazily on first attach.
  const raw = tagQuery.value as string | TagModel
  const name = typeof raw === 'string' ? raw.trim() : raw?.name

  if (!name) return

  try {
    tags.value = await ticketsApi.addTag(props.ticket.id, name)
    tagQuery.value = ''
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 5000 })
  }
}

async function removeTag(tagId: string) {
  try {
    tags.value = await ticketsApi.removeTag(props.ticket.id, tagId)
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
  }
}

// ---- Watchers ----

const agents = ref<Agent[]>([])
const watcherToAdd = ref<string | null>(null)

const isWatching = computed(() => watchers.value.some((w) => w.userId === auth.user?.id))

async function loadAgents() {
  if (agents.value.length) return
  try {
    agents.value = await authApi.agents(props.ticket.departmentId)
  } catch {
    // Falls back to the self-watch button alone.
  }
}

async function toggleSelfWatch() {
  if (!auth.user) return

  try {
    watchers.value = isWatching.value
      ? await ticketsApi.removeWatcher(props.ticket.id, auth.user.id)
      : await ticketsApi.addWatcher(props.ticket.id, auth.user.id)
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
  }
}

async function addWatcher() {
  if (!watcherToAdd.value) return

  try {
    watchers.value = await ticketsApi.addWatcher(props.ticket.id, watcherToAdd.value)
    watcherToAdd.value = null
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 6000 })
  }
}

async function removeWatcher(userId: string) {
  try {
    watchers.value = await ticketsApi.removeWatcher(props.ticket.id, userId)
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
  }
}

// ---- Links ----

const linkDialog = ref(false)
const linkType = ref<TicketLinkType>(TicketLinkType.RelatedTo)
const linkTargetQuery = ref('')
const linkCandidates = ref<TicketListItem[]>([])
const linkTargetId = ref<string | null>(null)
const savingLink = ref(false)

const linkTypeOptions = Object.entries(TicketLinkType)
  .filter(([, v]) => typeof v === 'number')
  .map(([name, value]) => ({ label: t(`ticket.linkType.${name}`), value: value as TicketLinkType }))

async function searchLinkTargets() {
  try {
    const page = await ticketsApi.search({ search: linkTargetQuery.value || undefined, pageSize: 15 })
    // Never offer this ticket as its own link target.
    linkCandidates.value = page.items.filter((i) => i.id !== props.ticket.id)
  } catch {
    linkCandidates.value = []
  }
}

async function addLink() {
  if (!linkTargetId.value || savingLink.value) return

  savingLink.value = true
  try {
    links.value = await ticketsApi.addLink(props.ticket.id, linkTargetId.value, linkType.value)
    linkDialog.value = false
    linkTargetId.value = null
    linkTargetQuery.value = ''
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 6000 })
  } finally {
    savingLink.value = false
  }
}

async function removeLink(linkId: string) {
  try {
    links.value = await ticketsApi.removeLink(props.ticket.id, linkId)
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
  }
}

// ---- Merge ----

const mergeDialog = ref(false)
const mergeTargetId = ref<string | null>(null)
const mergeReason = ref('')
const merging = ref(false)

async function confirmMerge() {
  if (!mergeTargetId.value || merging.value) return

  merging.value = true
  try {
    const target = await ticketsApi.merge(props.ticket.id, mergeTargetId.value, mergeReason.value || null)
    mergeDialog.value = false
    toast.add({ severity: 'success', summary: t('ticket.merge.done', { number: target.number }), life: 5000 })

    // This ticket is now closed and its content lives on the target, so following the
    // merge is the only sensible next view.
    await router.push({ name: 'ticket-detail', params: { id: target.id } })
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 7000 })
  } finally {
    merging.value = false
  }
}

// ---- Escalation ----

const escalationDialog = ref(false)
const escalationDelta = ref(1)
const escalationReason = ref('')
const escalating = ref(false)

function openEscalation(delta: number) {
  escalationDelta.value = delta
  escalationReason.value = ''
  escalationDialog.value = true
}

async function confirmEscalation() {
  if (!escalationReason.value.trim() || escalating.value) return

  escalating.value = true
  try {
    const updated = await ticketsApi.changeEscalation(
      props.ticket.id,
      escalationDelta.value,
      escalationReason.value.trim(),
    )
    escalationDialog.value = false
    emit('changed', updated)
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 6000 })
  } finally {
    escalating.value = false
  }
}
</script>

<template>
  <div class="flex flex-col gap-4">
    <!-- Tags -->
    <section class="rounded-xl border border-surface-200 p-3 dark:border-surface-800">
      <h3 class="mb-2 text-sm font-semibold">{{ t('ticket.tags') }}</h3>

      <div class="mb-2 flex flex-wrap gap-1">
        <Chip
          v-for="tag in tags"
          :key="tag.id"
          :label="tag.name"
          :removable="canEdit"
          :style="tag.colorHex ? { backgroundColor: tag.colorHex, color: '#fff' } : undefined"
          @remove="removeTag(tag.id)"
        />
        <span v-if="!tags.length" class="text-sm text-surface-500 dark:text-surface-400">
          {{ t('ticket.noTags') }}
        </span>
      </div>

      <div v-if="canEdit" class="flex gap-2">
        <AutoComplete
          v-model="tagQuery"
          :suggestions="tagSuggestions"
          option-label="name"
          :placeholder="t('ticket.addTag')"
          class="flex-1"
          input-class="w-full"
          @complete="suggestTags"
          @keyup.enter="addTag"
        />
        <Button icon="pi pi-plus" size="small" outlined :aria-label="t('ticket.addTag')" @click="addTag" />
      </div>
    </section>

    <!-- Watchers -->
    <section class="rounded-xl border border-surface-200 p-3 dark:border-surface-800">
      <div class="mb-2 flex items-center justify-between">
        <h3 class="text-sm font-semibold">{{ t('ticket.watchers') }}</h3>
        <Button
          :icon="isWatching ? 'pi pi-eye-slash' : 'pi pi-eye'"
          :label="isWatching ? t('ticket.unwatch') : t('ticket.watch')"
          text
          size="small"
          @click="toggleSelfWatch"
        />
      </div>

      <ul v-if="watchers.length" class="mb-2 flex flex-col gap-1">
        <li v-for="w in watchers" :key="w.userId" class="flex items-center gap-2 text-sm">
          <i class="pi pi-user text-surface-400" />
          <span>{{ w.displayName }}</span>
          <Button
            v-if="w.userId === auth.user?.id || isSupervisor"
            icon="pi pi-times"
            text
            rounded
            size="small"
            class="ms-auto"
            :aria-label="t('app.delete')"
            @click="removeWatcher(w.userId)"
          />
        </li>
      </ul>
      <p v-else class="mb-2 text-sm text-surface-500 dark:text-surface-400">{{ t('ticket.noWatchers') }}</p>

      <div v-if="isSupervisor" class="flex gap-2">
        <Select
          v-model="watcherToAdd"
          :options="agents"
          :option-label="(a: Agent) => ui.localizedFullName(a)"
          option-value="id"
          :placeholder="t('ticket.addWatcher')"
          class="flex-1"
          @before-show="loadAgents"
        />
        <Button icon="pi pi-plus" size="small" outlined :disabled="!watcherToAdd" @click="addWatcher" />
      </div>
    </section>

    <!-- Links -->
    <section class="rounded-xl border border-surface-200 p-3 dark:border-surface-800">
      <div class="mb-2 flex items-center justify-between">
        <h3 class="text-sm font-semibold">{{ t('ticket.links') }}</h3>
        <Button
          v-if="canEdit"
          icon="pi pi-link"
          :label="t('ticket.addLink')"
          text
          size="small"
          @click="linkDialog = true"
        />
      </div>

      <ul v-if="links.length" class="flex flex-col gap-2">
        <li v-for="link in links" :key="link.id" class="flex items-center gap-2 text-sm">
          <Tag
            severity="secondary"
            :value="t(`ticket.linkType.${TicketLinkType[link.type]}`)"
            rounded
          />
          <RouterLink
            :to="{ name: 'ticket-detail', params: { id: link.otherTicketId } }"
            class="min-w-0 flex-1 truncate text-primary hover:underline"
          >
            <span class="ltr-nums">{{ link.otherTicketNumber }}</span> — {{ link.otherTicketSubject }}
          </RouterLink>
          <Button
            v-if="canEdit"
            icon="pi pi-times"
            text
            rounded
            size="small"
            :aria-label="t('app.delete')"
            @click="removeLink(link.id)"
          />
        </li>
      </ul>
      <p v-else class="text-sm text-surface-500 dark:text-surface-400">{{ t('ticket.noLinks') }}</p>
    </section>

    <!-- Escalation + merge -->
    <section class="rounded-xl border border-surface-200 p-3 dark:border-surface-800">
      <h3 class="mb-2 text-sm font-semibold">{{ t('ticket.escalationLevel') }}</h3>

      <div class="mb-3 flex items-center gap-2">
        <Tag
          :severity="ticket.escalationLevel > 0 ? 'danger' : 'secondary'"
          :value="String(ticket.escalationLevel)"
          rounded
        />
        <Button
          v-if="canEdit"
          icon="pi pi-arrow-up"
          :label="t('ticket.escalate')"
          text
          size="small"
          @click="openEscalation(1)"
        />
        <Button
          v-if="canEdit && ticket.escalationLevel > 0"
          icon="pi pi-arrow-down"
          :label="t('ticket.deEscalate')"
          text
          size="small"
          @click="openEscalation(-1)"
        />
      </div>

      <Button
        v-if="canMerge"
        icon="pi pi-sign-in"
        :label="t('ticket.merge.action')"
        severity="secondary"
        outlined
        size="small"
        class="w-full"
        @click="mergeDialog = true"
      />
    </section>

    <!-- Add link -->
    <Dialog v-model:visible="linkDialog" modal :header="t('ticket.addLink')" :style="{ width: '30rem' }">
      <div class="flex flex-col gap-3">
        <Select
          v-model="linkType"
          :options="linkTypeOptions"
          option-label="label"
          option-value="value"
          class="w-full"
        />
        <InputText
          v-model="linkTargetQuery"
          :placeholder="t('ticket.searchToLink')"
          class="w-full"
          @keyup.enter="searchLinkTargets"
        />
        <Button :label="t('app.search')" size="small" outlined @click="searchLinkTargets" />
        <Select
          v-model="linkTargetId"
          :options="linkCandidates"
          :option-label="(i: TicketListItem) => `${i.number} — ${i.subject}`"
          option-value="id"
          :placeholder="t('ticket.one')"
          class="w-full"
        />
      </div>
      <template #footer>
        <Button :label="t('app.cancel')" outlined @click="linkDialog = false" />
        <Button :label="t('app.save')" :loading="savingLink" :disabled="!linkTargetId" @click="addLink" />
      </template>
    </Dialog>

    <!-- Merge -->
    <Dialog v-model:visible="mergeDialog" modal :header="t('ticket.merge.action')" :style="{ width: '32rem' }">
      <div class="flex flex-col gap-3">
        <p class="text-sm text-surface-600 dark:text-surface-300">{{ t('ticket.merge.explain') }}</p>
        <InputText
          v-model="linkTargetQuery"
          :placeholder="t('ticket.searchToLink')"
          class="w-full"
          @keyup.enter="searchLinkTargets"
        />
        <Button :label="t('app.search')" size="small" outlined @click="searchLinkTargets" />
        <Select
          v-model="mergeTargetId"
          :options="linkCandidates"
          :option-label="(i: TicketListItem) => `${i.number} — ${i.subject}`"
          option-value="id"
          :placeholder="t('ticket.merge.target')"
          class="w-full"
        />
        <Textarea v-model="mergeReason" rows="2" auto-resize :placeholder="t('ticket.statusNote')" class="w-full" />
      </div>
      <template #footer>
        <Button :label="t('app.cancel')" outlined @click="mergeDialog = false" />
        <Button
          :label="t('ticket.merge.confirm')"
          severity="danger"
          :loading="merging"
          :disabled="!mergeTargetId"
          @click="confirmMerge"
        />
      </template>
    </Dialog>

    <!-- Escalation -->
    <Dialog
      v-model:visible="escalationDialog"
      modal
      :header="escalationDelta > 0 ? t('ticket.escalate') : t('ticket.deEscalate')"
      :style="{ width: '28rem' }"
    >
      <div class="flex flex-col gap-2">
        <label class="text-sm font-medium">{{ t('ticket.escalationReason') }} *</label>
        <Textarea v-model="escalationReason" rows="3" auto-resize class="w-full" />
        <small class="text-surface-500 dark:text-surface-400">{{ t('ticket.escalationReasonHint') }}</small>
      </div>
      <template #footer>
        <Button :label="t('app.cancel')" outlined @click="escalationDialog = false" />
        <Button
          :label="t('app.confirm')"
          :loading="escalating"
          :disabled="!escalationReason.trim()"
          @click="confirmEscalation"
        />
      </template>
    </Dialog>
  </div>
</template>
