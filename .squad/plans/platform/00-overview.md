# Platform — plan overview

Feature slug: `platform`
PDF area: 12

Area 12 is a constraint on every other area rather than a screen of its own. The bootstrap
already built bilingual ar/en with RTL, a responsive shell, and department/branch as data;
this plan closes the remaining gaps.

## Stories

| NN | File | Title | Status |
|----|------|-------|--------|
| 06 | `06-story-platform.md` | Platform | **Partly implemented** |

## What landed

**Department/branch scoping enforcement.** `IScopedEntity` (Domain), `IScopeProvider`
(Application), `ScopeProvider` (Api, reading the `department_id` claim). Applied in
`TicketService` and `CustomerService`: `scope.Apply(...)` on every list query and
`scope.EnsureCanAccess(...)` after every load-by-id. Two decisions worth knowing:

- **Fails closed.** A non-global caller with no department claim sees nothing, not
  everything. A misconfigured agent account becomes an obvious support ticket rather than a
  silent leak.
- **Unrouted records stay visible.** A ticket with no department is visible to every scoped
  caller, because unrouted work has to be claimable by someone and the dashboard already
  surfaces an unassigned queue.

The scope is applied *before* the caller's own filters, so a filter can only narrow further —
asking explicitly for another department returns nothing rather than widening access.
Only aggregate roots implement `IScopedEntity`; children (comments, history, notes,
interactions) are gated by their parent's check, so a ticket moving department cannot leave
a child behind with a stale scope.

**Locale-drift guard.** `src/locales/locales.spec.ts` (vitest, 4 tests) fails the build when
a key exists in one catalogue and not the other, when a `{placeholder}` differs between
languages, on empty strings, or when ar.json stops being predominantly Arabic script. Wired
into `npm run build`, not just a script nobody runs. Verified by introducing deliberate
drift and watching it fail.

**Runtime branding.** Delivered as part of the `security-admin` story-04 configuration
(`BrandingSetting`, `/api/branding`), so it exists once rather than twice.
`stores/branding.ts` fetches it before the first paint from the anonymous endpoint — the
login screen needs the name and colour before anyone has a token — and applies the values to
the `--brand-*` CSS custom properties already declared on `:root`, plus PrimeVue's
`--p-primary-color`. No component style knows branding is configurable. A failed fetch
falls back to the compiled-in defaults rather than blocking startup. The logo URL is
restricted server-side to https or `/uploads/`, because it is rendered in an `img src` on
the anonymous login screen.

**Language switch preserves the route.** Already true: `ui.toggleLocale()` mutates store
state and the `<html>` attributes without navigating, so no route or form state is lost.

## Not done

- **Bundled Arabic font.** `main.css` names `Noto Sans Arabic` in the stack without shipping
  it, so rendering still depends on the OS having it. Needs the font files vendored and an
  `@font-face` rule.
- **Physical-direction lint.** No automated check yet for `ml-*`/`mr-*`/`left-*`/`right-*`
  used where the layout must mirror. Currently a review step.
- **375px verification across every screen.** Verified at 415px in a browser; the narrower
  breakpoint has not been walked screen by screen.
- **PWA.** The plan left "web and mobile friendly" as responsive-only versus installable
  undecided; still undecided, so nothing was built.
- **Per-department/branch branding.** `BrandingSetting` is a single row. Multi-tenant
  branding resolution (Branch → Department → Global) is designed in the plan but not built.
