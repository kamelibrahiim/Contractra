# Auth & Users API Contract

Status: 🟡 Draft v1.5 — engineering revision of v1.4, made under the owner's instruction of 2026-10-15 to resolve the documentation-consistency audit. It is the implementation baseline for Backend Sprint 2 from Slice 2 onward. The items in §19 still need owner confirmation; until the owner confirms or overrides them, each one behaves as written here. v1.4 is not retroactively approved by this revision.
Date: 2026-09-27 · revised 2026-10-01 (v1.4) · 2026-10-15 (v1.5)
Implements: Backend Sprint 2 (`docs/implementation/backend/sprints/sprint-02.md`) · plan `admin-user-creation-plan.md` §1B and §1D
Implementation status (2026-10-15): **no endpoint in this contract exists in code.** The authorization catalog seed (Slice 1, commits `1e10742` and `bc63d5f`) and the user credential/security-state schema (Slice 2, see `sprint-02.md`) are the only parts with code behind them. Everything else here is specification.

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
| **Input limits** (checked before any lookup or hashing, so oversized input cannot be used to exhaust CPU): `loginIdentifier` at most **256** characters; any password field at most **128** characters; `resetCode` at most 32 characters; auth request bodies at most 16 KiB. Over-limit input → 400 `validation.failed` (on login: the generic 401 `auth.invalid_credentials`, so no field-level hint is given). These are transport limits; they do not change ADR 0003's "no confirmed length limit" for stored identifiers created before them. | [E] E13 |

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

Rules [O] R14, R16 · [E] E13:
- `currentPassword` must be correct (during a forced change it is the temporary password, which must not have expired).
- `newPassword` at least **8** and at most **128** characters.
- `newPassword` must **differ from the current password**.
- There is no shared default password and no rule about one.

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

Completes a password reset that an Admin or the operator issued (§9.6, §10A). The person who completes it chooses the new password; nobody else ever learns it.

Request:
```json
{ "loginIdentifier": "eng.ahmed", "resetCode": "7KQ2-M9XD-4TBR-W8PZ", "newPassword": "••••••••" }
```

Checks, in order: throttling (§17) → shape and input limits (§1) → user lookup (case-insensitive, as §5.1) → user Active → an unconsumed, unexpired reset ticket for that user whose code hash matches (constant-time comparison) → new-password rules (8 … 128 characters).

For an unknown user, a disabled user, or a user with no matching ticket, the server performs the same hashing work as for a match, so timing does not reveal which case applied.

Effects (one transaction): password hash set, `mustChangePassword = false`, ticket consumed, security version bumped, all sessions revoked, `PasswordResetCompleted` audited with the user as actor. **No session is created**: the client sends the user to the login screen.

**204** on success. Errors:
| Status | `code` | When |
|---|---|---|
| 400 | `auth.reset_code_invalid` | Unknown identifier, disabled account, wrong / expired / consumed / superseded code — one response for all |
| 400 | `auth.password_too_short` / `auth.password_too_long` | New-password rules |
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

- Generated by the server with a **cryptographically secure** random generator (`RandomNumberGenerator`): **12 characters** from `A–Z`, `a–z`, `0–9`, containing at least one upper-case letter, one lower-case letter and one digit ("mixed letters and digits", R2). Candidates that miss a class are discarded and regenerated, so the distribution stays uniform over the accepted set (≈ 71 bits). [O] R2 · [E]
- Returned **once**, in the create (§9.1) or initial re-issue (§9.8) response. There is **no reveal endpoint**. [O]
- **Expires 72 hours after it is issued** (configuration `Auth:TemporaryPasswordLifetime`, 1–168 hours). After expiry it no longer authenticates (generic 401) and the account shows `credentialState = InitialPasswordRequired`; the Admin re-issues one (§9.8). [E] E4 — an unclaimed, never-expiring temporary password would be a standing credential known to a third party.
- Stored only as a password hash. Never stored in plaintext, never logged, never audited. [O] · [N] logical model §1.7 · NFR-SEC-002
- The Admin app shows it once with a copy action and does not persist it. [O]
- **Not used for password resets** since v1.5 (§9.6, §10A). [O] R44
- There is **no shared default password** and no default-password setting; the value `12345678` must not appear in the production contract or implementation. [O] R16

## 10A. Password reset code and threat model [O] R44 · [E] E1

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
| `auth.password_too_long` | 400 | change-password, password-reset/complete ([E] E13) |
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
| `reason` | Required for operator commands (§10A, §11); optional otherwise |
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

| # | Question | Current behaviour (safest temporary choice) |
|---|---|---|
| OC-1 | **R44 supersedes R3 for resets.** Confirm that an Admin-issued reset must never reveal a usable credential to the Admin, accepting that until a delivery channel exists every reset goes through the operator command (§10A), and the Admin app shows "done by the operator". Alternative the owner may choose instead: keep R3's temporary-password reset for accounts without sensitive permissions. | §9.6 as written: no secret to the Admin; API reset returns 503 until a channel exists. |
| OC-2 | **Delivery channel and contact data.** Which channel (email, SMS, WhatsApp, …) and which stored contact is authoritative for each user? `LoginIdentifier` is opaque (ADR 0003 §3.1) and no user contact field is confirmed; `Party.Phone` exists only for parties. | No channel; operator procedure. |
| OC-3 | **Creation exposure (R2).** Accept that account creation and initial re-issue show a temporary password to the Admin (residual risk, §10A), or require the same channel-based delivery for new accounts once a channel exists. | R2 kept: shown once to the Admin; expires after 72 hours (E4). |
| OC-4 | **Party validity for Engineer accounts.** Confirm: only `Active` parties; party-role code literal `Engineer` for «مهندس»; the business date in `Africa/Cairo`. The party-role code vocabulary is not defined in the authoritative documents. | §9.1 as written. |
| OC-5 | **Upstream documentation defects** (protected documents, not edited here): (a) spec UC-002 lists `UserDeactivated` / `RoleChanged`, while logical model §1.8 lists `UserDisabled` / `RoleEnded` — the index ranks the spec (15) above the logical model (16), so C5's choice of the logical-model names stands only as an owner decision; (b) logical model §15.4 `source` still names `PWA` after ADR-033; (c) `documentation-index-ar.md` does not list `docs/decisions/**` or `docs/implementation/**` (their order is given in `docs/implementation/README.md`). | Logical-model names and values used; documented here. |
| OC-6 | **`actor_role_code` for users with several roles.** Confirm the deterministic rule in §14.1, which the data dictionary does not spell out. | §14.1 rule. |
| OC-7 | **Finding the Party for an Engineer account.** §9.1 requires `partyId`, but no endpoint lets the Admin app search Parties, and the permission that should guard reading Parties (`Parties.View` or similar, spec §7.3) is not seeded (Q6). Decide the permission (and its role grant) so a read-only Party lookup (`GET /api/v1/parties?role=Engineer&status=Active&search=…`, audience `admin`) can be added. | No lookup: until decided, an Engineer account can only be created with a known Party id; the Admin app's Engineer form is blocked on this (frontend plan FA-4). |

---

## 20. Implementation map

| Contract area | Slice (`sprint-02.md`) | Status 2026-10-15 |
|---|---|---|
| Role/permission catalog and grants (§2) | 1 — US-BE-006 | ✅ Implemented (`1e10742`, tests fixed in `bc63d5f`) |
| `users` credential and security state, bootstrap flag/index/trigger, one account per Party, case-insensitive lookup (§3.5, §5.1, §9.1, §11) | 2 — US-BE-007 | See `sprint-02.md` §A for the current state |
| Sessions, refresh tokens, reset tickets, throttle store (§3.3, §10A, §17) | 2B — US-BE-008 | Planned |
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
