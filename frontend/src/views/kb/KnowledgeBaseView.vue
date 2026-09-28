<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import IconField from 'primevue/iconfield'
import InputIcon from 'primevue/inputicon'
import Tag from 'primevue/tag'
import Paginator from 'primevue/paginator'
import Skeleton from 'primevue/skeleton'
import Select from 'primevue/select'
import PageHeader from '@/components/PageHeader.vue'
import { kbApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { useAuthStore } from '@/stores/auth'
import { useFormat } from '@/composables/useFormat'
import {
  ArticleStatus,
  PERMISSIONS,
  type ArticleCategory,
  type ArticleListItem,
  type ArticleTag,
} from '@/types/api'

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const ui = useUiStore()
const auth = useAuthStore()
const { formatDate } = useFormat()

const items = ref<ArticleListItem[]>([])
const availableTags = ref<ArticleTag[]>([])
const categories = ref<ArticleCategory[]>([])
const total = ref(0)
const loading = ref(true)

const search = ref('')
const categoryId = ref<string | null>(null)
const tag = ref<string | null>(null)
const status = ref<ArticleStatus | null>(null)
const faqOnly = ref(false)
const page = ref(1)
const pageSize = ref(20)

const canManage = computed(() => auth.hasPermission(PERMISSIONS.kbManage))

const statusOptions = computed(() =>
  [ArticleStatus.Draft, ArticleStatus.Published, ArticleStatus.Archived].map((value) => ({
    value,
    label: t(`kb.status.${ArticleStatus[value]}`),
  })),
)

/** The tree is flattened for the picker; indentation keeps the shape readable. */
const categoryOptions = computed(() => {
  const out: { value: string; label: string }[] = []

  const walk = (nodes: ArticleCategory[], depth: number) => {
    for (const node of nodes) {
      out.push({ value: node.id, label: `${'— '.repeat(depth)}${ui.localized(node)}` })
      walk(node.children, depth + 1)
    }
  }

  walk(categories.value, 0)
  return out
})

let timer: ReturnType<typeof setTimeout> | undefined

async function load() {
  loading.value = true
  try {
    const result = await kbApi.search({
      search: search.value.trim() || undefined,
      categoryId: categoryId.value,
      tag: tag.value,
      status: status.value,
      isFaq: faqOnly.value ? true : undefined,
      page: page.value,
      pageSize: pageSize.value,
    })

    items.value = result.results.items
    total.value = result.results.totalCount
    availableTags.value = result.availableTags
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 5000 })
  } finally {
    loading.value = false
  }
}

watch([categoryId, tag, status, faqOnly], () => {
  page.value = 1
  void load()
})

watch(search, () => {
  // Debounced: typing a query should not fire a request per keystroke.
  if (timer) clearTimeout(timer)
  timer = setTimeout(() => {
    page.value = 1
    void load()
  }, 350)
})

function open(article: ArticleListItem) {
  void router.push({ name: 'kb-article', params: { id: article.id } })
}

function clearFilters() {
  search.value = ''
  categoryId.value = null
  tag.value = null
  status.value = null
  faqOnly.value = false
}

const hasFilters = computed(
  () => !!search.value || !!categoryId.value || !!tag.value || status.value !== null || faqOnly.value,
)

onMounted(async () => {
  await Promise.all([load(), kbApi.categories(false).then((c) => (categories.value = c))])
})
</script>

<template>
  <div>
    <PageHeader :title="t('kb.title')" :subtitle="t('kb.subtitle')">
      <template #actions>
        <Button
          v-if="canManage"
          :label="t('kb.newArticle')"
          icon="pi pi-plus"
          @click="router.push({ name: 'kb-article-new' })"
        />
      </template>
    </PageHeader>

    <div class="mb-4 flex flex-wrap items-center gap-3">
      <IconField class="grow md:max-w-md">
        <InputIcon class="pi pi-search" />
        <InputText v-model="search" :placeholder="t('kb.searchPlaceholder')" class="w-full" />
      </IconField>

      <Select
        v-model="categoryId"
        :options="categoryOptions"
        option-label="label"
        option-value="value"
        show-clear
        class="w-56"
        :placeholder="t('kb.allCategories')"
      />

      <Select
        v-if="canManage"
        v-model="status"
        :options="statusOptions"
        option-label="label"
        option-value="value"
        show-clear
        class="w-44"
        :placeholder="t('kb.allStatuses')"
      />

      <Button
        :label="t('kb.faqOnly')"
        :severity="faqOnly ? undefined : 'secondary'"
        :outlined="!faqOnly"
        size="small"
        icon="pi pi-question-circle"
        @click="faqOnly = !faqOnly"
      />

      <Button v-if="hasFilters" :label="t('app.clear')" text size="small" @click="clearFilters" />
    </div>

    <div v-if="availableTags.length > 0" class="mb-4 flex flex-wrap items-center gap-2">
      <span class="text-sm text-surface-500 dark:text-surface-400">{{ t('kb.tags') }}</span>
      <button
        v-for="item in availableTags"
        :key="item.slug"
        type="button"
        @click="tag = tag === item.slug ? null : item.slug"
      >
        <Tag
          :severity="tag === item.slug ? undefined : 'secondary'"
          rounded
          :value="ui.isArabic ? item.labelAr : item.labelEn"
        />
      </button>
    </div>

    <div v-if="loading" class="grid gap-3">
      <Skeleton v-for="n in 5" :key="n" height="5rem" />
    </div>

    <p v-else-if="items.length === 0" class="py-16 text-center text-surface-500 dark:text-surface-400">
      {{ hasFilters ? t('kb.noResults') : t('kb.empty') }}
    </p>

    <ul v-else class="grid gap-3">
      <li
        v-for="article in items"
        :key="article.id"
        class="cursor-pointer rounded-lg border border-surface-200 bg-surface-0 p-4 transition hover:border-primary dark:border-surface-700 dark:bg-surface-900"
        @click="open(article)"
      >
        <div class="mb-1 flex flex-wrap items-center gap-2">
          <h3 class="font-medium">{{ ui.isArabic ? article.titleAr : article.titleEn }}</h3>

          <Tag v-if="article.isFaq" severity="info" rounded :value="t('kb.faq')" />
          <Tag
            v-if="article.status !== ArticleStatus.Published"
            :severity="article.status === ArticleStatus.Draft ? 'warn' : 'secondary'"
            rounded
            :value="t(`kb.status.${ArticleStatus[article.status]}`)"
          />
          <Tag v-if="!article.isPublic" severity="secondary" rounded icon="pi pi-lock" :value="t('kb.internal')" />
        </div>

        <p
          v-if="article.excerpt"
          class="mb-2 line-clamp-2 text-sm text-surface-600 dark:text-surface-300"
        >
          {{ article.excerpt }}
        </p>
        <p
          v-else-if="ui.isArabic ? article.summaryAr : article.summaryEn"
          class="mb-2 line-clamp-2 text-sm text-surface-600 dark:text-surface-300"
        >
          {{ ui.isArabic ? article.summaryAr : article.summaryEn }}
        </p>

        <div class="flex flex-wrap items-center gap-3 text-xs text-surface-500 dark:text-surface-400">
          <span>{{ ui.isArabic ? article.categoryNameAr : article.categoryNameEn }}</span>
          <span v-if="article.publishedAt">{{ formatDate(article.publishedAt) }}</span>
          <span class="ltr-nums"><i class="pi pi-eye" /> {{ article.viewCount }}</span>
          <span class="ltr-nums text-emerald-600 dark:text-emerald-400">
            <i class="pi pi-thumbs-up" /> {{ article.helpfulCount }}
          </span>
        </div>
      </li>
    </ul>

    <Paginator
      v-if="total > pageSize"
      :rows="pageSize"
      :total-records="total"
      :first="(page - 1) * pageSize"
      class="mt-4"
      @page="(e: { page: number }) => { page = e.page + 1; load() }"
    />
  </div>
</template>
