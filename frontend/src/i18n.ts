import { createI18n } from 'vue-i18n'
import ar from '@/locales/ar.json'
import en from '@/locales/en.json'

export type AppLocale = 'ar' | 'en'

export const LOCALE_STORAGE_KEY = 'crm.locale'

export function readStoredLocale(): AppLocale {
  const stored = localStorage.getItem(LOCALE_STORAGE_KEY)
  // Arabic is the desk's primary language, so it is the default rather than a fallback.
  return stored === 'en' ? 'en' : 'ar'
}

export const i18n = createI18n({
  legacy: false,
  locale: readStoredLocale(),
  fallbackLocale: 'en',
  messages: { ar, en },
  // Both files are hand-maintained; warn loudly in dev when one drifts behind the other.
  missingWarn: import.meta.env.DEV,
  fallbackWarn: import.meta.env.DEV,
})

export const isRtl = (locale: AppLocale): boolean => locale === 'ar'
