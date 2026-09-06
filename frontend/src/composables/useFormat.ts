import { useUiStore } from '@/stores/ui'

/**
 * Locale-aware formatting. Arabic uses the Gregorian calendar with Latin digits here
 * deliberately: support desks reconcile ticket dates against email and phone-system
 * logs, and Hijri or Arabic-Indic digits make that comparison error-prone.
 */
export function useFormat() {
  const ui = useUiStore()

  const localeTag = () => (ui.isArabic ? 'ar-SA-u-ca-gregory-nu-latn' : 'en-GB')

  function formatDate(value: string | Date | null | undefined): string {
    if (!value) return '—'
    const date = typeof value === 'string' ? new Date(value) : value
    if (Number.isNaN(date.getTime())) return '—'

    return new Intl.DateTimeFormat(localeTag(), {
      year: 'numeric',
      month: 'short',
      day: '2-digit',
    }).format(date)
  }

  function formatDateTime(value: string | Date | null | undefined): string {
    if (!value) return '—'
    const date = typeof value === 'string' ? new Date(value) : value
    if (Number.isNaN(date.getTime())) return '—'

    return new Intl.DateTimeFormat(localeTag(), {
      year: 'numeric',
      month: 'short',
      day: '2-digit',
      hour: '2-digit',
      minute: '2-digit',
    }).format(date)
  }

  /** "3 hours ago" style, for activity feeds where exact timestamps add noise. */
  function formatRelative(value: string | Date | null | undefined): string {
    if (!value) return '—'
    const date = typeof value === 'string' ? new Date(value) : value
    if (Number.isNaN(date.getTime())) return '—'

    const diffMs = date.getTime() - Date.now()
    const units: [Intl.RelativeTimeFormatUnit, number][] = [
      ['year', 1000 * 60 * 60 * 24 * 365],
      ['month', 1000 * 60 * 60 * 24 * 30],
      ['day', 1000 * 60 * 60 * 24],
      ['hour', 1000 * 60 * 60],
      ['minute', 1000 * 60],
    ]

    const formatter = new Intl.RelativeTimeFormat(localeTag(), { numeric: 'auto' })

    for (const [unit, ms] of units) {
      if (Math.abs(diffMs) >= ms) {
        return formatter.format(Math.round(diffMs / ms), unit)
      }
    }

    return formatter.format(Math.round(diffMs / 1000), 'second')
  }

  function formatNumber(value: number | null | undefined): string {
    if (value === null || value === undefined) return '—'
    return new Intl.NumberFormat(ui.isArabic ? 'ar-SA-u-nu-latn' : 'en-GB').format(value)
  }

  /** True once the resolution deadline has passed and the ticket is still open. */
  function isOverdue(dueAt: string | null | undefined): boolean {
    if (!dueAt) return false
    const due = new Date(dueAt)
    return !Number.isNaN(due.getTime()) && due.getTime() < Date.now()
  }

  return { formatDate, formatDateTime, formatRelative, formatNumber, isOverdue }
}
