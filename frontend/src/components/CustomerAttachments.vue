<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useToast } from 'primevue/usetoast'
import { useConfirm } from 'primevue/useconfirm'
import Button from 'primevue/button'
import FileUpload, { type FileUploadUploaderEvent } from 'primevue/fileupload'
import DataTable from 'primevue/datatable'
import Column from 'primevue/column'
import { customersApi } from '@/api/services'
import { problemMessage } from '@/api/client'
import { useAuthStore } from '@/stores/auth'
import { useFormat } from '@/composables/useFormat'
import { PERMISSIONS, type AttachmentDetail } from '@/types/api'

const props = defineProps<{ customerId: string }>()

const { t } = useI18n()
const toast = useToast()
const confirm = useConfirm()
const auth = useAuthStore()
const { formatDateTime } = useFormat()

const files = ref<AttachmentDetail[]>([])
const loading = ref(true)
const uploading = ref(false)

const canUpload = computed(() => auth.hasPermission(PERMISSIONS.attachmentsUpload))
const canDelete = computed(() => auth.hasPermission(PERMISSIONS.attachmentsDelete))

/** Bytes are unreadable at a glance; kB/MB is what an agent actually wants. */
function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} kB`
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
}

function iconFor(contentType: string, fileName: string): string {
  const ext = fileName.split('.').pop()?.toLowerCase() ?? ''
  if (contentType.startsWith('image/')) return 'pi pi-image'
  if (ext === 'pdf') return 'pi pi-file-pdf'
  if (['xlsx', 'xls', 'csv'].includes(ext)) return 'pi pi-file-excel'
  if (['doc', 'docx'].includes(ext)) return 'pi pi-file-word'
  if (['zip', 'rar', '7z'].includes(ext)) return 'pi pi-box'
  return 'pi pi-file'
}

async function load() {
  loading.value = true
  try {
    files.value = await customersApi.attachments(props.customerId)
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.loadFailed')), life: 5000 })
  } finally {
    loading.value = false
  }
}

async function upload(event: FileUploadUploaderEvent) {
  // PrimeVue hands over File | File[] depending on multiple mode.
  const file = Array.isArray(event.files) ? event.files[0] : event.files
  if (!file || uploading.value) return

  uploading.value = true
  try {
    await customersApi.uploadAttachment(props.customerId, file)
    toast.add({ severity: 'success', summary: t('attachment.uploaded'), life: 3000 })
    await load()
  } catch (e) {
    // The API returns a 400 naming the disallowed type or the size cap.
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.saveFailed')), life: 7000 })
  } finally {
    uploading.value = false
  }
}

/**
 * Downloads through axios rather than a plain link, because the endpoint needs the bearer
 * token and an anchor href cannot carry one.
 */
async function download(attachment: AttachmentDetail) {
  try {
    const blob = await customersApi.downloadAttachment(props.customerId, attachment.id)
    const url = URL.createObjectURL(blob)

    const link = document.createElement('a')
    link.href = url
    link.download = attachment.fileName
    link.click()

    URL.revokeObjectURL(url)
  } catch (e) {
    toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
  }
}

function confirmDelete(attachment: AttachmentDetail) {
  confirm.require({
    message: t('attachment.deleteConfirm', { name: attachment.fileName }),
    header: t('app.confirm'),
    icon: 'pi pi-exclamation-triangle',
    acceptProps: { label: t('app.delete'), severity: 'danger' },
    rejectProps: { label: t('app.cancel'), outlined: true },
    accept: async () => {
      try {
        await customersApi.deleteAttachment(props.customerId, attachment.id)
        await load()
      } catch (e) {
        toast.add({ severity: 'error', summary: problemMessage(e, t('error.generic')), life: 5000 })
      }
    },
  })
}

onMounted(load)
</script>

<template>
  <div>
    <div v-if="canUpload" class="mb-3">
      <FileUpload
        mode="basic"
        :auto="true"
        :custom-upload="true"
        :choose-label="t('attachment.upload')"
        :disabled="uploading"
        @uploader="upload"
      />
      <small class="mt-1 block text-surface-500 dark:text-surface-400">
        {{ t('attachment.uploadHint') }}
      </small>
    </div>

    <DataTable :value="files" :loading="loading" size="small" striped-rows>
      <template #empty>
        <div class="p-6 text-center text-surface-500 dark:text-surface-400">
          {{ t('attachment.none') }}
        </div>
      </template>

      <Column :header="t('attachment.file')">
        <template #body="{ data }">
          <div class="flex items-center gap-2">
            <i :class="iconFor(data.contentType, data.fileName)" class="text-surface-400" />
            <span class="truncate">{{ data.fileName }}</span>
          </div>
        </template>
      </Column>

      <Column :header="t('attachment.size')" style="width: 8rem">
        <template #body="{ data }">
          <span class="ltr-nums text-sm">{{ formatSize(data.sizeBytes) }}</span>
        </template>
      </Column>

      <Column :header="t('attachment.uploadedBy')">
        <template #body="{ data }">
          <span class="text-sm">{{ data.uploadedByName ?? '—' }}</span>
        </template>
      </Column>

      <Column :header="t('customer.createdAt')">
        <template #body="{ data }">
          <span class="text-sm">{{ formatDateTime(data.createdAt) }}</span>
        </template>
      </Column>

      <Column :header="t('app.actions')" style="width: 7rem">
        <template #body="{ data }">
          <div class="flex items-center gap-1">
            <Button
              icon="pi pi-download"
              text
              rounded
              size="small"
              :aria-label="t('attachment.download')"
              @click="download(data)"
            />
            <Button
              v-if="canDelete"
              icon="pi pi-trash"
              severity="danger"
              text
              rounded
              size="small"
              :aria-label="t('app.delete')"
              @click="confirmDelete(data)"
            />
          </div>
        </template>
      </Column>
    </DataTable>
  </div>
</template>
