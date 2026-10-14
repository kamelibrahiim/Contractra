# Auth & Users API Contract

Status: 🟢 Draft v1.4 — source of truth for the backend, the Admin Flutter app and the Employee Flutter app
Date: 2026-09-27 · last revised 2026-10-01 (v1.4)
Implements: Backend Sprint 2 (`docs/implementation/backend/sprints/sprint-02.md`) · plan `admin-user-creation-plan.md` §1B
Not implemented yet: no endpoint in this contract exists in code today. Slice 2 has not started.

> **How to read this contract.** Every rule carries its basis:
> - **[N]** Normative: stated by an authoritative document (`docs/documentation-index-ar.md` order). Cited.
> - **[O]** Owner decision: decided by the project owner between 2026-09-26 and 2026-10-01, recorded in the plan as C1–C6 and R1–R43. Not a documented business rule, but binding.
> - **[E]** Engineering proposal: chosen here to make the contract implementable. Binding for the implementation until the owner overrides it; changing one needs no business decision, only a contract revision.
>
> If this contract and an authoritative document ever disagree, the document wins and the contract has a defect ([N] beats [O] beats [E]).
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
| Responses that carry a password or a token set `Cache-Control: no-store`. | [E] |
| Passwords, temporary passwords, access tokens and refresh tokens are **never logged or audited**, in request or response bodies. Request/response body logging must redact them. | [N] logical model §1.7 · audit addendum §9.1 · NFR-SEC-002 |

---

## 2. Identity, roles and permissions

| Rule | Basis |
|---|---|
| Roles: `Engineer`, `OfficeStaff`, `Accountant`, `Admin`. | [N] spec §7.1 |
| Seeded permissions: `Users.Manage`, `Permissions.Grant`, `Roles.Manage`, `Payroll.View`, `Apps.Admin.Access`, `Apps.Employee.Access`. | [N] spec §7.3 (first four, 🔵 proposed catalog) · [O] C2, R13 (scope and the two app-access codes) |
| Default role grants: Admin ← `Apps.Admin.Access`, `Users.Manage`, `Permissions.Grant`, `Roles.Manage`. OfficeStaff ← `Apps.Admin.Access`. Accountant ← `Apps.Admin.Access`. Engineer ← `Apps.Employee.Access`. | [N] spec §7.3 (Identity perms to Admin) · [O] R13 |
| **`Payroll.View` is never granted through a role**, Admin included; only as a direct per-user grant. | [N] BR-235 · ح-235 · FR-AUTH-006 · NFR-SEC-003 (enforced by DB triggers, ADR 0002 §6) |
| Roles are represented in the API by their **code** (string), never by database id. | [O] R9 |
| **Effective permissions** of a user at time *t* = permissions of every role assignment active at *t* ∪ every direct grant active at *t*. `Payroll.View` can only come from a direct grant. | [N] CAP-02 · BR-235 · [O] R10 · [E] definition of "active" = ADR 0003 §9.1/§9.2 (`IsActiveAt`) |
| A user may use an app only while holding its app-access permission: Admin app ← `Apps.Admin.Access`, Employee app ← `Apps.Employee.Access`. | [O] R13 |

Role codes carry no display name (ADR 0002 §3 stores none). **Clients translate role codes to Arabic labels through their i18n files** [E]. *(Frontend impact: `RoleOption.displayName` does not exist in this contract.)*

---

## 3. Tokens and sessions

### 3.1 Policy

| Rule | Basis |
|---|---|
| **Access token:** JWT, lifetime **30 minutes**. Kept **in memory only** on every client. | [O] R5, R18 · [E] JWT format details §3.2 |
| **Refresh token:** opaque random value, **rotated on every use**, **7 days maximum session lifetime** counted from login. Rotation does not extend the maximum. | [O] R5, R17 · [E] "counted from login" |
| **Idle timeout** (a session with no token activity for longer than this expires even before 7 days): **Engineer 8 hours; Accountant, Admin, OfficeStaff 60 minutes**. | [N] NFR-SEC-007 (🔵 proposed values) · [O] R17 adopted |
| The backend stores **only a cryptographic hash** of each refresh token (SHA-256), never the token. | [N] logical model §1.7 (tokens not stored) · [O] R5, R18 (a hash is stored, the token is not) |
| **Web** receives the refresh token in a cookie (§3.4), never in the body. **Mobile** receives it in the response body and stores it with `flutter_secure_storage`. | [O] R6, R18 |
| Tokens are never logged by server or client. | [N] logical model §1.7 · audit addendum §9.1 |

### 3.2 Access token claims [E]

| Claim | Meaning |
|---|---|
| `sub` | User id (uuid) |
| `sid` | Session id (the refresh-token family, §3.3) |
| `app` | `admin` or `employee` — the client app the session was opened for |
| `sv` | The user's **security version** at issue time (§3.5) |
| `perm` | Effective permission codes at issue time (array) |
| `role` | Active role codes at issue time (array) |
| `iat`, `exp`, `iss`, `aud` | Standard. `exp = iat + 30 min` |

Signed with HMAC-SHA256; the signing key comes from configuration / user-secrets and is never committed [N] CLAUDE.md (secrets) · [E] algorithm. `mustChangePassword` is **not** a claim; it is read from the database on every request together with the security version (§3.5).

### 3.3 Sessions, rotation and revocation (resolves F) [E]

A **session** is one login on one client app. It is a *family* of refresh tokens: login creates the family and its first token; every refresh consumes the current token and issues the next one in the same family.

Each session stores: `id`, `user_id`, `client_app`, `client_platform` (`web` or `mobile`, fixed at login, §5.1), `created_at`, `absolute_expires_at` (= created + 7 days), `last_refreshed_at`, `revoked_at`, `revoke_reason`. Each refresh token stores: `token_hash`, `session_id`, `issued_at`, `consumed_at`. [E] · `client_platform` [O] R38

| Event | Effect | Basis |
|---|---|---|
| Refresh with a valid, unconsumed token | Token marked consumed; new token issued in the same session; new access token with **current** `sv`, `perm`, `role`. | [O] R5 |
| Refresh with a token consumed **30 seconds ago or less** (reuse grace, e.g. two browser tabs refreshing together) | A new **access token** is returned; the refresh token is **not** rotated again and the session is **not** revoked. | [O] R35 |
| Refresh with a token consumed **more than 30 seconds ago** (reuse) | Treated as theft: the **whole session is revoked**; 401 `auth.refresh_invalid`. | [O] R35 |
| Logout | Current session revoked. | [E] |
| **User disabled** | **All** the user's sessions revoked; security version bumped. | [O] R5 |
| **Password reset** by an Admin | **All** the user's sessions revoked; security version bumped. | [O] R5, R3 |
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

- `users.security_version` is an integer, incremented by every event listed in §3.3 as "security version bumped" (role assigned/ended, direct permission granted/revoked, disable, reactivate, password reset, password change).
- **Every authenticated request performs one indexed lookup** by user id, reading `security_version` and `must_change_password` from the same `users` row. [O] "one indexed lookup"
- If the token's `sv` ≠ the current value → **401 `auth.unauthenticated`**. The client refreshes once (§6): if the session is still valid, it gets a token with fresh permissions; if the session was revoked (disable, reset, password change elsewhere), refresh fails and the user must log in.
- Loading roles and permissions from the database on every request is **not** done. [O]
- A disabled user's tokens fail this check because disabling bumps the version and revokes the sessions. The status itself is therefore not read per request. [E]

### 3.6 Idle timeout — refresh-based inactivity policy (resolves G)

**Values** [N] NFR-SEC-007 · [O] R17, R21: **Engineer 8 hours; Accountant, Admin, OfficeStaff 60 minutes.** These are the business idle-timeout values. The 30-minute access-token lifetime does not change them.

**Policy** [O] R21:
- Inactivity is tracked **per session, at refresh**. Each session stores `last_refreshed_at`, set at login and updated at every successful refresh.
- At refresh, if `now − last_refreshed_at` exceeds the applicable idle timeout, the session is revoked (`revoke_reason = idle`) and the response is **401 `auth.session_expired`**. The 7-day absolute limit is checked at the same point.
- **No database write on any other API request.** Normal requests perform only the one indexed security-version lookup (§3.5).
- The applicable timeout is the **shortest** idle timeout among the user's active roles at refresh time; a user with no active role gets 60 minutes. [E] — the owner defined timeouts per role, not for multi-role users.
- **Clients must not refresh in the background.** Refresh only happens reactively (after a 401 from a normal call) or at app start to restore a session. A timer that refreshes without user activity would keep an inactive session alive. [E]

---

## 4. Client app identification and app access (resolves A)

| Rule | Basis |
|---|---|
| The login request carries **`clientApp`**: `"admin"` or `"employee"`. Any other value → 400 `validation.failed`. | [O] R13 (app must be identified) · [E] field name and values |
| The session remembers its `clientApp`; refresh re-checks that the user **still** holds the matching app-access permission. | [E] |
| Missing permission, with **correct** credentials → **403 `auth.app_access_denied`**. The check runs only **after** the password is verified, so it reveals nothing to someone without the password. Audited as `AccessDenied` (§14). App access is always enforced by the server from the user's permissions. | [O] R13, R12, R24 |
| A **disabled** account still gets the generic `auth.invalid_credentials` (§5.1); app-access is never reached for it. | [O] R11, R24 |
| Missing permission at refresh → session revoked, 401 `auth.refresh_invalid`. | [E] |

---

## 5. Auth endpoints

### 5.1 `POST /api/v1/auth/login` — no auth required

Request:
```json
{ "loginIdentifier": "eng.ahmed", "password": "••••••••", "clientApp": "employee" }
```

Checks, in order: request shape → user exists → user Active → password correct → app-access permission.

For an unknown or disabled account the server still performs a **dummy password-hash verification**, so response timing does not reveal whether the account exists or is disabled. [O] R42 · On success, if the stored hash needs an upgrade (`SuccessRehashNeeded`), the password is transparently rehashed. [O] R33

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
| 401 | `auth.invalid_credentials` | Unknown identifier, wrong password, **or disabled account** — identical response in all three cases | [N] UC-001 · FR-AUTH-002 · [O] R11 |
| 403 | `auth.app_access_denied` | Credentials correct, app permission missing | [O] R13 · [E] |

Every failed attempt is logged to Serilog with the real reason (never the password). [N] FR-AUTH-002 · ح-193 · audit addendum §9 · [O] R11

### 5.2 `POST /api/v1/auth/refresh` — refresh token required

- **Mobile:** body `{ "refreshToken": "<opaque>" }`.
- **Web:** no body; the cookie is sent automatically. The request **must** carry `X-CSRF-Protection: 1` (§8).

**200:** same shape as login (new access token; new refresh token in the body for mobile, or in the cookie for web, according to the session's stored `client_platform` [O] R38; current `mustChangePassword`).

Within the 30-second reuse grace (§3.3, [O] R35) the response carries a new access token only: no new refresh token (the mobile body omits `refreshToken`; the web response sets no cookie). The client keeps the refresh token it already has. [E] shape

**401** `auth.refresh_invalid` (unknown, consumed, revoked, or app permission lost) or `auth.session_expired` (idle or 7-day limit). Clients treat both as "log in again".

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

Rules [O] R14, R16:
- `currentPassword` must be correct.
- `newPassword` at least **8 characters**.
- `newPassword` must **differ from the current password**.
- There is no shared default password and no rule about one.

Effects: the hash is updated, `mustChangePassword = false`, the security version is bumped, **every other session is revoked**, and `PasswordChanged` is audited in the same transaction. [O] R1, R5, owner flow §9 · [N] BR-186

**200:** new tokens for the **current** session, same shape as refresh (§5.2), shaped by the session's stored `client_platform` [O] R38. The current refresh token is **consumed** and a new one issued in the same session; the old access token is stale because the version changed. **Clients must store the returned tokens** — the old refresh token is consumed, and presenting it more than 30 seconds later counts as reuse (§3.3). [O] R36, R35

**The bootstrap admin may use this endpoint** to change its own password, under the same rules and with the same effects (hash updated, security version bumped, other sessions revoked). It never gets `mustChangePassword = true`, so the flag stays false. This endpoint changes only the caller's own password; it is not an administrative action on another account. [O] R26

Errors:
| Status | `code` | When |
|---|---|---|
| 400 | `auth.current_password_incorrect` | Wrong current password. **400, not 401**, so the client does not try to refresh. [E] |
| 400 | `auth.password_too_short` | New password shorter than 8 [O] R16 |
| 400 | `auth.password_same_as_current` | New password equals the current one [O] R16 |

---

## 6. 401 vs 403 semantics

| Status | Meaning | Client action | Basis |
|---|---|---|---|
| **401** | Not authenticated: missing, invalid, expired or **stale** access token (`auth.unauthenticated`), or failed login/refresh | On every call **except login, refresh and logout** (including `/auth/me` and `change-password`): refresh **once**, retry once; if refresh fails, clear the session and go to login. On login, refresh and logout: never refresh. If logout fails for any reason, the client clears its local session anyway. | [O] R37 |
| **403** | Authenticated but not allowed: `auth.forbidden` (missing permission), `auth.password_change_required`, `auth.app_access_denied` | Never refresh. Show the matching message. | [N] UC-002 exception flow (unauthorized ⟹ 403) · [E] |

Every 403 **to an authenticated user** (including `auth.app_access_denied` at login, after the password was verified) is written as an `AccessDenied` audit event **and** logged to Serilog (§14). A web refresh/logout rejected for a missing CSRF header (§8) happens before any actor is established: it is logged to **Serilog only** and is **not** an `AccessDenied` event. [N] logical model §1.8 · UC-002 · [O] R12, R40

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

Who gets the flag: users created by an Admin, and users whose password an Admin resets. **The bootstrap admin never does.** [O] R1, R3, R19

---

## 8. CSRF protection for the web cookie (resolves D) [E]

The browser sends the refresh cookie to every `/api/v1/auth` endpoint (cookie `Path`, §3.4), but only `refresh` reads it to authenticate; `logout` identifies the session from the access token and clears the cookie. Both carry the CSRF header requirement below. Three layers:

1. **`SameSite=Strict`**: the browser does not send the cookie on cross-site requests.
2. **Required custom header** `X-CSRF-Protection: 1` on web `refresh` and `logout`. A cross-site page cannot set a custom header without a CORS preflight, and the preflight fails for origins not on the allow-list. Web `refresh`/`logout` requests without the header → 403 `auth.forbidden`, logged to Serilog only, not an `AccessDenied` audit event (§6) [O] R40.
3. **CORS allow-list with credentials:** only the configured Admin web origin(s) may call the API with credentials. Origins come from configuration per environment; `*` is never used with credentials.

Every other endpoint uses the `Authorization: Bearer` header, which a cross-site page cannot attach, so no further CSRF measure applies to them.

---

## 9. Users endpoints — all require `Users.Manage` (and `Apps.Admin.Access` via the session)

`Users.Manage` covers create, list, read, disable, reactivate and reset. [N] FR-AUTH-003, FR-AUTH-004 · UC-002 · [O] R4, R15

**Server check order for every users endpoint** [O] R39:
1. Authentication (401 `auth.unauthenticated`)
2. Password-change gate (403 `auth.password_change_required`, §7)
3. Permission (403 `auth.forbidden` + `AccessDenied`)
4. Target exists (404 `users.not_found`)
5. Bootstrap protection (409 `users.bootstrap_account_protected`, §11)
6. Request validation and state-transition conflicts (400 / 409)

A caller without the permission therefore never learns whether an id belongs to the bootstrap admin.

### 9.1 `POST /api/v1/users` — create

Request [O] R9:
```json
{ "loginIdentifier": "eng.ahmed", "roleCodes": ["Engineer"], "partyId": "0193…" }
```

Rules:
- At least one role [N] FR-AUTH-003 (enforced in the Application layer, ADR 0003 §10).
- Every code must be an existing, **Active** role; a Deprecated role → 400 `users.role_unknown` [O] R43. Duplicate codes in `roleCodes` → 400 `validation.failed` [O] R43.
- Login identifier is opaque, unique case-insensitively, no leading/trailing whitespace [O] ADR 0003 §3. No password field in the request.
- **`partyId` is required when `roleCodes` contains `Engineer`** [O] R31. The Party must exist, hold an engineer party role, and not be linked to any user (one account per Party, [O] R30). The user and the `user_party_links` row are saved in the same transaction.
- When `roleCodes` does not contain `Engineer`, `partyId` must be absent; linking for other roles is a separate optional step not covered by this contract (§15). Present anyway → 400 `validation.failed`. [E]

Effects: user created Active; a **temporary password** generated (§10); `mustChangePassword = true`; roles assigned; for an Engineer, the party link created; `UserCreated` and one `RoleAssigned` per role audited in the same transaction [N] BR-186 · logical model §1.8.

**201 Created**, `Location: /api/v1/users/{id}`, `Cache-Control: no-store` (resolves I):
```json
{
  "user": { "id": "0193…", "loginIdentifier": "eng.ahmed", "status": "Active", "roles": ["Engineer"], "isBootstrapAdmin": false },
  "temporaryPassword": "A7k2Qm9xR4tB"
}
```

Errors: 400 `validation.failed` · 400 `users.login_identifier_invalid` · 409 `users.login_identifier_taken` · 400 `users.role_required` · 400 `users.role_unknown` · 400 `users.party_required` · 400 `users.party_invalid` (Party not found or holds no engineer party role) · 409 `users.party_already_linked`. *(Party codes: [E] names for [O] R30/R31.)*

### 9.2 `GET /api/v1/users` — list (resolves K)

Query [E]: `page` (1-based, default 1), `pageSize` (default 20, max 100), `search` (optional, case-insensitive substring of the login identifier), `status` (optional: `Active` or `Disabled`). Sorted by login identifier, case-insensitive ascending.

**200:**
```json
{
  "items": [ { "id": "0193…", "loginIdentifier": "eng.ahmed", "status": "Active", "roles": ["Engineer"], "isBootstrapAdmin": false } ],
  "page": 1,
  "pageSize": 20,
  "totalCount": 1
}
```
`roles` = currently active role codes. Unified paging shape for all future lists. [N] spec §26.1 principle 5 · [E] shape

### 9.3 `GET /api/v1/users/{id}` — read

**200:** the same user object as in the list. **404** `users.not_found`.

### 9.4 `POST /api/v1/users/{id}/disable`

User → Disabled; all sessions revoked; security version bumped; `UserDisabled` audited. **200** with the user object. [N] FR-AUTH-004 · logical model §1.5, §1.8 · [O] C5, R5

**The bootstrap admin cannot be disabled** (§11): rejected with 409 `users.bootstrap_account_protected` at step 5 of the §9 check order (before the state-transition check), and nothing changes. [O] R27, R39
Errors: 404 `users.not_found` · 409 `users.bootstrap_account_protected` · 409 `users.already_disabled`.

### 9.5 `POST /api/v1/users/{id}/reactivate`

User → Active; security version bumped; `UserReactivated` audited. **Reactivation does not restore any previous session**: sessions revoked by the disable stay revoked, and the user must authenticate again. **200** with the user object. [N] logical model §1.5, §1.8 · [O] R4, R25

On the bootstrap admin: rejected with 409 `users.bootstrap_account_protected`, like every other administrative mutation (§11). The account can never be disabled through the API, so reactivating it has no legitimate use. [O] R27 (confirmed for reactivate 2026-10-01)
Errors: 404 `users.not_found` · 409 `users.bootstrap_account_protected` · 409 `users.already_active`.

### 9.6 `POST /api/v1/users/{id}/reset-password`

New temporary password (§10); `mustChangePassword = true`; all sessions revoked; security version bumped; `PasswordReset` audited. [O] R3, R15, R5

**The bootstrap admin cannot be reset through this endpoint** (§11). The request is rejected before anything changes, and nothing is audited as `PasswordReset`. [O] R20, R27. Any other account — including other Admins, such as the second Admin created after first run (§11) — can be reset by a holder of `Users.Manage`. [O] R32

**A Disabled user cannot be reset**: 409 `users.already_disabled`; reactivate first (§9.5). [O] R43

**200**, `Cache-Control: no-store`:
```json
{ "temporaryPassword": "Zp4Lw8nD2yKc" }
```
Errors: 404 `users.not_found` · 409 `users.bootstrap_account_protected` · 409 `users.already_disabled`.

### 9.7 `GET /api/v1/roles` — requires `Users.Manage`

**200:**
```json
[ { "code": "Accountant" }, { "code": "Admin" }, { "code": "Engineer" }, { "code": "OfficeStaff" } ]
```
Active roles only, sorted by code. No display name (§2). [E]

---

## 10. Temporary password [O] R2, R3 · owner clarification §8

- Generated by the server with a **cryptographically secure** random generator: **12 characters**, letters and digits (`A–Z`, `a–z`, `0–9`). [O]
- Returned **once**, in the create (§9.1) or reset (§9.6) response. There is **no reveal endpoint**. [O]
- Stored only as a password hash. Never stored in plaintext, never logged, never audited. [O] · [N] logical model §1.7 · NFR-SEC-002
- The Admin app shows it once with a copy action and does not persist it. [O]
- There is **no shared default password** and no default-password setting; the value `12345678` must not appear in the production contract or implementation. [O] R16

---

## 11. Bootstrap admin [O] D2, C3, R19, R20, R26–R29, R32, R39, R41, R42

**Creation**
- Created once at startup, only when no active user holds the `Admin` role, from `Bootstrap:AdminLoginIdentifier` and `Bootstrap:AdminPassword` (user-secrets in development, environment variables in production). Missing configuration → warning, no user created.
- `mustChangePassword = false`, now and always (exempt from the forced change). Assigned the `Admin` role. Granted `Payroll.View` **directly** (never through the role). Audited in the same transaction with `actor_type = System`: `UserCreated`, `RoleAssigned` (Admin) and `PayrollViewGranted` — **not** `PermissionGranted` (§14). [O] R41
- **The seeder never overwrites an existing bootstrap admin password** — not on restart, not if the configured password changes. [O] R19, R26

**Own password** [O] R26
- The bootstrap admin **may change its own password** through `POST /api/v1/auth/change-password` (§5.5), with the normal rules (at least 8 characters, different from the current password) and the normal effects (hash updated, security version bumped, other sessions revoked).

**Protection from administrative mutation** [O] R27

| Action on the bootstrap admin (by **any** actor, including the bootstrap admin itself) [O] R42 | Result |
|---|---|
| Reset password (§9.6) | 409 `users.bootstrap_account_protected` |
| Disable (§9.4) | 409 `users.bootstrap_account_protected` |
| Reactivate (§9.5) | 409 `users.bootstrap_account_protected` [O] R27 (confirmed 2026-10-01) |
| Assign, end or otherwise change roles | Rejected with 409 `users.bootstrap_account_protected`. No role endpoint exists in this contract yet (§15); **any future one must apply this rule.** [O] R27 |
| Grant or revoke a **direct permission** — including revoking its direct `Payroll.View` | Rejected with 409 `users.bootstrap_account_protected`. No direct-permission endpoint exists in this contract yet (§15); **any future one must apply this rule.** [O] R28 |
| Delete | No delete endpoint exists for any user [N] FR-AUTH-004; none may ever be added for this account. |

- The protection check looks at the **target** account only, regardless of who the actor is. [O] R42
- It runs at step 5 of the §9 check order: **after** authentication, the password-change gate, the permission check and the 404 check, and **before** request validation and state-transition conflicts. It changes nothing when it rejects. A caller without the permission never reaches it, so it cannot discover the bootstrap id. [O] R39
- A protection rejection is **HTTP 409**, logged to **Serilog** with the correlation ID, and **not** recorded as an `AccessDenied` audit event (it is not a 403). [O] confirmed 2026-10-01
- **Enforced server-side**, in the Application use cases (`ResetPassword`, `DisableUser`, `ReactivateUser`, and any future role or direct-permission use case), by reading `is_bootstrap_admin` on the target user. The Admin UI may hide these actions, but **hiding is not a security control**. [O] R27, R28 · [N] BR-018 · FR-AUTH-008

**Lost bootstrap password** [O] R29, R32
- The API provides **no** reset or recovery path for the bootstrap admin: the normal `Users.Manage` reset endpoint stays blocked (§9.6), and there is no self-service recovery.
- If the password is lost, recovery is **out-of-band** (R29). The break-glass arrangement is [O] R32 (it implements R29 and does not change R27):
  - **Second Admin.** A second Admin account is created right after first run. It can reset any Admin's password **except** the bootstrap admin's (§9.6), so day-to-day administration never depends on the bootstrap password alone.
  - **Operator-only CLI.** `Contractra.Api` provides an operator-only command (for example `reset-bootstrap-password`), runnable only on the server — not an HTTP endpoint. It writes an audit event with `actor_type = System` and prints nothing sensitive to logs. Its exact invocation and the operating procedure around it are defined with the slice that builds it and in the operations documentation.
- The bootstrap seeder never overwrites an existing bootstrap password, so changing `Bootstrap:AdminPassword` in configuration is **not** a recovery mechanism.

**How the bootstrap account is identified** [O] R20 (must be documented) · [E] mechanism:
- A persisted, non-null boolean **`users.is_bootstrap_admin`**, default `false`.
- Set to `true` **only** by the bootstrap seeder, at the moment it creates the account. No endpoint can set, clear or change it; it is not part of any request body.
- A database partial unique index allows **at most one** row with `is_bootstrap_admin = true`.
- Why a flag and not a comparison with `Bootstrap:AdminLoginIdentifier`: the configured value can be absent (it is only needed until the first admin exists) or changed later, and a rename in configuration must never silently move protection to another account.
- Exposed read-only in the user object as `"isBootstrapAdmin": true|false`, so the Admin app can hide the reset action. The server check is authoritative; hiding the button is not protection. [N] BR-018 · FR-AUTH-008
- The column is added with the Slice 2 schema changes (not implemented yet).

---

## 12. ProblemDetails error codes (resolves E)

Body example:
```json
{ "type": "about:blank", "title": "Invalid credentials", "status": 401, "code": "auth.invalid_credentials", "correlationId": "0193…" }
```
`validation.failed` adds `errors`: `{ "<field>": ["<message>"] }`. `detail` is optional and never contains secrets.

| Code | Status | Endpoint(s) |
|---|---|---|
| `validation.failed` | 400 | any (includes duplicate `roleCodes`, [O] R43) |
| `auth.invalid_credentials` | 401 | login |
| `auth.unauthenticated` | 401 | any protected endpoint (missing/invalid/expired/stale token) |
| `auth.refresh_invalid` | 401 | refresh |
| `auth.session_expired` | 401 | refresh (idle or 7-day limit) |
| `auth.forbidden` | 403 | any (missing permission; web refresh/logout without the CSRF header — the latter Serilog only, not `AccessDenied`, [O] R40) |
| `auth.password_change_required` | 403 | any except §7's list |
| `auth.app_access_denied` | 403 | login |
| `auth.current_password_incorrect` | 400 | change-password |
| `auth.password_too_short` | 400 | change-password |
| `auth.password_same_as_current` | 400 | change-password |
| `users.not_found` | 404 | users/{id}/* |
| `users.login_identifier_invalid` | 400 | create |
| `users.login_identifier_taken` | 409 | create |
| `users.role_required` | 400 | create |
| `users.role_unknown` | 400 | create (unknown or Deprecated role, [O] R43) |
| `users.party_required` | 400 | create (`Engineer` without `partyId`, [O] R31) |
| `users.party_invalid` | 400 | create (Party not found, or holds no engineer party role, [O] R31) |
| `users.party_already_linked` | 409 | create (Party already linked to a user, [O] R30) |
| `users.already_disabled` | 409 | disable; reset-password on a Disabled user ([O] R43) |
| `users.already_active` | 409 | reactivate |
| `users.bootstrap_account_protected` | 409 | reset-password, disable, reactivate, and any future role or direct-permission change on the bootstrap admin (§11). Logged to Serilog; not an `AccessDenied` audit event. |
| `server.error` | 500 | any (no internals in the body) |

Basis: codes named in the owner prompts (`auth.invalid_credentials`, `auth.password_change_required`, `users.login_identifier_taken`, `users.role_required`) · everything else [E]. **`users.disabled` is not returned by login** [O] R11 and is not used anywhere in this contract.

---

## 13. Correlation ID (resolves L) [N] spec §26.1 principle 6 · [O] R8

- Header: `X-Correlation-ID`. Optional; a request without it is **never** rejected.
- A client value is **accepted** only if it is 1–64 characters from `A–Z a–z 0–9 - _ .` [E]. Otherwise it is **ignored** (not an error) and the server generates a new one (a UUID).
- The server always returns the ID used in the response header `X-Correlation-ID`, includes it in ProblemDetails as `correlationId`, and writes it to Serilog and to every audit event created by the request.

---

## 14. Audit and logging obligations

Audit events are written **in the same database transaction** as the change; if the audit write fails, the change rolls back. [N] BR-186 · ح-186 · FR-AUTH-007

| Event | Written by | Basis |
|---|---|---|
| `UserCreated` | create, bootstrap | [N] logical model §1.8 |
| `UserDisabled` | disable | [N] §1.8 · [O] C5 |
| `UserReactivated` | reactivate | [N] §1.8 |
| `RoleAssigned` | create (per role) | [N] §1.8 |
| `RoleEnded` | ending a role (no endpoint in this contract yet) | [N] §1.8 · [O] C5 |
| `PermissionGranted` / `PermissionRevoked` | direct grants of any permission **other than** `Payroll.View` (no endpoint in this contract yet) | [N] §1.8 · [O] R41 |
| `PayrollViewGranted` / `PayrollViewRevoked` | bootstrap; future grant endpoint. Emitted **instead of** `PermissionGranted`/`PermissionRevoked` for `Payroll.View`, never both. | [N] §1.8 · BR-235 · [O] R41 |
| `PasswordChanged` | change-password | [O] D6 |
| `PasswordReset` | reset-password | [O] D6, R15 |
| `AccessDenied` | every 403 to an authenticated user (not the web CSRF-header rejection, §6) | [N] §1.8 · UC-002 · [O] R12, R40 |

- `RoleAssigned` is written by create (one per role) **and by the bootstrap seeder** (Admin). [O] R41
- Every audit event carries an **`actor_type`** (`User` or `System`), a nullable **`actor_user_id`** (set when, and only when, `actor_type = User`; a CHECK constraint links the two), and the correlation ID. Bootstrap seeding and the break-glass CLI (§11) use `actor_type = System`. [N] ح-193 · [O] R8, R40
- **Serilog** records: every failed login with its real reason, every 403 (including the CSRF-header rejection), every refresh failure, every bootstrap-protection 409. Never passwords, temporary passwords, tokens or hashes. [N] FR-AUTH-002 · audit addendum §9 · [O] R11, R12, R40
- Session revocations are recorded in the sessions table (`revoked_at`, `revoke_reason`). No separate audit event is defined for them. [E]

---

## 15. Out of scope for this contract

Account lockout (plan §4 Q5, deferred) · endpoints for assigning/ending roles after creation, granting/revoking direct permissions and `Payroll.View` (FR-AUTH-005; deferred — when added, they must reject the bootstrap admin per §11, R27/R28) · linking a user to a party as a separate step, for non-Engineer users (Engineers are linked at creation, §9.1, R31) · the remaining §7.3 permissions (Q6) · self-service password recovery ("forgot password" stays disabled in both apps) · the exact invocation of the bootstrap break-glass CLI and its operating procedure (R29, R32; defined with that slice and in operations documentation — it is not an HTTP API).

---

## 16. Frontend impact (current apps vs this contract)

The apps were built mock-first before this contract existed. Changes a frontend task must make (not done here):

1. Prefix every path with `/api/v1`.
2. Send `clientApp` on login (`admin` / `employee`); the Admin web build sends `X-Client-Platform: web` and `X-CSRF-Protection: 1` on refresh/logout, and uses `withCredentials` so the cookie flows; it no longer stores a refresh token.
3. Remove every use of `12345678`: the change-password client rule becomes "≥ 8 characters and different from the current password"; the create/reset dialogs show the `temporaryPassword` from the response, with a copy action.
4. Drop the distinct "account disabled" login message (`users.disabled`); a disabled login is `auth.invalid_credentials`.
5. Handle the new codes (§12), notably `auth.app_access_denied`, `auth.session_expired`, `auth.current_password_incorrect`.
6. `GET /roles` returns `code` only; map codes to Arabic labels via i18n.
7. Never refresh in the background (§3.6).
8. `mustChangePassword` stays in the login response and `/auth/me` (so the existing casts remain valid).
9. Mobile builds send `X-Client-Platform: mobile` (or omit it); only the browser build sends `web`. Neither value affects authorization.
10. Read `isBootstrapAdmin` from the user object and hide "reset password", "disable" and "reactivate" (and any future role editing) for that account; handle `users.bootstrap_account_protected` anyway. The bootstrap admin can still use the normal change-password screen for its own password.
11. After reactivation, nothing is restored: the reactivated user signs in again.
12. **Reset dialog only on success (F-05).** Today the Admin app shows the password dialog even when reset-password fails (for example 409 `users.bootstrap_account_protected`, 409 `users.already_disabled` or 404). Show the `temporaryPassword` dialog only on a 200, and the matching error otherwise.
13. **Store the tokens returned by change-password (F-08).** Today both apps discard them. The response replaces the session's tokens (§5.5, R36): keep the new access token, and on mobile store the new refresh token; continuing with the old ones fails.
14. **Voluntary change-password route (F-09).** Today both routers send a signed-in user away from `/change-password` unless the change is forced. Any signed-in user — notably the bootstrap admin, who is never forced (R26) — must be able to reach it.
15. **Refresh rule (R37).** Refresh once on a 401 from `/auth/me` and `change-password` as for any other call; never on login, refresh or logout. If logout fails, clear the local session anyway.
16. **Create Engineer users with `partyId` (R31).** The new-user form must collect the engineer's Party when `Engineer` is selected, and handle `users.party_required`, `users.party_invalid` and `users.party_already_linked`.
17. **Refresh within the reuse grace (R35)** may return no new refresh token (§5.2); the client then keeps the one it has.

---

## 17. Revision history

| Date | Change |
|---|---|
| 2026-09-27 | v1. Written from owner decisions C1–C5, R1–R19 and the reconciliation report. Engineering details A–L resolved as [E] proposals. |
| 2026-09-27 | v1.1. Owner clarifications R20–R25: bootstrap admin protected from reset and identified by `is_bootstrap_admin` (§9.6, §11); idle timeout redefined as a refresh-based inactivity policy (§3.6); `SameSite=Strict` tied to the same-site deployment model (§3.4); `X-Client-Platform` is `web`/`mobile`, a transport hint only (§5.1); app-access and disabled-login behaviour confirmed (§4); reactivation restores no session (§9.5). |
| 2026-10-01 | v1.2. Owner decisions R26–R27: the bootstrap admin may change its own password (§5.5, §11); it is protected from reset, disable, reactivate and role changes, enforced server-side (§9.4–§9.6, §11, §12, §16). |
| 2026-10-01 | v1.3. Owner decisions R28–R29: bootstrap admin protected from direct-permission grants/revokes, including its `Payroll.View` (§11, §12, §15); no API recovery path for a lost bootstrap password, recovery is out-of-band and an operational procedure must exist (§11). Confirmed as owner decisions: reactivate on the bootstrap admin → 409, and protection rejections are 409 + Serilog, not `AccessDenied`. |
| 2026-10-01 | v1.4. Owner decisions R30–R43 from the final contract audit: one account per Party and `partyId` required for Engineers (§9.1, §12); bootstrap break-glass — second Admin and operator-only CLI (§9.6, §11, §15); password hash and transparent rehash, dummy hash on login (§5.1); 30-second refresh reuse grace (§3.3, §5.2); change-password consumes and reissues the refresh token (§3.3, §5.5); refresh rule limited to login/refresh/logout (§6); `client_platform` stored on the session (§3.3, §5.1); users-endpoint check order (§9, §11); `actor_type`/nullable `actor_user_id`, CSRF rejection Serilog-only (§6, §8, §14); bootstrap audit events and `PayrollViewGranted` instead of `PermissionGranted` (§11, §14); protection checks the target (§11); duplicate/deprecated role and disabled-reset rules (§9.1, §9.6, §12). Header corrected; revision history put in date order; frontend items F-05, F-08, F-09 and the new R-driven items added to §16. |
