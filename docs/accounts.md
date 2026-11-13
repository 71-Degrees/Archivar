# Accounts, roles and sign-in

Read this before changing accounts, sign-in, roles, claims or the admin users page. Ported from TriviaNight
(`C:\71Degrees\repos\TriviaNight\docs\accounts.md`), trimmed to what Archivar needs. This is security-sensitive code:
changes here need the owner's go-ahead before pushing (see CLAUDE.md).

## The model

- **`User`** (`Archivar.Core.Accounts`) - the login identity: random GUID id, email, display name, optional password hash
  (PBKDF2, `PasswordHasher`), `EmailVerifiedAt`.
- **`UserProfile`** - the role/permission record keyed by the same id: `Role` (`Member` | `Admin`), `IsDisabled`.
  Created on first sign-in (`OnSignedIn` → `EnsureProfileAsync`).
- Storage: one JSON file each under `App_Data/users` and `App_Data/profiles` (`Storage:UsersFolder` /
  `Storage:ProfilesFolder` override), wrapped in 30-second caches cleared by every write made through the app.

## Signing in

Every path signs into the same cookie (`archivar_auth`, 14 days sliding, HttpOnly, SameSite=Lax) through
`AccountServices.SignInUserAsync` → `AccountClaims.CreatePrincipal`, so every check reads the same claims:

| Claim | Value |
|---|---|
| `NameIdentifier` | user id |
| `Name` | display name |
| `Email` | email |
| `Role` | `Member` or `Admin` |
| `archivar:email_confirmed` | `1`, only once the email is proven by a code |

- **Password**: `/account/register` → `POST /account/register-submit`; `/account/login` → `POST /account/login-submit`.
  "No such account", "no password" and "wrong password" get the same answer, so the form can't probe for emails.
- **Emailed code** (`AccountEmailService`): `POST /account/code-send` then `/account/code-verify`. 6 digits, 10 minutes,
  single use, 5 wrong tries, stored hashed, bound to the requesting browser by the `archivar_code_client` cookie. The first
  confirmation creates the account (no password) with its email already confirmed. An **Admin with a password must use
  the password** - a code alone can't open an account that manages other accounts.
- **Confirm / change email** (signed in, `/account`): `POST /account/email-send` then `/account/email-verify`, which
  re-issues the cookie so `archivar:email_confirmed` and a changed address apply at once.
- `returnUrl` always goes through `LocalReturnUrl.Sanitize` - no open redirects.

## Roles and claims

- Checks use the role claim: `[Authorize(Policy = AccountServices.AdminPolicy)]` (= `RequireRole("Admin")`),
  `<AuthorizeView Roles="@Roles.Admin">`, `.RequireAuthorization(AccountServices.AdminPolicy)` on endpoints.
  Don't read the profile to check a role.
- The claim stays current: the cookie's `OnValidatePrincipal` reads the (cached) profile on every request and, when the
  stored role differs from the claim, re-issues the cookie with the new role. A promotion/demotion applies within one
  request on this server (the write clears the cache), or within 30 s on another instance.
- **Bootstrapping an Admin**: list the email in `Auth:AdminEmails` (comma-separated). It is promoted on sign-in, but only
  once that email is **confirmed** - registering with an admin's address without proving it is never enough.
- Admins manage everyone on `/admin/users` (change role, disable, re-enable). An admin can't change or disable their own
  account there - the easy way to lose the last admin.

## Disabled accounts

Disabling (`/admin/users`) signs the account out on its next request: `OnValidatePrincipal` rejects a principal whose
profile is disabled **or missing** (fail closed). The sign-in endpoints refuse a disabled account up front, and
`OnSignedIn` is the backstop.

## Rate limits

- Password sign-in / registration: 10 per IP per 10 minutes and 60 overall (`AccountRateLimits`).
- Codes: 5 per address per hour, 10 per browser per hour, 60 per hour and 200 per day overall
  (`AccountEmail:MaxPerClientPerHour` / `MaxPerHour` / `MaxPerDay`), so the mail account can't be used up.

## Email delivery

Azure Communication Services when both `Email:AzureConnectionString` and `Email:FromEmail` are set (the from address
must be a verified MailFrom on the linked ACS domain); otherwise `NullEmailSender` just logs - never a startup crash.
**Local development never emails codes by default** - the code shows on screen - because a dev machine may hold real
secrets. Set `AccountEmail:SendInDevelopment=true` only to test real delivery on purpose. A provider failure returns
`SendFailed` and discards the code.

## Local development shortcuts

- `Auth:DevAutoLoginEmail` (appsettings.Development.json / user-secrets): signs every unauthenticated request in as
  that existing account. Development only (checked in code too). Leave blank to test signed-out pages.
- Codes appear in the page and in the `devCode` query parameter.

## Not ported from TriviaNight (on purpose)

Leaderboard/masked-email display, team name/avatar, seat handoff, beta invite codes, content-terms acceptance, Blob
storage repositories, SMTP/SendGrid email fallbacks. Add them back from TriviaNight if Archivar needs them.
