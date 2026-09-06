<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import InputText from 'primevue/inputtext'
import ColorPicker from 'primevue/colorpicker'
import Select from 'primevue/select'
import Button from 'primevue/button'
import ToggleSwitch from 'primevue/toggleswitch'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import DatePicker from 'primevue/datepicker'
import Tag from 'primevue/tag'
import Password from 'primevue/password'
import PageHeader from '@/components/PageHeader.vue'
import { systemConfigApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useAuthStore } from '@/stores/auth'
import { useBrandingStore } from '@/stores/branding'
import { useUiStore } from '@/stores/ui'
import { useFormat } from '@/composables/useFormat'
import {
  CommunicationChannel,
  PERMISSIONS,
  type Branding,
  type BusinessHoursDay,
  type ChannelToggle,
  type FeatureFlag,
  type Holiday,
} from '@/types/api'

const { t } = useI18n()
const toast = useToast()
const confirm = useConfirm()
const auth = useAuthStore()
const brandingStore = useBrandingStore()
const ui = useUiStore()
const { formatDate } = useFormat()

const canManage = auth.hasPermission(PERMISSIONS.systemConfigManage)

const branding = ref<Branding>({
  companyNameAr: '',
  companyNameEn: '',
  logoUrl: null,
  primaryColor: '#0f766e',
  secondaryColor: null,
  defaultLocale: 'ar',
})
const days = ref<BusinessHoursDay[]>([])
const holidays = ref<Holiday[]>([])
const flags = ref<FeatureFlag[]>([])
const channels = ref<ChannelToggle[]>([])

const savingBranding = ref(false)
const savingHours = ref(false)

const newHolidayDate = ref<Date | null>(null)
const newHolidayAr = ref('')
const newHolidayEn = ref('')

const localeOptions = [
  { label: t('app.arabic'), value: 'ar' },
  { label: t('app.english'), value: 'en' },
]

// DayOfWeek is 0=Sunday in .NET; label from the i18n catalogue rather than toLocaleString
// so the order and wording stay under our control.
const DAY_KEYS = ['sunday', 'monday', 'tuesday', 'wednesday', 'thursday', 'friday', 'saturday']

function dayLabel(day: number) {
  return t(`settings.day.${DAY_KEYS[day]}`)
}

/** PrimeVue ColorPicker works in bare hex; the API stores it with the leading #. */
function hexIn(value: string | null): string {
  return (value ?? '#0f766e').replace('#', '')
}

function hexOut(value: string): string {
  return value.startsWith('#') ? value : `#${value}`
}

async function loadAll() {
  const results = await Promise.allSettled([
    systemConfigApi.branding(),
    systemConfigApi.businessHours(),
    systemConfigApi.holidays(),
    systemConfigApi.featureFlags(),
    systemConfigApi.channels(),
  ])

  if (results[0].status === 'fulfilled') branding.value = results[0].value
  if (results[1].status === 'fulfilled') days.value = results[1].value
  if (results[2].status === 'fulfilled') holidays.value = results[2].value
  if (results[3].status === 'fulfilled') flags.value = results[3].value
  if (results[4].status === 'fulfilled') channels.value = results[4].value
}

async function saveBranding() {
  if (savingBranding.value) return

  savingBranding.value = true
  try {
    const saved = await systemConfigApi.updateBranding({
      ...branding.value,
      primaryColor: branding.value.primaryColor ? hexOut(branding.value.primaryColor) : null,
      secondaryColor: branding.value.secondaryColor ? hexOut(branding.value.secondaryColor) : null,
    })
    branding.value = saved
    // Apply immediately so the admin sees the result without reloading.
    brandingStore.set(saved)
    toast.add({ severity: 'success', summary: t('settings.brandingSaved'), life: 3000 })
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 6000 })
  } finally {
    savingBranding.value = false
  }
}

async function saveHours() {
  if (savingHours.value) return

  savingHours.value = true
  try {
    days.value = await systemConfigApi.saveBusinessHours(days.value)
    toast.add({ severity: 'success', summary: t('settings.hoursSaved'), life: 3000 })
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 6000 })
  } finally {
    savingHours.value = false
  }
}

async function addHoliday() {
  if (!newHolidayDate.value || !newHolidayAr.value.trim() || !newHolidayEn.value.trim()) return

  try {
    // Date only, in local terms: a holiday is a calendar day, not an instant, so sending
    // an ISO timestamp would shift it across a timezone boundary.
    const d = newHolidayDate.value
    const iso = `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`

    const created = await systemConfigApi.addHoliday({
      date: iso,
      nameAr: newHolidayAr.value.trim(),
      nameEn: newHolidayEn.value.trim(),
    })

    holidays.value = [...holidays.value, created].sort((a, b) => a.date.localeCompare(b.date))
    newHolidayDate.value = null
    newHolidayAr.value = ''
    newHolidayEn.value = ''
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 6000 })
  }
}

function removeHoliday(holiday: Holiday) {
  confirm.require({
    message: t('settings.deleteHolidayConfirm', { name: ui.isArabic ? holiday.nameAr : holiday.nameEn }),
    header: t('app.confirm'),
    icon: 'pi pi-exclamation-triangle',
    acceptProps: { label: t('app.delete'), severity: 'danger' },
    rejectProps: { label: t('app.cancel'), outlined: true },
    accept: async () => {
      try {
        await systemConfigApi.deleteHoliday(holiday.id)
        holidays.value = holidays.value.filter((h) => h.id !== holiday.id)
      } catch (e) {
        toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
      }
    },
  })
}

async function toggleFlag(flag: FeatureFlag) {
  try {
    await systemConfigApi.saveFeatureFlag({
      key: flag.key,
      isEnabled: flag.isEnabled,
      descriptionAr: flag.descriptionAr,
      descriptionEn: flag.descriptionEn,
    })
  } catch (e) {
    flag.isEnabled = !flag.isEnabled
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 5000 })
  }
}

async function saveChannel(channel: ChannelToggle, apiKey?: string, apiSecret?: string) {
  try {
    const saved = await systemConfigApi.updateChannel(channel.channel, {
      isEnabled: channel.isEnabled,
      endpoint: channel.endpoint,
      // Undefined leaves the stored secret alone; the field is only sent when typed into.
      ...(apiKey !== undefined ? { apiKey } : {}),
      ...(apiSecret !== undefined ? { apiSecret } : {}),
    })
    Object.assign(channel, saved)
    toast.add({ severity: 'success', summary: t('settings.channelSaved'), life: 2500 })
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 6000 })
  }
}

/** Per-row secret drafts, keyed by channel, so typing in one row does not touch another. */
const secretDrafts = ref<Record<number, { apiKey: string; apiSecret: string }>>({})

function draftFor(channel: CommunicationChannel) {
  secretDrafts.value[channel] ??= { apiKey: '', apiSecret: '' }
  return secretDrafts.value[channel]
}

onMounted(loadAll)
</script>

<template>
  <div>
    <PageHeader :title="t('settings.title')" :subtitle="t('settings.subtitle')" />

    <Tabs value="branding">
      <TabList>
        <Tab value="branding">{{ t('settings.branding') }}</Tab>
        <Tab value="hours">{{ t('settings.businessHours') }}</Tab>
        <Tab value="holidays">{{ t('settings.holidays') }}</Tab>
        <Tab value="flags">{{ t('settings.featureFlags') }}</Tab>
        <Tab value="channels">{{ t('settings.channels') }}</Tab>
      </TabList>

      <TabPanels>
        <!-- Branding -->
        <TabPanel value="branding">
          <div class="grid max-w-3xl gap-4 py-2 sm:grid-cols-2">
            <div class="flex flex-col gap-1.5">
              <label class="text-sm font-medium">{{ t('settings.companyNameAr') }}</label>
              <InputText v-model="branding.companyNameAr" dir="rtl" :disabled="!canManage" class="w-full" />
            </div>
            <div class="flex flex-col gap-1.5">
              <label class="text-sm font-medium">{{ t('settings.companyNameEn') }}</label>
              <InputText v-model="branding.companyNameEn" dir="ltr" :disabled="!canManage" class="w-full" />
            </div>
            <div class="flex flex-col gap-1.5 sm:col-span-2">
              <label class="text-sm font-medium">{{ t('settings.logoUrl') }}</label>
              <InputText v-model="branding.logoUrl" dir="ltr" :disabled="!canManage" class="w-full" />
              <small class="text-surface-500 dark:text-surface-400">{{ t('settings.logoUrlHint') }}</small>
            </div>
            <div class="flex flex-col gap-1.5">
              <label class="text-sm font-medium">{{ t('settings.primaryColor') }}</label>
              <div class="flex items-center gap-2">
                <ColorPicker
                  :model-value="hexIn(branding.primaryColor)"
                  :disabled="!canManage"
                  @update:model-value="(v: string) => (branding.primaryColor = hexOut(v))"
                />
                <InputText v-model="branding.primaryColor" dir="ltr" :disabled="!canManage" class="w-32" />
              </div>
            </div>
            <div class="flex flex-col gap-1.5">
              <label class="text-sm font-medium">{{ t('settings.defaultLocale') }}</label>
              <Select
                v-model="branding.defaultLocale"
                :options="localeOptions"
                option-label="label"
                option-value="value"
                :disabled="!canManage"
                class="w-full"
              />
            </div>
          </div>

          <Button
            v-if="canManage"
            :label="t('app.save')"
            :loading="savingBranding"
            size="small"
            class="mt-4"
            @click="saveBranding"
          />
        </TabPanel>

        <!-- Business hours -->
        <TabPanel value="hours">
          <p class="mb-3 text-sm text-surface-500 dark:text-surface-400">{{ t('settings.businessHoursHint') }}</p>

          <DataTable :value="days" size="small" striped-rows class="max-w-2xl">
            <Column :header="t('settings.dayLabel')">
              <template #body="{ data }">{{ dayLabel(data.day) }}</template>
            </Column>
            <Column :header="t('settings.workingDay')">
              <template #body="{ data }">
                <ToggleSwitch v-model="data.isWorkingDay" :disabled="!canManage" />
              </template>
            </Column>
            <Column :header="t('settings.openAt')">
              <template #body="{ data }">
                <InputText
                  v-model="data.openAt"
                  dir="ltr"
                  placeholder="08:00:00"
                  :disabled="!canManage || !data.isWorkingDay"
                  class="w-28"
                />
              </template>
            </Column>
            <Column :header="t('settings.closeAt')">
              <template #body="{ data }">
                <InputText
                  v-model="data.closeAt"
                  dir="ltr"
                  placeholder="17:00:00"
                  :disabled="!canManage || !data.isWorkingDay"
                  class="w-28"
                />
              </template>
            </Column>
          </DataTable>

          <Button
            v-if="canManage"
            :label="t('app.save')"
            :loading="savingHours"
            size="small"
            class="mt-4"
            @click="saveHours"
          />
        </TabPanel>

        <!-- Holidays -->
        <TabPanel value="holidays">
          <div v-if="canManage" class="mb-4 flex flex-wrap items-end gap-2">
            <DatePicker v-model="newHolidayDate" show-icon icon-display="input" :placeholder="t('settings.date')" />
            <InputText v-model="newHolidayAr" :placeholder="t('settings.nameAr')" dir="rtl" />
            <InputText v-model="newHolidayEn" :placeholder="t('settings.nameEn')" dir="ltr" />
            <Button
              icon="pi pi-plus"
              :label="t('settings.addHoliday')"
              size="small"
              :disabled="!newHolidayDate || !newHolidayAr.trim() || !newHolidayEn.trim()"
              @click="addHoliday"
            />
          </div>

          <DataTable :value="holidays" size="small" striped-rows class="max-w-2xl">
            <template #empty>
              <div class="p-6 text-center text-surface-500 dark:text-surface-400">{{ t('app.noData') }}</div>
            </template>
            <Column :header="t('settings.date')">
              <template #body="{ data }">{{ formatDate(data.date) }}</template>
            </Column>
            <Column :header="t('settings.name')">
              <template #body="{ data }">{{ ui.isArabic ? data.nameAr : data.nameEn }}</template>
            </Column>
            <Column v-if="canManage" :header="t('app.actions')">
              <template #body="{ data }">
                <Button
                  icon="pi pi-trash"
                  severity="danger"
                  text
                  rounded
                  size="small"
                  :aria-label="t('app.delete')"
                  @click="removeHoliday(data)"
                />
              </template>
            </Column>
          </DataTable>
        </TabPanel>

        <!-- Feature flags -->
        <TabPanel value="flags">
          <p class="mb-3 text-sm text-surface-500 dark:text-surface-400">{{ t('settings.featureFlagsHint') }}</p>

          <DataTable :value="flags" size="small" striped-rows class="max-w-3xl">
            <template #empty>
              <div class="p-6 text-center text-surface-500 dark:text-surface-400">
                {{ t('settings.noFlags') }}
              </div>
            </template>
            <Column field="key" :header="t('settings.flagKey')">
              <template #body="{ data }"><span class="ltr-nums font-medium">{{ data.key }}</span></template>
            </Column>
            <Column :header="t('settings.description')">
              <template #body="{ data }">
                {{ (ui.isArabic ? data.descriptionAr : data.descriptionEn) ?? '—' }}
              </template>
            </Column>
            <Column :header="t('settings.enabled')">
              <template #body="{ data }">
                <ToggleSwitch v-model="data.isEnabled" :disabled="!canManage" @change="toggleFlag(data)" />
              </template>
            </Column>
          </DataTable>
        </TabPanel>

        <!-- Channels -->
        <TabPanel value="channels">
          <p class="mb-3 text-sm text-surface-500 dark:text-surface-400">{{ t('settings.channelsHint') }}</p>

          <div class="flex max-w-3xl flex-col gap-3">
            <div
              v-for="channel in channels"
              :key="channel.channel"
              class="rounded-lg border border-surface-200 p-3 dark:border-surface-800"
            >
              <div class="flex flex-wrap items-center gap-3">
                <span class="font-medium">
                  {{ t(`channel.${CommunicationChannel[channel.channel]}`) }}
                </span>
                <Tag
                  :severity="channel.hasCredentials ? 'success' : 'secondary'"
                  :value="channel.hasCredentials ? t('settings.credentialsSet') : t('settings.noCredentials')"
                  rounded
                />
                <div class="ms-auto flex items-center gap-2">
                  <ToggleSwitch v-model="channel.isEnabled" :disabled="!canManage" />
                  <span class="text-sm">{{ channel.isEnabled ? t('settings.enabled') : t('settings.disabled') }}</span>
                </div>
              </div>

              <div v-if="canManage" class="mt-3 grid gap-2 sm:grid-cols-4">
                <InputText
                  v-model="channel.endpoint"
                  :placeholder="t('settings.endpoint')"
                  dir="ltr"
                  class="w-full sm:col-span-2"
                />
                <Password
                  v-model="draftFor(channel.channel).apiKey"
                  :placeholder="t('settings.apiKey')"
                  :feedback="false"
                  toggle-mask
                  class="w-full"
                  input-class="w-full"
                  :input-props="{ dir: 'ltr', autocomplete: 'off' }"
                />
                <Button
                  :label="t('app.save')"
                  size="small"
                  outlined
                  @click="
                    saveChannel(
                      channel,
                      draftFor(channel.channel).apiKey || undefined,
                      draftFor(channel.channel).apiSecret || undefined,
                    )
                  "
                />
              </div>
              <p v-if="canManage" class="mt-2 text-xs text-surface-500 dark:text-surface-400">
                {{ t('settings.secretHint') }}
              </p>
            </div>
          </div>
        </TabPanel>
      </TabPanels>
    </Tabs>
  </div>
</template>
