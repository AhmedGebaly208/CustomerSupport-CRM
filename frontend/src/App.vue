<script setup lang="ts">
import { RouterView } from 'vue-router'
import Toast from 'primevue/toast'
import ConfirmDialog from 'primevue/confirmdialog'
import { useUiStore } from '@/stores/ui'

// Instantiating the store applies the persisted locale/dir/theme to <html> through its
// immediate watchers, before the first view renders.
useUiStore()
</script>

<template>
  <RouterView />
  <Toast :position="'top-center'" />
  <ConfirmDialog />
</template>
