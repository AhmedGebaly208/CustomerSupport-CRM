<script setup lang="ts">
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import IconField from 'primevue/iconfield'
import InputIcon from 'primevue/inputicon'
import Dialog from 'primevue/dialog'
import { kbApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { useAuthStore } from '@/stores/auth'
import { ArticleStatus, PERMISSIONS, type ArticleListItem, type ArticleTicketLink } from '@/types/api'

const props = defineProps<{ ticketId: string }>()

/** Emitted when the agent chooses to paste an article link into their reply. */
const emit = defineEmits<{ insert: [text: string] }>()

const { t } = useI18n()
const toast = useToast()
const ui = useUiStore()
const auth = useAuthStore()

const links = ref<ArticleTicketLink[]>([])
const searchOpen = ref(false)
const search = ref('')
const results = ref<ArticleListItem[]>([])
const searching = ref(false)

const canLink = computed(() => auth.hasPermission(PERMISSIONS.ticketsComment))

let timer: ReturnType<typeof setTimeout> | undefined

async function load() {
  try {
    links.value = await kbApi.ticketArticles(props.ticketId)
  } catch {
    // The panel is supplementary; a failure here should not disturb the ticket.
    links.value = []
  }
}

async function runSearch() {
  if (!search.value.trim()) {
    results.value = []
    return
  }

  searching.value = true
  try {
    // Only published articles: an agent should not cite a draft to a customer.
    const result = await kbApi.search({
      search: search.value.trim(),
      status: ArticleStatus.Published,
      pageSize: 8,
    })

    results.value = result.results.items
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 5000 })
  } finally {
    searching.value = false
  }
}

watch(search, () => {
  if (timer) clearTimeout(timer)
  timer = setTimeout(runSearch, 350)
})

async function link(article: ArticleListItem) {
  try {
    await kbApi.linkToTicket(props.ticketId, article.id)
    searchOpen.value = false
    search.value = ''
    results.value = []
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
  }
}

async function unlink(item: ArticleTicketLink) {
  try {
    await kbApi.unlinkFromTicket(props.ticketId, item.id)
    await load()
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
  }
}

function titleOf(item: { titleAr: string; titleEn: string }) {
  return ui.isArabic ? item.titleAr : item.titleEn
}

/** Hands the reply box a link the customer can follow, in their reading direction. */
function insertLink(item: ArticleTicketLink) {
  const slug = ui.isArabic ? item.slugAr : item.slugEn
  emit('insert', `${titleOf(item)}: ${window.location.origin}/kb/${encodeURIComponent(slug)}`)
}

onMounted(load)
</script>

<template>
  <div>
    <div class="mb-3 flex items-center justify-between gap-2">
      <h3 class="font-medium">{{ t('kb.linkedArticles') }}</h3>
      <Button
        v-if="canLink"
        icon="pi pi-search"
        :label="t('kb.linkToTicket')"
        text
        size="small"
        @click="searchOpen = true"
      />
    </div>

    <p v-if="links.length === 0" class="py-4 text-center text-sm text-surface-500 dark:text-surface-400">
      {{ t('kb.noLinkedArticles') }}
    </p>

    <ul v-else class="divide-y divide-surface-200 dark:divide-surface-700">
      <li v-for="item in links" :key="item.id" class="group flex items-center gap-2 py-2">
        <i class="pi pi-book shrink-0 text-surface-400" />

        <RouterLink
          :to="{ name: 'kb-article', params: { id: item.articleId } }"
          class="min-w-0 flex-1 truncate text-sm text-primary hover:underline"
        >
          {{ titleOf(item) }}
        </RouterLink>

        <div class="flex shrink-0 items-center gap-1 opacity-0 transition group-hover:opacity-100">
          <Button
            icon="pi pi-copy"
            text
            rounded
            size="small"
            :aria-label="t('kb.insertLink')"
            @click="insertLink(item)"
          />
          <Button
            v-if="canLink"
            icon="pi pi-times"
            severity="danger"
            text
            rounded
            size="small"
            @click="unlink(item)"
          />
        </div>
      </li>
    </ul>

    <Dialog
      v-model:visible="searchOpen"
      modal
      :header="t('kb.linkToTicket')"
      :style="{ width: '34rem' }"
      :breakpoints="{ '960px': '95vw' }"
    >
      <IconField class="mb-3 w-full">
        <InputIcon :class="searching ? 'pi pi-spinner pi-spin' : 'pi pi-search'" />
        <InputText v-model="search" :placeholder="t('kb.searchPlaceholder')" class="w-full" autofocus />
      </IconField>

      <p
        v-if="search.trim() && results.length === 0 && !searching"
        class="py-6 text-center text-sm text-surface-500 dark:text-surface-400"
      >
        {{ t('kb.noResults') }}
      </p>

      <ul class="max-h-80 divide-y divide-surface-200 overflow-y-auto dark:divide-surface-700">
        <li v-for="article in results" :key="article.id">
          <button
            type="button"
            class="w-full py-3 text-start hover:bg-surface-100 dark:hover:bg-surface-800"
            @click="link(article)"
          >
            <span class="block text-sm font-medium">{{ titleOf(article) }}</span>
            <span
              v-if="article.excerpt"
              class="mt-1 block line-clamp-2 text-xs text-surface-500 dark:text-surface-400"
            >
              {{ article.excerpt }}
            </span>
          </button>
        </li>
      </ul>
    </Dialog>
  </div>
</template>
