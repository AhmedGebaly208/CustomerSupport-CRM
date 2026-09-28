<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import InputText from 'primevue/inputtext'
import IconField from 'primevue/iconfield'
import InputIcon from 'primevue/inputicon'
import Button from 'primevue/button'
import Skeleton from 'primevue/skeleton'
import Message from 'primevue/message'
import Accordion from 'primevue/accordion'
import AccordionPanel from 'primevue/accordionpanel'
import AccordionHeader from 'primevue/accordionheader'
import AccordionContent from 'primevue/accordioncontent'
import { aiApi, portalApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import type { ArticleListItem, ChatAnswer } from '@/types/api'

const { t } = useI18n()
const toast = useToast()
const ui = useUiStore()

const search = ref('')
const articles = ref<ArticleListItem[]>([])
const loading = ref(true)

const question = ref('')
const answer = ref<ChatAnswer | null>(null)
const asking = ref(false)

let timer: ReturnType<typeof setTimeout> | undefined

async function load() {
  loading.value = true
  try {
    const result = await portalApi.articles(search.value.trim() || undefined)
    articles.value = result.results.items
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 6000 })
  } finally {
    loading.value = false
  }
}

watch(search, () => {
  if (timer) clearTimeout(timer)
  timer = setTimeout(load, 350)
})

async function ask() {
  if (!question.value.trim() || asking.value) return

  asking.value = true
  try {
    answer.value = await aiApi.ask(question.value.trim(), ui.locale)
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 6000 })
  } finally {
    asking.value = false
  }
}

const title = (article: ArticleListItem) => (ui.isArabic ? article.titleAr : article.titleEn)
const summary = (article: ArticleListItem) => (ui.isArabic ? article.summaryAr : article.summaryEn)

onMounted(load)
</script>

<template>
  <div>
    <h1 class="mb-1 text-2xl font-semibold">{{ t('portal.help') }}</h1>
    <p class="mb-6 text-sm text-surface-500 dark:text-surface-400">{{ t('portal.helpSubtitle') }}</p>

    <!-- Ask -->
    <div class="mb-6 rounded-lg border border-surface-200 bg-surface-0 p-4 dark:border-surface-700 dark:bg-surface-900">
      <label class="mb-2 block text-sm font-medium">{{ t('portal.askQuestion') }}</label>

      <div class="flex gap-2">
        <InputText
          v-model="question"
          class="w-full"
          :placeholder="t('portal.askPlaceholder')"
          @keyup.enter="ask"
        />
        <Button icon="pi pi-send" :loading="asking" :disabled="!question.trim()" @click="ask" />
      </div>

      <div v-if="answer" class="mt-3">
        <p class="whitespace-pre-line text-sm">{{ answer.answer }}</p>

        <p
          v-if="answer.sources.length"
          class="mt-2 text-xs text-surface-500 dark:text-surface-400"
        >
          {{ t('portal.source') }}:
          {{ ui.isArabic ? answer.sources[0].titleAr : answer.sources[0].titleEn }}
        </p>

        <Message v-if="answer.shouldEscalate" severity="warn" :closable="false" class="mt-3 text-sm">
          {{ t('portal.askEscalate') }}
        </Message>
      </div>
    </div>

    <!-- Browse -->
    <IconField class="mb-4 w-full">
      <InputIcon class="pi pi-search" />
      <InputText v-model="search" :placeholder="t('portal.searchHelp')" class="w-full" />
    </IconField>

    <div v-if="loading" class="grid gap-2">
      <Skeleton v-for="n in 4" :key="n" height="3rem" />
    </div>

    <p v-else-if="articles.length === 0" class="py-12 text-center text-surface-500 dark:text-surface-400">
      {{ t('portal.noArticles') }}
    </p>

    <Accordion v-else :value="[]" multiple>
      <AccordionPanel v-for="article in articles" :key="article.id" :value="article.id">
        <AccordionHeader>{{ title(article) }}</AccordionHeader>
        <AccordionContent>
          <p v-if="summary(article)" class="text-sm text-surface-600 dark:text-surface-300">
            {{ summary(article) }}
          </p>
          <p v-else-if="article.excerpt" class="text-sm text-surface-600 dark:text-surface-300">
            {{ article.excerpt }}
          </p>
        </AccordionContent>
      </AccordionPanel>
    </Accordion>
  </div>
</template>
