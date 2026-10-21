# Auth & Users API Contract

Status: 🟡 Draft v1.5 — engineering revision of v1.4, made under the owner's instruction of 2026-10-15 to resolve the documentation-consistency audit. It is the implementation baseline for Backend Sprint 2 from Slice 2 onward. The items in §19 still need owner confirmation; until the owner confirms or overrides them, each one behaves as written here. v1.4 is not retroactively approved by this revision. **§22 lists proposed v1.6 amendments: A–H and M1–M5 (2026-10-15; Slice 2B persistence and the throttle policy) and I1–I20 (2026-10-16; Slice 3 audit events, designed in repository ADR 0006). They are pending review and are not applied to §1–§21: until accepted, §1–§21 remain the v1.5 text.** Within §22 I, only the owner decisions R46 (audit vocabulary) and R47 (production-data gate) of 2026-10-16 are decided; every other item there is a proposal.
Date: 2026-09-27 · revised 2026-10-01 (v1.4) · 2026-10-15 (v1.5) · 2026-10-16 (§22 I proposed; still Draft v1.5)
Implements: Backend Sprint 2 (`docs/implementation/backend/sprints/sprint-02.md`) · plan `admin-user-creation-plan.md` §1B and §1D
Implementation status (2026-10-15): **no endpoint in this contract exists in code.** The authorization catalog seed (Slice 1, commits `1e10742` and `bc63d5f`), the user credential/security-state schema (Slice 2) and the persistence for sessions, refresh tokens, reset tickets and the throttle store (Slice 2B, ADR 0005; no throttle policy) are the only parts with code behind them (see `sprint-02.md` §A). Everything else here is specification.

> **Credential design superseded (2026-10-21).** Owner decisions **R49–R51** (`admin-user-creation-plan.md` §1F) replace this contract's temporary-password, forced-change and reset-code design: the system generates the password and sends the username and password to the employee's WhatsApp; the Admin is shown no password; no change is forced. The affected sections (§5.6, §7, §9.1, §9.6, §9.8, §10, §10A, and the matching items of §12, §16 and §19) are marked where they begin; the replacement is **§23**. Until contract v1.6 is issued (roadmap PS-03) they describe v1.5 and the code at `1644077`, not the planned behaviour.

> **How to read this contract.** Every rule carries its basis:
> - **[N]** Normative: stated by an authoritative document (`docs/documentation-index-ar.md` order). Cited.
> - **[O]** Owner decision: decided by the project owner, recorded in the plan as C1–C6, R1–R43 (2026-09-26 … 2026-10-01) and R44–R45 (owner instructions of 2026-10-15, plan §1D). Not a documented business rule, but binding.
> - **[E]** Engineering decision: chosen to make the contract implementable and secure. Binding for the implementation until the owner overrides it; changing one needs no business decision, only a contract revision. The v1.5 engineering decisions are numbered E1–E16 in plan §1D.
>
> If this contract and an authoritative document ever disagree, the document wins and the contract has a defect ([N] beats [O] beats [E]). Among owner decisions, a later decision that explicitly addresses the same point supersedes an earlier one; the superseded text is kept in the plan as history.
> The spec itself defers endpoint names, body shapes, pagination detail, error codes, the authentication mechanism and token validity until after the logical model (spec §26.3). This contract is where those deferred items are settled for the Identity slice only.

---

## 1. Conventions

| Rule | Basis |
|---|---|
| Every endpoint is under **`/api/v1/`**. | [N] spec §26.1 principle 8 · [O] R7 |
| JSON bodies, `camelCase` property names, UTF-8. | [E] |
| Errors use **RFC 9457 ProblemDetails** (`application/problem+json`) with a stable `code` extension member (§12). Clients branch on `code`, never on `title` or `detail`. | [N] spec §26.1 principle 4 (errors explain what blocked the action) · [E] shape |
| Every modifying call is authorized **on the server**. Hiding a button in the app is not protection. | [N] FR-AUTH-008 · BR-018 · BR-138 · NFR-SEC-001 |
| Timestamps are ISO-8601 UTC (`2026-09-27T08:00:00Z`). | [E] (UTC matches ADR 0003 §9.1) |
| Every response sets `X-Correlation-ID` (§13). | [N] spec §26.1 principle 6 · [O] R8 |
| Responses that carry a password, a token or a reset code set `Cache-Control: no-store`. | [E] |
| Passwords, temporary passwords, reset codes, access tokens and refresh tokens are **never logged or audited**, in request or response bodies. Request/response body logging must redact them. | [N] logical model §1.7 · audit addendum §9.1 · NFR-SEC-002 |
| **HTTPS only** outside local development: the API listens on TLS 1.2 or newer and does not serve plain HTTP (no redirect from HTTP, which would already have sent credentials in clear). The Admin web origin sends `Strict-Transport-Security: max-age=31536000`. Local development may use `http://localhost`. | [N] NFR-SEC-008 · [E] E14 |
| **Input limits** (checked before any lookup or hashing, so oversized input cannot be used to exhaust CPU): `loginIdentifier` at most **256** characters; `resetCode` at most 32 characters; auth request bodies at most 16 KiB. Over-limit input → 400 `validation.failed` (on login: the generic 401 `auth.invalid_credentials`, so no field-level hint is given). These are transport limits; they do not change ADR 0003's "no confirmed length limit" for stored identifiers created before them. **A password field's 128-character maximum does not apply uniformly as this same kind of pre-lookup §1 check on every endpoint** — each endpoint below states which of its password fields get this pre-lookup transport check and which get an endpoint-specific business rule instead, even though several share the same 128-character number: on `login` (§5.1), the submitted password gets a pre-lookup 128-character limit, folded into the generic 401 `auth.invalid_credentials`; on `password-reset/complete` (§5.6), `newPassword`'s 128-character maximum **is** this same pre-lookup transport check, answered with 400 `validation.failed` — distinct from that same endpoint's 8-character minimum, which is a business rule checked later, after the identifier/ticket/code match; on `change-password` (§5.5), `currentPassword` gets a pre-lookup 128-character §1 check, but `newPassword`'s 8 and 128 bounds are **both** business rules, checked only after authentication and `currentPassword` verification succeed, each with its own `auth.password_too_short` / `auth.password_too_long` code. | [E] E13 |

---

## 2. Identity, roles and permissions

| Rule | Basis |
|---|---|
| Roles: `Engineer`, `OfficeStaff`, `Accountant`, `Admin`. | [N] spec §7.1 |
| Seeded permissions: `Users.Manage`, `Permissions.Grant`, `Roles.Manage`, `Payroll.View`, `Apps.Admin.Access`, `Apps.Employee.Access`. **Implemented** (migration `SeedAuthorizationCatalog`, commit `1e10742`). | [N] spec §7.3 (first four, 🔵 proposed catalog) · [O] C2, R13 (scope and the two app-access codes) |
| Default role grants: Admin ← `Apps.Admin.Access`, `Users.Manage`, `Permissions.Grant`, `Roles.Manage`. OfficeStaff ← `Apps.Admin.Access`. Accountant ← `Apps.Admin.Access`. Engineer ← `Apps.Employee.Access`. **Implemented** (same migration). | [N] spec §7.3 (Identity perms to Admin) · [O] R13 |
| **`Payroll.View` is never granted through a role**, Admin included; only as a direct per-user grant. | [N] BR-235 · ح-235 · FR-AUTH-006 · NFR-SEC-003 (enforced by DB triggers, ADR 0002 §6) |
| Roles are represented in the API by their **code** (string), never by database id. | [O] R9 |
| **Effective permissions** of a user at time *t* = permissions of every role assignment active at *t* ∪ every direct grant active at *t*. `Payroll.View` can only come from a direct grant. | [N] CAP-02 · BR-235 · [O] R10 · [E] definition of "active" = ADR 0003 §9.1/§9.2 (`IsActiveAt`) |
| A user may use an app only while holding its app-access permission: Admin app ← `Apps.Admin.Access`, Employee app ← `Apps.Employee.Access`. Enforcement: §4. | [O] R13 |

Role codes carry no display name (ADR 0002 §3 stores none). **Clients translate role codes to Arabic labels through their i18n files** [E]. *(Frontend impact: `RoleOption.displayName` does not exist in this contract.)*

---

## 3. Tokens and sessions

### 3.1 Policy

| Rule | Basis |
|---|---|
| **Access token:** JWT, lifetime **30 minutes**. Kept **in memory only** on every client. | [O] R5, R18 · [E] JWT format and validation §3.2 |
| **Refresh token:** opaque random value (32 bytes from a cryptographically secure generator, base64url), **rotated on every use**, **7 days maximum session lifetime** counted from login. Rotation does not extend the maximum. | [O] R5, R17 · [E] "counted from login", size |
| **Idle timeout** (a session with no token activity for longer than this expires even before 7 days): **Engineer 8 hours; Accountant, Admin, OfficeStaff 60 minutes**. These are configuration values (`Auth:IdleTimeout:<RoleCode>`) with these defaults; a configured value outside 5 minutes … 24 hours is rejected at startup. | [N] NFR-SEC-007 (🔵 proposed values; "configurable") · [O] R17 adopted · [E] E15 bounds |
| The backend stores **only a cryptographic hash** of each refresh token (SHA-256), never the token. | [N] logical model §1.7 (tokens not stored) · [O] R5, R18 (a hash is stored, the token is not) |
| **Web** receives the refresh token in a cookie (§3.4), never in the body. **Mobile** receives it in the response body and stores it with `flutter_secure_storage`. | [O] R6, R18 |
| Tokens are never logged by server or client. | [N] logical model §1.7 · audit addendum §9.1 |

### 3.2 Access token claims and validation [E]

| Claim | Meaning |
|---|---|
| `sub` | User id (uuid) |
| `sid` | Session id (the refresh-token family, §3.3) |
| `app` | `admin` or `employee` — the client app the session was opened for. Set by the server from the login request's `clientApp` **after** the app-access check (§4); never taken from a header. |
| `sv` | The user's **security version** at issue time (§3.5) |
| `perm` | Effective permission codes at issue time (array) |
| `role` | Active role codes at issue time (array) |
| `iat`, `nbf`, `exp`, `iss`, `aud` | Standard. `nbf = iat`, `exp = iat + 30 min`, `iss = "contractra-api"`, `aud = "contractra"` (configuration, per environment) |
| header `kid` | Identifier of the signing key (§3.2.1) |

**Validation of every presented access token** [E] E9 — a token failing any check is treated as absent (401 `auth.unauthenticated`):
- Algorithm pinned to **HS256**; a token with any other `alg` (including `none`) is rejected. The algorithm is never read from configuration supplied by the client.
- Signature valid under the key named by `kid`, which must be in the active key ring.
- `iss` and `aud` equal the configured values; `exp` and `nbf` validated with a clock skew of at most **30 seconds** (not the library default of 5 minutes). Servers keep their clock synchronized (NTP), see §18.
- `sub`, `sid`, `app`, `sv` present and well-formed; `app` is `admin` or `employee`.
- Then the per-request security-version lookup (§3.5) and the app-audience check (§4.1).

`mustChangePassword` is **not** a claim; it is read from the database on every request together with the security version (§3.5).

#### 3.2.1 Signing keys and rotation [E] E9

- Each signing key is at least **256 bits** of cryptographically random data, stored in the environment's secret store (user-secrets in development, environment variables or a secret manager in production). Keys are never committed, logged or returned by any endpoint. [N] CLAUDE.md (secrets)
- Keys live in a **key ring**: exactly one *active* key (signs new tokens) and zero or more *verification-only* keys, each with a `kid`.
- **Planned rotation:** add the new key as active; keep the previous key verification-only for at least the access-token lifetime plus the clock skew (31 minutes), then remove it. No user is signed out.
- **Emergency rotation** (key suspected compromised): replace the ring with a new active key and remove the old one immediately. Every outstanding access token fails validation; clients refresh once (§6). Refresh tokens are opaque database records, not signed with this key, so sessions survive and users are not forced to log in again.
- The ring is read at startup; a missing or too-short key prevents startup (fail closed), rather than silently generating one.

### 3.3 Sessions, rotation and revocation (resolves F) [E]

A **session** is one login on one client app. It is a *family* of refresh tokens: login creates the family and its first token; every refresh consumes the current token and issues the next one in the same family.

Each session stores: `id`, `user_id`, `client_app`, `client_platform` (`web` or `mobile`, fixed at login, §5.1), `created_at`, `absolute_expires_at` (= created + 7 days), `last_refreshed_at`, `revoked_at`, `revoke_reason`. Each refresh token stores: `token_hash`, `session_id`, `issued_at`, `consumed_at`. [E] · `client_platform` [O] R38. The tables are built in Slice 2B (`sprint-02.md`), before the Application use cases that revoke sessions.

| Event | Effect | Basis |
|---|---|---|
| Refresh with a valid, unconsumed token | Token marked consumed; new token issued in the same session; new access token with **current** `sv`, `perm`, `role`. | [O] R5 |
| Refresh with a token consumed **30 seconds ago or less** (reuse grace, e.g. two browser tabs refreshing together) | A new **access token** is returned; the refresh token is **not** rotated again and the session is **not** revoked. | [O] R35 |
| Refresh with a token consumed **more than 30 seconds ago** (reuse) | Treated as theft: the **whole session is revoked**; 401 `auth.refresh_invalid`. | [O] R35 |
| Logout | Current session revoked. | [E] |
| **User disabled** | **All** the user's sessions revoked; security version bumped. | [O] R5 |
| **Password reset issued** (§9.6) | The current password stops working; **all** the user's sessions revoked; security version bumped. | [O] R5, R44 |
| **Password reset completed** (§5.6) | New password set; security version bumped; all sessions revoked (none should remain). No session is created; the user then logs in. | [O] R44 · [E] E1 |
| **Initial password re-issued** (§9.8) | All the user's sessions revoked; security version bumped. | [E] E3 |
| **Password change** by the user | **All other** sessions revoked; security version bumped; in the current session the current refresh token is **consumed** and a new one is issued, returned by change-password together with a new access token (§5.5). | [O] R5, R36, owner flow §9 |
| Role assigned/ended, direct permission granted/revoked, user reactivated | Security version bumped. Sessions **not** revoked: the next refresh issues a token with the new permissions. | [O] R5 (freshness) |

Access tokens cannot be revoked individually; the security version (§3.5) is what invalidates them.

### 3.4 Web refresh cookie (resolves C) [O] R6 · [E] attributes

```
Set-Cookie: contractra_refresh=<token>; HttpOnly; Secure; SameSite=Strict; Path=/api/v1/auth; Expires=<session absolute_expires_at>
```

| Attribute | Value | Why |
|---|---|---|
| Name | `contractra_refresh` | [E] |
| `HttpOnly` | yes | JavaScript can never read it [O] R6 |
| `Secure` | yes | HTTPS only [O] R6 · [N] NFR-SEC-008 (TLS). Browsers treat `http://localhost` as secure, so development works. |
| `SameSite` | **`Strict`** | [O] R22 — for the **current same-site deployment model**: the Admin web app and the API share one registrable domain (e.g. `admin.example.com` and `api.example.com`). A **cross-site** deployment would need a different cookie and CSRF strategy; that is **not supported and not implemented** now. |
| `Path` | `/api/v1/auth` | Sent only to auth endpoints, not to every API call |
| `Domain` | not set (host-only) | [E] |
| Expiry | `Expires` = the session's absolute expiry. **Not** extended by rotation. | [E] |
| Logout / revocation | Server responds with the same cookie, empty value, `Max-Age=0`. | [E] |

### 3.5 Security version and freshness (resolves H) [O] R5 · owner clarification

- `users.security_version` is an integer, **starting at 1** for every user (and for rows that existed before the column, by backfill), and incremented **by exactly 1** by every successful security-state change listed in §3.3 as "security version bumped": role assigned/ended, direct permission granted/revoked, disable, reactivate, password reset issued, password reset completed, initial password re-issued, password change. Assignments made while a user is being created count too (a user created with one role has version 2); no token can exist before creation, so this needs no special case. A rejected change (for example an illegal status transition) leaves the version unchanged. The increment happens inside the `User` aggregate (ADR 0003, R34). [O] R5, R34 · [E] E7
- **Every authenticated request performs one indexed lookup** by user id, reading `security_version` and `must_change_password` from the same `users` row. [O] "one indexed lookup"
- If the token's `sv` ≠ the current value → **401 `auth.unauthenticated`**. The client refreshes once (§6): if the session is still valid, it gets a token with fresh permissions; if the session was revoked (disable, reset, password change elsewhere), refresh fails and the user must log in.
- Loading roles and permissions from the database on every request is **not** done. [O]
- A disabled user's tokens fail this check because disabling bumps the version and revokes the sessions. The status itself is therefore not read per request. [E]

### 3.6 Idle timeout — refresh-based inactivity policy (resolves G)

**Values** [N] NFR-SEC-007 · [O] R17, R21: **Engineer 8 hours; Accountant, Admin, OfficeStaff 60 minutes** (configurable within the bounds in §3.1). These are the business idle-timeout values. The 30-minute access-token lifetime does not change them.

**Policy** [O] R21:
- Inactivity is tracked **per session, at refresh**. Each session stores `last_refreshed_at`, set at login and updated at every successful refresh.
- At refresh, if `now − last_refreshed_at` exceeds the applicable idle timeout, the session is revoked (`revoke_reason = idle`) and the response is **401 `auth.session_expired`**. The 7-day absolute limit is checked at the same point.
- **No database write on any other API request.** Normal requests perform only the one indexed security-version lookup (§3.5).
- The applicable timeout is the **shortest** idle timeout among the user's active roles at refresh time; a user with no active role gets 60 minutes. [E] — the owner defined timeouts per role, not for multi-role users.
- **Clients must not refresh in the background.** Refresh only happens reactively (after a 401 from a normal call) or at app start to restore a session. A timer that refreshes without user activity would keep an inactive session alive. [E]
- *Known approximation (owner-accepted, R21):* because activity is observed only at refresh, a session can be idle for up to the idle timeout plus one access-token lifetime before it expires. This is the price of "no write per request".

---

## 4. Client app identification and app access (resolves A)

| Rule | Basis |
|---|---|
| The login request carries **`clientApp`**: `"admin"` or `"employee"`. Any other value → 400 `validation.failed`. | [O] R13 (app must be identified) · [E] field name and values |
| The session remembers its `clientApp`; refresh re-checks that the user **still** holds the matching app-access permission. | [E] |
| Missing permission, with **correct** credentials → **403 `auth.app_access_denied`**. The check runs only **after** the password is verified, so it reveals nothing to someone without the password. Audited as `AccessDenied` (§14). App access is always enforced by the server from the user's permissions. | [O] R13, R12, R24 |
| A **disabled** account still gets the generic `auth.invalid_credentials` (§5.1); app-access is never reached for it. | [O] R11, R24 |
| Missing permission at refresh → session revoked, 401 `auth.refresh_invalid`. | [E] |

### 4.1 App audience of every endpoint — enforced on every request [E] E6

`clientApp` decides which app a session belongs to; this section decides what a session of each app may call. Neither the `app` claim alone nor any client-supplied header is treated as authorization.

| Audience | Endpoints | Requirement on the presented access token |
|---|---|---|
| **public** | `POST /auth/login`, `POST /auth/refresh`, `POST /auth/password-reset/complete` | none (no access token) |
| **any session** | `POST /auth/logout`, `GET /auth/me`, `POST /auth/change-password` | valid token of either app |
| **admin** | every `/users` endpoint and `GET /roles` (§9) | `app = admin` **and** `perm` contains `Apps.Admin.Access` |
| **employee** | future Employee-app endpoints (none in this contract) | `app = employee` **and** `perm` contains `Apps.Employee.Access` |

- Order inside the pipeline: token validation (§3.2) → security-version lookup (§3.5) → password-change gate (§7) → **app audience** → endpoint permission (§9) → the rest of §9's check order.
- An audience mismatch → **403 `auth.app_access_denied`**, written as an `AccessDenied` audit event and to Serilog (§6, §14). Example: an Engineer's Employee-app token calling `GET /users` is refused here even before `Users.Manage` is checked.
- Why both checks: `app` proves which app the session was opened for (it is server-issued and signed); `perm` proves the user still held that app's permission when the token was issued; the security-version check proves nothing changed since. A permission change bumps the version, so a stale `perm` claim is never honoured.
- **`X-Client-Platform` is never consulted** for authorization (§5.1); it only shapes where the refresh token is delivered.
- A user who holds both app permissions has two separate sessions, one per app; a token from one is never accepted by the other's endpoints.
- Every new endpoint must declare its audience; an endpoint without one is a defect and must fail closed (refused) rather than default to "any session".

---

## 5. Auth endpoints

### 5.1 `POST /api/v1/auth/login` — no auth required

Request:
```json
{ "loginIdentifier": "eng.ahmed", "password": "••••••••", "clientApp": "employee" }
```

Checks, in order: throttling (§17) → request shape and input limits (§1) → user lookup → user Active → password correct and usable → app-access permission.

**Identifier matching** [O] ADR 0003 §3.2 · [E] E10: the user is found with `lower(login_identifier) = lower(@input)`, which uses the unique expression index `ux_users_login_identifier_lower`, so `Kamel`, `kamel` and `KAMEL` find the same account. The input is **not** trimmed or otherwise normalized: a value with leading or trailing whitespace matches no account and gets the generic 401. The stored identifier keeps its original casing and is what `/auth/me` returns.

**Usable password** [E] E2, E3: the password check fails (generic 401) when the stored hash is the "no usable password" marker (a reset is pending, or a pre-credential account, §11.1 of ADR 0003), or when the account still has a temporary password whose expiry (§10) has passed.

For an unknown or disabled account, and for an account without a usable password, the server still performs a **dummy password-hash verification**, so response timing does not reveal whether the account exists, is disabled or is pending a reset. [O] R42 · On success, if the stored hash needs an upgrade (`SuccessRehashNeeded`), the password is transparently rehashed. [O] R33

**200** (mobile — any request without `X-Client-Platform: web`):
```json
{
  "accessToken": "<jwt>",
  "accessTokenExpiresAt": "2026-09-27T08:30:00Z",
  "refreshToken": "<opaque>",
  "mustChangePassword": true
}
```
**200** (web): same body **without** `refreshToken`; the refresh token is set as the cookie (§3.4).

**`X-Client-Platform` header** [O] R23, R38:
- Supported values: **`web`** and **`mobile`**. `web` → refresh token in the cookie (§3.4); `mobile` → refresh token in the body. [O]
- Header absent → treated as `mobile`. Any other value → 400 `validation.failed`. [E]
- **Read at login only.** The value is stored on the session as `client_platform` (§3.3). Refresh and change-password shape their responses from the stored value and **ignore** the header. [O] R38
- The Admin app sends `web` only when running in a browser; the mobile builds send `mobile`.
- It is **only a transport / response-shaping hint. It is not a security boundary**: it grants nothing, and **no authorization decision may depend on it**. App access comes from `clientApp` checked against the user's permissions (§4), never from this header.

Errors:
| Status | `code` | When | Basis |
|---|---|---|---|
| 400 | `validation.failed` | Missing/empty field, unknown `clientApp` | [E] |
| 401 | `auth.invalid_credentials` | Unknown identifier, wrong password, **disabled account**, no usable password, or expired temporary password — identical response in every case | [N] UC-001 · FR-AUTH-002 · [O] R11 · [E] E2 |
| 403 | `auth.app_access_denied` | Credentials correct, app permission missing | [O] R13 · [E] |
| 429 | `auth.too_many_attempts` | Throttled (§17); `Retry-After` header set | [O] R45 · [E] E5 |

Every failed attempt is logged to Serilog with the real reason (never the password). A successful login is logged to Serilog as well (no audit event). [N] FR-AUTH-002 · UC-001 (audit events: "—", successful login in Serilog) · ح-193 · audit addendum §9, §12.1 · [O] R11

### 5.2 `POST /api/v1/auth/refresh` — refresh token required

- **Mobile:** body `{ "refreshToken": "<opaque>" }`.
- **Web:** no body; the cookie is sent automatically. The request **must** carry `X-CSRF-Protection: 1` (§8).

**200:** same shape as login (new access token; new refresh token in the body for mobile, or in the cookie for web, according to the session's stored `client_platform` [O] R38; current `mustChangePassword`).

Within the 30-second reuse grace (§3.3, [O] R35) the response carries a new access token only: no new refresh token (the mobile body omits `refreshToken`; the web response sets no cookie). The client keeps the refresh token it already has. [E] shape

**401** `auth.refresh_invalid` (unknown, consumed, revoked, or app permission lost) or `auth.session_expired` (idle or 7-day limit). Clients treat both as "log in again". **429** `auth.too_many_attempts` when the refresh endpoint is throttled (§17).

A disabled user cannot refresh: their sessions were revoked when they were disabled. [O] R5

### 5.3 `POST /api/v1/auth/logout` — access token required

- Revokes the **current** session (the `sid` in the access token). Mobile sends no body; web must also send `X-CSRF-Protection: 1`. Web response clears the cookie.
- **204** always when authenticated, even if the session was already revoked. [E]
- Allowed while `mustChangePassword` is true (§7).

### 5.4 `GET /api/v1/auth/me` — access token required

**200:**
```json
{
  "id": "0193…",
  "loginIdentifier": "eng.ahmed",
  "roles": ["Engineer"],
  "permissions": ["Apps.Employee.Access"],
  "mustChangePassword": false
}
```
- `permissions` are the **effective** permissions (§2), sorted by code; `Payroll.View` appears only when directly granted. `roles` are the active role codes. [N] CAP-02 · [O] R10 · [E] shape
- Values come from the database at request time, not from the token's claims. [E]
- Allowed while `mustChangePassword` is true (§7). (Resolves J.)

### 5.5 `POST /api/v1/auth/change-password` — access token required

Request:
```json
{ "currentPassword": "••••••••", "newPassword": "••••••••" }
```

Checks, in order [O] R14, R16 · [E] E13: the access token (validated by the HTTP pipeline before this logic runs) → throttle admission (§17), per user — a request can be throttled here, before `currentPassword`'s length is ever checked → `currentPassword`'s own §1 input limit (128 characters, checked before any lookup) → the caller's session is still active → `currentPassword` must be correct (during a forced change it is the temporary password, which must not have expired) → `newPassword` at least **8** and at most **128** characters, both a business rule checked only here, after `currentPassword` is confirmed correct — so a wrong or expired current password is reported as such even when the submitted `newPassword` is also too short or too long → `newPassword` must **differ from the current password**. There is no shared default password and no rule about one.

Which refresh token is consumed [E] E11: the request carries no refresh token. The server consumes the current (latest unconsumed) refresh token of the session named by the access token's `sid`, and issues the replacement in the same session.

Effects: the hash is updated, `mustChangePassword = false`, any temporary-password expiry is cleared, the security version is bumped, **every other session is revoked**, and `PasswordChanged` is audited in the same transaction. [O] R1, R5, owner flow §9 · [N] BR-186

**200:** new tokens for the **current** session, same shape as refresh (§5.2), shaped by the session's stored `client_platform` [O] R38. The current refresh token is **consumed** and a new one issued in the same session; the old access token is stale because the version changed. **Clients must store the returned tokens** — the old refresh token is consumed, and presenting it more than 30 seconds later counts as reuse (§3.3). [O] R36, R35

**The bootstrap admin may use this endpoint** to change its own password, under the same rules and with the same effects (hash updated, security version bumped, other sessions revoked). It never gets `mustChangePassword = true`, so the flag stays false. This endpoint changes only the caller's own password; it is not an administrative action on another account. [O] R26

Wrong-current-password attempts are throttled per user (§17): this protects a stolen, still-valid session from being used to guess the password. Throttling does not revoke the session.

Errors:
| Status | `code` | When |
|---|---|---|
| 400 | `auth.current_password_incorrect` | Wrong current password. **400, not 401**, so the client does not try to refresh. [E] |
| 400 | `auth.password_too_short` | New password shorter than 8 [O] R16 |
| 400 | `auth.password_too_long` | New password longer than 128 [E] E13 |
| 400 | `auth.password_same_as_current` | New password equals the current one [O] R16 |
| 429 | `auth.too_many_attempts` | Throttled (§17) [O] R45 |

### 5.6 `POST /api/v1/auth/password-reset/complete` — no auth required [O] R44 · [E] E1

> **v1.6 (owner decisions R49–R51, §23.1):** no flow uses this endpoint any more (a reset sends a new password to the employee's WhatsApp). Planned to leave the API surface (roadmap OD-2); the code and tables stay dormant. The text below describes v1.5 and the code at `1644077`.

Completes a password reset that an Admin or the operator issued (§9.6, §10A). The person who completes it chooses the new password; nobody else ever learns it.

Request:
```json
{ "loginIdentifier": "eng.ahmed", "resetCode": "7KQ2-M9XD-4TBR-W8PZ", "newPassword": "••••••••" }
```

Checks, in order: throttling (§17) → shape and input limits (§1), including the 128-character maximum on `newPassword` → user lookup (case-insensitive, as §5.1) → user Active → an unconsumed, unexpired reset ticket for that user whose code hash matches (constant-time comparison) → new-password minimum length (8 characters).

The 128-character maximum on `newPassword` is a §1 transport limit: like `loginIdentifier` and `resetCode`, it is checked before any lookup or hashing, so an over-limit password never reaches the user/ticket/code check and is answered with 400 `validation.failed`, not `auth.password_too_long`. The 8-character minimum is the opposite: a §5.6 business rule, checked last, only after the identifier, account status and code have all matched — so a too-short password paired with a wrong code, unknown identifier or disabled account still gets the generic `auth.reset_code_invalid`, never the password rule. `auth.password_too_long` is reachable only by a caller that reaches the underlying use case directly, bypassing this endpoint's §1 check. [E] E13

For an unknown user, a disabled user, or a user with no matching ticket, the server performs the same hashing work as for a match, so timing does not reveal which case applied.

Effects (one transaction): password hash set, `mustChangePassword = false`, ticket consumed, security version bumped, all sessions revoked, `PasswordResetCompleted` audited with the user as actor. **No session is created**: the client sends the user to the login screen.

**204** on success. Errors:
| Status | `code` | When |
|---|---|---|
| 400 | `validation.failed` | Shape/input-limit violation (§1), including `newPassword` over 128 characters — checked before any lookup |
| 400 | `auth.reset_code_invalid` | Unknown identifier, disabled account, wrong / expired / consumed / superseded code, or a too-short `newPassword` paired with any of those — one response for all |
| 400 | `auth.password_too_short` | New password shorter than 8 characters, with a matched identifier/ticket/code |
| 429 | `auth.too_many_attempts` | Throttled (§17) |

A failed code attempt never consumes or burns the ticket (otherwise anyone who knows a login identifier could cancel a user's pending reset); guessing is made infeasible by the code's entropy (§10A) and the throttling in §17.

---

## 6. 401 vs 403 semantics

| Status | Meaning | Client action | Basis |
|---|---|---|---|
| **401** | Not authenticated: missing, invalid, expired or **stale** access token (`auth.unauthenticated`), or failed login/refresh | On every call **except login, refresh, logout and password-reset completion** (including `/auth/me` and `change-password`): refresh **once**, retry once; if refresh fails, clear the session and go to login. On login, refresh, logout and reset completion: never refresh. If logout fails for any reason, the client clears its local session anyway. | [O] R37 · [E] reset completion added |
| **403** | Authenticated but not allowed: `auth.forbidden` (missing permission), `auth.password_change_required`, `auth.app_access_denied` (wrong app, §4.1, or app permission missing at login) | Never refresh. Show the matching message. | [N] UC-002 exception flow (unauthorized ⟹ 403) · [E] |
| **429** | Throttled (§17) | Never refresh. Wait for `Retry-After` seconds before allowing another attempt; show a generic "too many attempts, try again later" message. | [O] R45 · [E] E5 |

Every 403 **to an authenticated user** (including `auth.app_access_denied` at login, after the password was verified) is written as an `AccessDenied` audit event **and** logged to Serilog (§14). A web refresh/logout rejected for a missing CSRF header (§8) happens before any actor is established: it is logged to **Serilog only** and is **not** an `AccessDenied` event. A 429 is Serilog only. [N] logical model §1.8 · UC-002 · [O] R12, R40

---

## 7. Password-change-required behaviour (resolves B)

> **v1.6 (R49–R51, §23.1):** creation and reset no longer set `must_change_password`; the gate below stays as a dormant mechanism with no trigger. The text below describes v1.5 and the code at `1644077`.

While `mustChangePassword` is true, the user may call only: [O] R1 · owner flow §9 · [E] exact list

| Allowed | Why |
|---|---|
| `POST /api/v1/auth/change-password` | The purpose of the state |
| `POST /api/v1/auth/logout` | Always allowed |
| `GET /api/v1/auth/me` | The client learns the state from it (R10) |
| `POST /api/v1/auth/refresh` | Keeps the session alive during the change; idle timeout still applies |

Every other endpoint → **403 `auth.password_change_required`**. The check reads `must_change_password` in the same per-request lookup as the security version (§3.5), so it applies immediately.

Who gets the flag: users created by an Admin, and users whose initial password an Admin re-issues (§9.8). Since v1.5 a password **reset** does not set it: the user chooses the new password while completing the reset (§5.6), which already gives the result R1 asks for — the user, not the Admin, knows the password in use. **The bootstrap admin never gets the flag.** [O] R1, R19, R44 · [E] E1

Because a user cannot reach any business endpoint while the flag is true, an account that has never left this state has done nothing in the system except sign in and out. §9.8 relies on that.

---

## 8. CSRF protection for the web cookie (resolves D) [E]

The browser sends the refresh cookie to every `/api/v1/auth` endpoint (cookie `Path`, §3.4), but only `refresh` reads it to authenticate; `logout` identifies the session from the access token and clears the cookie. Both carry the CSRF header requirement below. Three layers:

1. **`SameSite=Strict`**: the browser does not send the cookie on cross-site requests.
2. **Required custom header** `X-CSRF-Protection: 1` on web `refresh` and `logout`. A cross-site page cannot set a custom header without a CORS preflight, and the preflight fails for origins not on the allow-list. Web `refresh`/`logout` requests without the header → 403 `auth.forbidden`, logged to Serilog only, not an `AccessDenied` audit event (§6) [O] R40.
3. **CORS allow-list with credentials:** only the configured Admin web origin(s) may call the API with credentials. Origins come from configuration per environment; `*` is never used with credentials.

Every other endpoint uses the `Authorization: Bearer` header, which a cross-site page cannot attach, so no further CSRF measure applies to them. `password-reset/complete` reads no cookie and uses no ambient credential, so it needs no CSRF header.

---

## 9. Users endpoints — audience `admin`, all require `Users.Manage`

`Users.Manage` covers create, list, read, disable, reactivate, issuing a password reset and re-issuing an initial password. [N] FR-AUTH-003, FR-AUTH-004 · UC-002 · [O] R4, R15 · [E] E3 (re-issue)

**Server check order for every users endpoint** [O] R39 · [E] E6 (step 3):
1. Authentication (401 `auth.unauthenticated`)
2. Password-change gate (403 `auth.password_change_required`, §7)
3. App audience `admin` (403 `auth.app_access_denied` + `AccessDenied`, §4.1)
4. Permission (403 `auth.forbidden` + `AccessDenied`)
5. Target exists (404 `users.not_found`)
6. Bootstrap protection (409 `users.bootstrap_account_protected`, §11)
7. Request validation and state-transition conflicts (400 / 409 / 503)

A caller without the permission therefore never learns whether an id belongs to the bootstrap admin.

### 9.1 `POST /api/v1/users` — create

> **v1.6 (R49–R51, §23.2):** the response carries no `temporaryPassword`; the password is generated by the delivery worker and sent to the employee's WhatsApp; no forced change; the request gains the WhatsApp number and consent. The text below describes v1.5 and the code at `1644077`.

Request [O] R9:
```json
{ "loginIdentifier": "eng.ahmed", "roleCodes": ["Engineer"], "partyId": "0193…" }
```

Rules:
- At least one role [N] FR-AUTH-003 (enforced in the Application layer, ADR 0003 §10).
- Every code must be an existing, **Active** role; a Deprecated role → 400 `users.role_unknown` [O] R43. Duplicate codes in `roleCodes` → 400 `validation.failed` [O] R43.
- Login identifier is opaque, unique case-insensitively, no leading/trailing whitespace, at most 256 characters [O] ADR 0003 §3 · [E] E13. No password field in the request.
- **`partyId` is required when `roleCodes` contains `Engineer`** [O] R31. The Party must be valid for an engineer account on the **business date** [E] E8:
  - it exists;
  - its status is **`Active`** — `NeedsClassification` is the status of unresolved historical names awaiting the accountant's review (logical model §2, BR-058) and `Inactive` is a party no longer in use, so neither is a confirmed, current engineer identity;
  - it holds a party role with code **`Engineer`** (the technical code of the documented party role «مهندس», logical model §2) that is active on the business date, using the existing inclusive rule `PartyRole.IsActiveOn` (`EffectiveFrom ≤ date ≤ EffectiveTo`, open end allowed);
  - it is not linked to any user (one account per Party, [O] R30).
  The **business date** is today's calendar date in the configured business time zone `Business:TimeZone`, default `Africa/Cairo` (the time zone the documents use for business scheduling, ASM-09 / ح-216). The user and the `user_party_links` row are saved in the same transaction.
- When `roleCodes` does not contain `Engineer`, `partyId` must be absent; linking for other roles is a separate optional step not covered by this contract (§15). Present anyway → 400 `validation.failed`. [E]

Effects: user created Active; a **temporary password** generated (§10) with an expiry; `mustChangePassword = true`; roles assigned; for an Engineer, the party link created; `UserCreated` and one `RoleAssigned` per role audited in the same transaction [N] BR-186 · logical model §1.8.

**201 Created**, `Location: /api/v1/users/{id}`, `Cache-Control: no-store` (resolves I):
```json
{
  "user": { "id": "0193…", "loginIdentifier": "eng.ahmed", "status": "Active", "roles": ["Engineer"], "isBootstrapAdmin": false, "credentialState": "TemporaryPasswordIssued" },
  "temporaryPassword": "A7k2Qm9xR4tB",
  "temporaryPasswordExpiresAt": "2026-10-18T08:00:00Z"
}
```

Errors: 400 `validation.failed` · 400 `users.login_identifier_invalid` · 409 `users.login_identifier_taken` · 400 `users.role_required` · 400 `users.role_unknown` · 400 `users.party_required` · 400 `users.party_invalid` (with a `reason` extension: `not_found`, `not_active` or `no_active_engineer_role`) · 409 `users.party_already_linked`. *(Party codes: [E] names for [O] R30/R31.)* A concurrent create of the same identifier or Party is caught by the database unique indexes (SQLSTATE 23505) and mapped to the matching 409.

### 9.2 `GET /api/v1/users` — list (resolves K)

Query [E]: `page` (1-based, default 1), `pageSize` (default 20, max 100), `search` (optional, case-insensitive substring of the login identifier), `status` (optional: `Active` or `Disabled`). Sorted by login identifier, case-insensitive ascending.

**200:**
```json
{
  "items": [ { "id": "0193…", "loginIdentifier": "eng.ahmed", "status": "Active", "roles": ["Engineer"], "isBootstrapAdmin": false, "credentialState": "PasswordSet" } ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1
}
```
`roles` = currently active role codes. Unified paging shape for all future lists. [N] spec §26.1 principle 5 · [E] shape

**`credentialState`** [E] E3 — derived on read, never stored, tells the Admin which credential action applies:

| Value | Meaning | Admin action that applies |
|---|---|---|
| `PasswordSet` | The user has chosen their own password | Issue a password reset (§9.6) |
| `TemporaryPasswordIssued` | Temporary password not used yet and not expired | Wait, or re-issue (§9.8) |
| `InitialPasswordRequired` | Temporary password expired, or an account created before credentials existed | Re-issue initial password (§9.8) |
| `ResetPending` | A reset was issued and its code has not expired | Wait, or issue a new reset |
| `ResetRequired` | A reset was issued but its code expired unused | Issue a new reset |

### 9.3 `GET /api/v1/users/{id}` — read

**200:** the same user object as in the list. **404** `users.not_found`.

### 9.4 `POST /api/v1/users/{id}/disable`

User → Disabled; all sessions revoked; security version bumped; `UserDisabled` audited. **200** with the user object. [N] FR-AUTH-004 · logical model §1.5, §1.8 · [O] C5, R5

**The bootstrap admin cannot be disabled** (§11): rejected with 409 `users.bootstrap_account_protected` at step 6 of the §9 check order (before the state-transition check), and nothing changes. [O] R27, R39
Errors: 404 `users.not_found` · 409 `users.bootstrap_account_protected` · 409 `users.already_disabled`.

### 9.5 `POST /api/v1/users/{id}/reactivate`

User → Active; security version bumped; `UserReactivated` audited. **Reactivation does not restore any previous session**: sessions revoked by the disable stay revoked, and the user must authenticate again. **200** with the user object. [N] logical model §1.5, §1.8 · [O] R4, R25

On the bootstrap admin: rejected with 409 `users.bootstrap_account_protected`, like every other administrative mutation (§11). The account can never be disabled through the API, so reactivating it has no legitimate use. [O] R27 (confirmed for reactivate 2026-10-01)
Errors: 404 `users.not_found` · 409 `users.bootstrap_account_protected` · 409 `users.already_active`.

### 9.6 `POST /api/v1/users/{id}/reset-password` — issue a password reset [O] R44 · [E] E1

> **v1.6 (R49–R51, §23.3):** replaced — a reset invalidates the old password and sessions, queues a new generated password for WhatsApp delivery and returns `202` with the delivery status; no reset ticket, no code, no password in the response, no forced change. The text below describes v1.5 and the code at `1644077`.

**Changed in v1.5.** The Admin no longer receives a temporary password. The Admin *issues* the reset; a single-use reset code (§10A) is delivered to the user through a configured delivery channel; the user completes the reset and chooses the new password (§5.6). The Admin never sees the code or the new password, so the reset cannot be used to take over the account. (Threat model: §10A.)

Request: no body.

Preconditions, checked at step 7 of §9 (after the bootstrap check at step 6):
- The target is Active. A Disabled user → 409 `users.already_disabled`; reactivate first. [O] R43
- The target has completed at least one password change (`mustChangePassword = false`). An account still on its first temporary password → 409 `users.initial_password_pending`; use §9.8 instead. [E] E3
- **A delivery channel is available for the target.** If none is configured → **503 `users.reset_delivery_unavailable`**, and **nothing changes** (fail closed: the user keeps their current password and sessions). Serilog warning with the correlation ID. Until a channel is built (§19 OC-2), this is the response for every account; the operator path in §10A is the interim procedure.

Effects (one transaction, then delivery):
1. Every earlier unconsumed reset ticket of the user is marked superseded.
2. A new ticket is stored: code hash (SHA-256), `issued_at`, `expires_at` (§10A), `issued_by_user_id`, `channel`.
3. The user's current password is invalidated (stored hash replaced by the "no usable password" marker), so a password the Admin suspected was compromised stops working at once.
4. All the user's sessions are revoked; security version bumped.
5. `PasswordResetIssued` audited (actor = the Admin; channel and ticket id; never the code).
6. After commit, the code is handed to the delivery channel. A delivery failure is logged to Serilog and leaves the ticket valid until it expires; the Admin may issue a new reset, which supersedes it.

**202 Accepted**:
```json
{ "channel": "<channel-name>", "expiresAt": "2026-10-16T08:00:00Z" }
```
No secret is ever returned by this endpoint.

**The bootstrap admin cannot be reset through this endpoint** (§11): 409 `users.bootstrap_account_protected`, before anything changes, nothing audited. [O] R20, R27. Any other account — including other Admins, such as the second Admin created after first run (§11) — can have a reset issued by a holder of `Users.Manage`. [O] R32

Errors: 404 `users.not_found` · 409 `users.bootstrap_account_protected` · 409 `users.already_disabled` · 409 `users.initial_password_pending` · 503 `users.reset_delivery_unavailable`.

### 9.7 `GET /api/v1/roles` — audience `admin`, requires `Users.Manage`

**200:**
```json
[ { "code": "Accountant" }, { "code": "Admin" }, { "code": "Engineer" }, { "code": "OfficeStaff" } ]
```
Active roles only, sorted by code. No display name (§2). [E]

### 9.8 `POST /api/v1/users/{id}/initial-password` — re-issue an initial temporary password [E] E3

> **v1.6 (R49–R51, §23.1):** retired — it returns a password to the Admin. Re-issue is `reset-password`. The text below describes v1.5 and the code at `1644077`.

For an account that has **never** left the forced-change state (`mustChangePassword = true`): its temporary password expired, was lost before first use, or the account was created before credentials existed. Because such an account has never been able to call any business endpoint (§7), giving the Admin a fresh temporary password exposes nothing the creation flow (§9.1, R2) did not already expose.

Preconditions (step 7 of §9): target Active (409 `users.already_disabled`); `mustChangePassword = true` (otherwise 409 `users.initial_password_not_reissuable` — the user has chosen a password, so §9.6 applies).

Effects (one transaction): new temporary password (§10) with a new expiry; all sessions revoked; security version bumped; `InitialPasswordReissued` audited.

**200**, `Cache-Control: no-store`:
```json
{ "temporaryPassword": "Zp4Lw8nD2yKc", "temporaryPasswordExpiresAt": "2026-10-18T08:00:00Z" }
```

The bootstrap admin: 409 `users.bootstrap_account_protected` (it never has the flag, §11). Errors: 404 · 409 `users.bootstrap_account_protected` · 409 `users.already_disabled` · 409 `users.initial_password_not_reissuable`.

---

## 10. Temporary password (account creation and initial re-issue only) [O] R2 · owner clarification §8 · [E] E4

> **v1.6 (R49–R51, §23.1):** the generation rules stay; the password is no longer temporary, is not returned to anyone, has no expiry (optional expiry: roadmap OD-25) and is never stored except as a hash. The text below describes v1.5 and the code at `1644077`.

- Generated by the server with a **cryptographically secure** random generator (`RandomNumberGenerator`): **12 characters** from `A–Z`, `a–z`, `0–9`, containing at least one upper-case letter, one lower-case letter and one digit ("mixed letters and digits", R2). Candidates that miss a class are discarded and regenerated, so the distribution stays uniform over the accepted set (≈ 71 bits). [O] R2 · [E]
- Returned **once**, in the create (§9.1) or initial re-issue (§9.8) response. There is **no reveal endpoint**. [O]
- **Expires 72 hours after it is issued** (configuration `Auth:TemporaryPasswordLifetime`, 1–168 hours). After expiry it no longer authenticates (generic 401) and the account shows `credentialState = InitialPasswordRequired`; the Admin re-issues one (§9.8). [E] E4 — an unclaimed, never-expiring temporary password would be a standing credential known to a third party.
- Stored only as a password hash. Never stored in plaintext, never logged, never audited. [O] · [N] logical model §1.7 · NFR-SEC-002
- The Admin app shows it once with a copy action and does not persist it. [O]
- **Not used for password resets** since v1.5 (§9.6, §10A). [O] R44
- There is **no shared default password** and no default-password setting; the value `12345678` must not appear in the production contract or implementation. [O] R16

## 10A. Password reset code and threat model [O] R44 · [E] E1

> **v1.6 (R49–R51, §23.1):** the reset-code mechanism is no longer used by any flow. The text below describes v1.5 and the code at `1644077`.

**Threat addressed.** In v1.4 a holder of `Users.Manage` received the reset user's temporary password, could sign in as that user, satisfy the forced change with a password of their own, and act as the user — including a user who directly holds `Payroll.View`. The audit trail would show `PasswordReset` by the Admin followed by actions attributed to the victim. v1.5 removes the capability: the Admin can deny access (they could already disable the account) but can no longer *become* the user.

**Code.** 16 characters from the Crockford base-32 alphabet (80 bits, generated with `RandomNumberGenerator`), shown and typed in four groups of four (`7KQ2-M9XD-4TBR-W8PZ`); hyphens, spaces and case are ignored on input. Stored as a SHA-256 hash only; with 80 bits of entropy and the throttling in §17, an online guess is not feasible. Never logged or audited.

**Lifetime.** Single use. Expires **24 hours** after issue (configuration `Auth:PasswordResetCodeLifetime`, 15 minutes … 72 hours). A newer ticket for the same user supersedes every older one. Consumed only by a successful completion (§5.6).

**Delivery channel abstraction.** Delivery goes through an Application interface (`IPasswordResetDelivery`); each implementation names its channel and reports whether it can reach a given user. **No channel is implemented in Sprint 2.** Building one (for example email or SMS) needs a decision on which contact data is authoritative for each user (§19 OC-2); the API refuses resets with 503 until then.

**Interim operator procedure (no channel yet).** An operator-only command in `Contractra.Api` — `issue-password-reset --login <identifier> --reason <text>` — runs only on the server, applies the same preconditions as §9.6 (Active, not bootstrap, has chosen a password), creates the ticket with channel `operator`, prints the code **once to the operator's terminal** (never to logs), and audits `PasswordResetIssued` with `actor_type = System`, `source = SystemJob` and the reason. The operator hands the code to the user in person or by phone, and the user completes the reset in the app (§5.6). Server operators can already read and change the database, so this does not widen who can act as a user; it keeps that ability with operators instead of handing it to every holder of `Users.Manage`. Built with the bootstrap break-glass command in Slice 5.

**Privileged accounts.** The same flow applies to Admins and to direct holders of `Payroll.View`; none of them has a weaker path. The bootstrap admin is excluded from both the endpoint and the operator command and keeps its own break-glass (§11, R29/R32).

**Residual risk (recorded, not hidden).** Account creation and the initial re-issue (§9.8) still show a temporary password to the Admin (R2). An Admin could sign in as a brand-new account before handing it over. Such an account has no history and no business access until its forced change, and its rightful user discovers the takeover at first sign-in, because the handed-over password no longer works. Removing this too needs a delivery channel and is §19 OC-3.

---

## 11. Bootstrap admin [O] D2, C3, R19, R20, R26–R29, R32, R39, R41, R42

**Creation**
- Created once at startup, only when no active user holds the `Admin` role, from `Bootstrap:AdminLoginIdentifier` and `Bootstrap:AdminPassword` (user-secrets in development, environment variables in production). Missing configuration → warning, no user created. A configured password that breaks the §5.5 rules (8 … 128 characters) prevents the seeding, with an error log naming the rule (never the value). [E] E13
- `mustChangePassword = false`, now and always (exempt from the forced change). Assigned the `Admin` role. Granted `Payroll.View` **directly** (never through the role). Audited in the same transaction with `actor_type = System`: `UserCreated`, `RoleAssigned` (Admin) and `PayrollViewGranted` — **not** `PermissionGranted` (§14). [O] R41
- **The seeder never overwrites an existing bootstrap admin password** — not on restart, not if the configured password changes. [O] R19, R26
- If the audit write fails, the whole seeding transaction rolls back and startup fails with an error, so there is never an admin without its creation record (§14.2). [E] E12

**Own password** [O] R26
- The bootstrap admin **may change its own password** through `POST /api/v1/auth/change-password` (§5.5), with the normal rules (at least 8 characters, different from the current password) and the normal effects (hash updated, security version bumped, other sessions revoked).

**Protection from administrative mutation** [O] R27

| Action on the bootstrap admin (by **any** actor, including the bootstrap admin itself) [O] R42 | Result |
|---|---|
| Issue a password reset (§9.6), or the operator command `issue-password-reset` (§10A) | 409 `users.bootstrap_account_protected` / command refuses |
| Re-issue an initial password (§9.8) | 409 `users.bootstrap_account_protected` |
| Disable (§9.4) | 409 `users.bootstrap_account_protected` |
| Reactivate (§9.5) | 409 `users.bootstrap_account_protected` [O] R27 (confirmed 2026-10-01) |
| Assign, end or otherwise change roles | Rejected with 409 `users.bootstrap_account_protected`. No role endpoint exists in this contract yet (§15); **any future one must apply this rule.** [O] R27 |
| Grant or revoke a **direct permission** — including revoking its direct `Payroll.View` | Rejected with 409 `users.bootstrap_account_protected`. No direct-permission endpoint exists in this contract yet (§15); **any future one must apply this rule.** [O] R28 |
| Delete | No delete endpoint exists for any user [N] FR-AUTH-004; none may ever be added for this account. |

- The protection check looks at the **target** account only, regardless of who the actor is. [O] R42
- It runs at step 6 of the §9 check order: **after** authentication, the password-change gate, the app audience, the permission check and the 404 check, and **before** request validation and state-transition conflicts. It changes nothing when it rejects. A caller without the permission never reaches it, so it cannot discover the bootstrap id. [O] R39
- A protection rejection is **HTTP 409**, logged to **Serilog** with the correlation ID, and **not** recorded as an `AccessDenied` audit event (it is not a 403). [O] confirmed 2026-10-01
- **Enforced server-side**, in the Application use cases (`IssuePasswordReset`, `ReissueInitialPassword`, `DisableUser`, `ReactivateUser`, and any future role or direct-permission use case), by reading `is_bootstrap_admin` on the target user. The `User` aggregate refuses the same mutations as a second line of defence, and the database forbids changing the flag after insert. The Admin UI may hide these actions, but **hiding is not a security control**. [O] R27, R28 · [N] BR-018 · FR-AUTH-008 · [E] E7

**Lost bootstrap password** [O] R29, R32
- The API provides **no** reset or recovery path for the bootstrap admin: the normal `Users.Manage` reset endpoint stays blocked (§9.6), and there is no self-service recovery.
- If the password is lost, recovery is **out-of-band** (R29). The break-glass arrangement is [O] R32 (it implements R29 and does not change R27):
  - **Second Admin.** A second Admin account is created right after first run. It can issue a reset for any Admin **except** the bootstrap admin (§9.6), so day-to-day administration never depends on the bootstrap password alone.
  - **Operator-only CLI.** `Contractra.Api` provides an operator-only command `reset-bootstrap-password`, runnable only on the server — not an HTTP endpoint. It writes an audit event with `actor_type = System` and prints nothing sensitive to logs. Its exact invocation and the operating procedure around it are defined with the slice that builds it (Slice 5) and in the operations documentation.
- The bootstrap seeder never overwrites an existing bootstrap password, so changing `Bootstrap:AdminPassword` in configuration is **not** a recovery mechanism.

**How the bootstrap account is identified** [O] R20 (must be documented) · [E] mechanism:
- A persisted, non-null boolean **`users.is_bootstrap_admin`**, default `false`.
- Set to `true` **only** by the bootstrap seeder, at the moment it creates the account. No endpoint can set, clear or change it; it is not part of any request body. A database trigger rejects any `UPDATE` that changes the column. [E] E7
- A database partial unique index allows **at most one** row with `is_bootstrap_admin = true`, and a CHECK constraint forbids `is_bootstrap_admin` together with `must_change_password`.
- Why a flag and not a comparison with `Bootstrap:AdminLoginIdentifier`: the configured value can be absent (it is only needed until the first admin exists) or changed later, and a rename in configuration must never silently move protection to another account.
- Exposed read-only in the user object as `"isBootstrapAdmin": true|false`, so the Admin app can hide the protected actions. The server check is authoritative; hiding the button is not protection. [N] BR-018 · FR-AUTH-008
- The column, index, CHECK constraint and trigger are part of the Slice 2 schema (`sprint-02.md`, US-BE-007).

---

## 12. ProblemDetails error codes (resolves E)

Body example:
```json
{ "type": "about:blank", "title": "Invalid credentials", "status": 401, "code": "auth.invalid_credentials", "correlationId": "0193…" }
```
`validation.failed` adds `errors`: `{ "<field>": ["<message>"] }`. `detail` is optional and never contains secrets. A 429 response carries `Retry-After` (seconds).

| Code | Status | Endpoint(s) |
|---|---|---|
| `validation.failed` | 400 | any (includes duplicate `roleCodes`, [O] R43) |
| `auth.invalid_credentials` | 401 | login |
| `auth.unauthenticated` | 401 | any protected endpoint (missing/invalid/expired/stale token) |
| `auth.refresh_invalid` | 401 | refresh |
| `auth.session_expired` | 401 | refresh (idle or 7-day limit) |
| `auth.forbidden` | 403 | any (missing permission; web refresh/logout without the CSRF header — the latter Serilog only, not `AccessDenied`, [O] R40) |
| `auth.password_change_required` | 403 | any except §7's list |
| `auth.app_access_denied` | 403 | login (app permission missing); any `admin`/`employee`-audience endpoint called with the other app's token (§4.1) |
| `auth.current_password_incorrect` | 400 | change-password |
| `auth.password_too_short` | 400 | change-password, password-reset/complete |
| `auth.password_too_long` | 400 | change-password; on password-reset/complete this code is a business-rule fallback inside the use case — the endpoint itself rejects an over-128 `newPassword` as `validation.failed` before any lookup (§1, §5.6) ([E] E13) |
| `auth.password_same_as_current` | 400 | change-password |
| `auth.reset_code_invalid` | 400 | password-reset/complete ([O] R44) |
| `auth.too_many_attempts` | 429 | login, refresh, change-password, password-reset/complete ([O] R45) |
| `users.not_found` | 404 | users/{id}/* |
| `users.login_identifier_invalid` | 400 | create |
| `users.login_identifier_taken` | 409 | create |
| `users.role_required` | 400 | create |
| `users.role_unknown` | 400 | create (unknown or Deprecated role, [O] R43) |
| `users.party_required` | 400 | create (`Engineer` without `partyId`, [O] R31) |
| `users.party_invalid` | 400 | create (`reason`: `not_found`, `not_active`, `no_active_engineer_role`, [O] R31 · [E] E8) |
| `users.party_already_linked` | 409 | create (Party already linked to a user, [O] R30) |
| `users.already_disabled` | 409 | disable; reset-password and initial-password on a Disabled user ([O] R43) |
| `users.already_active` | 409 | reactivate |
| `users.initial_password_pending` | 409 | reset-password on an account still on its first temporary password ([E] E3) |
| `users.initial_password_not_reissuable` | 409 | initial-password on an account that has chosen a password ([E] E3) |
| `users.reset_delivery_unavailable` | 503 | reset-password when no delivery channel can reach the user ([O] R44 · [E] E1) |
| `users.bootstrap_account_protected` | 409 | reset-password, initial-password, disable, reactivate, and any future role or direct-permission change on the bootstrap admin (§11). Logged to Serilog; not an `AccessDenied` audit event. |
| `server.error` | 500 | any (no internals in the body) |

Basis: codes named in the owner prompts (`auth.invalid_credentials`, `auth.password_change_required`, `users.login_identifier_taken`, `users.role_required`) · everything else [E]. **`users.disabled` is not returned by login** [O] R11 and is not used anywhere in this contract.

---

## 13. Correlation ID (resolves L) [N] spec §26.1 principle 6 · [O] R8

- Header: `X-Correlation-ID`. Optional; a request without it is **never** rejected.
- A client value is **accepted** only if it is 1–64 characters from `A–Z a–z 0–9 - _ .` [E]. Otherwise it is **ignored** (not an error) and the server generates a new one (a UUID).
- The server always returns the ID used in the response header `X-Correlation-ID`, includes it in ProblemDetails as `correlationId`, and writes it to Serilog and to every audit event created by the request.
- Operator commands (§10A, §11) and startup seeding generate their own correlation ID per run and print it, so their audit events and log lines can be tied together.

---

## 14. Audit and logging obligations

Audit events are written **in the same database transaction** as the change; if the audit write fails, the change rolls back. [N] BR-186 · ح-186 · FR-AUTH-007

| Event | Written by | Basis |
|---|---|---|
| `UserCreated` | create, bootstrap | [N] logical model §1.8 |
| `UserDisabled` | disable | [N] §1.8 · [O] C5 |
| `UserReactivated` | reactivate | [N] §1.8 |
| `RoleAssigned` | create (per role), bootstrap | [N] §1.8 · [O] R41 |
| `RoleEnded` | ending a role (no endpoint in this contract yet) | [N] §1.8 · [O] C5 |
| `PermissionGranted` / `PermissionRevoked` | direct grants of any permission **other than** `Payroll.View` (no endpoint in this contract yet) | [N] §1.8 · [O] R41 |
| `PayrollViewGranted` / `PayrollViewRevoked` | bootstrap; future grant endpoint. Emitted **instead of** `PermissionGranted`/`PermissionRevoked` for `Payroll.View`, never both. | [N] §1.8 · BR-235 · [O] R41 |
| `PasswordChanged` | change-password | [O] D6 |
| `PasswordResetIssued` | reset-password; operator `issue-password-reset` | [O] D6, R15, R44 · [E] E1 (replaces v1.4's `PasswordReset`) |
| `PasswordResetCompleted` | password-reset/complete | [O] R44 · [E] E1 |
| `InitialPasswordReissued` | initial-password | [E] E3 |
| `BootstrapPasswordReset` | operator `reset-bootstrap-password` | [O] R32 · [E] name |
| `AccessDenied` | every 403 to an authenticated user (not the web CSRF-header rejection, §6) | [N] §1.8 · UC-002 · [O] R12, R40 |
| `ThrottleCleared` | operator `clear-throttle` (§22 M5) | [O] owner decision 2026-10-19 (Slice 6 acceptance review D1, D4): the 16th code. Records the actor, time and reason of the clear; must not carry the cleared login identifier or its hash; targets `OperatorCommandRun` + the run's UUID (§22 M5 note) |

- **Serilog** records: every login (failed ones with their real reason), every 403 (including the CSRF-header rejection), every 429, every refresh failure, every bootstrap-protection 409, every reset-delivery failure. Never passwords, temporary passwords, reset codes, tokens or hashes. [N] FR-AUTH-002 · UC-001 · audit addendum §9, §12.1 · [O] R11, R12, R40
- Session revocations are recorded in the sessions table (`revoked_at`, `revoke_reason`). No separate audit event is defined for them. [E]

### 14.1 `audit_events` fields — logical model §15.4 reconciled with R40 [E] E12

The logical model (§15.4), the data dictionary and the database design define the audit record; R40 added `actor_type`. Both apply; nothing from §15.4 is dropped.

| Field | Rule |
|---|---|
| `id` | uuid (v7) |
| `occurred_at_utc` | `timestamptz`, **set from the server clock** inside the transaction; never from client input |
| `actor_type` | `User` or `System` [O] R40 |
| `actor_user_id` | uuid, FK → `users` (`RESTRICT`). Set if and only if `actor_type = User` (CHECK) [O] R40 |
| `actor_role_code` | The role **under which the actor acted**, at execution time — not their current role (data dictionary: «الدور المستخدم فعليًا … لا الدور الحالي»). Deterministic rule [E]: the active role through which the actor held the permission the action required (several → the first by ordinal code order); if that permission came only from a direct grant, or the action needs no permission (change own password, complete own reset), the actor's first active role code by the same order; `NULL` if the actor has no active role. Always `NULL` for `System` (CHECK). |
| `action_type` | The event name (table above) |
| `entity_type`, `entity_id` | The target, for example `User` + the user id |
| `business_document_number` | `NULL` for Identity events (no business document) |
| `managed_company_id`, `operation_id`, `site_id` | `NULL` for Identity events |
| `previous_status`, `new_status` | Set for status changes (`UserDisabled`, `UserReactivated`) |
| `reason` | Required for operator commands (§10A, §11; `clear-throttle`, §22 M5); optional otherwise |
| `approved_by_user_id` | `NULL` (no approval step in Identity events) |
| `correlation_id` | **Always set** (§13); `NOT NULL` |
| `causation_id` | Optional: the id of the event that caused this one, when one exists |
| `source` | One of the logical model's values `Web`, `PWA`, `OfflineSync`, `Import`, `SystemJob`. Mapping today: Admin app → `Web`; Employee app → `PWA`; startup seeding and operator commands → `SystemJob`. *`PWA` names the engineer's field client of ADR-004, which ADR-033 replaced by the Employee Flutter app; the value is kept until the logical model is updated (§19 OC-5).* |
| `idempotency_key` / `client_event_id` | `NULL` for Identity events (no offline replay) |
| `ip_address`, `user_agent` | Not stored for Identity events in Sprint 2 (the model says «عند الحاجة فقط»; Serilog has the request details) |
| `schema_version` | Integer, starts at `1`, incremented when the shape of a given event's data changes |

`audit_events` is append-only: the application database role has no `UPDATE`/`DELETE` on it, and a trigger rejects both, so even a privileged mistake cannot rewrite history. [N] NFR-SEC-009 · BR-187

### 14.2 When an audit write fails — fail closed [E] E12

| Situation | Behaviour |
|---|---|
| Any **state change** (create, disable, reactivate, reset issued/completed, initial re-issue, change password, role/permission change, bootstrap seeding, operator commands) | Audit row in the **same transaction**. If it cannot be written, the whole change rolls back and the caller gets 500 `server.error` (the operator command exits non-zero). There is no "change without its record". [N] BR-186 |
| **`AccessDenied`** (no state change to protect) | Written in its own short transaction. If it fails, the request is **still refused** with the original 403 — a denial never turns into an allow — and Serilog records `Audit.WriteFailed` with the correlation ID and the denied action, so the gap is visible and alertable. |
| Events that are Serilog-only by rule (login, 429, CSRF rejection, bootstrap-protection 409) | No audit row exists to fail; a Serilog failure never changes the response. |

---

## 15. Out of scope for this contract

Account **lockout** — hard locking an account after failed attempts is deliberately not adopted, because it would let anyone who knows a login identifier lock its owner out; throttling (§17) is used instead (plan §4 Q5, [O] R45) · endpoints for assigning/ending roles after creation, granting/revoking direct permissions and `Payroll.View` (FR-AUTH-005; deferred — when added, they must reject the bootstrap admin per §11, R27/R28) · linking a user to a party as a separate step, for non-Engineer users (Engineers are linked at creation, §9.1, R31) · the remaining §7.3 permissions (Q6) · **implementations of reset delivery channels** (§10A; contact-data decision §19 OC-2) · user-initiated "forgot password" (a user requesting a reset without an Admin) — not offered until a delivery channel exists; the apps keep it disabled · the exact invocation of the operator commands and their operating procedure (R29, R32; defined with Slice 5 and in operations documentation — they are not HTTP APIs).

---

## 16. Frontend impact (current apps vs this contract)

The apps were built mock-first before this contract existed. The ordered plan for both apps is `docs/implementation/sprints/frontend-auth-contract-alignment-plan.md`. Changes a frontend task must make (not done here):

1. Prefix every path with `/api/v1`.
2. Send `clientApp` on login (`admin` / `employee`); the Admin web build sends `X-Client-Platform: web` and `X-CSRF-Protection: 1` on refresh/logout, and uses `withCredentials` so the cookie flows; it no longer stores a refresh token.
3. Remove every use of `12345678`: the change-password client rule becomes "8–128 characters and different from the current password"; the create dialog shows the `temporaryPassword` (and its expiry) from the response, with a copy action.
4. Drop the distinct "account disabled" login message (`users.disabled`); a disabled login is `auth.invalid_credentials`.
5. Handle the new codes (§12), notably `auth.app_access_denied`, `auth.session_expired`, `auth.current_password_incorrect`, `auth.too_many_attempts`, `auth.reset_code_invalid`, `users.initial_password_pending`, `users.initial_password_not_reissuable`, `users.reset_delivery_unavailable`.
6. `GET /roles` returns `code` only; map codes to Arabic labels via i18n.
7. Never refresh in the background (§3.6).
8. `mustChangePassword` stays in the login response and `/auth/me` (so the existing casts remain valid).
9. Mobile builds send `X-Client-Platform: mobile` (or omit it); only the browser build sends `web`. Neither value affects authorization.
10. Read `isBootstrapAdmin` from the user object and hide "reset password", "re-issue initial password", "disable" and "reactivate" (and any future role editing) for that account; handle `users.bootstrap_account_protected` anyway. The bootstrap admin can still use the normal change-password screen for its own password.
11. After reactivation, nothing is restored: the reactivated user signs in again.
12. **Reset is no longer a password dialog (v1.5, R44).** "Reset password" calls §9.6 and, on 202, shows "a reset code was sent through <channel>, valid until <expiresAt>"; on 503 `users.reset_delivery_unavailable` it explains that resets are done by the system operator for now. The Admin app never displays a reset secret. On any non-2xx it shows the matching error (supersedes v1.4 item F-05).
13. **Store the tokens returned by change-password (F-08).** Today both apps discard them. The response replaces the session's tokens (§5.5, R36): keep the new access token, and on mobile store the new refresh token; continuing with the old ones fails.
14. **Voluntary change-password route (F-09).** Today both routers send a signed-in user away from `/change-password` unless the change is forced. Any signed-in user — notably the bootstrap admin, who is never forced (R26) — must be able to reach it.
15. **Refresh rule (R37).** Refresh once on a 401 from `/auth/me` and `change-password` as for any other call; never on login, refresh, logout or reset completion. If logout fails, clear the local session anyway.
16. **Create Engineer users with `partyId` (R31).** The new-user form must collect the engineer's Party when `Engineer` is selected, and handle `users.party_required`, `users.party_invalid` (with its `reason`) and `users.party_already_linked`.
17. **Refresh within the reuse grace (R35)** may return no new refresh token (§5.2); the client then keeps the one it has.
18. **Reset completion screen in both apps (v1.5).** A public route reachable from the login screen ("I have a reset code"): login identifier, reset code, new password, confirmation; calls §5.6; on 204 returns to login with a success message; maps `auth.reset_code_invalid`, the password rules and 429.
19. **Initial-password re-issue in the Admin app (v1.5).** Offer it only when `credentialState` is `TemporaryPasswordIssued` or `InitialPasswordRequired`; show the returned temporary password once, like creation.
20. **429 handling.** Respect `Retry-After`, disable the submit button until it elapses, show a generic message; never retry automatically.
21. **App audience.** A 403 `auth.app_access_denied` on a normal call means the session belongs to the other app: clear it and return to login with a message. Route guards may hide screens by permission, but the server decides.
22. **Correlation ID.** Send `X-Correlation-ID` (a UUID per user action) on every request and show the server's ID in error messages so support can find the logs.

---

## 17. Brute-force and abuse protection [O] R45 · [E] E5

**Goal.** Make online password, reset-code and token guessing slow and visible, including when the attacker spreads attempts across many addresses, **without** letting an attacker lock a legitimate user out.

**Keys.** `identifier key` = SHA-256 of `lower(submitted loginIdentifier)` — computed whether or not the account exists, so throttling reveals nothing about existence, and raw identifiers (which users sometimes mistype passwords into) are not stored. `client IP` = the connection's remote address, or the `X-Forwarded-For` value **only** when the request came through a configured trusted proxy (`ForwardedHeaders` with explicit `KnownProxies`/`KnownNetworks`); a client-supplied `X-Forwarded-For` is otherwise ignored.

**Limits (configuration `Auth:Throttle:*`; these are the defaults).**

| Layer | Applies to | Limit | When exceeded |
|---|---|---|---|
| 1 · per client IP | all **public** auth endpoints (§4.1) | 30 requests / minute | 429 until the minute window passes |
| 2 · per (identifier key, client IP) | failed login and reset-completion attempts | 5 failures / 15 minutes | 429 for that pair; the delay doubles with each further failure, up to 15 minutes. A success clears this pair's counter. |
| 3 · per identifier key, all IPs | failed login and reset-completion attempts | after 10 failures within 15 minutes | that identifier accepts **at most one attempt per 30 seconds** from any address (429 with `Retry-After`) until 15 minutes pass without a failure. This caps a distributed attack at about 120 guesses per hour per account, while the real user waits at most 30 seconds between tries — slowed, never locked out. |
| 4 · per user | wrong `currentPassword` on change-password | 5 failures / 15 minutes | 429 for that user's change-password; the session stays valid |
| 5 · per client IP | `POST /auth/refresh` | 60 / minute | 429 |

- Throttle checks run **before** any password hashing, so a flood cannot be turned into CPU exhaustion; requests that pass are still subject to the dummy-hash rule (§5.1).
- The response is the same 429 `auth.too_many_attempts` whatever the account's state.
- **Store.** Layers 2–4 are kept in a **shared store** so every API instance sees the same counts: a PostgreSQL table (Slice 2B) holding only `key_hash`, `scope`, `window_start`, `failure_count`, `blocked_until`, with expired rows deleted by a periodic cleanup (bounded size). The ASP.NET Core in-memory rate limiter is used **only** for layers 1 and 5, as a per-instance first line; on its own it would be per process, which is why it is never the only defence for layers 2–4. If the shared store is unavailable, login and reset completion **fail closed** with 503 rather than run unthrottled. *(The store is behind an Application interface, so a distributed cache can replace PostgreSQL later without changing the policy.)*
- **Observability.** Every 429 and every layer-3 activation is logged to Serilog (`Auth.Throttled`, with the layer, the identifier key — never the raw identifier — and the IP); layer-3 activations are logged as warnings so an operator can be alerted to an account under attack.
- **Tests** (Slice 6): each layer's threshold and reset, the doubling delay, that unknown and known identifiers are throttled identically, that a success clears only layer 2, that layer 3 slows but does not block a correct password after the wait, trusted-proxy handling of `X-Forwarded-For`, and fail-closed behaviour when the store is down.

---

## 18. Operational security requirements [E] E14

| Requirement | Detail |
|---|---|
| TLS | TLS 1.2+ everywhere outside local development (§1); certificates managed by the hosting environment. [N] NFR-SEC-008 |
| Secrets | JWT keys (§3.2.1), database credentials, bootstrap configuration: secret store or environment variables, never in the repository, logs or responses. Rotation procedure for JWT keys as §3.2.1. [N] CLAUDE.md |
| Clock | Servers synchronized by NTP; token validation allows at most 30 seconds of skew (§3.2). |
| Database roles | The application connects with a role that is not a superuser and cannot `UPDATE`/`DELETE` `audit_events` (§14.1); migrations run with a separate role. [N] NFR-SEC-009 |
| Trusted proxies | Explicit `KnownProxies`/`KnownNetworks` per environment (§17); never "trust all". |
| CORS | Explicit Admin web origins per environment (§8); `*` never with credentials. |
| Logging | Request/response body logging, if enabled, redacts the fields in §1; Serilog sinks are not publicly readable. |
| Operator commands | Run only on the server, by an operator account, with a `--reason`; their output (a reset code) is shown once on the terminal and must not be redirected to files or tickets. |

---

## 19. Owner confirmations still required

Each item below behaves as written in this contract until the owner confirms or overrides it. None of them blocks Slice 2.

> **Update 2026-10-21.** The R48 direction (temporary password shown to the Admin, forced change) was **superseded the same day by owner decisions R49–R51** (`admin-user-creation-plan.md` §1F): the system generates the password, sends the username and password to the employee's WhatsApp number, shows no password to the Admin and returns none in any ordinary response, and never forces a change. This **decides OC-1** (the temporary-password alternative is replaced by automatic delivery), **answers OC-2 for this purpose** (WhatsApp; the remaining contact-data questions are roadmap OD-21) and **makes OC-3 moot**. §5.6, §7, §9.1, §9.6, §9.8, §10 and §10A stay below as the description of v1.5 and of the code at `1644077` until contract v1.6 (§23) replaces them (roadmap PS-03); each carries a pointer. Still open: roadmap OD-1, OD-2, OD-18 … OD-23, OD-25 … OD-27.

| # | Question | Current behaviour (safest temporary choice) |
|---|---|---|
| OC-1 | **R44 supersedes R3 for resets.** Confirm that an Admin-issued reset must never reveal a usable credential to the Admin, accepting that until a delivery channel exists every reset goes through the operator command (§10A), and the Admin app shows "done by the operator". Alternative the owner may choose instead: keep R3's temporary-password reset for accounts without sensitive permissions. | §9.6 as written: no secret to the Admin; API reset returns 503 until a channel exists. |
| OC-2 | **Delivery channel and contact data.** Which channel (email, SMS, WhatsApp, …) and which stored contact is authoritative for each user? `LoginIdentifier` is opaque (ADR 0003 §3.1) and no user contact field is confirmed; `Party.Phone` exists only for parties. | No channel; operator procedure. |
| OC-3 | **Creation exposure (R2).** Accept that account creation and initial re-issue show a temporary password to the Admin (residual risk, §10A), or require the same channel-based delivery for new accounts once a channel exists. | R2 kept: shown once to the Admin; expires after 72 hours (E4). |
| OC-4 | **Party validity for Engineer accounts.** Confirm: only `Active` parties; party-role code literal `Engineer` for «مهندس»; the business date in `Africa/Cairo`. The party-role code vocabulary is not defined in the authoritative documents. | §9.1 as written. |
| OC-5 | **Upstream documentation defects** (protected documents, not edited here): (a) spec UC-002 lists `UserDeactivated` / `RoleChanged`, while logical model §1.8 lists `UserDisabled` / `RoleEnded` — the index ranks the spec (15) above the logical model (16), so C5's choice of the logical-model names stands only as an owner decision; (b) logical model §15.4 `source` still names `PWA` after ADR-033; (c) `documentation-index-ar.md` does not list `docs/decisions/**` or `docs/implementation/**` (their order is given in `docs/implementation/README.md`). | Logical-model names and values used; documented here. |
| OC-6 | ~~**`actor_role_code` for users with several roles.** Confirm the deterministic rule in §14.1, which the data dictionary does not spell out.~~ **Confirmed by the owner (2026-10-18), exactly as drafted in §14.1**: the active role through which the actor held the permission the action required (several → first by ordinal code order); if that permission came only from a direct grant, or the action needs no permission, the actor's first active role code by the same order; `NULL` if the actor has no active role. Applied consistently across every existing identity audit-writing path (`ActorRoleCodeRule`): `AccessDenied`, `PasswordChanged`, `PasswordResetCompleted`, `UserCreated`, `RoleAssigned`, `UserDisabled`, `UserReactivated`, `PasswordResetIssued`, `InitialPasswordReissued` — each resolved against the permission that action actually requires (`Users.Manage` for the five users-endpoint use cases; none, for the two self-service ones). No remaining Application-layer use case leaves it unconditionally `NULL`; `NULL` now only occurs when the rule itself resolves to it (no active role). | §14.1 rule, now confirmed and applied — not a temporary choice. |
| OC-7 | **Finding the Party for an Engineer account.** §9.1 requires `partyId`, but no endpoint lets the Admin app search Parties, and the permission that should guard reading Parties (`Parties.View` or similar, spec §7.3) is not seeded (Q6). Decide the permission (and its role grant) so a read-only Party lookup (`GET /api/v1/parties?role=Engineer&status=Active&search=…`, audience `admin`) can be added. | No lookup: until decided, an Engineer account can only be created with a known Party id; the Admin app's Engineer form is blocked on this (frontend plan FA-4). |
| OC-8 | **Layer 3 starvation by a distributed attacker** (added 2026-10-15). With the §22 proposals, a single source cannot monopolize a user's sign-in attempts, but about 30 or more coordinated sources can keep taking every 30-second slot (§22 H). Accept this as a documented limitation (with operator alerting and an operator unblock command), or require trusted-device protection (§22 M3: a long-lived device credential, a new table, changes to both apps). Relates to owner instruction R45. | Undecided; the throttle policy (§17, §22) is not finalized until this is reviewed. Does not block Slice 2B. |
| OC-9 | **Retention of session, refresh-token and reset-ticket history** (added 2026-10-15). Session rows are the only record of revocations (§14). | Session, refresh-token and reset-ticket history is kept; Slice 2B deletes only expired throttle rows (ADR 0005 E24). *Clarified 2026-10-19:* throttle counters are not history — §17 and §22 D also clear a counter on success, and `clear-throttle` (§22 M5) clears one identifier's layer 3 row. |

---

## 20. Implementation map

| Contract area | Slice (`sprint-02.md`) | Status 2026-10-15 |
|---|---|---|
| Role/permission catalog and grants (§2) | 1 — US-BE-006 | ✅ Implemented (`1e10742`, tests fixed in `bc63d5f`) |
| `users` credential and security state, bootstrap flag/index/trigger, one account per Party, case-insensitive lookup (§3.5, §5.1, §9.1, §11) | 2 — US-BE-007 | See `sprint-02.md` §A for the current state |
| Sessions, refresh tokens, reset tickets, throttle store (§3.3, §10A, §17) | 2B — US-BE-008 | Persistence implemented (ADR 0005; see `sprint-02.md` §A). Throttle policy not implemented (§22, pending review) |
| `audit_events` (§14) | 3 — US-BE-009 | Planned |
| Use cases: create, disable, reactivate, issue/complete reset, initial re-issue, change password (§5.5, §5.6, §9) | 4 — US-BE-010 | Planned |
| Bootstrap seeder and operator commands (§10A, §11) | 5 — US-BE-011 | Planned |
| Auth endpoints, JWT and key ring, app audience, throttling, CSRF, CORS, HTTPS, correlation ID, ProblemDetails (§3–§8, §12, §13, §17, §18) | 6 — US-BE-012 | Planned |
| Users endpoints (§9) | 7 — US-BE-013 | Planned |
| Both Flutter apps (§16) | Frontend alignment plan, phases FA-1 … FA-6 | Planned; starts after the matching backend endpoints exist |

---

## 21. Revision history

| Date | Change |
|---|---|
| 2026-09-27 | v1. Written from owner decisions C1–C5, R1–R19 and the reconciliation report. Engineering details A–L resolved as [E] proposals. |
| 2026-09-27 | v1.1. Owner clarifications R20–R25: bootstrap admin protected from reset and identified by `is_bootstrap_admin` (§9.6, §11); idle timeout redefined as a refresh-based inactivity policy (§3.6); `SameSite=Strict` tied to the same-site deployment model (§3.4); `X-Client-Platform` is `web`/`mobile`, a transport hint only (§5.1); app-access and disabled-login behaviour confirmed (§4); reactivation restores no session (§9.5). |
| 2026-10-01 | v1.2. Owner decisions R26–R27: the bootstrap admin may change its own password (§5.5, §11); it is protected from reset, disable, reactivate and role changes, enforced server-side (§9.4–§9.6, §11, §12, §16). |
| 2026-10-01 | v1.3. Owner decisions R28–R29: bootstrap admin protected from direct-permission grants/revokes, including its `Payroll.View` (§11, §12, §15); no API recovery path for a lost bootstrap password, recovery is out-of-band and an operational procedure must exist (§11). Confirmed as owner decisions: reactivate on the bootstrap admin → 409, and protection rejections are 409 + Serilog, not `AccessDenied`. |
| 2026-10-01 | v1.4. Owner decisions R30–R43 from the final contract audit: one account per Party and `partyId` required for Engineers (§9.1, §12); bootstrap break-glass — second Admin and operator-only CLI (§9.6, §11, §15); password hash and transparent rehash, dummy hash on login (§5.1); 30-second refresh reuse grace (§3.3, §5.2); change-password consumes and reissues the refresh token (§3.3, §5.5); refresh rule limited to login/refresh/logout (§6); `client_platform` stored on the session (§3.3, §5.1); users-endpoint check order (§9, §11); `actor_type`/nullable `actor_user_id`, CSRF rejection Serilog-only (§6, §8, §14); bootstrap audit events and `PayrollViewGranted` instead of `PermissionGranted` (§11, §14); protection checks the target (§11); duplicate/deprecated role and disabled-reset rules (§9.1, §9.6, §12). Header corrected; revision history put in date order; frontend items F-05, F-08, F-09 and the new R-driven items added to §16. |
| 2026-10-15 | v1.5. Resolves the 2026-10-15 documentation-consistency audit under the owner's instructions R44–R45 and engineering decisions E1–E16 (plan §1D). **Password reset redesigned** so the Admin never learns a usable credential: issue + single-use code + user completion (§5.6, §9.6, §10A), with an interim operator procedure and fail-closed 503 while no delivery channel exists; `PasswordReset` event split into `PasswordResetIssued`/`PasswordResetCompleted`. **Temporary passwords** limited to creation and a new initial re-issue (§9.8), with a 72-hour expiry and a class guarantee (§10); `credentialState` added to the user object (§9.2). **Brute-force protection** (§17) without hard lockout; 429 `auth.too_many_attempts`. **App audience** enforced on every request (§4.1) and added to the §9 check order. **Audit** fields reconciled with logical model §15.4 and fail-closed rules defined (§14.1, §14.2). **Login identifier** matching made explicit (§5.1). **Party validity** for Engineer accounts defined (§9.1). **Security version** initial value and increments defined (§3.5). JWT validation, key ring and rotation (§3.2, §3.2.1); input limits, HTTPS, password maximum length (§1, §5.5); configurable idle timeouts (§3.1); operational requirements (§18); owner confirmations (§19); implementation map (§20). Status set to Draft v1.5 pending §19; v1.4 is not retroactively approved. |
| 2026-10-15 | §22 added: **proposed** v1.6 amendments for Slice 2B persistence (F, G) and for the throttle policy (A–E, H, mitigation M1–M5), and §19 OC-8, OC-9. Pending review; §1–§21 are unchanged and remain the v1.5 Draft text. |
| 2026-10-16 | §22 I added: **proposed** v1.6 amendments I1–I20 for the Slice 3 audit trail (§13, §14, §14.1, §18, §19), designed in repository ADR 0006 (proposed, pending owner review; not business ADR-006). Records owner decisions **R46** (audit source `EmployeeApp`; events `UserDisabled` and `RoleEnded`) and **R47** (no production use with real business or user data before the Audit Completion slice). Every other §22 I item is pending review. The contract stays **Draft v1.5**; §1–§20 are unchanged, and this row is the only change to §21. |
| 2026-10-18 | §19 OC-6 confirmed by owner decision: the §14.1 `actor_role_code` rule, exactly as drafted, with no change to its wording. Applied consistently to every existing identity audit-writing path, not only the two first updated. This decision is narrower than ADR 0006/§22 I's still-pending status: it confirms only the rule itself, not ADR 0006 as a whole, not the rest of §22, and not the throttle policy. §1–§18, §20 are unchanged; §22's own "proposed, not applied, not approved policy" status is unchanged. |
| 2026-10-19 | Owner decisions from the Slice 6 acceptance review (D1, D4): `ThrottleCleared` is added to the §14 event table as the 16th audit code, written by operator `clear-throttle` (§22 M5); its audit event, the command's output and its Serilog messages must not carry the cleared login identifier or its unsalted hash. §14.1 `reason` now names `clear-throttle` among the operator commands that require one. §22 I3's count and the §22 M5 implementation note updated to match (the M5 note records the target `OperatorCommandRun` + the run's UUID, a dated deviation from §14.1/ADR E33/E36, with no schema change). §19 OC-9's current-behaviour cell clarified to ADR 0005 E24's own scope; OC-9 itself stays undecided. No owner-decision number was assigned to these decisions. ADR 0006 and §22 stay **proposed**; OC-8 stays undecided; the contract stays **Draft v1.5**. |
| 2026-10-21 | Note only (no change to §1–§18, §20, §22): owner decision R48 (temporary-password reset shown to the Admin, no delivery channel) overrides OC-1 and supersedes §9.6/§10A; recorded in the §19 preface and `admin-user-creation-plan.md` §1E. The rewrite is scheduled as contract v1.6 in roadmap PS-03. The code still follows v1.5. (R48 itself was superseded later the same day by R49–R51; see the next row.) |
| 2026-10-21 | §23 written: **derived** v1.6 amendments from owner decisions R49–R51 (system-generated password delivered by WhatsApp, no forced change, English-only usernames, delivery status, webhook, error codes, audit, frontend impact); an earlier same-day draft of §23 based on an employee-chosen-password activation link was **withdrawn** and removed. Pointers added to §5.6, §7, §9.1, §9.6, §9.8, §10, §10A and the §19 note. §1–§22 otherwise unchanged; nothing is implemented; the code still follows v1.5. |

---

## 22. Proposed v1.6 amendments — pending review (2026-10-15 · 2026-10-16)

> **Status: proposed, not applied, not approved policy.** Nothing in this section changes §1–§21. F and G describe how Slice 2B stores data (engineering decisions E20–E22, ADR 0005) and are approved for implementation as persistence design; their wording here becomes part of the contract only when the contract revision is accepted. A–E, H and M1–M5 are **proposed changes to the brute-force policy (§17)**; they are not implemented in Slice 2B and are not final until reviewed together with OC-8. **I (2026-10-16)** is the proposed audit wording for Slice 3 (§13, §14, §18, §19), designed in repository ADR 0006 (`docs/decisions/0006-audit-events-persistence.md`, also proposed); only its owner decisions R46 and R47 (2026-10-16) and the unnumbered owner decisions of 2026-10-19 on `ThrottleCleared` (§21; the E33/E36 target deviation recorded with them stays open) are decided. §19 OC-6, a separate owner confirmation, covers the §14.1 `actor_role_code` rule only. Classification: *clarification* = makes existing text precise without changing its intent; *policy change* = changes what the text permits.

T = threshold, W = window (15 minutes by default). "Max" = most attempts possible against one account.

### A. Window type (§17) — policy change

- **Current:** "5 failures / 15 minutes" (layers 2, 4); "after 10 failures within 15 minutes" (layer 3). The window type is not stated; "within" reads as sliding.
- **Proposed:** an **anchored fixed window**: it starts at the first counted attempt after a reset and ends W later.
- **Consequences:** implementable with the five stored columns. At a window boundary up to 2(T−1) failures can fall inside one 15-minute span before the limit applies; long-run rates are unchanged. An exact sliding window would need the last T attempt times per key (a sixth column or an events table).
- **Max before the limit applies (sliding → anchored):** layer 2: 5 → 8 · layer 3: 10 → 18 before slow mode · layer 4: 5 → 8.

### B. Layer 2 escalation (§17) — clarification with a material effect

- **Current:** 5 failures / 15 minutes → 429 for the pair; "the delay doubles with each further failure, up to 15 minutes"; a success clears the counter. No base delay and no end of escalation are stated.
- **Proposed:** base delay **30 s** when the 5th failure is reached; delays 30, 60, 120, 240, 480 s, then capped at 900 s. Escalation ends after **15 minutes without a failure**, or on a success.
- **Consequences:** without a quiet-period end, the counter resets at the fixed-window boundary before the 900-second cap is ever reached (around t = 930 s), so one source gets about 35 attempts per hour against an account. With B: **12 in the first hour, then 4 per hour**. A real user who mistypes 5 times waits 30 s; 10 consecutive mistakes give 15-minute waits on that network only, and one success clears them. B is also what limits one source to one admitted attempt per 900 s, which H relies on.

### C. Layer 3 slot (§17) — clarification

- **Current:** "accepts at most one attempt per 30 seconds from any address until 15 minutes pass without a failure."
- **Proposed:** the same, enforced at admission (before the password is verified): every admitted attempt, including a successful one, takes the slot; a success does not end slow mode (as the §17 test list already says).
- **Consequences:** at most **120 attempts per hour per account** in slow mode, the real user included. Without enforcement at admission, parallel attempts would all pass.

### D. Counting at admission (§17) — clarification (one minor behavioural effect)

- **Current:** limits are stated in failures; "throttle checks run before any password hashing"; concurrency is not addressed.
- **Proposed:** an attempt is counted when it is admitted. A success removes its own count (layers 3, 4) or clears the counter (layer 2). An attempt whose completion is never recorded (crash) counts as a failure until its window ends.
- **Consequences:** thresholds hold exactly under any concurrency (max exactly T per window). Without D, a parallel burst is bounded only by layer 1, and on change-password (no layer 1) not at all. A real user's in-flight attempt counts for milliseconds; a server crash costs one count.

### E. Source grouping (§17) — policy change

- **Current:** "client IP" is not defined further.
- **Proposed:** one source = one IPv4 address, or one IPv6 **/64**, determined after the trusted-proxy rules of §17.
- **Consequences:** without it, one IPv6 host can rotate through 2^64 addresses and bypass layers 1, 2 and 5. Users sharing a /64 with an attacker (same LAN) share that source's limits.

### F. Sessions and refresh tokens (§3.3) — clarification (implemented in Slice 2B)

`refresh_tokens` has an `id`; at most one unconsumed refresh token per session (checked at commit); rotation consumes the old token, inserts the new one and updates `last_refreshed_at` in one transaction; a concurrent rotation of the same token loses and changes nothing. Revoke reasons: `logout`, `idle`, `refresh_reuse`, `app_access_lost`, `user_disabled`, `password_reset_issued`, `password_reset_completed`, `initial_password_reissued`, `password_changed`; the first revocation is kept. Absolute expiry is derived from `absolute_expires_at` and is not written as a revocation.

### G. Reset tickets (§9.6, §10A) — clarification (implemented in Slice 2B)

*Open* = not consumed and not superseded; at most one open ticket per user. Issuing supersedes every open ticket (expired ones included) and inserts the new one in one transaction. A ticket can be consumed only while open and before `expires_at` (also enforced by the database). A wrong, expired, consumed or superseded code writes nothing to the ticket. Tickets are kept after expiry (`credentialState = ResetRequired`).

### H. Correction of the Layer 3 claim (§17) — correction

§17 says the real user "waits at most 30 seconds between tries". That holds only while the attacker cannot take every slot. With A–E: each admitted attacker attempt is a failure that escalates the attacker's own layer 2 counter, and a source rejected by layer 2 cannot take a layer 3 slot (admission is all-or-nothing), so in the long run **one source gets at most one slot per 900 s of the 30 available**. Taking every slot needs about **30 or more coordinated sources**; such an attacker can keep the real user waiting indefinitely (OC-8).

### Layer 3 mitigation options (for Slice 6, pending review with OC-8)

| Option | Effect | Trade-off | Schema |
|---|---|---|---|
| **M1** — E (source grouping) + layer 2 checked in the same admission + B + rejected attempts never take a slot | No single source can monopolize | Does not stop ≈30+ coordinated sources | None |
| M2 — k > 1 slots per period, at most one per source | A few sources cannot starve the user | Raises the guess cap to k × 120/hour; still starvable by ≈30k sources. Not recommended. | None (new scope rows) |
| **M3** — trusted devices: a device that signed in successfully before gets its own per-device limit instead of the shared slot | Known devices cannot be starved by distributed attackers | A new long-lived device credential, retention and privacy rules, changes to both apps; a first sign-in from a new device can still be starved | New `trusted_devices` table (Slice 6) |
| M4 — CAPTCHA or proof-of-work in slow mode | Raises the attacker's cost per attempt | Third-party dependency or battery cost on field devices; solvable by bots. Not recommended. | Possibly a replay cache |
| **M5** — operator command to clear an identifier's throttle (with reason, audited) + Serilog alert when slow mode starts | Incident response | Manual | None |

Proposed: M1 and M5 required in Slice 6; M3 is the owner decision OC-8.

**Implementation note (2026-10-19; does not change this section's Proposed status or OC-8).** M1 and M5 are now implemented in code, pending the same owner review as the rest of this section:
- **M1 (source grouping):** `Contractra.Api/Http/ClientSource.cs`'s `ThrottleGroupOf` groups one IPv6 /64 (computed from address bytes via `IPNetwork`, never string truncation) or one IPv4 address, applied only to the shared throttle's layers 2-3 (`AuthEndpoints.cs`'s login and reset-completion call sites) — layer 1/5's in-memory limiter is unaffected, per §17's own "never the only defence" framing of that layer. B, D and the atomic layer-2/3 admission (rejected attempts never take a slot) were already implemented; E was the missing piece this closes.
- **M5 (audited operator clear):** operator command `clear-throttle --identifier <text> --reason <text>` (`OperatorCommandRunner`, `ClearThrottleUseCase`) clears only layer 3's identifier-wide slow-mode state for the named identifier (hashed, never the raw identifier, per §17); it does not clear layer 2's per-source pairs, which are not enumerable by identifier alone. Audits `ThrottleCleared` (the 16th audit action, §14 table; migration `AddThrottleClearedAuditAction` widens the action-type CHECK — adding an event follows repository ADR 0006 E27, "a module that adds events replaces the CHECK in its own migration"; §22 I3's "a later rename adds a value" concerns renames). **Data minimization (owner decision 2026-10-19):** the event records the actor, time and reason; the command's output and its Serilog messages carry neither the identifier nor its key hash (only the correlation id). **Target (dated implementation note, 2026-10-19):** `entity_type = "OperatorCommandRun"`, `entity_id` = the command run's UUID — the per-run GUID the operator-command runner generates once per run (`OperatorCommandRunner.cs`), which is also the run's correlation id and part of the idempotency key `ThrottleCleared:command:<run id>`. No separate run record or id exists; none was invented. The event therefore identifies the operation that made the change, not the cleared identifier. This deviates from §14.1's "the target" and from ADR 0006 E36 (Identity events target the aggregate root) and E33 (operation identifiers belong in `causation_id`), and is necessary because `entity_id` is NOT NULL, the throttle row has no identity other than the hash, and the approved data-minimization decision forbids storing the hash. No schema change and no nullable `entity_id`: this supersedes the nullable-`entity_id` option the owner chose earlier the same day, which is not implemented. **Updated 2026-10-19:** the delete and the audit write now run in one transaction (`IThrottleIdentifierEraser`, ADR 0006 §6; see sprint-02.md §A for the design), so a failed audit write rolls the clear back too — a failure always means nothing changed, with no partial-success case to report. The Serilog slow-mode-activation warning was already implemented and is unchanged.

### Proposed limits after A–E and M1 (pending review)

| Attacker | Max against one account |
|---|---|
| One source (login and reset completion share counters) | 9 in the first 15 minutes, 12 in the first hour, then 4 per hour |
| Many sources | 18 before slow mode, then 120 per hour (the real user included) |
| Change-password, per user | 5 per window, at most 8 in any 15-minute span, 20 per hour |

### I. Audit events (§13, §14, §14.1, §18, §19) — proposed 2026-10-16 for Slice 3

> Design and rationale: repository ADR 0006 (proposed; not to be confused with business ADR-006 in `docs/architecture-decision-records-ar.md`). Owner decisions **R46** (vocabulary) and **R47** (production-data gate) of 2026-10-16, and the unnumbered owner decisions of 2026-10-19 on `ThrottleCleared` (recorded in §21 and reflected in I3; the E33/E36 target deviation stays open), are decided; every other item below is a proposal pending review (§19 OC-6 separately confirms the §14.1 `actor_role_code` rule that I5 relies on). Until accepted, §13–§19 remain the v1.5 text — **except that R46 and R47, as owner decisions, already govern the implementation where the v1.5 text differs** (`docs/implementation/README.md` §1 ranks owner decisions above this contract). The protected authoritative documents that still use the older terms are pending correction by their owner (ADR 0006 §3, §10); R46 is recorded there as an explicit owner-directed exception, not as a change to them.

| # | Section | Current (v1.5) | Proposed | Classification · basis |
|---|---|---|---|---|
| I1 | §14.1 `source` | Values `Web`, `PWA`, `OfflineSync`, `Import`, `SystemJob`; Employee app → `PWA` | Values `Web`, **`EmployeeApp`**, `OfflineSync`, `Import`, `SystemJob`. Admin app → `Web`; Employee app (any platform) → `EmployeeApp`; startup seeding and operator commands → `SystemJob`. A `System` actor implies `SystemJob` (one-way). | Policy change · **owner decision R46** (resolves the naming business ADR-033 deferred to the audit design). The authoritative documents still say `PWA`; correction proposed in ADR 0006 §10 U1. |
| I2 | §14 event table | `UserDisabled`, `RoleEnded` [O] C5 | Unchanged names, now confirmed: **`UserDisabled`**, **`RoleEnded`** | Clarification · **owner decision R46** (confirms C5 against spec UC-002 `UserDeactivated`/`RoleChanged`). Spec correction proposed in ADR 0006 §10 U2; closes OC-5(a) for the contract. |
| I3 | §14 catalog | 15 events in the table (16 since 2026-10-19: `ThrottleCleared`, owner decision) | The stored catalog is exactly the codes of that table — 15 when proposed, 16 since 2026-10-19 — enforced by a database CHECK. `PasswordReset` (removed in v1.5) and `LoginFailed` (Serilog only) are not audit codes. Codes are permanent once written: a later rename adds a value and keeps the old one valid. | Clarification · ADR 0006 E27 |
| I4 | §14.1 `occurred_at_utc` | "set from the server clock inside the transaction" | "Server" = the **application server's** clock in UTC (NTP, §18), read once per use case and shared with the business change it records; never client input; no database default. | Clarification · ADR 0006 E26 |
| I5 | §14.1 `actor_*` | As written | Unchanged. Added: `actor_role_code` for a `User` is NULL only when the actor holds no active role. No system user and no system role exist. | Clarification · ADR 0006 E28; data-dictionary correction ADR 0006 §10 U3 |
| I6 | §14.1 `entity_type`, `entity_id` | "The target, for example `User` + the user id" | Identity events target `User` + the user id. Which role, permission or reset ticket an event concerns is recorded in the event's snapshot (I7). `entity_id` is required. The `AccessDenied` target is decided in Slice 6. | Clarification · ADR 0006 E36 |
| I7 | §14.1 (new) `snapshot` | Not mentioned | `audit_events.snapshot` — the logical model's optional snapshot field for specific events (§15.5) — records **context**: the image of the affected child record after the action, from an approved key list per action type (`schema_version` 1): `RoleAssigned`, `RoleEnded` → `user_role_id`, `role_id`, `role_code`, `assigned_at`, `ended_at`; `PermissionGranted`, `PermissionRevoked`, `PayrollViewGranted`, `PayrollViewRevoked` → `user_permission_id`, `permission_id`, `permission_code`, `granted_at`, `revoked_at`; `PasswordResetIssued`, `PasswordResetCompleted` → `password_reset_ticket_id`, `issued_by_user_id`, `channel`, `issued_at`, `expires_at`, `consumed_at`. Values are captured at the time of the event; the ids are references, not a substitute for the captured values. Passwords, temporary passwords, password hashes, reset codes, tokens, token hashes and login identifiers are never captured. `audit_event_changes` holds only genuine before/after field values (logical model §15.3) and is created by the Audit Completion slice, not by Slice 3. | Addition · ADR 0006 E37, §5.2 |
| I8 | §14.1 `idempotency_key` / `client_event_id` | Both named; `NULL` for Identity events | One column, `idempotency_key`; no `client_event_id` column (a client event ID is stored as the key). Global partial unique index `WHERE idempotency_key IS NOT NULL` (logical model §15.7). Key `<action_type>:<operation key>`, composed from the event's own action type so different action types never collide, plus a mandatory `:<stable sub-identifier>` when one operation produces several events of the same action type (logical model patch v1.2.1 §6); the database checks that the key starts with the row's action type and a colon. **Business-operation idempotency** (the key of the business record or operation, ح-103, business ADR-018) stays the defence against repeats; the audit key is the backstop against a second audit row (ح-188). **Required** for every event whose source is `OfflineSync`, `Import` or `SystemJob` (ح-188; integrity rule PRV-08 and layer 4: records from sync, import or a job), including the Sprint 2 seeder (`UserCreated:bootstrap:<user id>`, `RoleAssigned:bootstrap:<user id>`, `PayrollViewGranted:bootstrap:<user id>`) and operator commands (`PasswordResetIssued:command:<run correlation id>`). Online events carry the key whenever their business operation has one (business ADR-018 covers request resend for inputs with a financial effect). The Sprint 2 Identity endpoints carry none because no source requires one for them — not as a general exemption for online requests; their repeats are refused by state checks or are new, separately audited operations. A duplicate fails the whole transaction; the audit insert never uses `ON CONFLICT DO NOTHING`. | Clarification · ADR 0006 E30, §5.3; logical-model correction ADR 0006 §10 U5 |
| I9 | §14.1 `schema_version` | Integer, starts at `1` | `integer NOT NULL`, ≥ 1, per action type; 1 for every Sprint 2 event; no default. | Clarification · ADR 0006 E29; data-dictionary correction U4 |
| I10 | §14.1 `managed_company_id`, `operation_id`, `site_id` | `NULL` for Identity events | Unchanged, and enforced: a temporary CHECK keeps all three NULL until the migration that adds their foreign keys. The referenced target of `managed_company_id` is undefined. | Clarification · ADR 0006 E31, §11 |
| I11 | §14.1 `approved_by_user_id` | `NULL` | FK → `users` `RESTRICT`, kept NULL by a temporary CHECK until its meaning is defined by the first module with an approval step. | Clarification · ADR 0006 E32, §11 |
| I12 | §14.1 `causation_id` | "the id of the event that caused this one" | The opaque identifier of the **operation** that caused this one (an import batch, job run or command), same format as `correlation_id`. Causality between two audit events is an `audit_event_links` row of type `CAUSED_BY` (Audit Completion). Not written in Sprint 2. | Correction · ADR 0006 E33 |
| I13 | §14.1 `reason` | Required for operator commands; optional otherwise | 1–1000 characters, no leading or trailing whitespace. Required for operator-command events (§10A `--reason`, §18), as v1.5 says. Seeder events carry a fixed descriptive reason (US-BE-011; permitted by "optional otherwise"). No rule for `System` events in general: future scheduled jobs are decided by their module. Corrections require a reason (integrity rule IMM-03). Never contains a secret. | Clarification · ADR 0006 E34 |
| I14 | §14.1 `previous_status`, `new_status` | Set for status changes | Both set or both NULL, different from each other; values are the `users.status` codes `Active`, `Disabled`. | Clarification · ADR 0006 E35 |
| I15 | §14.1 `ip_address`, `user_agent` | Listed; "not stored for Identity events in Sprint 2" | **Not columns** in the initial schema; added only with an authoritative need and a privacy justification. | Policy change · ADR 0006 E38 |
| I16 | §13, §14.1 `correlation_id` | 1–64 characters `A–Z a–z 0–9 - _ .`; invalid client value ignored and a UUID generated | Unchanged, and also enforced by a database CHECK. The Domain validates and never generates; generation is at the HTTP boundary (Slice 6) and per run in the seeder and operator commands (Slice 5). A replaced client value is logged by length and hash, never raw. | Clarification · ADR 0006 E33 |
| I17 | §14.1 append-only | "no `UPDATE`/`DELETE` on it, and a trigger rejects both" | Applies to `audit_events` (Slice 3) and to every audit table the Audit Completion slice adds: statement-level triggers reject `UPDATE`, `DELETE` **and `TRUNCATE`** (SQLSTATE 42501); the migration revokes `UPDATE`, `DELETE`, `TRUNCATE` and `TRIGGER` from non-owner grantees at creation; the application role holds only `SELECT` and `INSERT`. Rolling back the migration refuses to drop a non-empty audit table. | Addition (TRUNCATE) · ADR 0006 E39, E40 |
| I18 | §18 Database roles | Application role cannot `UPDATE`/`DELETE` `audit_events`; migrations run with a separate role | Added: the application never runs migrations at startup; the application role does not own the audit tables; a startup privilege self-check stops the application in Production if it holds `UPDATE`, `DELETE`, `TRUNCATE` or `TRIGGER` on an audit table (the four privileges the migration revokes), owns one, or is a superuser (warning only in other environments). Role names and provisioning are environment-specific deployment documentation. | Addition · ADR 0006 E42, E43, §7 |
| I19 | §14 (new) Scope and gates | — | Slice 3 provides the explicit business-event level only; it does **not** by itself satisfy the generic data-change level of ح-185 / FR-AUD-003. **Gate A:** no migration introducing a financial table or financial module is merged before the Audit Completion slice is implemented, tested, reviewed and approved (ح-185, FR-AUD-003, FR-AUD-005, IMM-02). **Gate B (owner decision R47):** no production deployment manages real business or user data before Audit Completion has passed its acceptance criteria. Those criteria include retention (ح-190): no delete or archive path for audit rows; `audit_retention_policies` (logical model §15.7) with mandatory approver and date; an event type without an approved policy is retained indefinitely; retention durations (OQ-001) stay a decision of the manager and the accountant. Development and automated testing continue normally. | Addition · ADR 0006 §8 · R47 |
| I20 | §19 OC-5 | (a) UC-002 vs §1.8 names; (b) `PWA` | (a) and (b) are decided for the implementation by R46; the protected documents still need the corrections in ADR 0006 §10 (U1, U2). (c) is unchanged. | Clarification · R46 |

---

## 23. v1.6 amendments derived from owner decisions R49–R51 (2026-10-21) — WhatsApp-delivered passwords and English-only usernames

> **Status: derived, not yet applied.** The owner confirmed R49, R50 and R51 on 2026-10-21 (`admin-user-creation-plan.md` §1F). This section turns them into contract shapes so the sprints can be written against them. **§1–§22 above are unchanged and still describe the code at `1644077`** (temporary password returned to the Admin, forced change, reset-code machinery). Where §1–§22 conflict with this section, **this section wins for the planned v1.6** and the conflicting v1.5 text is superseded when v1.6 is issued (PS-03, roadmap PS3-X-05). The shapes below have not been reviewed by the owner as API text, and the username details (§23.5) remain Proposed. **No endpoint, state or code below exists yet.** An earlier proposal in this section (an employee-chosen password through a single-use activation link) was **withdrawn** on the same day and is not part of the design. **Corrected 2026-10-22:** the earlier text of this section generated a new password on every delivery attempt; that is withdrawn (a delayed message could carry a password that a later retry had already replaced). One password is generated per operation, its hash is stored with the operation, and retries send the same password from a short-lived encrypted copy (plan §1F.4) — recommended, with production key custody still awaiting the owner (OD-30).

### 23.1 What each v1.5 section becomes

| v1.5 text | Treatment in v1.6 |
|---|---|
| §5.5 `change-password` | **Unchanged.** Voluntary; current password required; 8–128 characters; must differ. The only way a user changes their own password. |
| §5.6 `POST /auth/password-reset/complete`, §10A reset code and threat model | **Not used by any flow.** Removed from the v1.6 API surface per roadmap OD-2 (recommended); code and tables stay dormant; not deleted without an explicit decision. The public endpoint, its throttle layer entry and `auth.reset_code_invalid` are dormant. |
| §7 password-change-required gate (403 `auth.password_change_required`) | **Dormant.** `must_change_password` is never set by creation or reset (R49/R50). The mechanism and its tests stay; no flow triggers it. Apps may keep generic handling until the gate is retired (future decision). |
| §9.1 create: `temporaryPassword` and `temporaryPasswordExpiresAt` in the 201 body, `mustChangePassword = true` | **Replaced** by §23.2: no password in the response, no forced change. |
| §9.6 `reset-password` (202/503, reset ticket, `users.reset_delivery_unavailable`) | **Replaced** by §23.3. |
| §9.8 `initial-password` | **Retired.** It returns a password to the Admin (forbidden by R51). Re-issue is `reset-password`. |
| §10 temporary password (12 characters, mixed classes, 72 h expiry, returned once) | **Re-scoped:** the *generation* rules stay (CSPRNG, 12 characters, upper+lower+digit by rejection sampling, hash only, never logged); the password is *not returned*, is *not temporary* and has **no expiry** (an optional expiry is OD-25). |
| §9.2 `credentialState` | Becomes `PasswordSet`, `DeliveryPending`, `DeliveryFailed` (§23.1a). The v1.5 values `TemporaryPasswordIssued`, `InitialPasswordRequired`, `ResetPending`, `ResetRequired` are no longer produced for accounts created or reset under v1.6. |
| §9.4 disable, §9.5 reactivate, §11 bootstrap protection | Unchanged; disabling an account cancels its open issuance. |
| §12 error codes | See §23.7. |
| §14.1 audit | See §23.8. |
| §16 items 12 and 18 (Admin reset / reset-completion screen), the 12345678 and default-password items | Restated in §23.10. |
| §19 OC-1, OC-2, OC-3 | Decided (R50), narrowed to OD-21, moot. |

**§23.1a Credential state.** Derived from the account's **latest issuance**, not from whether the hash is usable (under one-password-per-operation the hash is valid from the moment of the operation, before the employee knows the password): latest issuance open → `DeliveryPending`; latest issuance ended without delivery (definitive rejection, consent withdrawn, `key_unavailable`, `expired_before_delivery`) → `DeliveryFailed`; otherwise (delivered, superseded by a later successful one, or no issuance) → `PasswordSet`. A failure does **not** revert the stored hash: the undelivered password is unguessable and is replaced at the next re-issue. The delivery status is a separate field. Any non-production account created under v1.5 that still has `must_change_password = true` keeps the dormant gate until a decision clears it; no data change is made without the owner.

### 23.2 `POST /api/v1/users` — create (v1.6)

Audience `admin`, `Users.Manage`, check order as §9. Request:
```json
{
  "loginIdentifier": "ahmed.mohamed",
  "roleCodes": ["Engineer"],
  "partyId": "0193…",
  "whatsapp": { "number": "+201XXXXXXXXX", "consent": { "affirmed": true, "basis": "verbal" } }
}
```
Rules in addition to §9.1: the number is normalised to E.164 and validated (format only; optional country allow-list — OD-21); `consent.affirmed` must be `true`, `basis` from an owner vocabulary (proposed `written`, `verbal`); the login identifier must satisfy the username policy (§23.5, Proposed). Where the number comes from for an Engineer (the Party's phone as an editable default) and where it lives for a non-Engineer account are OD-21 and OD-26. If WhatsApp delivery is disabled or not configured, **or the credential-sealing key is absent or unreadable**, → **503** `users.credential_delivery_unavailable` and nothing is created (no silent fallback to another mechanism). There is no password field and no `credentialMethod`.

Effects (one transaction): the server generates **one** password (CSPRNG), stores **its hash in the user record**, **seals** the plaintext into the delivery outbox row (AES-256-GCM or equivalent, key outside the database; plan §1F.4) and creates the user Active with `must_change_password = false` and no expiry; roles and the Party link as §9.1; the consent record; a credential issuance (kind `Initial`) and a queued delivery; audit events (§23.8). The plaintext exists only in the request's memory and is dropped when the transaction ends; the response cannot contain it. The delivery worker later opens the sealed copy and sends the **same** password on every retry; it never generates or changes a password.

**201 Created**, `Location`, `Cache-Control: no-store`:
```json
{
  "user": { "id": "0193…", "loginIdentifier": "ahmed.mohamed", "status": "Active", "roles": ["Engineer"], "isBootstrapAdmin": false, "credentialState": "DeliveryPending" },
  "credentialDelivery": { "issuanceId": "0193…", "kind": "Initial", "status": "Queued", "attempts": 0, "updatedAt": "2026-10-21T08:00:01Z", "destinationMasked": "+20•••••••34", "failureCategory": null, "stalled": false }
}
```
A WhatsApp failure after this point never rolls back or hides the account (R51). Errors: as §9.1 plus 400 `users.whatsapp_number_invalid`, 400 `users.whatsapp_consent_required`, 400 `users.login_identifier_invalid` (with `reason`), 409 `users.login_identifier_taken` (with up to three free `alternatives`), 503 `users.credential_delivery_unavailable`.

### 23.3 `POST /api/v1/users/{id}/reset-password` — re-issue (v1.6)

Check order as §9 (bootstrap protection at step 6). Body `{ "expectedIssuanceId": "0193…" }` (optional on the first reset; **required when the account has an open or failed issuance**, so a double click or a stale screen gets 409 instead of issuing twice). **The request cannot name a destination**: it uses the number stored for the account. Preconditions: Active (409 `users.already_disabled`), not the bootstrap admin (409 `users.bootstrap_account_protected`), not the caller's own account and other limits per OD-1, a stored number with affirmed consent (409 `users.whatsapp_destination_missing`), delivery enabled (503 `users.credential_delivery_unavailable`), not within the minimum interval or daily cap (429 `users.reissue_too_soon`, `Retry-After`).

Effects (one transaction; the audit write is part of it, §14.2): the server generates **one new** password, **replaces the user's hash with its hash**, revokes **all sessions**, bumps `security_version`, keeps `must_change_password = false`, marks any earlier open issuance `Superseded` and **purges its sealed copy**, and writes the new issuance (kind `Reset`), the sealed copy of the new password and the queued delivery. **Consequence, stated explicitly:** the old password and every session are invalidated **at request time**, not at delivery; until the new password reaches the employee, or after delivery has failed, the employee cannot sign in, and the Admin re-issues. This is the planned behaviour but is **not a confirmed product decision** (roadmap OD-29: the alternative keeps the old password valid until the provider accepts the message). **202 Accepted**, `Cache-Control: no-store`, body `{ "credentialDelivery": { … } }` as above. It never contains a password.

### 23.4 Delivery: status endpoint and worker behaviour

- **`GET /api/v1/users/{id}/credential-delivery`** (audience `admin`, `Users.Manage`) → 200 with the latest `credentialDelivery` object plus `history` (previous issuances: kind, status, attempts, timestamps, failure category — no numbers, no text). `status`: `Queued | Sending | Accepted | Sent | Delivered | Read | Failed | Unknown | Superseded | Cancelled`; `failureCategory`: `invalid_number | not_on_whatsapp | template_unavailable | auth_or_permission | rate_limited | provider_outage | consent_withdrawn | key_unavailable | expired_before_delivery` or `null`; `stalled` is true when a non-terminal row is older than the configured threshold. 404 `users.credential_delivery_not_found` if the account never had one. The users list (§9.2) adds `credentialState` (§23.1a) and a `deliveryStatus` summary and supports a "needs attention" filter.
- **Worker (background; ADR 0007).** Per attempt: claim the delivery row with a lease; re-check that the issuance is current (not superseded or cancelled), the account Active, consent not withdrawn, the employee has not signed in or changed the password since, and the retention deadline has not passed; **open the sealed copy** (only here), send the approved template with the username and password, record provider message id and `Accepted` or a failure category. **Every retry of an operation sends the same password**; the worker never generates, hashes or stores a password and never touches the user record. The sealed copy is purged (columns set to `NULL`, in the same transaction as the state change) at the earliest of: provider `Delivered`/`Read`; the employee's first sign-in or password change after the issuance; terminal failure; supersession or cancellation; the `purge_by` deadline (proposed 24 h, configurable; OD-18). Key missing or unreadable → `Failed(key_unavailable)`, no retry, purge, alert; authentication failure on open → `Failed`, purge, security alert. A sweeper enforces the deadline; a CHECK enforces "terminal status ⇒ no ciphertext". Retries are for transient outcomes only; limits are OD-18.
- A Disabled account's open issuance becomes `Cancelled`. Reactivating does not revive it; the Admin re-issues.

### 23.5 Usernames — English-only (confirmed); details Proposed (OD-19)

**Confirmed (R49):** English-only usernames, suggested from the employee's name, editable by the Admin before creation. **Proposed details** (`admin-user-creation-plan.md` §1G): lowercase ASCII `a–z 0–9 . _ -`, 3–32 characters, starts with a letter, `given.second`, next free numeric suffix on collision, reserved names, no phone-like digit runs, fixed after creation; `LoginIdentifier` and existing identifiers stay opaque (the policy is a creation-time validator; ADR 0003 §3.1 needs a dated note, OD-19).

**`POST /api/v1/users/username-suggestions`** (audience `admin`, `Users.Manage`, throttled; `POST` so names stay out of URLs and logs). Request `{ "name": "أحمد محمد", "latinName": "Ahmed Mohamed" }` (`latinName` optional and preferred when present) or `{ "candidate": "ahmed.mohamed" }`. Response 200 `{ "policyVersion": "v1", "suggestions": [ { "value": "ahmed.mohamed2", "available": true, "source": "latinName" } ], "alternatives": ["ahmed.mohamed3"] }`; no usable suggestion → `{ "suggestions": [], "reason": "name_unusable" }` (never an empty or placeholder value). Availability is advisory; creation re-checks and the unique index `ux_users_login_identifier_lower` decides. Validation reasons on a typed value: `too_short`, `too_long`, `bad_characters`, `bad_shape`, `reserved`, `phone_like`, `whitespace`.

### 23.6 WhatsApp webhook (machine endpoint)

`GET /api/v1/integrations/whatsapp/webhook` answers Meta's handshake only when the verify token matches (constant-time). `POST` accepts events only when the signature header is a valid HMAC-SHA-256 of the **raw** body under the app secret (constant-time; rejected before parsing; fixed body-size limit); any well-signed event gets 200, including unknown message ids, so Meta does not retry endlessly. Processing is idempotent on (message id, status, provider timestamp), never moves a status backwards, stores only message id, status, timestamp and error code/title, and logs nothing of the payload. It is outside the `admin`/`employee` audience rules (§4.1); its protection is the signature plus size and rate limits. Header names and payload fields come from Meta's documentation current at build time.

### 23.7 Error codes

New: `users.credential_delivery_unavailable` (503) · `users.credential_delivery_not_found` (404) · `users.whatsapp_number_invalid` (400) · `users.whatsapp_consent_required` (400) · `users.whatsapp_destination_missing` (409) · `users.reissue_too_soon` (429, `Retry-After`) · `users.issuance_changed` (409, stale or missing `expectedIssuanceId`) · `users.login_identifier_invalid` gains `reason` · `users.login_identifier_taken` gains `alternatives`. Dormant: `auth.reset_code_invalid`, `users.reset_delivery_unavailable`, `auth.password_change_required`. Retired with their endpoints: `users.initial_password_not_applicable`. A WhatsApp provider failure is **never** an HTTP error of create or reset — the work is queued; failures appear as `status` and `failureCategory`.

### 23.8 Audit (same transaction as the change; fail closed)

Recommended (ADR 0007 decides): **no new action codes.** Creation writes the existing `UserCreated` and `RoleAssigned` events; its snapshot adds `credential_issuance_id` and `consent_affirmed` (boolean). A reset writes the existing `PasswordResetIssued` with `credential_issuance_id`, `channel` (`whatsapp`), `issued_at` — replacing the ticket-oriented allow-list keys for this action. Session revocations are recorded in the sessions table as today (§14); no separate audit event. `PasswordResetCompleted` is unused (dormant) and `InitialPasswordReissued` is retired with §9.8. **Never captured:** the password, its hash, the destination number, the message text, provider secrets, provider payloads, and the login identifier where §14.1 already forbids it. Delivery status changes from the webhook are operational data in the delivery table, not audit events. If ADR 0007 prefers a dedicated action code for terminal delivery failure, it is one additive migration written like the E27 deviation (`NOT VALID` then `VALIDATE`, tested `Down`, not applied to a shared database without the owner). `actor_role_code` follows OC-6.

### 23.9 Operational and security rules

Every create/reset response and the status endpoint: `Cache-Control: no-store`. **The plaintext password exists only:** in the request's memory during the create/reset transaction (to hash and to seal it); sealed (encrypted) in the outbox row until purged; in the worker's memory while it sends; in the TLS request to Meta; and on the employee's phone. It is never stored in plaintext in the database, never logged, never in audit events, error responses, metrics or any API response. The sealed column is never selected outside the worker's repository and is excluded from every read model. The sealing key is held outside the database in the environment's secret store, readable only by the API host's runtime identity (plan §1F.4; production custody is OD-30). The typed HTTP client for Meta has request/response body logging disabled and its credentials come from the secret store; tests prove the password, the number and the message text are in no log, audit row, ProblemDetails, exception text, metric label or Admin response, on success and failure paths including the worker, and that a scan of the database after delivery finds the plaintext nowhere. Login throttling (§17), session revocation on reset and the `security_version` check stay the compensating controls. The fake sender cannot be registered outside Development/Test. Metrics: queue depth, oldest-queued age, failure counts by category (names only).

### 23.10 Frontend impact (replaces §16 items 12 and 18 and the password dialogs)

- **Admin:** create form with name, optional Latin spelling, server-suggested editable English username (no client-side transliteration), WhatsApp number entered twice, consent checkbox, then a delivery-status panel (timeline, failure category in Arabic, stalled flag, polling while non-terminal); a reset action that shows the same panel and **never a password dialog**; users-list `credentialState` badge, delivery summary and "needs attention" filter; handlers for every code in §23.7. The default-password dialog of v1.4 and every field named `temporaryPassword` are removed.
- **Employee:** sign in with the username and password received on WhatsApp; **no forced-change screen and no first-login prompt**; voluntary change-password stays available (an optional, dismissible suggestion is a UX choice for the owner, not built unless asked); no reset-code screen; no activation route.
- **Both:** `12345678` appears nowhere; `mustChangePassword` is still read but is `false` for new accounts; mock repositories follow these shapes in the same step as the real ones.

### 23.11 Open confirmations (none approved; roadmap §4)

OD-1 limits of Admin reset and who may change a stored number · OD-2 fate of the reset-code machinery · OD-18 retry limits, back-off, resend rules and the sealed-copy retention window · OD-19 username details and the ADR 0003 §3.1 amendment · **OD-29 what a reset invalidates while delivery is pending or failed** · **OD-30 production custody of the sealing key, key-destruction schedule and explicit acceptance of an encrypted copy at rest** · OD-20 mistaken username or number · OD-21 destination source, number validation policy, consent vocabulary · OD-22 retention and privacy · OD-23 Meta setup, template wording and the contingency if Meta refuses credential content · OD-25 optional expiry of an unused password · OD-26 which accounts use the flow · OD-27 in-band fallback.
