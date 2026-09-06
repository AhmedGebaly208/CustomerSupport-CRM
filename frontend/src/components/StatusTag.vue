<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import Tag from 'primevue/tag'
import { TicketPriority, TicketStatus } from '@/types/api'

const props = defineProps<{
  status?: TicketStatus
  priority?: TicketPriority
}>()

const { t } = useI18n()

// PrimeVue's Tag severities, mapped so colour carries the same meaning everywhere:
// red = needs attention now, green = done, grey = parked.
const STATUS_SEVERITY: Record<TicketStatus, string> = {
  [TicketStatus.New]: 'info',
  [TicketStatus.Open]: 'info',
  [TicketStatus.Pending]: 'warn',
  [TicketStatus.OnHold]: 'secondary',
  [TicketStatus.Resolved]: 'success',
  [TicketStatus.Closed]: 'contrast',
  [TicketStatus.Reopened]: 'danger',
}

const PRIORITY_SEVERITY: Record<TicketPriority, string> = {
  [TicketPriority.Low]: 'secondary',
  [TicketPriority.Normal]: 'info',
  [TicketPriority.High]: 'warn',
  [TicketPriority.Urgent]: 'danger',
}

const isStatus = computed(() => props.status !== undefined)

const severity = computed(() =>
  isStatus.value ? STATUS_SEVERITY[props.status!] : PRIORITY_SEVERITY[props.priority!],
)

const label = computed(() =>
  isStatus.value
    ? t(`status.${TicketStatus[props.status!]}`)
    : t(`priority.${TicketPriority[props.priority!]}`),
)
</script>

<template>
  <Tag :severity="severity" :value="label" rounded />
</template>
