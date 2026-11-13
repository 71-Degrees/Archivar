# AI/search discoverability

What's discoverable on Archivar, why, and where the code lives. Cross-project reasoning: `C:\71Degrees\repos\ai-discoverability-playbook.md`.

## Public vs. not

**Public and evergreen:** `/`, `/terms`, `/privacy`, `/contact` (add each new public page here, to the sitemap, and to llms.txt in the same change).

**Not public:** `/account`, `/account/*`, `/admin/*` (sign-in and account pages), `/feedback/*` (form result pages), `/healthz`, `/version`, `/not-found`,
`/Error` - disallowed in robots.txt; llms.txt says the account/admin pages aren't public content. Anything login-gated or session-scoped added
later belongs here too, with a `Disallow:` line.

## Where each piece lives

| What | Where |
|---|---|
| `robots.txt` | `src/Archivar.Web/Program.cs`, `/robots.txt` |
| `sitemap.xml` (just the public pages) | Same file, `/sitemap.xml` |
| `llms.txt` (hand-written; says what's excluded too) | Same file, `/llms.txt` |
| Canonical + meta description + Open Graph + JSON-LD per public page | `Components/Shared/PageSeo.razor` on `/`, `/terms`, `/privacy`, `/contact` (no `og:image` yet - add artwork, then the tag). The home page description is a placeholder until Archivar's purpose is settled |
| JSON-LD rendering helper | `JsonLd.cs` (a data block, so the CSP's `script-src 'self'` doesn't block it) |
| Public address used in all of the above | `Site:BaseUrl` App Setting; falls back to the request's own host |

## Gotchas carried over from TriviaNight

- Blazor's `<HeadContent>` is last-writer-wins per render, not merged. A per-page SEO component must be a complete replacement for
  the layout's default head content, not "just the extra bits".
- If a staging environment is added, it must send `X-Robots-Tag: noindex, nofollow` and serve a blanket-disallow robots.txt.
