<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import Textarea from 'primevue/textarea'
import Skeleton from 'primevue/skeleton'
import Rating from 'primevue/rating'
import Message from 'primevue/message'
import StatusTag from '@/components/StatusTag.vue'
import { portalApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { useFormat } from '@/composables/useFormat'
import type { PortalAttachment, PortalMessage, PortalTicket } from '@/types/api'

const props = defineProps<{ id: string }>()

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const confirm = useConfirm()
const ui = useUiStore()
const { formatDateTime } = useFormat()

const ticket = ref<PortalTicket | null>(null)
const messages = ref<PortalMessage[]>([])
const attachments = ref<PortalAttachment[]>([])
const loading = ref(true)
const reply = ref('')
const sending = ref(false)
const rating = ref<number | null>(null)

async function load() {
  loading.value = true
  try {
    ticket.value = await portalApi.ticket(props.id)
    rating.value = ticket.value.satisfactionScore

    const [m, a] = await Promise.allSettled([
      portalApi.messages(props.id),
      portalApi.attachments(props.id),
    ])

    if (m.status === 'fulfilled') messages.value = m.value
    if (a.status === 'fulfilled') attachments.value = a.value
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 6000 })
  } finally {
    loading.value = false
  }
}

async function send() {
  if (!reply.value.trim() || sending.value) return

  sending.value = true
  try {
    await portalApi.reply(props.id, reply.value.trim())
    reply.value = ''
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 6000 })
  } finally {
    sending.value = false
  }
}

function confirmClose() {
  confirm.require({
    message: t('portal.closeConfirm'),
    header: t('app.confirm'),
    icon: 'pi pi-check-circle',
    acceptProps: { label: t('portal.close') },
    rejectProps: { label: t('app.cancel'), outlined: true },
    accept: async () => {
      try {
        ticket.value = await portalApi.close(props.id)
      } catch (e) {
        toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 6000 })
      }
    },
  })
}

async function rate(score: number) {
  try {
    await portalApi.rate(props.id, score)
    toast.add({ severity: 'success', summary: t('portal.thanksForFeedback'), life: 4000 })
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 6000 })
  }
}

onMounted(load)
</script>

<template>
  <div>
    <Button
      :label="t('portal.backToRequests')"
      icon="pi pi-arrow-left"
      text
      class="mb-3"
      @click="router.push({ name: 'portal-tickets' })"
    />

    <div v-if="loading" class="space-y-3">
      <Skeleton height="6rem" />
      <Skeleton height="14rem" />
    </div>

    <template v-else-if="ticket">
      <div class="mb-4 rounded-lg border border-surface-200 bg-surface-0 p-4 dark:border-surface-700 dark:bg-surface-900">
        <div class="mb-2 flex flex-wrap items-center gap-2">
          <span class="ltr-nums text-sm font-medium text-primary">{{ ticket.number }}</span>
          <StatusTag :status="ticket.status" />
          <span
            v-if="ticket.categoryNameEn"
            class="text-xs text-surface-500 dark:text-surface-400"
          >
            {{ ui.isArabic ? ticket.categoryNameAr : ticket.categoryNameEn }}
          </span>

          <Button
            v-if="ticket.status !== 5"
            :label="t('portal.close')"
            size="small"
            outlined
            severity="secondary"
            class="ms-auto"
            @click="confirmClose"
          />
        </div>

        <h1 class="mb-1 text-xl font-semibold">{{ ticket.subject }}</h1>
        <p class="text-xs text-surface-500 dark:text-surface-400">
          {{ formatDateTime(ticket.createdAt) }}
        </p>
      </div>

      <!-- Conversation -->
      <div class="mb-4 space-y-3">
        <div class="rounded-lg border border-surface-200 bg-surface-0 p-4 dark:border-surface-700 dark:bg-surface-900">
          <p class="mb-2 text-xs font-medium text-surface-500 dark:text-surface-400">
            {{ t('portal.you') }}
          </p>
          <p class="whitespace-pre-line text-sm">{{ ticket.description }}</p>
        </div>

        <div
          v-for="message in messages"
          :key="message.id"
          class="rounded-lg border p-4"
          :class="message.fromSupport
            ? 'border-primary-200 bg-primary-50 dark:border-primary-800 dark:bg-primary-950'
            : 'border-surface-200 bg-surface-0 dark:border-surface-700 dark:bg-surface-900'"
        >
          <div class="mb-2 flex items-center justify-between gap-2">
            <span class="text-xs font-medium text-surface-500 dark:text-surface-400">
              {{ message.fromSupport ? t('portal.support') : t('portal.you') }}
            </span>
            <span class="text-xs text-surface-500 dark:text-surface-400">
              {{ formatDateTime(message.createdAt) }}
            </span>
          </div>

          <p class="whitespace-pre-line text-sm">{{ message.body }}</p>
        </div>
      </div>

      <div v-if="attachments.length" class="mb-4">
        <h2 class="mb-2 text-sm font-medium">{{ t('portal.attachments') }}</h2>
        <ul class="flex flex-wrap gap-2">
          <li
            v-for="file in attachments"
            :key="file.id"
            class="rounded border border-surface-200 px-3 py-1 text-xs dark:border-surface-700"
          >
            <i class="pi pi-paperclip me-1" />{{ file.fileName }}
          </li>
        </ul>
      </div>

      <!-- Reply -->
      <div class="mb-4 rounded-lg border border-surface-200 bg-surface-0 p-4 dark:border-surface-700 dark:bg-surface-900">
        <Message
          v-if="ticket.status === 4 || ticket.status === 5"
          severity="info"
          :closable="false"
          class="mb-3 text-sm"
        >
          {{ t('portal.replyReopens') }}
        </Message>

        <Textarea
          v-model="reply"
          rows="4"
          auto-resize
          class="w-full"
          :placeholder="t('portal.replyPlaceholder')"
        />

        <div class="mt-2 flex justify-end">
          <Button
            :label="t('portal.send')"
            icon="pi pi-send"
            :disabled="!reply.trim()"
            :loading="sending"
            @click="send"
          />
        </div>
      </div>

      <!-- Satisfaction -->
      <div
        v-if="ticket.canRate || ticket.satisfactionScore !== null"
        class="rounded-lg border border-surface-200 bg-surface-0 p-4 text-center dark:border-surface-700 dark:bg-surface-900"
      >
        <p class="mb-3 font-medium">
          {{ ticket.satisfactionScore !== null ? t('portal.yourRating') : t('portal.howDidWeDo') }}
        </p>

        <Rating
          v-model="rating"
          :stars="5"
          :readonly="ticket.satisfactionScore !== null"
          @update:model-value="(v) => typeof v === 'number' && rate(v)"
        />
      </div>
    </template>
  </div>
</template>
