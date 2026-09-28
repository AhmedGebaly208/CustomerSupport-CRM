<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import Tag from 'primevue/tag'
import { SlaState, type SlaClock } from '@/types/api'
import { useFormat } from '@/composables/useFormat'

const props = defineProps<{
  clock: SlaClock
  /** Shows the remaining or overdue time next to the label. Off in dense tables. */
  showRemaining?: boolean
}>()

const { t } = useI18n()
const { formatDateTime } = useFormat()

type Severity = 'success' | 'info' | 'warn' | 'danger' | 'secondary'

/**
 * Colour carries the meaning here, so it is chosen from the state rather than from the
 * percentage — a paused clock at 95% is not the same problem as a running one.
 */
const severity = computed<Severity>(() => {
  switch (props.clock.state) {
    case SlaState.Met:
      return 'success'
    case SlaState.Breached:
      return 'danger'
    case SlaState.AtRisk:
      return 'warn'
    case SlaState.Paused:
      return 'secondary'
    case SlaState.Running:
      return 'info'
    default:
      return 'secondary'
  }
})

const icon = computed(() => {
  switch (props.clock.state) {
    case SlaState.Met:
      return 'pi pi-check-circle'
    case SlaState.Breached:
      return 'pi pi-exclamation-triangle'
    case SlaState.AtRisk:
      return 'pi pi-clock'
    case SlaState.Paused:
      return 'pi pi-pause'
    default:
      return 'pi pi-hourglass'
  }
})

const label = computed(() => t(`sla.state.${SlaState[props.clock.state]}`))

/**
 * Minutes are unreadable past an hour or two, and a target measured in working minutes
 * would be actively misleading shown as calendar days — "2d" against a Thursday evening
 * deadline means Sunday. Hours are the largest unit used for that reason.
 */
function humanise(minutes: number): string {
  const abs = Math.abs(minutes)
  if (abs < 60) return t('sla.minutes', { n: abs })

  const hours = Math.floor(abs / 60)
  const rest = abs % 60

  return rest === 0
    ? t('sla.hours', { n: hours })
    : t('sla.hoursMinutes', { h: hours, m: rest })
}

const remaining = computed(() => {
  const value = props.clock.remainingMinutes
  if (value === null || props.clock.state === SlaState.None) return null

  return value < 0
    ? t('sla.overdueBy', { time: humanise(value) })
    : t('sla.remaining', { time: humanise(value) })
})

const tooltip = computed(() => {
  if (props.clock.state === SlaState.None) return t('sla.noPolicy')

  const parts = [label.value]
  if (props.clock.dueAt) parts.push(t('sla.dueAt', { at: formatDateTime(props.clock.dueAt) }))
  if (props.clock.percentConsumed !== null) {
    parts.push(t('sla.consumed', { percent: props.clock.percentConsumed }))
  }

  return parts.join(' · ')
})
</script>

<template>
  <span v-if="clock.state === SlaState.None" class="text-surface-400 text-sm">—</span>

  <span v-else v-tooltip.top="tooltip" class="inline-flex items-center gap-2">
    <Tag :severity="severity" :icon="icon" :value="label" rounded />

    <span
      v-if="showRemaining && remaining"
      class="ltr-nums text-xs"
      :class="(clock.remainingMinutes ?? 0) < 0 ? 'text-red-600 dark:text-red-400' : 'text-surface-500 dark:text-surface-400'"
    >
      {{ remaining }}
    </span>
  </span>
</template>
