<script setup lang="ts">
import { computed } from 'vue'
import Chart from 'primevue/chart'
import { useUiStore } from '@/stores/ui'

const props = defineProps<{
  type: 'line' | 'bar' | 'doughnut'
  labels: string[]
  series: { label: string; data: number[] }[]
  title?: string
}>()

const ui = useUiStore()

/**
 * A fixed, ordered palette rather than random colours: the same series keeps the same colour
 * across every chart on the page, which is what lets two charts be read together. Chosen to
 * stay distinguishable in both themes and for the commonest colour-vision deficiencies.
 */
const palette = ['#0f766e', '#f59e0b', '#6366f1', '#ef4444', '#10b981', '#8b5cf6', '#ec4899']

const data = computed(() => ({
  labels: props.labels,
  datasets: props.series.map((series, index) => {
    const colour = palette[index % palette.length]

    // A doughnut colours each slice, not each series.
    if (props.type === 'doughnut') {
      return {
        label: series.label,
        data: series.data,
        backgroundColor: props.labels.map((_, i) => palette[i % palette.length]),
        borderWidth: 0,
      }
    }

    return {
      label: series.label,
      data: series.data,
      borderColor: colour,
      backgroundColor: props.type === 'line' ? `${colour}22` : colour,
      borderWidth: 2,
      fill: props.type === 'line',
      tension: 0.3,
    }
  }),
}))

const options = computed(() => {
  const text = ui.theme === 'dark' ? '#cbd5e1' : '#475569'
  const grid = ui.theme === 'dark' ? '#33415555' : '#e2e8f055'

  return {
    responsive: true,
    maintainAspectRatio: false,
    plugins: {
      legend: {
        // Legend on the reading side, so it does not sit where the eye starts.
        position: 'bottom' as const,
        labels: { color: text, usePointStyle: true, boxWidth: 8 },
        rtl: ui.isArabic,
      },
      tooltip: { rtl: ui.isArabic },
    },
    scales:
      props.type === 'doughnut'
        ? undefined
        : {
            x: { ticks: { color: text }, grid: { color: grid } },
            y: {
              // Counts are whole things; a y-axis offering 2.5 tickets is nonsense.
              ticks: { color: text, precision: 0 },
              grid: { color: grid },
              beginAtZero: true,
            },
          },
  }
})
</script>

<template>
  <div class="rounded-lg border border-surface-200 bg-surface-0 p-4 dark:border-surface-700 dark:bg-surface-900">
    <h3 v-if="title" class="mb-3 font-medium">{{ title }}</h3>

    <!-- Fixed height: Chart.js needs a sized container, and without one a responsive chart
         grows on every resize. -->
    <div class="h-64">
      <Chart :type="type" :data="data" :options="options" class="h-full" />
    </div>
  </div>
</template>
