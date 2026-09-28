<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import Tag from 'primevue/tag'
import Skeleton from 'primevue/skeleton'
import Dialog from 'primevue/dialog'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import PageHeader from '@/components/PageHeader.vue'
import { kbApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import { useAuthStore } from '@/stores/auth'
import { useFormat } from '@/composables/useFormat'
import { ArticleStatus, PERMISSIONS, type ArticleDetail, type ArticleVersion } from '@/types/api'

const props = defineProps<{ id: string }>()

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const confirm = useConfirm()
const ui = useUiStore()
const auth = useAuthStore()
const { formatDateTime } = useFormat()

const article = ref<ArticleDetail | null>(null)
const loading = ref(true)
const voted = ref<boolean | null>(null)

const versions = ref<ArticleVersion[]>([])
const versionsOpen = ref(false)

const canManage = computed(() => auth.hasPermission(PERMISSIONS.kbManage))
const canPublish = computed(() => auth.hasPermission(PERMISSIONS.kbPublish))

const title = computed(() => (ui.isArabic ? article.value?.titleAr : article.value?.titleEn) ?? '')
const summary = computed(() => (ui.isArabic ? article.value?.summaryAr : article.value?.summaryEn) ?? '')

/**
 * The body is author-written HTML. It is rendered with v-html, which is safe here only
 * because authoring is gated behind kb.manage — a permission held by staff, not by
 * customers or the portal. Were the portal ever to accept article content, this would need
 * sanitising on the way in.
 */
const body = computed(() => (ui.isArabic ? article.value?.bodyAr : article.value?.bodyEn) ?? '')

async function load() {
  loading.value = true
  try {
    article.value = await kbApi.article(props.id)

    // Counted once per open, after the read succeeds, so a failed load is not a view.
    void kbApi.recordView(props.id).catch(() => {})
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 5000 })
  } finally {
    loading.value = false
  }
}

async function vote(isHelpful: boolean) {
  try {
    const result = await kbApi.vote(props.id, isHelpful)
    voted.value = result.yourVote

    if (article.value) {
      article.value.helpfulCount = result.helpfulCount
      article.value.notHelpfulCount = result.notHelpfulCount
    }
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
  }
}

async function setStatus(status: ArticleStatus) {
  try {
    article.value = await kbApi.setStatus(props.id, status)
    toast.add({ severity: 'success', summary: t('app.saved'), life: 3000 })
  } catch (e) {
    // Publishing refuses a half-translated article, which is worth reading in full.
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 8000 })
  }
}

async function openVersions() {
  try {
    versions.value = await kbApi.versions(props.id)
    versionsOpen.value = true
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 5000 })
  }
}

function confirmRestore(version: ArticleVersion) {
  confirm.require({
    message: t('kb.restoreConfirm', { n: version.versionNumber }),
    header: t('app.confirm'),
    icon: 'pi pi-history',
    acceptProps: { label: t('kb.restore') },
    rejectProps: { label: t('app.cancel'), outlined: true },
    accept: async () => {
      try {
        article.value = await kbApi.restoreVersion(props.id, version.id)
        versionsOpen.value = false
      } catch (e) {
        toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
      }
    },
  })
}

function confirmDelete() {
  confirm.require({
    message: t('kb.deleteConfirm'),
    header: t('app.confirm'),
    icon: 'pi pi-exclamation-triangle',
    acceptProps: { label: t('app.delete'), severity: 'danger' },
    rejectProps: { label: t('app.cancel'), outlined: true },
    accept: async () => {
      try {
        await kbApi.remove(props.id)
        await router.push({ name: 'kb' })
      } catch (e) {
        // Refused while tickets cite it; the message says to archive instead.
        toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 8000 })
      }
    },
  })
}

onMounted(load)
</script>

<template>
  <div>
    <div v-if="loading" class="space-y-3">
      <Skeleton height="2.5rem" width="60%" />
      <Skeleton height="20rem" />
    </div>

    <template v-else-if="article">
      <PageHeader :title="title" :subtitle="summary || undefined">
        <template #actions>
          <Button icon="pi pi-arrow-left" text rounded @click="router.push({ name: 'kb' })" />

          <Button
            v-if="canManage"
            icon="pi pi-history"
            text
            rounded
            :aria-label="t('kb.versions')"
            @click="openVersions"
          />

          <Button
            v-if="canManage"
            :label="t('app.edit')"
            icon="pi pi-pencil"
            outlined
            @click="router.push({ name: 'kb-article-edit', params: { id: article.id } })"
          />

          <Button
            v-if="canPublish && article.status !== ArticleStatus.Published"
            :label="t('kb.publish')"
            icon="pi pi-send"
            @click="setStatus(ArticleStatus.Published)"
          />

          <Button
            v-if="canPublish && article.status === ArticleStatus.Published"
            :label="t('kb.archive')"
            icon="pi pi-inbox"
            severity="secondary"
            outlined
            @click="setStatus(ArticleStatus.Archived)"
          />

          <Button
            v-if="canPublish"
            icon="pi pi-trash"
            severity="danger"
            text
            rounded
            :aria-label="t('app.delete')"
            @click="confirmDelete"
          />
        </template>
      </PageHeader>

      <div class="mb-4 flex flex-wrap items-center gap-2 text-sm">
        <Tag
          :severity="article.status === ArticleStatus.Published ? 'success' : article.status === ArticleStatus.Draft ? 'warn' : 'secondary'"
          rounded
          :value="t(`kb.status.${ArticleStatus[article.status]}`)"
        />
        <Tag v-if="article.isFaq" severity="info" rounded :value="t('kb.faq')" />
        <Tag v-if="!article.isPublic" severity="secondary" rounded icon="pi pi-lock" :value="t('kb.internal')" />

        <span class="text-surface-500 dark:text-surface-400">
          {{ ui.isArabic ? article.categoryNameAr : article.categoryNameEn }}
        </span>

        <span v-if="article.authorName" class="text-surface-500 dark:text-surface-400">
          {{ article.authorName }}
        </span>

        <span v-if="article.publishedAt" class="text-surface-500 dark:text-surface-400">
          {{ formatDateTime(article.publishedAt) }}
        </span>

        <span class="ltr-nums text-surface-500 dark:text-surface-400">
          <i class="pi pi-eye" /> {{ article.viewCount }}
        </span>
      </div>

      <div v-if="article.tags.length > 0" class="mb-4 flex flex-wrap gap-2">
        <Tag
          v-for="tag in article.tags"
          :key="tag.slug"
          severity="secondary"
          rounded
          :value="ui.isArabic ? tag.labelAr : tag.labelEn"
        />
      </div>

      <article
        class="kb-body rounded-lg border border-surface-200 bg-surface-0 p-6 dark:border-surface-700 dark:bg-surface-900"
        v-html="body"
      />

      <div
        class="mt-6 flex flex-wrap items-center gap-3 rounded-lg border border-surface-200 p-4 dark:border-surface-700"
      >
        <span class="text-sm font-medium">{{ t('kb.wasThisHelpful') }}</span>

        <Button
          :label="t('kb.yes')"
          icon="pi pi-thumbs-up"
          size="small"
          :outlined="voted !== true"
          :severity="voted === true ? 'success' : 'secondary'"
          @click="vote(true)"
        />
        <Button
          :label="t('kb.no')"
          icon="pi pi-thumbs-down"
          size="small"
          :outlined="voted !== false"
          :severity="voted === false ? 'danger' : 'secondary'"
          @click="vote(false)"
        />

        <span class="ltr-nums ms-auto text-sm text-surface-500 dark:text-surface-400">
          {{ t('kb.voteCounts', { up: article.helpfulCount, down: article.notHelpfulCount }) }}
        </span>
      </div>
    </template>

    <Dialog
      v-model:visible="versionsOpen"
      modal
      :header="t('kb.versions')"
      :style="{ width: '46rem' }"
      :breakpoints="{ '960px': '95vw' }"
    >
      <DataTable :value="versions" size="small" striped-rows>
        <template #empty>
          <div class="p-4 text-center text-sm text-surface-500 dark:text-surface-400">
            {{ t('kb.noVersions') }}
          </div>
        </template>

        <Column :header="t('kb.version')" style="width: 5rem">
          <template #body="{ data }"><span class="ltr-nums">#{{ data.versionNumber }}</span></template>
        </Column>

        <Column :header="t('kb.articleTitle')">
          <template #body="{ data }">{{ ui.isArabic ? data.titleAr : data.titleEn }}</template>
        </Column>

        <Column :header="t('kb.editedBy')">
          <template #body="{ data }">{{ data.editorName ?? '—' }}</template>
        </Column>

        <Column :header="t('kb.editedAt')">
          <template #body="{ data }">
            <span class="text-sm">{{ formatDateTime(data.editedAt) }}</span>
          </template>
        </Column>

        <Column :header="t('kb.changeNote')">
          <template #body="{ data }">
            <span class="text-sm">{{ data.changeNote ?? '—' }}</span>
          </template>
        </Column>

        <Column style="width: 6rem">
          <template #body="{ data }">
            <Button :label="t('kb.restore')" text size="small" @click="confirmRestore(data)" />
          </template>
        </Column>
      </DataTable>
    </Dialog>
  </div>
</template>

<style scoped>
/* Author-written HTML has no classes of its own, so the container styles it. */
.kb-body :deep(h1),
.kb-body :deep(h2),
.kb-body :deep(h3) {
  font-weight: 600;
  margin-block: 1rem 0.5rem;
}

.kb-body :deep(h1) { font-size: 1.5rem; }
.kb-body :deep(h2) { font-size: 1.25rem; }
.kb-body :deep(h3) { font-size: 1.1rem; }

.kb-body :deep(p) { margin-block: 0.75rem; line-height: 1.8; }

.kb-body :deep(ul),
.kb-body :deep(ol) {
  margin-block: 0.75rem;
  padding-inline-start: 1.5rem;
  list-style-position: outside;
}

.kb-body :deep(ul) { list-style-type: disc; }
.kb-body :deep(ol) { list-style-type: decimal; }
.kb-body :deep(li) { margin-block: 0.25rem; }

.kb-body :deep(a) { color: var(--p-primary-color); text-decoration: underline; }
.kb-body :deep(img) { max-width: 100%; height: auto; border-radius: 0.5rem; }

.kb-body :deep(code) {
  font-family: ui-monospace, monospace;
  background: var(--p-surface-100);
  padding: 0.1rem 0.3rem;
  border-radius: 0.25rem;
  /* Code is read left to right whatever the surrounding text direction. */
  direction: ltr;
  display: inline-block;
}

.kb-body :deep(pre) {
  background: var(--p-surface-100);
  padding: 1rem;
  border-radius: 0.5rem;
  overflow-x: auto;
  direction: ltr;
  text-align: start;
}

:global(.app-dark) .kb-body :deep(code),
:global(.app-dark) .kb-body :deep(pre) {
  background: var(--p-surface-800);
}

.kb-body :deep(table) { width: 100%; border-collapse: collapse; margin-block: 1rem; }

.kb-body :deep(th),
.kb-body :deep(td) {
  border: 1px solid var(--p-surface-300);
  padding: 0.5rem;
  text-align: start;
}

:global(.app-dark) .kb-body :deep(th),
:global(.app-dark) .kb-body :deep(td) {
  border-color: var(--p-surface-700);
}

.kb-body :deep(blockquote) {
  border-inline-start: 3px solid var(--p-primary-color);
  padding-inline-start: 1rem;
  margin-block: 1rem;
  color: var(--p-text-muted-color);
}
</style>
