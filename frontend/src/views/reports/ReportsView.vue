<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import DatePicker from 'primevue/datepicker'
import Select from 'primevue/select'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import Skeleton from 'primevue/skeleton'
import Message from 'primevue/message'
import PageHeader from '@/components/PageHeader.vue'
import ReportChart from '@/components/ReportChart.vue'
import { reportsApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { useAuthStore } from '@/stores/auth'
import { useFormat } from '@/composables/useFormat'
import {
  ExportFormat,
  PERMISSIONS,
  ReportGranularity,
  ReportKind,
  type AgentReport,
  type CsatReport,
  type DimensionBucket,
  type ReportQuery,
  type SlaReport,
  type TicketReport,
} from '@/types/api'

const { t } = useI18n()
const toast = useToast()
const ui = useUiStore()
const auth = useAuthStore()
const { formatDate } = useFormat()

const tab = ref('tickets')
const loading = ref(false)

const from = ref(startOfMonth())
const to = ref(new Date())
const granularity = ref<ReportGranularity>(ReportGranularity.Day)

const tickets = ref<TicketReport | null>(null)
const sla = ref<SlaReport | null>(null)
const agents = ref<AgentReport | null>(null)
const csat = ref<CsatReport | null>(null)

const canExport = computed(() => auth.hasPermission(PERMISSIONS.reportsExport))

function startOfMonth() {
  const now = new Date()
  return new Date(now.getFullYear(), now.getMonth(), 1)
}

const granularityOptions = computed(() => [
  { value: ReportGranularity.Day, label: t('report.day') },
  { value: ReportGranularity.Week, label: t('report.week') },
  { value: ReportGranularity.Month, label: t('report.month') },
])

const query = computed<ReportQuery>(() => ({
  from: from.value.toISOString(),
  to: to.value.toISOString(),
  granularity: granularity.value,
}))

/** A null ratio means there was nothing to measure. Rendering it as 0% would read as total
 *  failure, so it becomes a dash. */
const pct = (value: number | null) => (value === null ? '—' : `${value}%`)
const num = (value: number | null) => (value === null ? '—' : String(value))

const label = (bucket: DimensionBucket) => {
  if (bucket.key === 'none') return t('report.none')
  return ui.isArabic ? bucket.labelAr : bucket.labelEn
}

const changePercent = computed(() => {
  const report = tickets.value
  if (!report || report.previousTotal === 0) return null

  return Math.round(((report.total - report.previousTotal) / report.previousTotal) * 100)
})

async function load() {
  loading.value = true
  try {
    if (tab.value === 'tickets') tickets.value = await reportsApi.tickets(query.value)
    else if (tab.value === 'sla') sla.value = await reportsApi.sla(query.value)
    else if (tab.value === 'agents') agents.value = await reportsApi.agents(query.value)
    else if (tab.value === 'csat') csat.value = await reportsApi.csat(query.value)
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 6000 })
  } finally {
    loading.value = false
  }
}

const kindOfTab: Record<string, ReportKind> = {
  tickets: ReportKind.Tickets,
  sla: ReportKind.Sla,
  agents: ReportKind.Agents,
  csat: ReportKind.Csat,
}

async function download(format: ExportFormat) {
  try {
    const blob = await reportsApi.exportReport(
      kindOfTab[tab.value],
      format,
      query.value,
      ui.locale,
    )

    const url = URL.createObjectURL(blob)
    const link = document.createElement('a')
    link.href = url
    link.download = `${tab.value}.${format === ExportFormat.Csv ? 'csv' : 'xlsx'}`
    link.click()
    URL.revokeObjectURL(url)
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 6000 })
  }
}

watch([tab, from, to, granularity], load)
onMounted(load)
</script>

<template>
  <div>
    <PageHeader :title="t('report.title')" :subtitle="t('report.subtitle')">
      <template #actions>
        <Button
          v-if="canExport"
          :label="t('report.exportXlsx')"
          icon="pi pi-file-excel"
          outlined
          size="small"
          @click="download(ExportFormat.Xlsx)"
        />
        <Button
          v-if="canExport"
          :label="t('report.exportCsv')"
          icon="pi pi-file"
          outlined
          size="small"
          @click="download(ExportFormat.Csv)"
        />
      </template>
    </PageHeader>

    <div class="mb-4 flex flex-wrap items-end gap-3">
      <div>
        <label class="mb-1 block text-sm font-medium">{{ t('report.from') }}</label>
        <DatePicker v-model="from" date-format="yy-mm-dd" class="w-44" />
      </div>
      <div>
        <label class="mb-1 block text-sm font-medium">{{ t('report.to') }}</label>
        <DatePicker v-model="to" date-format="yy-mm-dd" class="w-44" />
      </div>
      <div>
        <label class="mb-1 block text-sm font-medium">{{ t('report.granularity') }}</label>
        <Select
          v-model="granularity"
          :options="granularityOptions"
          option-label="label"
          option-value="value"
          class="w-36"
        />
      </div>
    </div>

    <Tabs v-model:value="tab">
      <TabList>
        <Tab value="tickets">{{ t('report.tickets') }}</Tab>
        <Tab value="sla">{{ t('report.sla') }}</Tab>
        <Tab value="agents">{{ t('report.agents') }}</Tab>
        <Tab value="csat">{{ t('report.csat') }}</Tab>
      </TabList>

      <TabPanels>
        <!-- Tickets -->
        <TabPanel value="tickets">
          <Skeleton v-if="loading" height="18rem" />

          <template v-else-if="tickets">
            <div class="mb-4 grid gap-3 sm:grid-cols-3">
              <div class="rounded-lg border border-surface-200 p-4 dark:border-surface-700">
                <div class="text-sm text-surface-500 dark:text-surface-400">{{ t('report.total') }}</div>
                <div class="ltr-nums text-2xl font-semibold">{{ tickets.total }}</div>
              </div>
              <div class="rounded-lg border border-surface-200 p-4 dark:border-surface-700">
                <div class="text-sm text-surface-500 dark:text-surface-400">{{ t('report.previousPeriod') }}</div>
                <div class="ltr-nums text-2xl font-semibold">{{ tickets.previousTotal }}</div>
              </div>
              <div class="rounded-lg border border-surface-200 p-4 dark:border-surface-700">
                <div class="text-sm text-surface-500 dark:text-surface-400">{{ t('report.change') }}</div>
                <div
                  class="ltr-nums text-2xl font-semibold"
                  :class="changePercent === null ? '' : changePercent > 0 ? 'text-amber-500' : 'text-emerald-500'"
                >
                  {{ changePercent === null ? '—' : `${changePercent > 0 ? '+' : ''}${changePercent}%` }}
                </div>
              </div>
            </div>

            <ReportChart
              v-if="tickets.trend.length"
              type="line"
              :labels="tickets.trend.map((b) => formatDate(b.start))"
              :series="[
                { label: t('report.total'), data: tickets.trend.map((b) => b.count) },
                { label: t('report.previousPeriod'), data: tickets.previousTrend.map((b) => b.count) },
              ]"
              class="mb-4"
            />

            <Message v-else severity="secondary" :closable="false">{{ t('report.noData') }}</Message>

            <div class="grid gap-4 lg:grid-cols-2">
              <ReportChart
                v-if="tickets.byStatus.length"
                type="doughnut"
                :title="t('report.byStatus')"
                :labels="tickets.byStatus.map(label)"
                :series="[{ label: t('report.tickets'), data: tickets.byStatus.map((b) => b.count) }]"
              />
              <ReportChart
                v-if="tickets.byPriority.length"
                type="bar"
                :title="t('report.byPriority')"
                :labels="tickets.byPriority.map(label)"
                :series="[{ label: t('report.tickets'), data: tickets.byPriority.map((b) => b.count) }]"
              />
              <ReportChart
                v-if="tickets.byChannel.length"
                type="bar"
                :title="t('report.byChannel')"
                :labels="tickets.byChannel.map(label)"
                :series="[{ label: t('report.tickets'), data: tickets.byChannel.map((b) => b.count) }]"
              />
              <ReportChart
                v-if="tickets.byDepartment.length"
                type="bar"
                :title="t('report.byDepartment')"
                :labels="tickets.byDepartment.map(label)"
                :series="[{ label: t('report.tickets'), data: tickets.byDepartment.map((b) => b.count) }]"
              />
            </div>
          </template>
        </TabPanel>

        <!-- SLA -->
        <TabPanel value="sla">
          <Skeleton v-if="loading" height="18rem" />

          <template v-else-if="sla">
            <div class="mb-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <div
                v-for="tile in [
                  { key: 'measured', value: String(sla.measured) },
                  { key: 'firstResponseAttainment', value: pct(sla.firstResponseAttainment) },
                  { key: 'resolutionAttainment', value: pct(sla.resolutionAttainment) },
                  { key: 'breaches', value: String(sla.firstResponseBreached + sla.resolutionBreached) },
                ]"
                :key="tile.key"
                class="rounded-lg border border-surface-200 p-4 dark:border-surface-700"
              >
                <div class="text-sm text-surface-500 dark:text-surface-400">{{ t(`report.${tile.key}`) }}</div>
                <div class="ltr-nums text-2xl font-semibold">{{ tile.value }}</div>
              </div>
            </div>

            <div class="mb-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <div
                v-for="tile in [
                  { key: 'avgFirstResponse', value: num(sla.averageFirstResponseMinutes) },
                  { key: 'medianFirstResponse', value: num(sla.medianFirstResponseMinutes) },
                  { key: 'avgResolution', value: num(sla.averageResolutionMinutes) },
                  { key: 'medianResolution', value: num(sla.medianResolutionMinutes) },
                ]"
                :key="tile.key"
                class="rounded-lg border border-surface-200 p-4 dark:border-surface-700"
              >
                <div class="text-sm text-surface-500 dark:text-surface-400">{{ t(`report.${tile.key}`) }}</div>
                <div class="ltr-nums text-xl font-semibold">
                  {{ tile.value }}
                  <span v-if="tile.value !== '—'" class="text-sm font-normal">{{ t('report.minutes') }}</span>
                </div>
              </div>
            </div>

            <ReportChart
              v-if="sla.breachTrend.length"
              type="bar"
              :title="t('report.breaches')"
              :labels="sla.breachTrend.map((b) => formatDate(b.start))"
              :series="[{ label: t('report.breaches'), data: sla.breachTrend.map((b) => b.count) }]"
            />
            <Message v-else severity="secondary" :closable="false">{{ t('report.noData') }}</Message>
          </template>
        </TabPanel>

        <!-- Agents -->
        <TabPanel value="agents">
          <Message severity="info" :closable="false" class="mb-3 text-sm">
            {{ t('report.creditNote') }}
          </Message>

          <DataTable :value="agents?.rows ?? []" :loading="loading" size="small" striped-rows>
            <template #empty>
              <div class="p-6 text-center text-surface-500 dark:text-surface-400">{{ t('report.noData') }}</div>
            </template>

            <Column :header="t('report.agent')">
              <template #body="{ data }">{{ ui.isArabic ? data.nameAr : data.nameEn }}</template>
            </Column>
            <Column :header="t('report.handled')">
              <template #body="{ data }"><span class="ltr-nums">{{ data.handled }}</span></template>
            </Column>
            <Column :header="t('report.resolved')">
              <template #body="{ data }"><span class="ltr-nums">{{ data.resolved }}</span></template>
            </Column>
            <Column :header="t('report.reopenRate')">
              <template #body="{ data }"><span class="ltr-nums">{{ pct(data.reopenRate) }}</span></template>
            </Column>
            <Column :header="t('report.avgFirstResponse')">
              <template #body="{ data }"><span class="ltr-nums">{{ num(data.averageFirstResponseMinutes) }}</span></template>
            </Column>
            <Column :header="t('report.avgResolution')">
              <template #body="{ data }"><span class="ltr-nums">{{ num(data.averageResolutionMinutes) }}</span></template>
            </Column>
            <Column :header="t('report.satisfaction')">
              <template #body="{ data }"><span class="ltr-nums">{{ num(data.averageSatisfaction) }}</span></template>
            </Column>
            <Column :header="t('report.currentLoad')">
              <template #body="{ data }"><span class="ltr-nums">{{ data.currentLoad }}</span></template>
            </Column>
          </DataTable>
        </TabPanel>

        <!-- CSAT -->
        <TabPanel value="csat">
          <Skeleton v-if="loading" height="18rem" />

          <template v-else-if="csat">
            <div class="mb-4 grid gap-3 sm:grid-cols-4">
              <div
                v-for="tile in [
                  { key: 'averageScore', value: num(csat.averageScore) },
                  { key: 'responses', value: String(csat.responses) },
                  { key: 'eligible', value: String(csat.eligibleTickets) },
                  { key: 'responseRate', value: pct(csat.responseRate) },
                ]"
                :key="tile.key"
                class="rounded-lg border border-surface-200 p-4 dark:border-surface-700"
              >
                <div class="text-sm text-surface-500 dark:text-surface-400">{{ t(`report.${tile.key}`) }}</div>
                <div class="ltr-nums text-2xl font-semibold">{{ tile.value }}</div>
              </div>
            </div>

            <ReportChart
              v-if="csat.responses > 0"
              type="bar"
              :title="t('report.distribution')"
              :labels="csat.distribution.map((d) => d.key)"
              :series="[{ label: t('report.responses'), data: csat.distribution.map((d) => d.count) }]"
            />
            <Message v-else severity="secondary" :closable="false">{{ t('report.noData') }}</Message>
          </template>
        </TabPanel>
      </TabPanels>
    </Tabs>
  </div>
</template>
