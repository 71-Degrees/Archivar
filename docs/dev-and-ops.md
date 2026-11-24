# Dev shortcuts and production diagnostics

Ported from TriviaNight's `docs/dev-and-ops.md`, trimmed to what Archivar has.

## Dev shortcuts (Development only)

Mapped only when the environment is Development (`Operations/DevEndpoints.cs`) - they don't exist anywhere else.

| URL | What it does |
|---|---|
| `/dev/seed` | Makes sure `seed.member@example.test` (Member) and `seed.admin@example.test` (Admin) exist with confirmed emails, plus two sample feedback items, then signs in as the member and opens `/account`. |
| `/dev/seed?as=admin` | Same, signed in as the admin, landing on `/admin/users`. Add `&to=/path` to land elsewhere. |
| `/dev/seed?as=pjj` | Same seed, signed in as your own account (PJJ), landing on `/admin/users`. |
| `/dev/logged-out` | Signs this browser out and stops `Auth:DevAutoLoginEmail` signing it back in (a cookie, this browser only). |
| `/dev/logged-in` | Undoes that: the auto-login signs this browser in again on the next request. |

**PJJ, your own account, is seeded on every Development startup** (`Operations/OwnerSeed.cs`): `patrickjohnson859@hotmail.com`,
Admin, email confirmed, password from the Windows user environment variable `DEV_SEED_PJJ_PASSWORD_HASH` (a PBKDF2
hash - never the password, never in the repo). An existing PJJ account keeps its password. `DevSeed:Enabled=false`
turns it off (the integration tests do).

Use these instead of clicking through code sign-in for routine local testing. Emailed codes are never sent in
Development anyway (the code shows on screen).

## Logging and alerts (`Operations/Observability.cs`)

- **Serilog** replaces the default logging: console always, one line per request (`UseSerilogRequestLogging`).
- **Alert email digest** (`Logging/EmailAlertSink.cs`): outside Development, when real email is configured and
  `Email:AlertEmail` is set, Warning/Error/Fatal events are batched and emailed - every 5 minutes, at once after 20
  events, or at once on a Fatal. The sink never throws.
- **Application Insights**: on when `APPLICATIONINSIGHTS_CONNECTION_STRING` is set (App Service sets it when you link a
  resource). Requests, dependencies, exceptions and every log line, tagged with the build version. Unset = off.
- The startup log says which of these are on: `Application Insights: on|off; alert emails: on|off`.

Before reporting "no errors in production": check Application Insights (`exceptions` / `traces` with
`severityLevel >= 2`) *and* ask the owner whether any alert digest emails arrived - either one alone can miss things.

## Public address and canonical host

`Site:BaseUrl` (App Setting) is the site's public address. It drives robots.txt/sitemap/llms.txt links and turns on
the canonical-host redirect (`CanonicalHostRedirect`): a GET/HEAD on any other host name (the `*.azurewebsites.net`
name, the bare domain) gets a 308 to the same path on the public address. `/healthz`, `/version` and `/api/*` answer
on any name so probes and the deploy tool keep working; localhost is never redirected. Unset = no redirect.

## Static files: caching and compression

`MapStaticAssets()` covers what TriviaNight needed its own `StaticAssetCachePolicy` + response-compression middleware
for. A **published** build serves every fingerprinted file (`@Assets["app.css"]` → `app.d2hl9cvh80.css`) with
`Cache-Control: max-age=31536000, immutable` and precompressed Brotli/gzip. Pages are never compressed (they carry
antiforgery tokens - BREACH). Two things keep that true:

- Reference every file in `wwwroot` through `@Assets[...]` (a test fails on any un-fingerprinted css/js reference).
- In Development, and when running an unpublished build, assets are served `no-cache` on purpose - verify caching
  against a published build or the live site, not `dotnet run`.

## Scheduled-downtime notice (`/admin/notice`, Admin only)

Set a start time (UTC) and length: a banner shows on every page from now until the window ends (`SiteBanner`, read
from a 30-second in-memory copy). Optionally email it to the Admins or to every enabled account with a confirmed
email - **"Preview only" is ticked by default** and just reports how many would get it. Development never sends it
unless `Maintenance:SendInDevelopment=true`. Clear it any time. Stored in `App_Data/site-settings`
(`Storage:SiteSettingsFolder`).

## Release notes (`/admin/releases`, Admin only)

Each production deploy is a git tag `release-yyyy-MM-dd-HHmm` (UTC), created and pushed by the deploy tool's `prod` command; a
release is the commits between two tags, sorted into New features / Bug fixes / Improvements / Behind the scenes by
the wording of the commit subject (`CommitClassifier`). `tools/Deploy build` writes `release-notes.json` into the
publish output (a running site has no git); `tools/Deploy notes` writes a local copy to `src/Archivar.Web/` (gitignored)
so the page works under `dotnet run`. The page also shows this server's build, start time, storage calls and per-cache
hit rates.

## Deploy tool (`tools/Deploy`, the shared engine in `tools/WebCore`)

`tools/Deploy` is a one-line launcher for the deploy engine every 71 Degrees app shares (the `tools/WebCore` submodule); this app's settings are in `deploy/deploy.json`. The commands, the settings file and what an app must provide
(`/version`, `/healthz`, optionally a status endpoint) are documented in `tools/WebCore/docs/deploy.md`. In short: `check` (tests + compile + scripts + code smells; the pre-push hook and CI run it), `install-hooks`, `build`,
`stage` / `prod` (staging first, then promote exactly what was tested; **live infrastructure - ask first**), `rollback`, `verify`, `perf`, `status`, `notice`, `setting`, `console`, `notes`, `report`, `sbom`.

Any command that talks to the site takes `--url` to point it elsewhere - e.g. `perf --url http://localhost:5288`
against a published build. A `--url` run keeps its own timing history, so it never becomes production's baseline.

## Dependency hygiene

NuGet Audit fails the build on a known-vulnerable package, direct or transitive (`Directory.Build.props`); lock files
are committed and `tools/Deploy check` restores `--locked-mode`; `nuget.config` pins nuget.org; `global.json` holds the SDK to the .NET 10 line (10.0.401 or newer, `rollForward: latestFeature`; never .NET 11). There is no hosted CI and
no Dependabot: the pre-push hook is the only gate, and a package bump is a deliberate commit (edit the version, `dotnet restore --force-evaluate`, commit the lock files with it, run `check`). An SBOM is generated on demand (`tools/Deploy -- sbom`), never committed - a snapshot goes stale.

## Behind a proxy: the real client IP

The per-IP limits (password sign-in, registration, feedback) key on the client's IP address. Behind a reverse proxy -
App Service on **Linux**, or Front Door / Application Gateway in front of either OS - every request arrives from the
proxy, so all visitors would share one limit. Set the App Setting **`ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`** there:
ASP.NET Core then takes the client IP (and https) from the proxy's `X-Forwarded-For` / `X-Forwarded-Proto`, one hop.
App Service on Windows passes the real client IP through already. `ForwardedHeadersTests` shows the difference.
Don't turn it on for a site reachable *without* a proxy in front - clients could then fake their own IP.
