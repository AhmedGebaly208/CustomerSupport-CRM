import { defineStore } from 'pinia'
import { ref } from 'vue'
import { systemConfigApi } from '@/api/services'
import type { Branding } from '@/types/api'

/**
 * Runtime branding (PDF area 12 "Custom branding").
 *
 * Applied by writing the values onto the CSS custom properties already declared on
 * `:root` in `assets/main.css`, so no component style has to know that branding is
 * configurable. Fetched before the first paint, from an anonymous endpoint, because the
 * login screen needs the company name and colour before anyone has a token.
 */
export const useBrandingStore = defineStore('branding', () => {
  const branding = ref<Branding | null>(null)
  const loaded = ref(false)

  function apply(value: Branding) {
    const root = document.documentElement.style

    if (value.primaryColor) {
      root.setProperty('--brand-primary', value.primaryColor)
      // PrimeVue reads its own token for component accents; keep the two in step so a
      // branded colour reaches buttons and tags, not just our own CSS.
      root.setProperty('--p-primary-color', value.primaryColor)
    }

    if (value.secondaryColor) {
      root.setProperty('--brand-secondary', value.secondaryColor)
    }

    // The document title is the one piece of branding outside the Vue tree.
    const name = document.documentElement.lang === 'en' ? value.companyNameEn : value.companyNameAr
    if (name) document.title = name
  }

  async function load() {
    try {
      const value = await systemConfigApi.branding()
      branding.value = value
      apply(value)
    } catch {
      // Branding is cosmetic: if the endpoint is unreachable the app must still start on
      // the compiled-in defaults rather than blocking at a blank screen.
    } finally {
      loaded.value = true
    }
  }

  /** Called after an admin saves, so the change is visible without a reload. */
  function set(value: Branding) {
    branding.value = value
    apply(value)
  }

  return { branding, loaded, load, set }
})
