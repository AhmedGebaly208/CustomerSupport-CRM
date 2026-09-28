<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useToast } from 'primevue/usetoast'
import Button from 'primevue/button'
import InputText from 'primevue/inputtext'
import Textarea from 'primevue/textarea'
import Select from 'primevue/select'
import ToggleSwitch from 'primevue/toggleswitch'
import Chips from 'primevue/autocomplete'
import Message from 'primevue/message'
import Tabs from 'primevue/tabs'
import TabList from 'primevue/tablist'
import Tab from 'primevue/tab'
import TabPanels from 'primevue/tabpanels'
import TabPanel from 'primevue/tabpanel'
import PageHeader from '@/components/PageHeader.vue'
import { kbApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useUiStore } from '@/stores/ui'
import type { ArticleCategory, ArticleTag, SaveArticleRequest } from '@/types/api'

const props = defineProps<{ id?: string }>()

const { t } = useI18n()
const router = useRouter()
const toast = useToast()
const ui = useUiStore()

const categories = ref<ArticleCategory[]>([])
const saving = ref(false)
const loading = ref(true)
const slugsLocked = ref(false)
const tagInput = ref<string[]>([])

const form = reactive<SaveArticleRequest>({
  categoryId: '',
  titleAr: '',
  titleEn: '',
  summaryAr: null,
  summaryEn: null,
  bodyAr: '',
  bodyEn: '',
  isFaq: false,
  isPublic: true,
  departmentId: null,
  branchId: null,
  tags: [],
  changeNote: null,
})

const isEdit = computed(() => !!props.id)

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

const formError = computed(() => {
  if (!form.categoryId) return t('kb.error.categoryRequired')
  if (!form.titleAr.trim() || !form.titleEn.trim()) return t('kb.error.titleRequired')
  return null
})

/** Publishing needs both bodies; saving a draft does not. Surfaced as a hint rather than an
 *  error so an article can be written one language at a time. */
const publishWarning = computed(() =>
  !form.bodyAr.trim() || !form.bodyEn.trim() ? t('kb.error.bodyBothLanguages') : null,
)

async function save() {
  if (formError.value || saving.value) return

  saving.value = true
  try {
    const request: SaveArticleRequest = {
      ...form,
      summaryAr: form.summaryAr?.trim() || null,
      summaryEn: form.summaryEn?.trim() || null,
      changeNote: form.changeNote?.trim() || null,
      tags: tagInput.value.map<ArticleTag>((label) => ({ slug: '', labelAr: label, labelEn: label })),
    }

    const saved = props.id
      ? await kbApi.update(props.id, request)
      : await kbApi.create(request)

    toast.add({ severity: 'success', summary: t('app.saved'), life: 3000 })
    await router.push({ name: 'kb-article', params: { id: saved.id } })
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 7000 })
  } finally {
    saving.value = false
  }
}

onMounted(async () => {
  try {
    categories.value = await kbApi.categories(true)

    if (props.id) {
      const article = await kbApi.article(props.id)

      Object.assign(form, {
        categoryId: article.categoryId,
        titleAr: article.titleAr,
        titleEn: article.titleEn,
        summaryAr: article.summaryAr,
        summaryEn: article.summaryEn,
        bodyAr: article.bodyAr,
        bodyEn: article.bodyEn,
        isFaq: article.isFaq,
        isPublic: article.isPublic,
        departmentId: article.departmentId,
        branchId: article.branchId,
        changeNote: null,
      })

      tagInput.value = article.tags.map((x) => (ui.isArabic ? x.labelAr : x.labelEn))
      slugsLocked.value = article.slugsLocked
    } else if (categoryOptions.value.length > 0) {
      form.categoryId = categoryOptions.value[0].value
    }
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 5000 })
  } finally {
    loading.value = false
  }
})
</script>

<template>
  <div>
    <PageHeader :title="isEdit ? t('kb.editArticle') : t('kb.newArticle')">
      <template #actions>
        <Button :label="t('app.cancel')" text @click="router.back()" />
        <Button
          :label="t('app.save')"
          icon="pi pi-check"
          :disabled="!!formError || saving || loading"
          :loading="saving"
          @click="save"
        />
      </template>
    </PageHeader>

    <div class="space-y-4">
      <div class="grid gap-4 md:grid-cols-3">
        <div class="md:col-span-2">
          <label class="mb-1 block text-sm font-medium">{{ t('kb.category') }} *</label>
          <Select
            v-model="form.categoryId"
            :options="categoryOptions"
            option-label="label"
            option-value="value"
            class="w-full"
            :placeholder="t('kb.pickCategory')"
          />
        </div>

        <div class="flex items-end gap-6">
          <div class="flex items-center gap-2">
            <ToggleSwitch v-model="form.isFaq" input-id="kb-faq" />
            <label for="kb-faq" class="text-sm">{{ t('kb.faq') }}</label>
          </div>

          <div class="flex items-center gap-2">
            <ToggleSwitch v-model="form.isPublic" input-id="kb-public" />
            <label for="kb-public" class="text-sm">{{ t('kb.publicToCustomers') }}</label>
          </div>
        </div>
      </div>

      <Message v-if="slugsLocked" severity="info" :closable="false" class="text-sm">
        {{ t('kb.slugsLocked') }}
      </Message>

      <Tabs value="ar">
        <TabList>
          <Tab value="ar">{{ t('kb.arabicSide') }}</Tab>
          <Tab value="en">{{ t('kb.englishSide') }}</Tab>
        </TabList>

        <TabPanels>
          <TabPanel value="ar">
            <div class="space-y-3 py-2" dir="rtl">
              <div>
                <label class="mb-1 block text-sm font-medium">{{ t('kb.articleTitle') }} *</label>
                <InputText v-model="form.titleAr" class="w-full" />
              </div>
              <div>
                <label class="mb-1 block text-sm font-medium">{{ t('kb.summary') }}</label>
                <Textarea v-model="form.summaryAr" rows="2" auto-resize class="w-full" />
              </div>
              <div>
                <label class="mb-1 block text-sm font-medium">{{ t('kb.body') }}</label>
                <Textarea v-model="form.bodyAr" rows="16" class="w-full font-mono text-sm" />
                <small class="text-surface-500 dark:text-surface-400">{{ t('kb.bodyHint') }}</small>
              </div>
            </div>
          </TabPanel>

          <TabPanel value="en">
            <div class="space-y-3 py-2" dir="ltr">
              <div>
                <label class="mb-1 block text-sm font-medium">{{ t('kb.articleTitle') }} *</label>
                <InputText v-model="form.titleEn" class="w-full" />
              </div>
              <div>
                <label class="mb-1 block text-sm font-medium">{{ t('kb.summary') }}</label>
                <Textarea v-model="form.summaryEn" rows="2" auto-resize class="w-full" />
              </div>
              <div>
                <label class="mb-1 block text-sm font-medium">{{ t('kb.body') }}</label>
                <Textarea v-model="form.bodyEn" rows="16" class="w-full font-mono text-sm" />
                <small class="text-surface-500 dark:text-surface-400">{{ t('kb.bodyHint') }}</small>
              </div>
            </div>
          </TabPanel>
        </TabPanels>
      </Tabs>

      <div class="grid gap-4 md:grid-cols-2">
        <div>
          <label class="mb-1 block text-sm font-medium">{{ t('kb.tags') }}</label>
          <Chips
            v-model="tagInput"
            multiple
            :typeahead="false"
            class="w-full"
            :placeholder="t('kb.tagsHint')"
          />
        </div>

        <div v-if="isEdit">
          <label class="mb-1 block text-sm font-medium">{{ t('kb.changeNote') }}</label>
          <InputText v-model="form.changeNote" class="w-full" :placeholder="t('kb.changeNoteHint')" />
        </div>
      </div>

      <Message v-if="formError" severity="error" :closable="false" class="text-sm">
        {{ formError }}
      </Message>

      <Message v-else-if="publishWarning" severity="warn" :closable="false" class="text-sm">
        {{ publishWarning }}
      </Message>
    </div>
  </div>
</template>
