<script setup lang="ts">
defineProps<{
  title: string
  subtitle?: string
}>()
</script>

<template>
  <div class="mb-5 flex flex-wrap items-center justify-between gap-3">
    <div class="min-w-0">
      <h1 class="truncate text-xl font-semibold lg:text-2xl">{{ title }}</h1>
      <p v-if="subtitle" class="mt-0.5 text-sm text-surface-500 dark:text-surface-400">
        {{ subtitle }}
      </p>
    </div>

    <div class="flex shrink-0 items-center gap-2">
      <slot name="actions" />
    </div>
  </div>
</template>
