<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import MultiSelect from 'primevue/multiselect'
import Tag from 'primevue/tag'
import Skeleton from 'primevue/skeleton'
import { customersApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { useFormat } from '@/composables/useFormat'
import { CustomerActivityType, type CustomerActivityItem } from '@/types/api'

const props = defineProps<{ customerId: string }>()

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const ui = useUiStore()
const { formatDateTime, formatRelative } = useFormat()

const items = ref<CustomerActivityItem[]>([])
const loading = ref(true)
const page = ref(1)
const pageSize = 25
const hasMore = ref(false)
const types = ref<CustomerActivityType[]>([])

const typeOptions = Object.entries(CustomerActivityType)
  .filter(([, v]) => typeof v === 'number')
  .map(([name, value]) => ({
    label: t(`activity.type.${name}`),
    value: value as CustomerActivityType,
  }))

const ICON: Record<CustomerActivityType, string> = {
  [CustomerActivityType.Ticket]: 'pi pi-ticket',
  [CustomerActivityType.Interaction]: 'pi pi-comments',
  [CustomerActivityType.Note]: 'pi pi-pencil',
  [CustomerActivityType.Attachment]: 'pi pi-paperclip',
}

const TONE: Record<CustomerActivityType, string> = {
  [CustomerActivityType.Ticket]: 'text-primary',
  [CustomerActivityType.Interaction]: 'text-blue-500',
  [CustomerActivityType.Note]: 'text-amber-500',
  [CustomerActivityType.Attachment]: 'text-emerald-500',
}

async function load(append = false) {
  loading.value = true
  try {
    const result = await customersApi.activity(
      props.customerId,
      page.value,
      pageSize,
      types.value.length ? types.value : undefined,
    )

    items.value = append ? [...items.value, ...result.items] : result.items
    hasMore.value = result.hasNext
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 5000 })
  } finally {
    loading.value = false
  }
}

watch(types, () => {
  page.value = 1
  load()
})

function loadMore() {
  page.value += 1
  load(true)
}

/** A ticket entry links to its ticket; the other sources have no page of their own. */
function openIfTicket(item: CustomerActivityItem) {
  if (item.type === CustomerActivityType.Ticket) {
    router.push({ name: 'ticket-detail', params: { id: item.id } })
  }
}

function titleOf(item: CustomerActivityItem): string {
  const title = ui.isArabic ? item.titleAr : item.titleEn
  return title ?? item.snippet ?? t(`activity.type.${CustomerActivityType[item.type]}`)
}

onMounted(() => load())
</script>

<template>
  <div>
    <div class="mb-3 flex flex-wrap items-center gap-2">
      <MultiSelect
        v-model="types"
        :options="typeOptions"
        option-label="label"
        option-value="value"
        :placeholder="t('activity.allTypes')"
        :max-selected-labels="2"
        class="min-w-[14rem]"
      />
      <Button icon="pi pi-refresh" text size="small" :aria-label="t('app.search')" @click="page = 1; load()" />
    </div>

    <div v-if="loading && !items.length" class="flex flex-col gap-2">
      <Skeleton v-for="n in 4" :key="n" height="3rem" />
    </div>

    <div v-else-if="!items.length" class="p-6 text-center text-surface-500 dark:text-surface-400">
      {{ t('activity.empty') }}
    </div>

    <ol v-else class="flex flex-col gap-3">
      <li
        v-for="item in items"
        :key="`${item.type}-${item.id}`"
        class="flex gap-3 rounded-lg border border-surface-200 p-3 dark:border-surface-800"
        :class="item.type === CustomerActivityType.Ticket ? 'cursor-pointer hover:bg-surface-50 dark:hover:bg-surface-800' : ''"
        @click="openIfTicket(item)"
      >
        <i :class="[ICON[item.type], TONE[item.type]]" class="mt-1 shrink-0" />

        <div class="min-w-0 flex-1">
          <div class="flex flex-wrap items-center gap-2">
            <span class="font-medium">{{ titleOf(item) }}</span>
            <span v-if="item.refNumber" class="ltr-nums text-xs text-primary">{{ item.refNumber }}</span>
            <Tag
              v-if="item.status"
              severity="secondary"
              :value="item.status"
              rounded
              class="text-xs"
            />
          </div>

          <p
            v-if="item.snippet"
            class="mt-1 line-clamp-2 text-sm text-surface-600 dark:text-surface-300"
          >
            {{ item.snippet }}
          </p>

          <div class="mt-1 flex flex-wrap gap-3 text-xs text-surface-500 dark:text-surface-400">
            <span>{{ t(`activity.type.${CustomerActivityType[item.type]}`) }}</span>
            <span v-if="item.actorName">{{ item.actorName }}</span>
            <span class="ms-auto" :title="formatDateTime(item.occurredAt)">
              {{ formatRelative(item.occurredAt) }}
            </span>
          </div>
        </div>
      </li>
    </ol>

    <div v-if="hasMore" class="mt-3 text-center">
      <Button :label="t('activity.loadMore')" outlined size="small" :loading="loading" @click="loadMore" />
    </div>
  </div>
</template>
