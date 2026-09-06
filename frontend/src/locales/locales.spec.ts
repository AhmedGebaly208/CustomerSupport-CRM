import { describe, expect, it } from 'vitest'
import ar from './ar.json'
import en from './en.json'

/**
 * The bilingual promise in PDF area 12 is only as good as the two catalogues staying in
 * step. Eleven more feature stories will add strings to these files; without this guard a
 * key added to one language and forgotten in the other ships as a raw `user.title`
 * placeholder in the UI, which nobody notices until a customer does.
 */
type Catalogue = Record<string, unknown>

function flatten(value: Catalogue, prefix = ''): Set<string> {
  const keys = new Set<string>()

  for (const [key, child] of Object.entries(value)) {
    const path = `${prefix}${key}`

    if (child !== null && typeof child === 'object' && !Array.isArray(child)) {
      for (const nested of flatten(child as Catalogue, `${path}.`)) keys.add(nested)
    } else {
      keys.add(path)
    }
  }

  return keys
}

/** Extracts {placeholders} so a translation cannot silently drop an interpolation. */
function placeholders(catalogue: Catalogue, path: string): string[] {
  const value = path
    .split('.')
    .reduce<unknown>((node, key) => (node as Catalogue | undefined)?.[key], catalogue)

  if (typeof value !== 'string') return []

  return [...value.matchAll(/\{(\w+)\}/g)].map((match) => match[1]).sort()
}

describe('locale catalogues', () => {
  const arabic = flatten(ar as Catalogue)
  const english = flatten(en as Catalogue)

  it('define exactly the same keys', () => {
    const missingFromEnglish = [...arabic].filter((key) => !english.has(key)).sort()
    const missingFromArabic = [...english].filter((key) => !arabic.has(key)).sort()

    expect(missingFromEnglish, 'keys present in ar.json but missing from en.json').toEqual([])
    expect(missingFromArabic, 'keys present in en.json but missing from ar.json').toEqual([])
  })

  it('use the same interpolation placeholders in both languages', () => {
    const mismatched = [...arabic]
      .filter((key) => english.has(key))
      .map((key) => ({
        key,
        ar: placeholders(ar as Catalogue, key),
        en: placeholders(en as Catalogue, key),
      }))
      .filter((row) => row.ar.join(',') !== row.en.join(','))

    expect(mismatched, 'placeholders differ between ar.json and en.json').toEqual([])
  })

  it('have no empty translations', () => {
    const empty = (catalogue: Catalogue, keys: Set<string>) =>
      [...keys].filter((key) => {
        const value = key
          .split('.')
          .reduce<unknown>((node, part) => (node as Catalogue | undefined)?.[part], catalogue)
        return typeof value === 'string' && value.trim().length === 0
      })

    expect(empty(ar as Catalogue, arabic), 'empty Arabic strings').toEqual([])
    expect(empty(en as Catalogue, english), 'empty English strings').toEqual([])
  })

  it('keep Arabic actually Arabic', () => {
    // Guards against an untranslated English string being pasted into ar.json. A handful of
    // entries are legitimately Latin (the language switcher labels, brand names), so this
    // asserts on the proportion rather than demanding every value contain Arabic script.
    const arabicScript = /[؀-ۿ]/
    const values = [...arabic]
      .map((key) =>
        key.split('.').reduce<unknown>((node, part) => (node as Catalogue | undefined)?.[part], ar as Catalogue),
      )
      .filter((value): value is string => typeof value === 'string' && value.trim().length > 0)

    const withArabic = values.filter((value) => arabicScript.test(value)).length

    expect(withArabic / values.length).toBeGreaterThan(0.9)
  })
})
