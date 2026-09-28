<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import Message from 'primevue/message'
import { aiApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import {
  AiCallOutcome,
  AiFeature,
  AiSuggestionOutcome,
  type CategorySuggestion,
  type SuggestedArticle,
  type SuggestedSolutions,
  type TicketSummary,
} from '@/types/api'

const props = defineProps<{ ticketId: string }>()

const emit = defineEmits<{
  insertReply: [text: string]
  applyCategory: [categoryId: string]
}>()

const { t } = useI18n()
const toast = useToast()
const ui = useUiStore()

const busy = ref<AiFeature | null>(null)

const summary = ref<TicketSummary | null>(null)
const reply = ref<{ body: string; rationale: string | null; basedOn: SuggestedArticle[] } | null>(null)
const category = ref<CategorySuggestion | null>(null)
const solutions = ref<SuggestedSolutions | null>(null)

/** Assistance failing must never look like the ticket failing, so a refusal is shown in the
 *  panel rather than thrown as a page error. */
async function run<T>(feature: AiFeature, work: () => Promise<T>, assign: (value: T) => void) {
  if (busy.value !== null) return

  busy.value = feature
  try {
    assign(await work())
  } catch (e) {
    toast.add({ severity: 'warn', summary: problemMessage(e, t('ai.failed')), life: 6000 })
  } finally {
    busy.value = null
  }
}

const titleOf = (article: SuggestedArticle) => (ui.isArabic ? article.titleAr : article.titleEn)

function acceptReply() {
  if (!reply.value) return

  emit('insertReply', reply.value.body)
  void aiApi.feedback(AiFeature.SuggestedReply, AiSuggestionOutcome.Accepted, props.ticketId)
  reply.value = null
}

function rejectReply() {
  void aiApi.feedback(AiFeature.SuggestedReply, AiSuggestionOutcome.Rejected, props.ticketId)
  reply.value = null
}

function acceptCategory() {
  if (!category.value?.categoryId) return

  emit('applyCategory', category.value.categoryId)
  void aiApi.feedback(AiFeature.Categorisation, AiSuggestionOutcome.Accepted, props.ticketId)
  category.value = null
}
</script>

<template>
  <div class="rounded-lg border border-surface-200 p-4 dark:border-surface-700">
    <div class="mb-3 flex items-center gap-2">
      <i class="pi pi-sparkles text-primary" />
      <h3 class="font-medium">{{ t('ai.title') }}</h3>
    </div>

    <div class="mb-3 flex flex-wrap gap-2">
      <Button
        :label="t('ai.summarise')"
        icon="pi pi-align-left"
        size="small"
        outlined
        :loading="busy === AiFeature.Summary"
        @click="run(AiFeature.Summary, () => aiApi.summary(props.ticketId), (v) => (summary = v))"
      />
      <Button
        :label="t('ai.suggestReply')"
        icon="pi pi-comment"
        size="small"
        outlined
        :loading="busy === AiFeature.SuggestedReply"
        @click="run(AiFeature.SuggestedReply, () => aiApi.suggestReply(props.ticketId), (v) => (reply = v))"
      />
      <Button
        :label="t('ai.suggestSolutions')"
        icon="pi pi-book"
        size="small"
        outlined
        :loading="busy === AiFeature.SuggestedSolutions"
        @click="run(AiFeature.SuggestedSolutions, () => aiApi.suggestSolutions(props.ticketId), (v) => (solutions = v))"
      />
      <Button
        :label="t('ai.suggestCategory')"
        icon="pi pi-tag"
        size="small"
        outlined
        :loading="busy === AiFeature.Categorisation"
        @click="run(AiFeature.Categorisation, () => aiApi.suggestCategory(props.ticketId), (v) => (category = v))"
      />
    </div>

    <p class="mb-3 text-xs text-surface-500 dark:text-surface-400">{{ t('ai.suggestionNote') }}</p>

    <!-- Summary -->
    <div v-if="summary" class="mb-3">
      <Message v-if="summary.outcome !== AiCallOutcome.Success" severity="secondary" :closable="false" class="text-sm">
        {{ t('ai.noAnswer') }}<span v-if="summary.rationale"> — {{ summary.rationale }}</span>
      </Message>

      <div v-else class="rounded border border-surface-200 p-3 text-sm dark:border-surface-700">
        <p class="whitespace-pre-line">{{ summary.summary }}</p>
        <p v-if="summary.rationale" class="mt-2 text-xs text-surface-500 dark:text-surface-400">
          {{ t('ai.why') }}: {{ summary.rationale }}
        </p>
      </div>
    </div>

    <!-- Suggested reply -->
    <div v-if="reply" class="mb-3">
      <Message v-if="!reply.body" severity="secondary" :closable="false" class="text-sm">
        {{ t('ai.noAnswer') }}<span v-if="reply.rationale"> — {{ reply.rationale }}</span>
      </Message>

      <div v-else class="rounded border border-surface-200 p-3 dark:border-surface-700">
        <p class="whitespace-pre-line text-sm">{{ reply.body }}</p>

        <p v-if="reply.basedOn.length" class="mt-2 text-xs text-surface-500 dark:text-surface-400">
          {{ t('ai.basedOn') }}:
          <RouterLink
            v-for="article in reply.basedOn"
            :key="article.id"
            :to="{ name: 'kb-article', params: { id: article.id } }"
            class="text-primary hover:underline"
          >
            {{ titleOf(article) }}
          </RouterLink>
        </p>

        <div class="mt-3 flex gap-2">
          <Button :label="t('ai.insert')" icon="pi pi-check" size="small" @click="acceptReply" />
          <Button :label="t('ai.reject')" size="small" text severity="secondary" @click="rejectReply" />
        </div>
      </div>
    </div>

    <!-- Suggested solutions -->
    <div v-if="solutions" class="mb-3">
      <Message
        v-if="solutions.articles.length === 0"
        severity="secondary"
        :closable="false"
        class="text-sm"
      >
        {{ t('ai.noAnswer') }}<span v-if="solutions.rationale"> — {{ solutions.rationale }}</span>
      </Message>

      <ul v-else class="divide-y divide-surface-200 text-sm dark:divide-surface-700">
        <li v-for="article in solutions.articles" :key="article.id" class="py-2">
          <RouterLink
            :to="{ name: 'kb-article', params: { id: article.id } }"
            class="text-primary hover:underline"
          >
            {{ titleOf(article) }}
          </RouterLink>
        </li>
      </ul>
    </div>

    <!-- Category -->
    <div v-if="category" class="mb-1">
      <Message v-if="!category.categoryId" severity="secondary" :closable="false" class="text-sm">
        {{ t('ai.noAnswer') }}<span v-if="category.rationale"> — {{ category.rationale }}</span>
      </Message>

      <div v-else class="flex flex-wrap items-center gap-2 rounded border border-surface-200 p-3 text-sm dark:border-surface-700">
        <span class="font-medium">
          {{ ui.isArabic ? category.categoryNameAr : category.categoryNameEn }}
        </span>
        <span v-if="category.rationale" class="text-xs text-surface-500 dark:text-surface-400">
          {{ category.rationale }}
        </span>
        <Button :label="t('ai.applyCategory')" size="small" class="ms-auto" @click="acceptCategory" />
      </div>
    </div>
  </div>
</template>
