<script setup lang="ts">
import StatusTag from '@/components/StatusTag.vue'
import { useUiStore } from '@/stores/ui'
import type { TicketListItem } from '@/types/api'

defineProps<{
  title: string
  icon: string
  tickets: TicketListItem[]
  emptyText: string
}>()

const ui = useUiStore()
</script>

<template>
  <div class="rounded-lg border border-surface-200 bg-surface-0 p-4 dark:border-surface-700 dark:bg-surface-900">
    <div class="mb-3 flex items-center gap-2">
      <i :class="icon" class="text-surface-400" />
      <h2 class="font-semibold">{{ title }}</h2>
      <span v-if="tickets.length > 0" class="ltr-nums text-sm text-surface-400">{{ tickets.length }}</span>
    </div>

    <p v-if="tickets.length === 0" class="py-6 text-center text-sm text-surface-500 dark:text-surface-400">
      {{ emptyText }}
    </p>

    <ul v-else class="divide-y divide-surface-200 dark:divide-surface-700">
      <li v-for="ticket in tickets" :key="ticket.id">
        <RouterLink
          :to="{ name: 'ticket-detail', params: { id: ticket.id } }"
          class="flex items-start gap-3 py-2 hover:bg-surface-50 dark:hover:bg-surface-800"
        >
          <span class="ltr-nums shrink-0 text-sm font-medium text-primary">{{ ticket.number }}</span>

          <span class="min-w-0 flex-1">
            <span class="line-clamp-1 text-sm">{{ ticket.subject }}</span>
            <span class="block text-xs text-surface-500 dark:text-surface-400">
              {{ ui.isArabic ? ticket.customerNameAr : ticket.customerNameEn }}
            </span>
          </span>

          <StatusTag :status="ticket.status" class="shrink-0" />
        </RouterLink>
      </li>
    </ul>
  </div>
</template>
