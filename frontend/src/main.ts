import { createApp } from 'vue'
import { createPinia } from 'pinia'
import PrimeVue from 'primevue/config'
import Aura from '@primeuix/themes/aura'
import ToastService from 'primevue/toastservice'
import ConfirmationService from 'primevue/confirmationservice'
import Tooltip from 'primevue/tooltip'

import App from './App.vue'
import { router } from './router'
import { useBrandingStore } from '@/stores/branding'
import { i18n } from './i18n'

import 'primeicons/primeicons.css'
import '@/assets/main.css'

const app = createApp(App)

app.use(createPinia())
app.use(i18n)
app.use(PrimeVue, {
  theme: {
    preset: Aura,
    options: {
      // Matches the class the UI store toggles on <html>.
      darkModeSelector: '.app-dark',
      // Keep PrimeVue's own layer beneath Tailwind's so utility classes win.
      cssLayer: { name: 'primevue', order: 'theme, base, primevue' },
    },
  },
  ripple: true,
})
app.use(ToastService)
app.use(ConfirmationService)
app.directive('tooltip', Tooltip)

app.use(router)

// Branding is fetched before the first paint so the login screen renders with the tenant's
// name and colour rather than flashing the defaults. It never blocks startup: the store
// swallows a failure and the compiled-in defaults stand.
const branding = useBrandingStore()
void branding.load().finally(() => app.mount('#app'))
