<script setup lang="ts">
import { onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Dialog from 'primevue/dialog'
import InputText from 'primevue/inputtext'
import InputNumber from 'primevue/inputnumber'
import MultiSelect from 'primevue/multiselect'
import DatePicker from 'primevue/datepicker'
import ToggleSwitch from 'primevue/toggleswitch'
import Tag from 'primevue/tag'
import Message from 'primevue/message'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import PageHeader from '@/components/PageHeader.vue'
import { integrationsApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useFormat } from '@/composables/useFormat'
import {
  WebhookDeliveryStatus,
  type ApiKey,
  type WebhookDelivery,
  type WebhookSubscription,
} from '@/types/api'

const { t } = useI18n()
const toast = useToast()
const confirm = useConfirm()
const { formatDateTime } = useFormat()

const tab = ref('keys')
const keys = ref<ApiKey[]>([])
const hooks = ref<WebhookSubscription[]>([])
const scopes = ref<string[]>([])
const events = ref<string[]>([])
const loading = ref(false)
const saving = ref(false)

/** Shown once, then gone. The server stores only a hash, so there is no second chance. */
const revealed = ref<{ title: string; value: string } | null>(null)

// ---- API keys ----

const keyDialog = ref(false)
const keyForm = reactive({
  name: '',
  scopes: [] as string[],
  expiresAt: null as Date | null,
  rateLimitPerMinute: 120,
})

function openKeyCreate() {
  Object.assign(keyForm, { name: '', scopes: [], expiresAt: null, rateLimitPerMinute: 120 })
  keyDialog.value = true
}

async function createKey() {
  if (!keyForm.name.trim() || keyForm.scopes.length === 0 || saving.value) return

  saving.value = true
  try {
    const created = await integrationsApi.createKey({
      name: keyForm.name.trim(),
      scopes: keyForm.scopes,
      expiresAt: keyForm.expiresAt ? keyForm.expiresAt.toISOString() : null,
      rateLimitPerMinute: keyForm.rateLimitPerMinute,
    })

    keyDialog.value = false
    revealed.value = { title: t('integration.keyCreated'), value: created.plainKey }
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 7000 })
  } finally {
    saving.value = false
  }
}

function confirmRevoke(key: ApiKey) {
  confirm.require({
    message: t('integration.revokeConfirm', { name: key.name }),
    header: t('app.confirm'),
    icon: 'pi pi-exclamation-triangle',
    acceptProps: { label: t('integration.revoke'), severity: 'danger' },
    rejectProps: { label: t('app.cancel'), outlined: true },
    accept: async () => {
      try {
        await integrationsApi.revokeKey(key.id, null)
        await load()
      } catch (e) {
        toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
      }
    },
  })
}

// ---- Webhooks ----

const hookDialog = ref(false)
const editingHookId = ref<string | null>(null)
const hookForm = reactive({ name: '', url: '', events: [] as string[], isActive: true })

const deliveriesFor = ref<WebhookSubscription | null>(null)
const deliveries = ref<WebhookDelivery[]>([])

function openHookCreate() {
  editingHookId.value = null
  Object.assign(hookForm, { name: '', url: '', events: [], isActive: true })
  hookDialog.value = true
}

function openHookEdit(hook: WebhookSubscription) {
  editingHookId.value = hook.id
  Object.assign(hookForm, {
    name: hook.name,
    url: hook.url,
    events: [...hook.events],
    isActive: hook.isActive,
  })
  hookDialog.value = true
}

async function saveHook() {
  if (!hookForm.name.trim() || !hookForm.url.trim() || hookForm.events.length === 0 || saving.value) return

  saving.value = true
  try {
    if (editingHookId.value) {
      await integrationsApi.updateWebhook(editingHookId.value, { ...hookForm })
    } else {
      const created = await integrationsApi.createWebhook({ ...hookForm })
      revealed.value = { title: t('integration.secretCreated'), value: created.secret }
    }

    hookDialog.value = false
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 7000 })
  } finally {
    saving.value = false
  }
}

async function openDeliveries(hook: WebhookSubscription) {
  deliveriesFor.value = hook
  try {
    deliveries.value = await integrationsApi.deliveries(hook.id)
  } catch {
    deliveries.value = []
  }
}

async function redeliver(delivery: WebhookDelivery) {
  const hook = deliveriesFor.value
  if (!hook) return

  try {
    await integrationsApi.redeliver(hook.id, delivery.id)
    toast.add({ severity: 'success', summary: t('integration.queued'), life: 3000 })
    await openDeliveries(hook)
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
  }
}

function deliverySeverity(status: WebhookDeliveryStatus) {
  switch (status) {
    case WebhookDeliveryStatus.Delivered:
      return 'success'
    case WebhookDeliveryStatus.Retrying:
      return 'warn'
    case WebhookDeliveryStatus.Failed:
      return 'danger'
    default:
      return 'secondary'
  }
}

async function copy(value: string) {
  try {
    await navigator.clipboard.writeText(value)
    toast.add({ severity: 'success', summary: t('integration.copied'), life: 2000 })
  } catch {
    // Clipboard access can be denied; the value is on screen to copy by hand.
  }
}

async function load() {
  loading.value = true
  try {
    const [k, h, s, e] = await Promise.all([
      integrationsApi.keys(),
      integrationsApi.webhooks(),
      integrationsApi.scopes(),
      integrationsApi.events(),
    ])

    keys.value = k
    hooks.value = h
    scopes.value = s
    events.value = e
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
    <PageHeader :title="t('integration.title')" :subtitle="t('integration.subtitle')" />

    <Tabs v-model:value="tab">
      <TabList>
        <Tab value="keys">{{ t('integration.apiKeys') }}</Tab>
        <Tab value="webhooks">{{ t('integration.webhooks') }}</Tab>
      </TabList>

      <TabPanels>
        <!-- API keys -->
        <TabPanel value="keys">
          <div class="mb-3 flex justify-end">
            <Button :label="t('integration.newKey')" icon="pi pi-plus" size="small" @click="openKeyCreate" />
          </div>

          <DataTable :value="keys" :loading="loading" size="small" striped-rows>
            <template #empty>
              <div class="p-6 text-center text-surface-500 dark:text-surface-400">
                {{ t('integration.noKeys') }}
              </div>
            </template>

            <Column :header="t('integration.name')">
              <template #body="{ data }">
                <div class="flex items-center gap-2">
                  <span>{{ data.name }}</span>
                  <Tag v-if="!data.isActive" severity="danger" rounded :value="t('integration.revoked')" />
                </div>
              </template>
            </Column>

            <Column :header="t('integration.prefix')">
              <template #body="{ data }">
                <code class="ltr-nums text-xs">csk_{{ data.prefix }}…</code>
              </template>
            </Column>

            <Column :header="t('integration.scopes')">
              <template #body="{ data }">
                <span class="text-xs">{{ data.scopes.length }}</span>
              </template>
            </Column>

            <Column :header="t('integration.rateLimit')">
              <template #body="{ data }">
                <span class="ltr-nums text-sm">{{ data.rateLimitPerMinute }}/min</span>
              </template>
            </Column>

            <Column :header="t('integration.lastUsed')">
              <template #body="{ data }">
                <span class="text-sm">{{ data.lastUsedAt ? formatDateTime(data.lastUsedAt) : '—' }}</span>
              </template>
            </Column>

            <Column :header="t('integration.expires')">
              <template #body="{ data }">
                <span class="text-sm">{{ data.expiresAt ? formatDateTime(data.expiresAt) : '—' }}</span>
              </template>
            </Column>

            <Column style="width: 6rem">
              <template #body="{ data }">
                <Button
                  v-if="data.isActive"
                  icon="pi pi-ban"
                  severity="danger"
                  text
                  rounded
                  size="small"
                  :aria-label="t('integration.revoke')"
                  @click="confirmRevoke(data)"
                />
              </template>
            </Column>
          </DataTable>
        </TabPanel>

        <!-- Webhooks -->
        <TabPanel value="webhooks">
          <div class="mb-3 flex justify-end">
            <Button :label="t('integration.newWebhook')" icon="pi pi-plus" size="small" @click="openHookCreate" />
          </div>

          <DataTable :value="hooks" :loading="loading" size="small" striped-rows>
            <template #empty>
              <div class="p-6 text-center text-surface-500 dark:text-surface-400">
                {{ t('integration.noWebhooks') }}
              </div>
            </template>

            <Column :header="t('integration.name')">
              <template #body="{ data }">
                <div class="flex items-center gap-2">
                  <span>{{ data.name }}</span>
                  <Tag v-if="data.disabledAt" severity="danger" rounded :value="t('integration.autoDisabled')" />
                  <Tag v-else-if="!data.isActive" severity="secondary" rounded :value="t('app.inactive')" />
                </div>
              </template>
            </Column>

            <Column :header="t('integration.url')">
              <template #body="{ data }"><code class="text-xs">{{ data.url }}</code></template>
            </Column>

            <Column :header="t('integration.events')">
              <template #body="{ data }"><span class="text-xs">{{ data.events.join(', ') }}</span></template>
            </Column>

            <Column :header="t('integration.failures')">
              <template #body="{ data }">
                <span
                  class="ltr-nums text-sm"
                  :class="data.consecutiveFailures > 0 ? 'text-red-500' : ''"
                >
                  {{ data.consecutiveFailures }}
                </span>
              </template>
            </Column>

            <Column style="width: 9rem">
              <template #body="{ data }">
                <div class="flex items-center gap-1">
                  <Button
                    icon="pi pi-history"
                    text
                    rounded
                    size="small"
                    :aria-label="t('integration.deliveries')"
                    @click="openDeliveries(data)"
                  />
                  <Button icon="pi pi-pencil" text rounded size="small" @click="openHookEdit(data)" />
                </div>
              </template>
            </Column>
          </DataTable>
        </TabPanel>
      </TabPanels>
    </Tabs>

    <!-- The one-time secret -->
    <Dialog
      :visible="revealed !== null"
      modal
      :header="revealed?.title"
      :style="{ width: '34rem' }"
      :closable="true"
      @update:visible="(v) => { if (!v) revealed = null }"
    >
      <Message severity="warn" :closable="false" class="mb-3 text-sm">
        {{ t('integration.copyNow') }}
      </Message>

      <div class="flex items-center gap-2">
        <code class="flex-1 break-all rounded bg-surface-100 p-3 text-xs dark:bg-surface-800">
          {{ revealed?.value }}
        </code>
        <Button icon="pi pi-copy" @click="revealed && copy(revealed.value)" />
      </div>
    </Dialog>

    <!-- Key editor -->
    <Dialog v-model:visible="keyDialog" modal :header="t('integration.newKey')" :style="{ width: '32rem' }">
      <div class="space-y-4">
        <div>
          <label class="mb-1 block text-sm font-medium">{{ t('integration.name') }} *</label>
          <InputText v-model="keyForm.name" class="w-full" />
        </div>

        <div>
          <label class="mb-1 block text-sm font-medium">{{ t('integration.scopes') }} *</label>
          <MultiSelect
            v-model="keyForm.scopes"
            :options="scopes"
            filter
            display="chip"
            class="w-full"
            :placeholder="t('integration.pickScopes')"
          />
          <small class="text-surface-500 dark:text-surface-400">{{ t('integration.scopesHint') }}</small>
        </div>

        <div class="grid gap-4 sm:grid-cols-2">
          <div>
            <label class="mb-1 block text-sm font-medium">{{ t('integration.expires') }}</label>
            <DatePicker v-model="keyForm.expiresAt" show-clear class="w-full" />
          </div>
          <div>
            <label class="mb-1 block text-sm font-medium">{{ t('integration.rateLimit') }}</label>
            <InputNumber v-model="keyForm.rateLimitPerMinute" :min="1" :max="10000" class="w-full" />
          </div>
        </div>
      </div>

      <template #footer>
        <Button :label="t('app.cancel')" text @click="keyDialog = false" />
        <Button
          :label="t('app.save')"
          :disabled="!keyForm.name.trim() || keyForm.scopes.length === 0 || saving"
          :loading="saving"
          @click="createKey"
        />
      </template>
    </Dialog>

    <!-- Webhook editor -->
    <Dialog
      v-model:visible="hookDialog"
      modal
      :header="editingHookId ? t('integration.editWebhook') : t('integration.newWebhook')"
      :style="{ width: '32rem' }"
    >
      <div class="space-y-4">
        <div>
          <label class="mb-1 block text-sm font-medium">{{ t('integration.name') }} *</label>
          <InputText v-model="hookForm.name" class="w-full" />
        </div>

        <div>
          <label class="mb-1 block text-sm font-medium">{{ t('integration.url') }} *</label>
          <InputText v-model="hookForm.url" class="w-full" dir="ltr" placeholder="https://" />
        </div>

        <div>
          <label class="mb-1 block text-sm font-medium">{{ t('integration.events') }} *</label>
          <MultiSelect
            v-model="hookForm.events"
            :options="events"
            display="chip"
            class="w-full"
            :placeholder="t('integration.pickEvents')"
          />
        </div>

        <div class="flex items-center gap-2">
          <ToggleSwitch v-model="hookForm.isActive" input-id="hook-active" />
          <label for="hook-active" class="text-sm">{{ t('app.active') }}</label>
        </div>

        <Message severity="info" :closable="false" class="text-sm">
          {{ t('integration.signatureHint') }}
        </Message>
      </div>

      <template #footer>
        <Button :label="t('app.cancel')" text @click="hookDialog = false" />
        <Button :label="t('app.save')" :loading="saving" @click="saveHook" />
      </template>
    </Dialog>

    <!-- Deliveries -->
    <Dialog
      :visible="deliveriesFor !== null"
      modal
      :header="t('integration.deliveries')"
      :style="{ width: '46rem' }"
      :breakpoints="{ '960px': '95vw' }"
      @update:visible="(v) => { if (!v) deliveriesFor = null }"
    >
      <DataTable :value="deliveries" size="small" striped-rows>
        <template #empty>
          <div class="p-4 text-center text-sm text-surface-500 dark:text-surface-400">
            {{ t('integration.noDeliveries') }}
          </div>
        </template>

        <Column :header="t('integration.event')">
          <template #body="{ data }"><code class="text-xs">{{ data.eventType }}</code></template>
        </Column>

        <Column :header="t('app.status')">
          <template #body="{ data }">
            <Tag
              :severity="deliverySeverity(data.status)"
              rounded
              :value="t(`integration.deliveryStatus.${WebhookDeliveryStatus[data.status]}`)"
            />
          </template>
        </Column>

        <Column :header="t('integration.attempts')">
          <template #body="{ data }"><span class="ltr-nums text-sm">{{ data.attemptCount }}</span></template>
        </Column>

        <Column :header="t('integration.response')">
          <template #body="{ data }">
            <span class="ltr-nums text-sm">{{ data.lastStatusCode ?? '—' }}</span>
          </template>
        </Column>

        <Column :header="t('app.createdAt')">
          <template #body="{ data }"><span class="text-sm">{{ formatDateTime(data.createdAt) }}</span></template>
        </Column>

        <Column style="width: 7rem">
          <template #body="{ data }">
            <Button :label="t('integration.redeliver')" text size="small" @click="redeliver(data)" />
          </template>
        </Column>
      </DataTable>
    </Dialog>
  </div>
</template>
