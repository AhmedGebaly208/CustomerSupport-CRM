import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import { usePrimeVue } from 'primevue/config'
import { i18n, isRtl, LOCALE_STORAGE_KEY, readStoredLocale, type AppLocale } from '@/i18n'
import { primeVueLocale } from '@/locales/primevue'

const THEME_STORAGE_KEY = 'crm.theme'

export type AppTheme = 'light' | 'dark'

/**
 * Owns the two presentation-wide switches from PDF area 12: language (which also flips
 * text direction) and theme. Both are applied to <html> so PrimeVue, Tailwind logical
 * properties and native form controls all pick them up from one place.
 */
export const useUiStore = defineStore('ui', () => {
  // Resolved once here rather than inside the watcher: usePrimeVue() reads an injection,
  // which is only available while the store's setup is running.
  const primevue = usePrimeVue()

  const locale = ref<AppLocale>(readStoredLocale())
  const theme = ref<AppTheme>(
    (localStorage.getItem(THEME_STORAGE_KEY) as AppTheme | null) ??
      (window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'),
  )

  const direction = computed(() => (isRtl(locale.value) ? 'rtl' : 'ltr'))
  const isArabic = computed(() => locale.value === 'ar')

  /** Picks the field matching the active locale from any bilingual DTO. */
  function localized(entity: { nameAr?: string | null; nameEn?: string | null } | null | undefined): string {
    if (!entity) return ''
    return (isArabic.value ? entity.nameAr : entity.nameEn) ?? entity.nameEn ?? entity.nameAr ?? ''
  }

  /** Same idea for the fullNameAr/fullNameEn pairs on customers and agents. */
  function localizedFullName(
    entity: { fullNameAr?: string | null; fullNameEn?: string | null } | null | undefined,
  ): string {
    if (!entity) return ''
    return (isArabic.value ? entity.fullNameAr : entity.fullNameEn) ?? entity.fullNameEn ?? entity.fullNameAr ?? ''
  }

  function applyLocale(value: AppLocale) {
    locale.value = value
  }

  function toggleLocale() {
    applyLocale(locale.value === 'ar' ? 'en' : 'ar')
  }

  function applyTheme(value: AppTheme) {
    theme.value = value
  }

  function toggleTheme() {
    applyTheme(theme.value === 'dark' ? 'light' : 'dark')
  }

  watch(
    locale,
    (value) => {
      i18n.global.locale.value = value
      // PrimeVue's built-in strings live in its own config, not in our message files.
      primevue.config.locale = primeVueLocale(value)
      localStorage.setItem(LOCALE_STORAGE_KEY, value)

      const root = document.documentElement
      root.setAttribute('lang', value)
      // PrimeVue components and Tailwind logical properties both key off dir, so this
      // single attribute mirrors the entire layout.
      root.setAttribute('dir', isRtl(value) ? 'rtl' : 'ltr')
    },
    { immediate: true },
  )

  watch(
    theme,
    (value) => {
      localStorage.setItem(THEME_STORAGE_KEY, value)
      document.documentElement.classList.toggle('app-dark', value === 'dark')
    },
    { immediate: true },
  )

  return {
    locale,
    theme,
    direction,
    isArabic,
    localized,
    localizedFullName,
    applyLocale,
    toggleLocale,
    applyTheme,
    toggleTheme,
  }
})
