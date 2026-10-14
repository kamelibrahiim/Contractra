# Admin-created user accounts + authentication — implementation plan

> Planning document. It records decisions and their history; it is not itself an implementation record. What is actually built is tracked in `docs/implementation/backend/sprints/sprint-02.md` §A and in Git.

Status: 🟡 Re-baselined on contract **v1.5** — §1D (R44–R45, E1–E16, 2026-10-15) and §1B (R1–R43) supersede parts of §1; §5 Slices 2, 4, 6 and 7 are superseded by the contract and `sprint-02.md`
Date: 2026-09-26 (revised after owner review) · 2026-09-27 (reconciliation decisions R1–R25) · 2026-10-01 (R26–R43, re-baseline) · 2026-10-15 (R44–R45, E1–E16, audit resolution)
Source task: `claude-code-prompt-01-backend.md`
Scope: `Contractra.Domain`, `Contractra.Application`, `Contractra.Infrastructure`, `Contractra.Api`, both test projects, `docs/`. `apps/` is out of scope.

> ⚠️ **Read §1D, then §1B.** The owner's decisions after the cross-project reconciliation and the contract audits are recorded in §1B as R1–R43; the owner instructions of 2026-10-15 and the engineering decisions that resolve the documentation-consistency audit are in §1D. Where §1D or §1B and anything below them disagree, **the later section wins**; for the auth and user-management API, `auth-and-users-api-contract.md` (**v1.5**) is the source of truth. In particular: **C4 is superseded by R1** (forced password change is back), **C2 is amended by R13** (two app-access permissions added), and **the reset part of R3 is superseded by R44** (a reset no longer hands a temporary password to the Admin). §5 Slices 2, 4, 6 and 7 describe the pre-reconciliation design and are marked superseded; they are kept as written so the history of the decision is visible.
>
> *Correction (2026-10-15):* the first line of this document used to say no code or migration had been made "while producing this document". That was true of the 2026-09-26 draft only. Slice 1 is implemented and committed (`1e10742`, fixtures `bc63d5f`).

---

## 0. Verified current state (corrects the task file's assumptions)

The task file states "UserRole, UserPermission, UserPartyLink are NOT implemented" and treats US-BE-002/US-BE-003 as open. That is stale. Verified directly against code and `docs/decisions/0003-user-identity-persistence.md` (updated 2026-09-26):

- **Done:** `User`, `UserRole`, `UserPermission`, `UserPartyLink` — Domain entities, EF Core configuration, PostgreSQL constraints (partial unique indexes, `EXCLUDE` overlap constraints, `CHECK`, `RESTRICT` FKs), and tests. Five migrations: `AddUsers`, `MakeUserLoginIdentifierCaseInsensitive`, `AddUserRoles`, `AddUserPermissions`, `AddUserPartyLinks`.
- `User` has no `password_hash`, no `must_change_password`, no `created_at`.
- `Roles`/`Permissions`/`RolePermissions` exist (ADR 0002) with the `Payroll.View` role-grant guard (triggers). No seed data.
- **Not done, confirmed by direct search:** `Contractra.Application` has no files beyond the template `Class1.cs`. No authentication/JWT/password-hashing code anywhere (`PasswordHasher`, `JwtBearer`, `AddAuthentication`, `IPasswordHasher`, `password_hash` — zero matches). No `audit_events` table, migration, or `IAuditWriter`. `Contractra.Api/Program.cs` still has the `/weatherforecast` template and no real endpoints.

**Consequence for the suggested slice order in the task file:** slice 1 ("finish sprint-01 persistence: UserRole + UserPermission") is already done. The plan below starts from the roles/permissions catalog seed.

---

## 1. Confirmed decisions (owner-approved 2026-09-26 — implement exactly as stated, do not re-derive or re-ask)

These six decisions were made explicitly by the project owner in response to the draft plan. They override the corresponding items in the source task file (`claude-code-prompt-01-backend.md`) where they conflict with it. Each is recorded here as the confirmed instruction for Sprint 2; each ADR touched below must cite it as an **owner decision**, not a confirmed business rule, unless a document citation is given.

### C1 — Sprint tracking: new `sprint-02.md`, Sprint 1 stays closed
Sprint 1 (`sprint-01.md`, dated 2026-09-23) is not modified or appended to. This work is tracked as **Backend Sprint 2** in a new `docs/implementation/backend/sprints/sprint-02.md`, written once this plan's remaining open items (§4) are resolved.

### C2 — Permission catalog: seed only what is active today

> 🔶 **Amended by R13 (2026-09-27):** two app-access permissions, `Apps.Admin.Access` and `Apps.Employee.Access`, are added to the seed with default role grants. The rest of C2 (no other §7.3 permissions yet; future modules seed their own) still holds.
`system-specification-and-team-reference-ar.md` §7.3 remains the source catalog (still 🔵 Proposed, not confirmed as a business rule — see §3 D3). The seed does **not** cover the full ~35-permission catalog. Only permissions relevant to currently implemented Identity/Authorization functionality are seeded now:

- `Users.Manage`, `Permissions.Grant`, `Roles.Manage` (§7.3 Identity group — all three exist today with real Domain/DB behaviour behind them: user creation/disable/reactivate, direct permission grants, and the role catalog respectively).
- `Payroll.View` (§7.3 Labor group — exists as a permission code today specifically because BR-235/ADR 0002 already builds its two-sided enforcement; it is a special case seeded on its own, not part of "seed the Labor group").

No other §7.3 group (Parties, Operations, Custody, Expenses, the rest of Labor, Suppliers, Claims, Agreements, Accounting, Reports, Import, Audit) is seeded now, because none of those modules exist yet. Each future module introduces its own permissions in its own migration when it is built — this is the standing rule for Sprint 3+ work, not just a Sprint 2 scoping note.

**Consequence for role↔permission mapping (§3 Q1, still technically open on the *full* catalog):** for the three Identity permissions above, §7.3 marks all three ✅ for `Admin` only, and ❌ for `Engineer`/`OfficeStaff`/`Accountant`. That specific, narrow mapping is unambiguous regardless of §7.3's overall Proposed status, so it is used as-is: only `Admin` receives `Users.Manage`, `Permissions.Grant`, `Roles.Manage` via `role_permissions`. `Payroll.View` is granted to no role (see C3).

### C3 — Bootstrap admin gets `Payroll.View` directly, never through a role
The bootstrap admin (D2) receives `Payroll.View` as a **direct `UserPermission` grant**, created via `User.GrantPermission` in the same seeding step that creates the admin account. `Payroll.View` is never added to `role_permissions` for any role, including `Admin` — this is not new; it is BR-235 as already enforced by the ADR 0002 database triggers, and this decision confirms the seeder must use the sanctioned path (`UserPermission`, ADR 0003 §9.2.1) rather than attempt the forbidden one.

### C4 — No `must_change_password` in Sprint 2

> ⛔ **Superseded by R1–R3 (2026-09-27).** The owner reinstated the forced password change and replaced the shared default password with a random temporary password. C4 and its consequences below are kept as history only; do not implement them.
`must_change_password` is not implemented. It has no basis in `logical-data-model-ar.md`, the audit addendum, or any other confirmed source — it existed only as an owner decision (D1) in the source task file, and that decision is now withdrawn for Sprint 2.

**Direct consequence for D1's forced-password-change flow:** D1 as written depends structurally on `must_change_password` (login succeeds but only change-password/logout are allowed while the flag is true). Per owner decision, this gate is **dropped entirely** for Sprint 2:
- A user created by `CreateUser` (Slice 4) receives the configured default password (`Identity:DefaultInitialPassword`), hashed, with **no forced-change flag and no restricted-access gate**. They can log in and use every endpoint their roles/permissions allow immediately.
- `ChangePassword` (Slice 4) still exists as a **voluntary**, always-available action — a user may change their password whenever they want, subject only to D1's other stated rule (new password ≠ default, minimum length 8; that length/distinctness rule is kept, since it is a property of `ChangePassword` itself, not of a forced-change gate).
- `ResetPassword` (Admin action, D1) still resets a user back to the default password — but with no flag to set afterward, since none exists. The user simply has the default password again until they voluntarily change it.
- **This is a real, intentional weakening of the original security intent** (a leaked/known default password gives standing access, not just one-time access), accepted here because the owner has withdrawn the only basis for the stronger version. It must be recorded as such in the ADR (§5, Slice 2/4), not silently softened.

### C5 — Audit event names: `UserDisabled` / `RoleEnded` (Logical Data Model naming), not `UserDeactivated` / `RoleRevoked`
`logical-data-model-ar.md` §1.8 is canonical. The event list used throughout this plan (Slice 3, Slice 4) is corrected to:

`UserCreated`, `UserDisabled`, `UserReactivated`, `RoleAssigned`, `RoleEnded`, `PasswordChanged`, `PasswordReset`, `PermissionGranted`, `PermissionRevoked`, `PayrollViewGranted`, `PayrollViewRevoked`.

(`PasswordChanged`/`PasswordReset` are not in §1.8's list, which does not cover password events at all — they remain as named in the source task's D6, since no document defines them differently or forbids them; only the two names §1.8 *does* define, `UserDeactivated→UserDisabled` and `RoleRevoked→RoleEnded`, are corrected.)

### C6 — This document itself
This plan is updated in place (not re-created) to record C1–C6 as confirmed, and to separate confirmed decisions (§1), proposed design not yet acted on (§2), from genuinely unresolved/open items (§4), per the owner's explicit instruction. No code, migration, seed, or `apps/` change is made as part of this update.

---

## 1B. Owner decisions after reconciliation (2026-09-27 … 2026-10-01) — R1–R43

**Context.** A parallel frontend implementation (US-008…US-012, both Flutter apps, built mock-first) assumed an auth contract that neither the authoritative documents nor this plan had settled. The read-only reconciliation found that spec §26.3 item 5 explicitly **defers** "the specific authentication mechanism and token validity", and that none of the forced-change, default-password, reset, token-lifetime or token-storage rules exist in any source document. The owner answered each open question. These are **owner decisions**, not confirmed business rules, and every ADR that implements one must say so.

| # | Decision | Replaces / affects |
|---|---|---|
| **R1** | **Forced password change is required.** A user created by an Admin, and a user whose password an Admin resets, is flagged `must_change_password = true`. While the flag is true the user is restricted until they change their password. | Supersedes **C4**. Restores D1's gate and the `auth.password_change_required` error code. |
| **R2** | **Random temporary password, not a shared default.** On creation the server generates a **cryptographically random, 12-character password of mixed letters and digits**. It is returned **once**, in the create response body, for the Admin to hand over. It is stored only as a hash, never in plaintext, and is never logged or audited (logical model §1.7, NFR-SEC-002). | Replaces D1's shared `12345678` and the `Identity:DefaultInitialPassword` configuration key, which is dropped. |
| **R3** | **Password reset exists** and uses the same mechanism: an Admin resets a user's password, the server generates a new random temporary password (R2 rules), returns it once, and sets `must_change_password = true`. | Resolves reconciliation §C items 1–3. |
| **R4** | **Reactivation requires `Users.Manage`.** | Resolves reconciliation §D permission question. |
| **R5** | **Token model.** JWT access token, **30 minutes**. Refresh token, **7 days**, **rotated on every use**. Refresh tokens are stored **hashed** server-side. **All of a user's sessions are revoked** on disable and on password reset; on password change, **all other sessions** are revoked and the current one continues *(wording corrected 2026-10-01 to match the owner clarification below and contract §3.3)*. **Permission freshness uses claims plus a per-user security version:** permissions travel in the JWT, but every request checks a per-user security version stored in the database; any change to the user's roles, direct permissions or status, a reset, or a password change increments it, which invalidates every previously issued access token immediately. | Settles D5 formally. The hashed-storage choice is the owner's resolution of the logical model §1.7 wording ("tokens are not stored and not logged"): a hash is stored, the token itself is not. |
| **R6** | **Web clients:** the refresh token is carried in an **HttpOnly, Secure, SameSite cookie**, never exposed to JavaScript. | Resolves frontend gap G9 for web. Mobile clients are not covered by R6 (see §1C). |
| **R7** | **API path versioning:** all endpoints live under **`/api/v1/`**. | Applies spec §26.1 principle 8. |
| **R8** | **Correlation ID:** the client **may** send `X-Correlation-ID`; if it is missing or invalid the server generates one. The server always echoes it in the response header and uses it in Serilog and in audit events. | Applies spec §26.1 principle 6. |
| **R9** | **Create-user request carries `roleCodes`** (role code strings), not role IDs. | Matches the frontend request shape. |
| **R10** | **`GET /api/v1/auth/me` returns effective permissions and `mustChangePassword`** (together with the user's id, login identifier and roles). Effective permissions = role-derived ∪ active direct grants; `Payroll.View` only from a direct grant (BR-235, CAP-02). | Confirms the frontend `/me` shape in principle. |
| **R11** | **Login does not reveal a disabled account.** A disabled user's login attempt gets the **same generic response as invalid credentials**. The real reason is still logged to Serilog (FR-AUTH-002). | The frontend's distinct "account disabled" login message must go. `users.disabled` is not returned by login. |
| **R12** | **Access denied (403) is recorded in both the audit log and Serilog** (`AccessDenied`, logical model §1.8; UC-002 exception flow). | New backend obligation; depends on the minimal audit table (Slice 3). |
| **R13** | **Explicit app-access permissions.** Two new permission codes: **`Apps.Admin.Access`**, granted by default to `Admin`, `OfficeStaff` and `Accountant`; **`Apps.Employee.Access`**, granted by default to `Engineer`. Both are granted through `role_permissions` in the seed. A user without the permission for the app they are signing in to is refused at login for that app. These codes are **not** in spec §7.3; they are owner-decided additions. | Amends **C2**. Resolves frontend gap G7. Requires amending the Slice 1 seed (§1C). |
| **R14** | **Changing a password requires the current password**, including during the forced change (where the current password is the temporary one). | Confirms the frontend `change-password` request shape. |

### Final owner decisions on the remaining open items (2026-09-27) — R15–R19 and clarifications

| # | Decision |
|---|---|
| **R15** | **Password reset requires `Users.Manage`.** No separate reset permission. `Users.Manage` covers create, disable, reactivate and reset. Reset writes the `PasswordReset` audit event. |
| **R16** | **New-password rules:** minimum **8 characters**, and the new password **must differ from the user's current password**. The old "must differ from the default" rule is removed completely. There is **no shared default password** and **no default-password configuration setting**; `12345678` must not appear anywhere in the production authentication contract or implementation. |
| **R17** | **Idle timeout adopted (NFR-SEC-007):** Engineer **8 hours**; Accountant, Admin and OfficeStaff **60 minutes**. The 7-day refresh lifetime is the **maximum** session lifetime and does **not** replace the idle timeout; a session expires when its applicable idle timeout is exceeded. Full session policy: access token 30 minutes · refresh/session maximum 7 days · rotation on · idle timeout per role. |
| **R18** | **Mobile refresh token:** access token in memory; refresh token returned in the authentication response body and stored with secure device storage (`flutter_secure_storage`); never logged. **Web** keeps R6 (access token in memory, refresh token in an HttpOnly + Secure + SameSite cookie). The cookie strategy is **not** applied to mobile. On both, the backend stores only a cryptographic hash of the refresh token, never the token. |
| **R19** | **Bootstrap admin is exempt from the forced password change** (`mustChangePassword = false`). Its password still comes from `Bootstrap:*` configuration / user-secrets; it is created only once; its password is never overwritten by the seeder or by the normal create flow. |

### Final clarifications (owner, 2026-09-27) — R20–R25

| # | Decision |
|---|---|
| **R20** | **Bootstrap admin cannot be reset through the normal `Users.Manage` reset flow;** the reset flow rejects it. *(Wording corrected 2026-10-01: the earlier text said the account is "managed only through the bootstrap configuration/secret mechanism". That is superseded by R26 — it may change its own password through the API — and by R29/R32 — a lost password is recovered out-of-band, and changing `Bootstrap:AdminPassword` is not a recovery mechanism.)* The identification mechanism is documented in the API contract §11 (a seeder-only `is_bootstrap_admin` flag — an engineering choice). |
| **R21** | **Idle timeout is a refresh-based inactivity policy.** Values unchanged (Engineer 8 h; Accountant, Admin, OfficeStaff 60 min). The session's activity timestamp is evaluated and updated **at refresh**; **no database write on every API request**. The 30-minute access-token lifetime does not change the business idle-timeout values. |
| **R22** | **`SameSite=Strict`** for the current same-site deployment model. A cross-site deployment would need a different cookie/CSRF strategy; not supported and not implemented now. |
| **R23** | **`X-Client-Platform`** supports `web` and `mobile`. It is only a transport/response-shaping hint, **not a security boundary**; authorization must never rely on it. |
| **R24** | **App access** stays `403 auth.app_access_denied` after successful credential verification; disabled accounts stay generic; app access is server-enforced. |
| **R25** | **Reactivation does not restore previous sessions**; the user must authenticate again. |
| **R26** (2026-10-01) | **Bootstrap admin may change its own password** through the normal ChangePassword endpoint, with the normal rules (at least 8 characters, different from the current password) and effects (hash updated, security version bumped, other sessions invalidated). It stays exempt from the forced change. The bootstrap configuration/seeder never overwrites an existing bootstrap admin password. |
| **R27** (2026-10-01) | **Bootstrap admin is protected from normal administrative mutation:** it cannot be reset, disabled, have roles assigned/ended/changed, or be deleted through normal administration. Enforced **server-side**; hiding actions in the Admin UI is not a security control. It may still change its own password (R26). The rejection also applies to reactivate (owner-confirmed 2026-10-01). A protection rejection is HTTP 409, logged to Serilog, and not recorded as an `AccessDenied` audit event (owner-confirmed 2026-10-01). |
| **R28** (2026-10-01) | **Bootstrap admin is also protected from direct-permission mutation.** Future direct-permission grant/revoke operations must reject it with `409 users.bootstrap_account_protected`, **including revoking its direct `Payroll.View`**. It may still change its own password. Server-side enforcement is mandatory. |
| **R29** (2026-10-01) | **No API recovery path for the bootstrap admin password.** If it is lost, recovery is **out-of-band** through a secure deployment/infrastructure procedure; such a procedure must exist, but its details are not defined in the API contract. The normal `Users.Manage` reset stays blocked, and the seeder never overwrites an existing bootstrap password. |

### Owner decisions after the final contract audit (2026-10-01) — R30–R43

These are **owner decisions**, not confirmed business rules. They resolve the findings of the read-only contract consistency audit of 2026-10-01 and are applied in contract v1.4.

| # | Decision |
|---|---|
| **R30** | **`UNIQUE(party_id)` on `user_party_links`: one account per Party.** Reverses the 2026-09-26 owner decision that a party may be linked from more than one user (ADR 0003 §9.3, §10). A lost password is solved by reset and a disabled account by reactivate — never by a second account. Before the future migration, Slice 2 must verify that no existing rows violate it. |
| **R31** | **`CreateUser` for the `Engineer` role requires `partyId`.** The Party must exist, hold an engineer role, and be unlinked; the user and the link are saved in one transaction. For other roles, linking stays a separate optional step. |
| **R32** | **Bootstrap break-glass** (implements R29; does not change R27): (a) a second Admin account is created right after first run; it may reset any Admin except the bootstrap admin; (b) an operator-only CLI command in `Contractra.Api` (for example `reset-bootstrap-password`), runnable only on the server, writes an audit event (`actor_type = System`) and prints nothing sensitive to logs. |
| **R33** | **Password hash:** ASP.NET Core `PasswordHasher<TUser>` behind an Application `IPasswordHasher`; column `users.password_hash text NOT NULL`; on login, `SuccessRehashNeeded` triggers a transparent rehash. The Domain stores only the hash string. |
| **R34** | **`UserRole.End()` and `UserPermission.Revoke()` become `internal`;** `User.EndRole(...)` / `User.RevokePermission(...)` are the only public paths. Every `User` mutator that changes security state bumps `security_version` inside the aggregate. |
| **R35** | **Refresh reuse grace window of 30 seconds** (multi-tab web): a just-consumed token re-presented within 30 s returns a new access token without rotating or revoking; after 30 s, reuse is treated as theft and the whole session is revoked. |
| **R36** | **`change-password` consumes the current refresh token** and issues a new one in the same session (same shape as refresh). Clients must store the returned tokens. |
| **R37** | **"Never refresh" applies only to login, refresh and logout.** `/auth/me` and `change-password` follow the normal refresh-once-on-401 rule. If logout fails, the client clears its local session anyway. |
| **R38** | **`X-Client-Platform` is read at login only** and stored on the session (`client_platform`); refresh and change-password respond according to the stored value and ignore the header. |
| **R39** | **Server check order:** authentication → password-change gate → permission (403 + `AccessDenied`) → 404 → bootstrap protection (409 `users.bootstrap_account_protected`) → state-transition conflicts (409). A caller without permission can never learn a bootstrap id. |
| **R40** | **`audit_events` has `actor_type` (`User`/`System`) and a nullable `actor_user_id`**, with a CHECK linking them. A web CSRF-header rejection happens before any actor exists: Serilog only, not `AccessDenied`. Contract §6 wording becomes "every 403 to an authenticated user". |
| **R41** | **Bootstrap seeder audits `UserCreated`, `RoleAssigned(Admin)` and `PayrollViewGranted`** (`actor_type = System`). Granting `Payroll.View` emits `PayrollViewGranted` **instead of** `PermissionGranted`, never both. |
| **R42** | **The bootstrap protection check looks at the target** regardless of who the actor is. Login performs a dummy hash for unknown and disabled accounts so timing does not reveal them. |
| **R43** | **Create/reset edge cases:** duplicate role codes → 400 `validation.failed`; a Deprecated role cannot be assigned → 400 `users.role_unknown`; resetting a Disabled user's password → 409 `users.already_disabled` (reactivate first). |

**Clarifications that make R1–R14 precise (owner, 2026-09-27):**
- **App access (R13)** is purely permission-based: a user may use an app only if they hold its permission. The login flow must identify the requesting app so the backend can check the right permission. The request field is an API-contract detail, settled in the contract.
- **Temporary password (R2, R3):** no reveal endpoint; it is available only in the create/reset response. After a successful change, `mustChangePassword` becomes false.
- **Password-change flow (R1, R14, R16):** a user with `mustChangePassword = true` may use only the password-change flow and explicitly allowed session operations. A successful change updates the hash, clears the flag, bumps the security version and invalidates the user's other sessions.
- **Security version (R5):** the access token carries the permissions **and** the user's security version. Every authenticated request does **one indexed lookup** of the current version; a mismatch rejects the token. The version is bumped by: role assignment, role ending, direct permission grant, direct permission revoke, user disable, user reactivate (where security state changes), password reset and password change. Loading all roles/permissions per request is explicitly rejected.
- **Correlation ID (R8):** the client is never required to send one; requests without it are not rejected. Exact validation rules are a contract detail.

The exact engineering details (login app field, allowed calls during a forced change, cookie attributes, CSRF, error codes, revocation and idle-tracking mechanics, security-version failure behaviour, response shapes, pagination, correlation-ID validation) are resolved in `auth-and-users-api-contract.md`, which is the source of truth for backend, Admin Flutter and Employee Flutter.

**Unchanged by R1–R14:** C1 (Sprint 1 closed), C3 (`Payroll.View` direct grant for the bootstrap admin, never via a role), C5 (audit names `UserDisabled`/`RoleEnded`), BR-235, no user deletion, at least one role at creation, the full §7.3 catalog mapping (deferred, §4 Q6) and account lockout (deferred, §4 Q5). The R numbers here follow the owner's own question numbering; they are unrelated to the §4 Q numbers.

## 1C. Consequences and follow-ups created by R1–R14

> **Status 2026-10-01:** the engineering proposals below are resolved in contract v1.4 and by R15–R43. This section is kept as the record of what was open at the time.

**Engineering proposals (to confirm while writing the API contract):**
- **What a user may call while `must_change_password` is true (R1).** The source prompts said "only change-password and logout". R10 has the frontend read `mustChangePassword` from `/auth/me`, and R5 has clients refresh their tokens. Proposal: allow `change-password`, `logout`, `me` and `refresh`; everything else returns 403 `auth.password_change_required`.
- **How the server knows which app a login is for (R13).** It needs the app in the login request (for example an `app` field or a client header). The shape and the refusal response (a generic failure, or a specific code) are not decided.
- **Cookie details (R6):** `SameSite` value (`Strict` or `Lax`), whether the API and the web app share a site, CSRF protection for the cookie-based refresh, and CORS with credentials.
- **Security-version storage (R5):** a column on `users` (for example `security_version`), incremented by the Domain on every relevant change.
- **Error codes:** `auth.password_change_required` returns (R1). New codes for a wrong current password (R14) and an invalid transition on reactivate (reconciliation §D) still need names and HTTP statuses.

**Owner items that were open here are now closed** by R15–R19 above (reset permission, password rules, idle timeout, mobile token delivery, bootstrap admin). The engineering proposals above are resolved in `auth-and-users-api-contract.md`.

**Work to re-plan before Slice 2:**
- **Slice 1** — ✅ amended for R13 (2026-09-27): 6 permissions and 7 role grants; tests updated (RED shown, then GREEN). *Committed 2026-10-15 as `1e10742` (seed, seed tests, ADR 0004); the three existing test classes it required were committed separately as `bc63d5f`.*
- **Slice 2** now covers `password_hash` (R33), `must_change_password`, `security_version`, `is_bootstrap_admin`, the R34 aggregate change and the R30 `UNIQUE(party_id)` change. It is defined as **US-BE-007 in `sprint-02.md`**, which replaces §5 Slice 2 below.
- **Slice 4** gains temporary-password generation (R2, R3) and the password rules (R14, R16).
- **Slice 6** gains the forced-change gate, the web cookie and mobile body delivery (R6, R18), session revocation, the per-role idle timeout (R17), the security-version check, the app-access check, correlation IDs and `/api/v1`.
- **Slice 7** returns the temporary password once from create and reset.
- ✅ The API contract (`auth-and-users-api-contract.md`) is written (2026-09-27; v1.4 on 2026-10-01). Slice 2 is re-planned (US-BE-007, `sprint-02.md`); Slices 3–7 are re-planned against the contract as each one starts.

---

## 1D. Audit resolution (2026-10-15) — owner instructions R44–R45 and engineering decisions E1–E16

**Context.** The read-only documentation-consistency audit of 2026-10-15 found a reset-impersonation risk, no brute-force requirement, and several ambiguities (app authorization, audit fields, identifier matching, Party validity, security-version and migration semantics, slice ordering). The owner then instructed engineering to resolve them, owning routine technical and security decisions but not business policy. The two instructions that change an earlier owner decision are recorded as R44–R45; everything else is an engineering decision (E1–E16), applied in contract v1.5. Items that still need the owner are listed in contract §19 (OC-1 … OC-6).

### Owner instructions (2026-10-15)

| # | Instruction (owner's words, condensed) | Effect |
|---|---|---|
| **R44** | "Choose a secure, practical password-reset design that does not allow an administrator to learn a user's temporary plaintext password and impersonate the user … Preserve the existing bootstrap-admin protections and out-of-band break-glass recovery rules. If a delivery channel is not implemented, do not pretend it exists." | **Supersedes the reset half of R3** (temporary password returned to the Admin). R15 (`Users.Manage`), R20/R27 (bootstrap protection), R43 (no reset of a Disabled user) are unchanged. Creation (R2) is unchanged; its residual risk is contract §19 OC-3. Whether R44 should apply to every account or only to accounts with sensitive permissions is contract §19 OC-1; v1.5 applies it to every account, the safer reading. |
| **R45** | "Define and implement a practical defense against repeated login and reset attempts … without allowing an attacker to trivially lock out another user's account … Do not introduce an unbounded in-memory limiter as the only production defense if the deployment may use multiple instances." | Throttling becomes required (contract §17). **Q5 (lockout) stays deferred** and is now an explicit non-goal: no hard lockout. |

### Engineering decisions

| # | Decision | Why / alternatives rejected | Contract |
|---|---|---|---|
| **E1** | Reset = Admin *issues* (current password invalidated, sessions revoked) → single-use 80-bit code delivered to the user → user completes and chooses the password. No channel exists yet: the API fails closed with 503 and an operator command issues codes on the server. | Returning a reset code to the Admin would reproduce the impersonation. Burning a ticket after N wrong codes was rejected: it would let anyone who knows an identifier cancel a user's reset. | §5.6, §9.6, §10A |
| **E2** | No usable password (pending reset, pre-credential accounts) and expired temporary passwords fail login with the generic 401 and the dummy hash. | Consistent with R11/R42: no state is revealed. | §5.1 |
| **E3** | An account still on its first temporary password is re-armed with `POST /users/{id}/initial-password`, not with a reset; `credentialState` tells the Admin which action applies. | Such an account cannot have acted in the system (§7 gate), so this exposes nothing beyond creation (R2). Without it, an expired temporary password would need the operator. | §9.2, §9.8 |
| **E4** | Temporary passwords: CSPRNG, 12 chars, at least one upper, lower and digit by rejection sampling; expire after 72 h. | R2's "mixed letters and digits" made precise; a never-expiring temporary password is a standing credential known to a third party. | §10 |
| **E5** | Five throttling layers keyed by IP, (identifier, IP), identifier and user; distributed attacks slowed to one attempt per 30 s per account instead of locked; shared PostgreSQL store; in-memory limiter only as a per-instance first line; fail closed if the store is down. | Hard lockout rejected (DoS of legitimate users). In-memory-only rejected (multiple instances). | §17 |
| **E6** | Every endpoint declares an audience (`public`, `any session`, `admin`, `employee`); `admin`/`employee` endpoints require both the matching `app` claim and the app-access permission in `perm`; mismatch → 403 `auth.app_access_denied`; undeclared audience fails closed. | The `app` claim alone was not stated to be checked, and a header is caller-controlled. | §4.1, §9 |
| **E7** | `security_version` starts at 1 and increments by exactly 1 on every successful security-state change inside the aggregate, including assignments made during creation; rejected changes leave it unchanged. The aggregate refuses bootstrap-protected mutations; the database rejects any change to `is_bootstrap_admin` and forbids it together with `must_change_password`. | Removes the "fixed initial value" and "does creation count" ambiguities; defence in depth for R27. | §3.5, §11 |
| **E8** | An Engineer account's Party must be `Active` and hold an `Engineer` party role active on the business date (`PartyRole.IsActiveOn`), business date in `Africa/Cairo` by configuration. | `NeedsClassification` is "unresolved historical names" awaiting the accountant (logical model §2, BR-058). The party-role code literal and the time zone are not fixed by the documents (contract §19 OC-4). | §9.1 |
| **E9** | JWT: HS256 pinned, `iss`/`aud`/`exp`/`nbf` validated with ≤ 30 s skew, required claims, `kid` key ring with planned and emergency rotation; ≥ 256-bit keys; startup fails without a valid key. | Library defaults (5-minute skew, algorithm from header) are too permissive. Refresh tokens are opaque, so emergency rotation signs nobody out. | §3.2, §3.2.1 |
| **E10** | Login and reset completion find users with `lower(login_identifier) = lower(@input)` on the existing expression index; no trimming. | ADR 0003 §3.2's lookup note made a contract rule. | §5.1, §5.6 |
| **E11** | Change-password consumes the latest unconsumed refresh token of the session in `sid`; the request carries no token. | Removes the ambiguity in R36. | §5.5 |
| **E12** | `audit_events` keeps every logical-model §15.4 field and adds R40's `actor_type`; `actor_role_code` follows a deterministic rule; `source` uses the logical model's values with a stated mapping; state changes fail closed (rollback, 500); `AccessDenied` stays a denial even if its own audit write fails, with a Serilog alert. | The audit found the contract and §15.4 disagreeing; nothing from §15.4 is dropped. | §14.1, §14.2 |
| **E13** | Input limits (identifier 256, password 128, reset code 32, body 16 KiB) checked before hashing; bootstrap password must satisfy the same rules. | Prevents hashing-cost abuse with huge inputs; NIST SP 800-63B allows a 64+ maximum. | §1, §5.5, §11 |
| **E14** | HTTPS only (no HTTP redirect for the API), HSTS on the web origin, secrets only in secret stores, NTP, least-privilege database roles, explicit trusted proxies and CORS origins. | Operational requirements the contract lacked. | §1, §18 |
| **E15** | Idle timeouts are configuration with the NFR-SEC-007 defaults, bounded 5 min … 24 h. | NFR-SEC-007 says "configurable"; bounds prevent a typo from disabling the control. | §3.1 |
| **E16** | Migrations: `Up` is additive and preserves data (backfills, creates new indexes before dropping old ones, fails rather than rewrites on conflicting data). `Down` exists for development and test databases and is tested there; it is not a production rollback tool — production goes forward with a corrective migration. Rolling back `SeedAuthorizationCatalog` while users reference seeded roles fails atomically on the `RESTRICT` foreign keys and loses nothing. | Answers the audit's rollback questions without pretending Down is safe on live data. | `sprint-02.md` US-BE-007; ADR 0003 §11 |

**Correction to C5's rationale.** C5 says the logical model's event names win "since it is the higher-precedence source per `documentation-index-ar.md`". The index ranks `system-specification-and-team-reference-ar.md` at 15 and `logical-data-model-ar.md` at 16 (1 is highest), and spec UC-002 lists `UserDeactivated` / `RoleChanged`. C5's names stand as an **owner decision**, not by precedence; the spec/logical-model disagreement is reported for the documentation owner (contract §19 OC-5).

---

## 2. Proposed design (not yet a confirmed rule or owner decision — implement as designed unless flagged otherwise; revisit if evidence changes)

These are engineering choices this plan makes to carry out §1's decisions and the source task's remaining items. They are not blocked on approval the way §4's items are, but they are explicitly *design*, not fact, and should be reviewed during implementation rather than treated as settled history.

- **Slice numbering and order** (§5) — the 7 slices and their order are this plan's own proposal, not dictated by any document.
- **File/class names** throughout §5 (e.g. `IPasswordHasher`, `AuditWriter`, `BootstrapAdminSeeder`, endpoint file names) — proposed, not fixed; the Api/Application projects have no established convention yet since they are currently empty beyond templates, so this plan is also where those conventions get set.
- **`AuditEvent` field shape** (Slice 3) — to be drawn from `logical-data-model-ar.md` §15.4 at implementation time; not fully specified here.
- **Error code set** (§6) — the five names in the source task are confirmed as required; any additional codes needed during implementation (e.g. weak-password-on-change, invalid-refresh-token) are proposed at implementation time, following the same `domain.reason` shape, and are not to be silently invented if they touch a documented rule.
- **"Effective permissions" computation for the JWT** (Slice 6) — proposed as a narrow, token-scoped read, not a general authorization-resolution service. If a future sprint needs the general version, this narrow one should not be assumed sufficient without review.

---

## 3. Background: how the confirmed decisions were reached (for traceability, not action)

### D3 origin — role→permission mapping question
`system-specification-and-team-reference-ar.md` §7.3 is the only permission catalog in the docs, explicitly marked **🔵 Proposed — extensible** (line 465), not confirmed. C2 resolves the *scope* question (seed only the active subset) and, for that subset specifically, the mapping is unambiguous (§1 C2's "Consequence" paragraph). The mapping question for the other ~30 permissions remains genuinely open but is now moot for Sprint 2, since none of them are seeded.

### D2 origin — bootstrap admin + Payroll.View question
The source task's D3 explicitly asked "whether the seeded admin should also get `Payroll.View`." Resolved by C3: yes, as a direct grant.

### D1 origin — `must_change_password`
Confirmed absent from `logical-data-model-ar.md` and the audit addendum before this plan's first draft. Resolved by C4: dropped for Sprint 2, with the consequence spelled out there.

### D6 origin — audit event naming
`logical-data-model-ar.md` §1.8 names `UserDisabled`/`RoleEnded`; the source task's D6 list said `UserDeactivated`/`RoleRevoked`. Resolved by C5: the Logical Data Model wins, since it is the higher-precedence source per `documentation-index-ar.md`.

---

## 4. Unresolved / open items (still need an answer before the affected slice starts — not invented here)

### Q5 — Account lockout
FR-AUTH-002/BR-193 require logging failed logins but say nothing about lockout. The source task's D5 explicitly defers this ("No account lockout in this task — note it as a follow-up"). This is an accepted, documented gap for Sprint 2, not something needing a decision now — listed here only so it is not silently forgotten for Sprint 3+.

### Q6 — Full §7.3 catalog role↔permission mapping
Deferred, not decided, and not blocking Sprint 2 per C2. When a future module is built and its permissions are seeded, the same "stop and ask if §7 doesn't state a mapping clearly" rule from the source task's D3 applies again at that time.

No other undocumented business rule was found; everything else in this plan cites a source or a confirmed owner decision (§1).

---

## 5. Slices, in order

Slice 1 from the task file's suggested order is removed (already done, see §0). Numbering below is the plan's own (§2), referenced in commit messages as `feat(auth-slice-N): ...`.

### Slice 1 — Roles & permissions catalog seed (C2, C3)

> ✅ **Built, amended for R13, committed (`1e10742`; fixture fixes `bc63d5f`).** The final seed has **6 permissions and 7 role grants** (Admin ← `Apps.Admin.Access`, `Users.Manage`, `Permissions.Grant`, `Roles.Manage`; OfficeStaff ← `Apps.Admin.Access`; Accountant ← `Apps.Admin.Access`; Engineer ← `Apps.Employee.Access`). The "4 permissions / 3 rows" figures below are historical. The ADR is `docs/decisions/0004-authorization-catalog-seed.md`.

- **Files:**
  - New EF Core migration `SeedAuthorizationCatalog` (data-only, deterministic `Guid` v7-shaped IDs, additive, no schema change).
  - No Domain/Application changes — `Role`/`Permission`/`RolePermission` already exist (ADR 0002).
- **Content:**
  - 4 roles: `Engineer`, `OfficeStaff`, `Accountant`, `Admin`.
  - 4 permissions only: `Users.Manage`, `Permissions.Grant`, `Roles.Manage`, `Payroll.View`.
  - `role_permissions` rows: `Admin` ← `Users.Manage`, `Permissions.Grant`, `Roles.Manage` (per C2's consequence). No other role gets any of these four. `Payroll.View` gets **zero** `role_permissions` rows for any role (C3, BR-235) — the existing DB trigger from ADR 0002 would reject it anyway if attempted, so this also serves as a negative test of that trigger.
  - **Bootstrap admin's `Payroll.View` grant is NOT part of this migration** — it is created by the Slice 5 seeder at startup, as a `UserPermission`, once the bootstrap admin user exists (C3). This migration only seeds the reference data (roles, permissions, role_permissions), not any user-specific grant.
- **Tests:** integration test asserting exactly 4 roles, 4 permissions, and the 3 `role_permissions` rows described above exist after migration; a test asserting `Payroll.View` has zero `role_permissions` rows; a re-run-safety test (migration is additive/idempotent by EF's own mechanism — test confirms no duplicate-key error).
- **ADR:** new `docs/decisions/0004-authorization-catalog-seed.md` recording C2/C3 as owner decisions, citing §7.1/§7.3 exactly, noting §7.3's 🔵 Proposed status, and explicitly stating that future modules seed their own permissions in their own migrations (the standing rule from C2, not just this slice's scope note).

### Slice 2 — `password_hash` + hasher (C4-adjusted: no `must_change_password`)

> ⛔ **SUPERSEDED by contract v1.4 and US-BE-007 in `sprint-02.md`.** This text describes the withdrawn C4 design (no `must_change_password`). Kept as history only; do not implement it.

- **Files:**
  - `Contractra.Application/Identity/IPasswordHasher.cs` (interface: `Hash(string password)`, `Verify(string hash, string password)`).
  - `Contractra.Infrastructure/Identity/AspNetPasswordHasher.cs` (wraps `Microsoft.AspNetCore.Identity.PasswordHasher<TUser>`).
  - `Contractra.Domain/Identity/User.cs`: add `PasswordHash` (private set, required) only — **no `MustChangePassword` field** (C4). Needs a Domain method to set the password hash at creation and a `SetPasswordHash`/`ChangePassword`-style method for later changes, following the existing `Disable()`/`Reactivate()` guard pattern (no silent no-ops).
  - `Contractra.Infrastructure/Persistence/Configurations/UserConfiguration.cs`: map the one new column.
  - New EF Core migration `AddUserPasswordHash` (additive: one new non-null column on `users`; needs a backfill/default strategy for the `NOT NULL` add — call out at implementation time, though no real user rows exist outside dev today).
- **Domain tests:** password hash is opaque to the Domain (Domain never hashes — it stores whatever the Application layer computed).
- **Never logged/audited:** confirmed no logging call touches this field anywhere in this slice.
- **ADR:** extends `0003-user-identity-persistence.md` with a new subsection recording D4/C4 as an owner decision (not a confirmed rule) — `password_hash`'s handling rule *is* confirmed by `logical-data-model-ar.md` §1.7 (never logged/audited); the *absence* of a forced-change gate is C4's explicit, recorded trade-off (quote C4's consequence paragraph verbatim or near-verbatim).

### Slice 3 — Minimal `audit_events` (D6, corrected naming per C5)

- **Files:**
  - `Contractra.Domain/Audit/AuditEvent.cs` (or under a new `Contractra.Domain.Audit` namespace) — append-only, matching `logical-data-model-ar.md` §15's documented shape as closely as this minimal slice needs (event type, actor, entity type/id, timestamp, payload — exact field list drawn from §15.4 at implementation time).
  - `Contractra.Application/Audit/IAuditWriter.cs` (interface, e.g. `WriteAsync(AuditEvent)` called inside the same `DbContext`/transaction as the mutation).
  - `Contractra.Infrastructure/Audit/AuditWriter.cs` + `AuditEventConfiguration.cs`.
  - New EF Core migration `AddAuditEvents` (one table, append-only — no update/delete path in the Domain, matching §15's documented hard DB-level UPDATE/DELETE block; needs a DB-level trigger or rule forbidding UPDATE/DELETE, mirroring the ADR 0002 trigger pattern).
- **Events in scope (corrected per C5):** `UserCreated`, `UserDisabled`, `UserReactivated`, `RoleAssigned`, `RoleEnded`, `PasswordChanged`, `PasswordReset`, `PermissionGranted`, `PermissionRevoked`, `PayrollViewGranted`, `PayrollViewRevoked`.
- **Tests:** integration test proving atomicity — a forced audit-write failure rolls back the paired mutation (BR-186); a test proving `UPDATE`/`DELETE` on `audit_events` is rejected at the DB level.
- **ADR:** new `docs/decisions/0005-minimal-audit-events.md`, explicitly scoped as "W1-C minimal slice, not the full audit module," citing BR-186/§1.8/mvp-entity-scope-matrix-ar.md's W1-C note, and recording C5's naming correction.

### Slice 4 — Application layer: `CreateUser`, `DisableUser`, `ReactivateUser`, `ResetPassword`, `ChangePassword`

> ⛔ **SUPERSEDED by contract v1.4.** The default password, "reset to default", "≠ default" rule, role ids and the absence of `MustChangePassword` below are withdrawn (R1–R3, R9, R16, R31, R39, R43). Kept as history only; Slice 4 is re-planned in `sprint-02.md` when it starts.

- **Files:** `Contractra.Application/Identity/Commands/{CreateUser,DisableUser,ReactivateUser,ResetPassword,ChangePassword}.cs` (command + handler pairs, or a single use-case class per action — exact shape follows whatever thin pattern the Api layer expects; no Application-layer conventions exist yet in this codebase, so this slice also establishes them).
- **`CreateUser`:** takes login identifier + at least one role id (FR-AUTH-003 — enforced here, not in DB, per the source task's D7 and sprint-01 §H.3); creates the user with the configured default password (`Identity:DefaultInitialPassword`, hashed via `IPasswordHasher`); assigns the role(s) via `User.AssignRole`; writes `UserCreated` via `IAuditWriter` in the same transaction. **No `MustChangePassword` is set (C4) — the new user has normal, immediate access.**
- **`DisableUser`/`ReactivateUser`:** call the existing `User.Disable()`/`Reactivate()` (already guarded — throws on illegal transition, per ADR 0003 §4); write `UserDisabled`/`UserReactivated` (C5 naming).
- **`ResetPassword`:** sets the user's password back to the default (D1's password-reset action, minus the flag it used to also set — C4). Writes `PasswordReset`.
- **`ChangePassword`:** voluntary, always available; validates new password ≠ default, length ≥ 8 (D1, kept — this rule belongs to `ChangePassword` itself, not to the withdrawn forced-change gate); writes `PasswordChanged`.
- **Tests:** Application-layer unit tests for every guard (duplicate login identifier, zero roles rejected, illegal disable/reactivate, weak/default password rejected on change).

### Slice 5 — First-admin seeder (D2, plus C3's direct `Payroll.View` grant)

- **Files:** `Contractra.Infrastructure/Identity/BootstrapAdminSeeder.cs`, invoked from `Program.cs` at startup (idempotent — checks "no active user holding `Admin` role exists" before doing anything).
- Reads `Bootstrap:AdminLoginIdentifier`/`Bootstrap:AdminPassword` from configuration; missing config ⇒ warn and skip, no crash, no invented credentials (D2).
- Seeded admin: assigned the `Admin` role via `User.AssignRole`; granted `Payroll.View` directly via `User.GrantPermission` in the same seeding step (C3) — never via `role_permissions`. Writes `UserCreated` with actor `"system/bootstrap"`. Also writes `PayrollViewGranted` for the direct grant, in the same transaction, consistent with Slice 3's event list.
- **Local dev secrets:** set `Bootstrap:AdminLoginIdentifier` and `Bootstrap:AdminPassword` with `dotnet user-secrets set <key> <value> --project Contractra.Api` at implementation time. The values are chosen by the owner and are never written into any file in the repository.
- **Tests:** integration test — seeder is a no-op when an admin already exists; seeder never overwrites an existing password; seeder's admin has an active `Payroll.View` `UserPermission` grant and zero `Payroll.View` role grants.

### Slice 6 — Auth endpoints + JWT + permission-based authorization (D5)

> ⛔ **SUPERSEDED by contract v1.4.** Non-versioned paths, "no `mustChangePassword`", "no gate" and the per-request status check below are withdrawn (R1, R5, R7, R10, R21, R35–R38). Kept as history only; Slice 6 is re-planned in `sprint-02.md` when it starts.

- **Files:** `Contractra.Api/Endpoints/AuthEndpoints.cs` (or controller, matching whatever minimal-API-vs-controller convention the Api project currently favors — it currently has neither beyond the template, so this slice sets the convention), JWT configuration/services wiring in `Program.cs`, `Contractra.Infrastructure/Identity/RefreshToken*.cs` + EF configuration, new migration `AddRefreshTokens`.
- **Endpoints:** `POST /api/auth/login`, `POST /api/auth/refresh`, `POST /api/auth/logout`, `POST /api/auth/change-password`, `GET /api/auth/me`.
- **Token contents:** user id, login identifier, roles, effective permissions (role-derived ∪ direct grants − revocations, `Payroll.View` only from direct grant). **No `mustChangePassword` field (C4).**
- **No must-change-password gate** (C4) — every endpoint is available immediately per the user's roles/permissions.
- **Disabled users:** cannot log in or refresh (checked against `User.Status`).
- **Refresh tokens:** stored hashed, rotated on use, revocable.
- **CORS:** enabled for the Flutter admin app origin in Development, from configuration.
- **Tests:** API integration tests (`WebApplicationFactory`) for login success/failure, refresh rotation, disabled-user rejection, and the `/me` payload shape (confirming no `mustChangePassword` field is present).

### Slice 7 — User management endpoints

> ⛔ **SUPERSEDED by contract v1.4** (paths under `/api/v1/`, response shapes, error codes, check order R39). Every Slice 7 endpoint is gated by **`Users.Manage` only** (R4, R15; contract §9). Kept as history only; Slice 7 is re-planned in `sprint-02.md` when it starts.

- **Files:** `Contractra.Api/Endpoints/UserEndpoints.cs`.
- **Endpoints:** `POST /api/users`, `GET /api/users` (paged, search by login identifier, filter by status), `GET /api/users/{id}`, `POST /api/users/{id}/disable`, `/reactivate`, `/reset-password`, `GET /api/roles`.
- Each endpoint calls the Slice-4 Application commands; authorization policies from Slice 6 gate them with `Users.Manage` only (FR-AUTH-003/004; R4, R15). *(Corrected 2026-10-01: an earlier version also listed `Permissions.Grant` / FR-AUTH-005, which belongs to the deferred direct-permission endpoints.)*
- **Tests:** API integration tests including 401 (no token) and 403 (wrong permission) for each endpoint.

---

## 6. Error codes (per task instructions, ProblemDetails-based)

> **Corrected 2026-10-01.** The full error-code set is defined in contract v1.4 §12. In particular, **`users.disabled` is removed** (R11: a disabled login gets `auth.invalid_credentials`), and **`auth.password_change_required` is kept** (R1 reinstated the forced change).

*Historical text (C4 era, superseded):* Confirmed set, drawn from the source task directly: `auth.invalid_credentials`, `users.login_identifier_taken`, `users.role_required`, `users.disabled`. `auth.password_change_required` was dropped at that time because of C4.

---

## 7. What stays out of scope here (confirmed, not assumed)

- Linking a user to a Party as a separate step, and any link endpoint. *(Amended by R31, 2026-10-01: creating an `Engineer` user requires `partyId` and links it in the same transaction — that part is in scope for `CreateUser`. For other roles, linking stays a separate optional step, still out of scope.)*
- Full audit module (only the minimal W1-C slice above).
- Account lockout (Q5).
- Effective-permission resolution beyond what the JWT needs (no general query/reporting surface).
- The full §7.3 permission catalog beyond the six seeded permissions (C2 as amended by R13) (Q6).
- Any `apps/` (Flutter) change.

---

## 8. Next step

Per the source task and the owner's instruction accompanying C1–C6, this document is updated and stopped here. Implementation does not start yet. Before Slice 1 begins:
1. Write `sprint-02.md` (C1) restating §5 as numbered user stories/tasks, following `sprint-01.md`'s structure.
2. Confirm Q5/Q6 (§4) are acceptable as standing deferrals for Sprint 2 (they do not block starting Slice 1, which touches neither).
3. General go-ahead to begin Slice 1.
