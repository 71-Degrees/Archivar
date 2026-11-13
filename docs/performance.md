# Performance and caching

The rule (see CLAUDE.md): **every new read, outside call, static file or expensive computation gets a cache policy in the same change** -
what is cached, for how long, what clears it, and whether callers get their own copy. This page is the inventory that policy lives in.
The building blocks live in `Archivar.Core.Caching` (ported from TriviaNight): `SnapshotCache<T>` for one value read often, `KeyedCache<TKey,TValue>`
for one value per key, and `CacheRegistry` counting hits/misses per cache name. Use them - do not hand-roll a `ConcurrentDictionary` cache. An
empty or failed answer is never cached. Anything that decides who can do what: at most 30 s, cleared at once by a write made through this app.

## Inventory - server side (in memory)

| What | Cache | TTL | Cleared by | Copies |
|---|---|---|---|---|
| Profile per user / all profiles | `CachedUserProfileRepository` (`profiles.byUser`, `profiles.all`) | 30 s | any profile write (role, disable, ensure) | cloned |
| Account list | `CachedUserRepository` (`users.all`) | 30 s | any account write | cloned |
| Downtime notice | `MaintenanceService` (`maintenanceNotice`) | 30 s | set / clear on this server | immutable per read (a new notice is a new object) |
| Release notes | `ReleaseNotesService` (loaded once) | process life | a new build | read-only |

## Inventory - repositories, stores and outside calls (every one must be listed)

| Interface | Policy (Cached / HttpCached / Uncached) | Notes |
|---|---|---|
| `IUserProfileRepository` | Cached | read on **every** signed-in request (`OnValidatePrincipal`: disabled check + role claim sync) - this cache is what keeps that cheap |
| `IUserRepository` | Cached (list only) | `GetByEmailAsync` is one file read (sign-in only). `GetByIdAsync` scans every account file - only used by email confirmation, never per request; revisit if accounts grow large |
| `IMaintenanceStore` | Cached | one small file, read through `MaintenanceService` - at most once per 30 s whatever the traffic |
| `IEmailSender` (Azure Communication Services) | Uncached | outside call; sign-in/confirmation codes (rate-limited in `AccountEmailService`) and feedback notifications (rate-limited in `FeedbackService`) |
| `IFeedbackRepository` | Uncached | write-mostly. Reads scan every file: the admin inbox (`ListAllAsync`) and "Your data" (`CountBySubmitterAsync`) - both occasional, never on a public request path. Revisit if feedback volume grows |

## Static files

`MapStaticAssets()` serves `wwwroot` with fingerprinted URLs (`@Assets["..."]`) and long-lived, immutable caching - reference static files
through `@Assets[...]` so a changed file gets a new URL instead of a stale cached copy.

## Measuring it

- **`StorageCalls`** (`Archivar.Core.Caching`) wraps every repository *under* its cache, so each count is a real
  storage read or write - a cache miss or an uncached repository. Wrap any new repository the same way
  (`StorageCalls.Wrap<IThing>(new JsonThing(...))`).
- **`PerformanceSnapshotService`** logs one line every 5 minutes of activity: storage calls and cache hits/misses in
  that window. In Application Insights: `AppTraces | where Message startswith "Performance snapshot"`, tagged with
  the build version - "did this release make the site read more?" is a query, not a guess.
- **`/admin/releases`** shows the same numbers for the running server since it started, per cache.
- **`tools/Deploy perf`** times the key pages against a URL and checks static-file caching; **`tools/Deploy report`**
  summarises production request timings and storage calls per request, per build. See docs/dev-and-ops.md.
