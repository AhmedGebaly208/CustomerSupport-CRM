<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import Button from 'primevue/button'
import Badge from 'primevue/badge'
import Popover from 'primevue/popover'
import { notificationsApi } from '@/api/services'
import { useUiStore } from '@/stores/ui'
import { useFormat } from '@/composables/useFormat'
import { NotificationKind, type AppNotification } from '@/types/api'

const { t } = useI18n()
const router = useRouter()
const ui = useUiStore()
const { formatRelative } = useFormat()

/** Polling interval. Long enough not to be chatty, short enough that an escalation raised
 *  by the background sweep surfaces while the agent is still at their desk. */
const PollMs = 60_000

const panel = ref()
const items = ref<AppNotification[]>([])
const unreadCount = ref(0)
const loading = ref(false)
let timer: ReturnType<typeof setInterval> | undefined

const hasUnread = computed(() => unreadCount.value > 0)

async function load() {
  loading.value = true
  try {
    const result = await notificationsApi.list(false, 20)
    items.value = result.items
    unreadCount.value = result.unreadCount
  } catch {
    // A failed poll is not worth a toast; the next one will retry and the bell simply
    // keeps its previous count.
  } finally {
    loading.value = false
  }
}

const iconFor = (kind: NotificationKind): string => {
  switch (kind) {
    case NotificationKind.SlaBreached:
      return 'pi pi-exclamation-triangle text-red-500'
    case NotificationKind.SlaAtRisk:
      return 'pi pi-clock text-amber-500'
    case NotificationKind.TicketEscalated:
      return 'pi pi-arrow-up text-orange-500'
    case NotificationKind.TicketAssigned:
      return 'pi pi-user-plus text-primary'
    case NotificationKind.TicketCommented:
      return 'pi pi-comment text-surface-500'
    default:
      return 'pi pi-bell text-surface-500'
  }
}

/**
 * The server stores what happened, not how to say it, so the sentence is assembled here
 * from the kind and its parameters. Values that are themselves bilingual arrive as an
 * Ar/En pair and the active locale picks one.
 */
function describe(notification: AppNotification): string {
  const params = notification.parameters ?? {}
  const suffix = ui.isArabic ? 'Ar' : 'En'

  const resolved: Record<string, string> = {}
  for (const [key, value] of Object.entries(params)) {
    if (key.endsWith('Ar') || key.endsWith('En')) {
      if (key.endsWith(suffix)) resolved[key.slice(0, -2)] = value
    } else {
      resolved[key] = value
    }
  }

  const key = `notification.${NotificationKind[notification.kind]}`
  return t(key, resolved)
}

async function open(notification: AppNotification) {
  if (!notification.readAt) {
    try {
      await notificationsApi.markRead(notification.id)
      notification.readAt = new Date().toISOString()
      unreadCount.value = Math.max(0, unreadCount.value - 1)
    } catch {
      // Navigating still matters more than the read flag.
    }
  }

  if (notification.ticketId) {
    panel.value?.hide()
    await router.push({ name: 'ticket-detail', params: { id: notification.ticketId } })
  }
}

async function markAllRead() {
  await notificationsApi.markAllRead()
  await load()
}

onMounted(() => {
  void load()
  timer = setInterval(() => void load(), PollMs)
})

onBeforeUnmount(() => {
  if (timer) clearInterval(timer)
})
</script>

<template>
  <div>
    <Button
      text
      rounded
      :aria-label="t('notification.title')"
      @click="panel?.toggle($event)"
    >
      <span class="relative inline-flex">
        <i class="pi pi-bell text-lg" />
        <Badge
          v-if="hasUnread"
          :value="unreadCount > 99 ? '99+' : unreadCount"
          severity="danger"
          class="absolute -top-2 -end-3 scale-75"
        />
      </span>
    </Button>

    <Popover ref="panel">
      <div class="w-80 max-w-[90vw]">
        <div class="mb-2 flex items-center justify-between gap-2">
          <span class="font-semibold">{{ t('notification.title') }}</span>
          <Button
            v-if="hasUnread"
            :label="t('notification.markAllRead')"
            text
            size="small"
            @click="markAllRead"
          />
        </div>

        <div v-if="items.length === 0" class="py-6 text-center text-sm text-surface-500 dark:text-surface-400">
          {{ t('notification.empty') }}
        </div>

        <ul v-else class="max-h-96 divide-y divide-surface-200 overflow-y-auto dark:divide-surface-700">
          <li v-for="item in items" :key="item.id">
            <button
              type="button"
              class="flex w-full items-start gap-3 py-3 text-start hover:bg-surface-100 dark:hover:bg-surface-800"
              :class="{ 'font-semibold': !item.readAt }"
              @click="open(item)"
            >
              <i :class="iconFor(item.kind)" class="mt-1" />

              <span class="min-w-0 flex-1">
                <span class="block text-sm">{{ describe(item) }}</span>
                <span class="block text-xs text-surface-500 dark:text-surface-400">
                  {{ formatRelative(item.createdAt) }}
                </span>
              </span>

              <span v-if="!item.readAt" class="mt-2 size-2 shrink-0 rounded-full bg-primary" />
            </button>
          </li>
        </ul>
      </div>
    </Popover>
  </div>
</template>
