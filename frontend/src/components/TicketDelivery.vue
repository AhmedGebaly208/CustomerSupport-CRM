<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import Tag from 'primevue/tag'
import { channelsApi } from '@/api/services'
import { useFormat } from '@/composables/useFormat'
import { ChannelDirection, ChannelMessageStatus, type ChannelMessage } from '@/types/api'

const props = defineProps<{ ticketId: string }>()

const { t } = useI18n()
const { formatDateTime } = useFormat()

const messages = ref<ChannelMessage[]>([])

type Severity = 'success' | 'info' | 'warn' | 'danger' | 'secondary'

/** Colour tracks whether the customer has it, not how far along the machinery is. */
function severityOf(status: ChannelMessageStatus): Severity {
  switch (status) {
    case ChannelMessageStatus.Delivered:
    case ChannelMessageStatus.Sent:
      return 'success'
    case ChannelMessageStatus.Retrying:
      return 'warn'
    case ChannelMessageStatus.Failed:
      return 'danger'
    default:
      return 'secondary'
  }
}

onMounted(async () => {
  try {
    messages.value = await channelsApi.messages(props.ticketId)
  } catch {
    // Supplementary panel; the ticket reads fine without it.
    messages.value = []
  }
})
</script>

<template>
  <div>
    <h3 class="mb-3 font-medium">{{ t('channel.delivery') }}</h3>

    <p v-if="messages.length === 0" class="py-4 text-center text-sm text-surface-500 dark:text-surface-400">
      {{ t('channel.noMessages') }}
    </p>

    <ul v-else class="divide-y divide-surface-200 dark:divide-surface-700">
      <li v-for="message in messages" :key="message.id" class="py-2">
        <div class="flex flex-wrap items-center gap-2">
          <i
            :class="message.direction === ChannelDirection.Inbound ? 'pi pi-arrow-down text-blue-500' : 'pi pi-arrow-up text-primary'"
          />
          <span class="text-xs text-surface-500 dark:text-surface-400">
            {{ message.direction === ChannelDirection.Inbound ? t('channel.inbound') : t('channel.outbound') }}
          </span>

          <Tag
            :severity="severityOf(message.status)"
            rounded
            :value="t(`channel.status.${ChannelMessageStatus[message.status]}`)"
          />

          <span
            v-if="message.attemptCount > 1"
            class="ltr-nums text-xs text-surface-500 dark:text-surface-400"
          >
            {{ message.attemptCount }} {{ t('channel.attempts') }}
          </span>

          <span class="ms-auto text-xs text-surface-500 dark:text-surface-400">
            {{ formatDateTime(message.occurredAt) }}
          </span>
        </div>

        <p v-if="message.address" class="mt-1 truncate text-xs text-surface-500 dark:text-surface-400">
          {{ message.address }}
        </p>

        <p v-if="message.lastError" class="mt-1 text-xs text-red-500">{{ message.lastError }}</p>

        <p v-if="message.nextAttemptAt" class="mt-1 text-xs text-amber-600 dark:text-amber-400">
          {{ t('channel.retryAt') }} {{ formatDateTime(message.nextAttemptAt) }}
        </p>
      </li>
    </ul>
  </div>
</template>
