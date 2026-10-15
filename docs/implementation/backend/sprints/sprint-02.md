# Backend Sprint 2 — Admin-Created User Accounts + Authentication

> This sprint restates the approved plan in
> `docs/implementation/backend/features/admin-user-creation-plan.md` as
> numbered user stories and tasks, following the structure of the backend
> Sprint 1 file (`docs/implementation/backend/sprints/sprint-01.md`). Backend
> Sprint 1 stays closed and historical and is not modified by this sprint
> (Confirmed Decision C1). Scope decisions are the owner's (C1–C6, R1–R45)
> and engineering's (E1–E16), recorded in the plan; this file does not
> re-derive them.

Status: 🟡 Slices 1 and 2 done and committed (see §A) · Slice 2B specified (US-BE-008, ADR 0005), in progress · Slices 3–7 planned · baseline: **contract v1.5** (Draft; §22 proposals pending review), owner decisions R1–R45, engineering decisions E1–E25
Date: 2026-09-26 · updated 2026-09-27 · 2026-10-01 · 2026-10-15
Scope: `Contractra_System` backend (.NET 10 / ASP.NET Core / EF Core / PostgreSQL, Clean Architecture)
Source: `docs/implementation/backend/features/admin-user-creation-plan.md`

---

## 0. Documentation basis

Same authority order as Sprint 1 (`docs/documentation-index-ar.md`), then the working-document order in `docs/implementation/README.md`:

1. `docs/decisions/0003-user-identity-persistence.md` — the User/UserRole/UserPermission/UserPartyLink persistence this sprint builds on.
2. `docs/decisions/0002-authorization-catalog-persistence.md` §6 — the `Payroll.View` role-grant guard triggers the seed must not bypass.
3. `docs/implementation/backend/features/admin-user-creation-plan.md` — the plan. **§1D (R44–R45, E1–E16) and §1B (R1–R43) are binding and override §1 where they differ**; §1 (C1–C6) is binding except C4 (superseded) and C2 (amended); §4 Q6 is deferred and Q5 is a non-goal (R45); §5 Slices 2, 4, 6 and 7 are superseded.
4. `docs/implementation/backend/features/auth-and-users-api-contract.md` (**v1.5**) — source of truth for the auth and user-management API. Its §19 lists the owner confirmations still open; none blocks Slice 2.
5. `docs/decisions/0004-authorization-catalog-seed.md` — the Slice 1 seed.
6. `docs/decisions/0005-auth-session-and-throttle-persistence.md` — the Slice 2B persistence design (E17–E25).

**Two files are called `sprint-01.md`.** `docs/implementation/sprints/sprint-01.md` is the **frontend** Sprint 1 (Admin and Employee apps). `docs/implementation/backend/sprints/sprint-01.md` is the **backend** Sprint 1. This file only ever refers to the backend one as "Sprint 1".

**Cross-session reconciliation (resolved 2026-09-27).** The frontend (US-008…US-012, commits `384cf08`…`78de706`) was built against an auth contract that neither the documents nor this plan had settled. The owner answered each open question; the answers are recorded as R1–R14 in plan §1B. Notably, **R1 reinstates the forced password change, superseding C4**, and **R2 replaces the shared default password with a random temporary one**.

*Correction to an earlier version of this note:* it said commit `384cf08` added stories "directly to `sprint-01.md`". That commit edited the **frontend** sprint file, `docs/implementation/sprints/sprint-01.md`. The backend Sprint 1 file (`docs/implementation/backend/sprints/sprint-01.md`) was never modified, so C1 was not affected.

---

## A. CURRENT BACKEND STATE (verified 2026-10-15 against Git and a clean clone)

- **Backend Sprint 1 — closed.** `User`, `UserRole`, `UserPermission`, `UserPartyLink` (Domain, EF configuration, PostgreSQL constraints, tests) and the authorization catalog (`Role`, `Permission`, `RolePermission`, ADR 0002) are implemented. The Sprint 1 file still reads "Draft for review / planning document only" because C1 forbids editing it; its stories are complete as described in ADRs 0002 and 0003. This line is the record of its closure.
- **Slice 1 (US-BE-006) — done and committed.** `1e10742 feat(auth-slice-1): seed authorization catalog` (migration `SeedAuthorizationCatalog`, `AuthorizationCatalogSeedTests`, ADR 0004) and `bc63d5f test(auth-slice-1): keep catalog fixtures clear of the seeded codes` (the three existing test classes the seed required; without them a clean checkout of `1e10742` fails 12 integration tests). 4 roles, 6 permissions, 7 role grants, `Payroll.View` granted to no role.
- **Slice 2 (US-BE-007) — done and committed** in `feat(auth-slice-2): user credential and security state` (the commit after `3307bd0`): credential and security-state columns with their constraints, bootstrap index and trigger, backfill of existing rows, aggregate-only security mutations (R34) with security-version semantics (E7), one account per Party (R30), case-insensitive login lookup (E10), seed-rollback safety test. Details: ADR 0003 §11. Tests on PostgreSQL 16.15: Domain 215/215; Integration 206/215 — the same 9 environment-specific RESTRICT tests as below fail; every other test passes, including the 38 new integration test cases (177 → 215) and the 58 new Domain test cases (157 → 215).
- **Not done:** `Contractra.Application` has no files beyond the template. No authentication, JWT, password hashing, sessions, throttling or `audit_events`. `Contractra.Api/Program.cs` still has the `/weatherforecast` template.
- **Baseline on PostgreSQL 18.6 (worktree `backend/auth-slice-3` at `fb45c1d`, 2026-10-15):** build 0 warnings / 0 errors; Domain 215/215; Integration **215/215** — the 9 `ON DELETE RESTRICT` tests below pass on PostgreSQL 18, confirming their failures on 16.15 were version-specific. 28 ephemeral `contractra_test_<guid>` databases were created and all dropped.
- **Test baseline (clean clone of `bc63d5f`, PostgreSQL 16.15, 2026-10-15):** Domain 157/157 passed; Integration 168/177 passed. The 9 failures are environment-specific and pre-existing (identical at `78de706`, before the seed): they assert SQLSTATE `23001` for `ON DELETE RESTRICT`, which the project's PostgreSQL 18 (ADR 0001 §14) reports and PostgreSQL 16 reports as `23503`. The owner reported 177/177 on PostgreSQL 18 for the working tree that became `bc63d5f` (not re-run here, because no PostgreSQL 18 was available).

---

## B. CONFIRMED DECISIONS CARRIED FROM THE PLAN (binding for every task below)

| # | Decision | Effect on this sprint |
|---|---|---|
| C1 | Sprint 1 stays closed; this work is Sprint 2 | This file; the backend `sprint-01.md` is never edited by this sprint |
| C2 | Seed only Identity-active permissions: `Users.Manage`, `Permissions.Grant`, `Roles.Manage`, `Payroll.View` — not the full §7.3 catalog. **Amended by R13:** plus `Apps.Admin.Access` and `Apps.Employee.Access` (6 permissions, 7 role grants) | TASK-BE-024 (done) |
| C3 | Bootstrap admin gets `Payroll.View` as a direct `UserPermission`, never via `RolePermission` | TASK-BE-024 (seed excludes it from roles), Slice 5 |
| ~~C4~~ | ~~No `must_change_password`~~ — **superseded by R1–R3** | — |
| R1–R14 | Reconciliation decisions (plan §1B): forced password change; random 12-character temporary password returned once; reset uses the same mechanism (**reset half superseded by R44**); reactivate needs `Users.Manage`; JWT 30 min + rotating 7-day refresh, hashed storage, session revocation on disable/reset/password change, claims + per-user security version; web refresh token in an HttpOnly Secure SameSite cookie; `/api/v1`; optional client correlation ID; `roleCodes`; `/me` with effective permissions and `mustChangePassword`; generic response for disabled logins; `AccessDenied` audited and logged; app-access permissions; current password required to change it | Slices 2–7 |
| R15–R19 | Reset needs `Users.Manage`; new password ≥ 8 characters and different from the current one, no default password anywhere; NFR-SEC-007 idle timeouts (Engineer 8 h, others 60 min) on top of the 7-day maximum; mobile refresh token in the response body with secure device storage, web keeps the cookie; bootstrap admin exempt from the forced change | Slices 2–7 |
| R20–R25 | Bootstrap admin cannot be reset via `Users.Manage` (seeder-only flag); idle timeout checked and updated at refresh, no per-request write; `SameSite=Strict` for same-site deployment only; `X-Client-Platform` (`web`/`mobile`) is a transport hint, never used for authorization; app-access 403 after credential check; reactivation restores no session | Slices 2, 4, 6, 7 |
| R26–R29 | Bootstrap admin may change its own password; protected server-side from reset, disable, reactivate, role and direct-permission changes and deletion (409, Serilog only); no API recovery for its password — out-of-band procedure | Slices 4, 5, 7 |
| R30–R34 | One account per Party, `UNIQUE(party_id)`; Engineer creation requires `partyId`; bootstrap break-glass (second Admin + operator-only CLI); `PasswordHasher<TUser>` with `users.password_hash text NOT NULL`; `UserRole.End()`/`UserPermission.Revoke()` internal, `User.EndRole`/`User.RevokePermission`, `security_version` bumped inside the aggregate | Slice 2 (R30, R33 column, R34), Slices 4–5 |
| R35–R43 | 30-second refresh reuse grace; change-password consumes and reissues the refresh token; "never refresh" only for login/refresh/logout; `client_platform` stored on the session; server check order; `actor_type` + nullable `actor_user_id`, CSRF rejection Serilog-only; bootstrap seeder audits `RoleAssigned(Admin)` and `PayrollViewGranted`; protection checks the target; dummy hash on login; duplicate/deprecated role and disabled-reset rules | Slices 2B–7 |
| **R44** | (2026-10-15) A password reset must never reveal a usable credential to the Admin | Slices 2, 2B, 4, 5, 6, 7 (contract §5.6, §9.6, §10A) |
| **R45** | (2026-10-15) Brute-force protection required, without hard lockout | Slices 2B, 6 (contract §17) |
| **E1–E16** | (2026-10-15) Engineering decisions, plan §1D: reset issue/complete flow; generic failure for unusable/expired passwords; initial-password re-issue and `credentialState`; temporary-password rules and expiry; five-layer throttling; app audience; security-version semantics and bootstrap defence in depth; Party validity; JWT validation and key ring; identifier matching; change-password token consumption; audit fields and fail-closed rules; input limits; operational security; idle-timeout bounds; migration and rollback policy | All slices from 2 |
| Contract | `auth-and-users-api-contract.md` (v1.5) is the source of truth for the auth and user-management API (backend, Admin and Employee apps) | All auth/user slices |
| C5 | Audit event names are `UserDisabled` / `RoleEnded` (Logical Data Model §1.8), not `UserDeactivated` / `RoleRevoked` — an owner decision (see plan §1D on its rationale) | Slice 3+ |
| Q5 | Account lockout — **not adopted** (R45 uses throttling instead) | — |
| Q6 | Full §7.3 role↔permission mapping — deferred until each future module exists | Out of scope, all slices |

---

## C. SLICES → STORIES

| Slice | Story | Delivers | Depends on | Status |
|---|---|---|---|---|
| 1 | US-BE-006 | Authorization catalog seed | Sprint 1 | ✅ Done (`1e10742`, `bc63d5f`) |
| 2 | US-BE-007 | `users` credential and security state; aggregate-only security mutations; one account per Party; case-insensitive login lookup | Slice 1 | ✅ Done (see §A) |
| 2B | US-BE-008 | Persistence for sessions, refresh tokens, reset tickets and the throttle store | Slice 2 | Specified (ADR 0005); in progress |
| 3 | US-BE-009 | Minimal `audit_events` (contract §14) | Slice 2 | Planned |
| 4 | US-BE-010 | Application layer: hashing, temporary passwords, clock, use cases | 2, 2B, 3 | Planned |
| 5 | US-BE-011 | Bootstrap seeder; operator commands | 4 | Planned |
| 6 | US-BE-012 | Auth endpoints, JWT, app audience, throttling, HTTP pipeline | 4 (5 for end-to-end tests) | Planned |
| 7 | US-BE-013 | Users endpoints | 6 | Planned |

Why 2B exists (audit finding F5): disabling, resetting and changing a password must revoke sessions (contract §3.3), and the use cases that do it (Slice 4) come before the endpoints (Slice 6). The session store therefore has to exist before Slice 4, not be created by Slice 6.

### US-BE-006 — Roles & permissions catalog seed (Slice 1) — ✅ done

```
As a: backend developer building the authentication foundation

I want: the Role/Permission/RolePermission reference data needed by
Identity/Authorization functionality that already exists, seeded via an
EF Core data migration

So that: CreateUser (Slice 4) and the bootstrap admin seeder (Slice 5) have
real roles and permissions to assign, without inventing the full business
permission catalog before the modules it describes exist

Acceptance Criteria (final, as amended by R13 on 2026-09-27 and as tested by
AuthorizationCatalogSeedTests):
- Exactly 4 roles exist after migration: Engineer, OfficeStaff, Accountant,
  Admin (spec §7.1).
- Exactly 6 permissions exist after migration: Users.Manage,
  Permissions.Grant, Roles.Manage, Payroll.View (C2), Apps.Admin.Access,
  Apps.Employee.Access (R13). No other permission from spec §7.3 is seeded.
- role_permissions contains exactly 7 rows: Admin → Apps.Admin.Access,
  Users.Manage, Permissions.Grant, Roles.Manage; OfficeStaff →
  Apps.Admin.Access; Accountant → Apps.Admin.Access; Engineer →
  Apps.Employee.Access.
- Payroll.View has ZERO role_permissions rows for any role (BR-235, C3), and
  a raw-SQL attempt to add one is still rejected by the ADR 0002 trigger.
- The migration is data-only and additive; migrating down to
  AddAuthorizationCatalog removes exactly the seeded rows and migrating up
  restores them.
- Existing tests that inserted the now-seeded codes use non-colliding codes
  (commit bc63d5f), so a clean checkout passes.

History: the original criteria (4 permissions, 3 role rows, all under Admin)
were written before R13 and are superseded by the list above.
```

TASK-BE-024 (seed migration) and TASK-BE-025 (PostgreSQL integration tests): done, as recorded in ADR 0004.

### US-BE-007 — User credential and security state (Slice 2) — ✅ done

> Planned 2026-10-01 against contract v1.4 (R30, R33, R34); **re-specified 2026-10-15 for contract v1.5** (R44, E2, E3, E4, E7, E10, E16). Schema, Domain and one persistence query only: no Application use cases, no API, no audit table, no seeder, no session tables.

```
As a: backend developer building the authentication foundation

I want: the users table and the User aggregate to hold the credential and
security state the auth contract depends on, with every security-relevant
change going through the User aggregate

So that: the later slices can rely on password_hash, must_change_password,
temporary_password_expires_at, security_version and is_bootstrap_admin
existing with the right invariants, and no role, permission, status or
password can change without bumping the security version

Acceptance Criteria — schema:
- users.password_hash text NOT NULL, CHECK (password_hash <> '') (R33). The
  Domain stores only the hash string it is given and never hashes or
  validates a password. The value "!" is the reserved "no usable password"
  marker (E2): the Application checks HasUsablePassword and never passes it
  to the hasher (it is not even valid base64, so no hasher format accepts it).
- users.must_change_password boolean NOT NULL (R1).
- users.temporary_password_expires_at timestamptz NULL, with
  CHECK (temporary_password_expires_at IS NULL OR must_change_password) (E4).
- users.security_version integer NOT NULL, CHECK (security_version >= 1) (E7).
- users.is_bootstrap_admin boolean NOT NULL DEFAULT false; partial unique
  index ux_users_is_bootstrap_admin ON users (is_bootstrap_admin) WHERE
  is_bootstrap_admin — a second true row fails with SQLSTATE 23505;
  CHECK (NOT (is_bootstrap_admin AND must_change_password)) (R19); a trigger
  rejects any UPDATE that changes is_bootstrap_admin (E7).
- Existing rows are backfilled, not rewritten (E16): password_hash = "!",
  must_change_password = true, temporary_password_expires_at = NULL,
  security_version = 1, is_bootstrap_admin = false. Status is unchanged, so
  Disabled users stay Disabled. Such an account cannot log in; an Admin
  re-arms it with the initial-password re-issue (contract §9.8, Slice 4/7).
- user_party_links: ux_user_party_links_party_id UNIQUE (party_id) replaces
  the non-unique ix_user_party_links_party_id (R30). The migration creates
  the unique index first and drops the old one second, so the column is never
  unindexed; if duplicate party links exist, Up fails (23505) and changes
  nothing. A second link to the same party fails with SQLSTATE 23505.
- The R30 pre-check query (party_id values linked from more than one user)
  is documented in ADR 0003 §11 and must return zero rows on any shared
  database before the migration is applied there.
- Every new migration is additive; each has a down-and-up test on an
  ephemeral database. The EF model has no pending changes after the slice
  (checked by a test, since the dotnet-ef tool is not required).

Acceptance Criteria — Domain (User aggregate):
- The User constructor requires the password hash and must_change_password;
  temporary_password_expires_at is required exactly when
  must_change_password is true, must be UTC, and is set only together with a
  temporary password. A new user's security_version is 1.
- is_bootstrap_admin is true only for a user made by User.CreateBootstrapAdmin
  (Admin role + direct Payroll.View, must_change_password = false); no method
  changes it afterwards.
- UserRole.End() and UserPermission.Revoke() are internal; the only public
  paths are User.EndRole(role, endedAt, asOf) and
  User.RevokePermission(permission, revokedAt, asOf), which end the open
  assignment/grant of that role/permission or throw if there is none (R34).
- Each successful security-state mutator increments security_version by
  exactly one: AssignRole, EndRole, GrantPermission, RevokePermission,
  Disable, Reactivate, ChangePassword, BeginPasswordReset,
  ReissueTemporaryPassword (E7). Assignments during creation count. A
  rejected call leaves security_version and every other field unchanged.
- ChangePassword(newHash): Active users only; sets the hash, clears
  must_change_password and the temporary-password expiry. Used by the user's
  own change, by reset completion and by the bootstrap break-glass command.
- BeginPasswordReset(): Active, not bootstrap, must_change_password false
  (otherwise the initial re-issue applies, E3); replaces the hash with "!"
  (E1).
- ReissueTemporaryPassword(hash, expiresAt, asOf): Active, not bootstrap,
  must_change_password true; expiresAt must be UTC and after asOf (E3, E4).
- The aggregate refuses, on the bootstrap admin, every mutation the contract
  protects (§11): Disable, Reactivate, AssignRole, EndRole, GrantPermission,
  RevokePermission, BeginPasswordReset, ReissueTemporaryPassword. Its own
  ChangePassword is allowed (R26).
- HasUsablePassword is false exactly when the hash is "!".

Acceptance Criteria — persistence query:
- A persistence-level lookup finds a user by login identifier with
  lower(login_identifier) = lower(@input), using ux_users_login_identifier_lower
  (E10): mixed-case input finds the stored user; padded input finds nobody;
  the stored casing is returned unchanged.

Acceptance Criteria — rollback behaviour (E16):
- Rolling back SeedAuthorizationCatalog while a user holds a seeded role fails
  on the RESTRICT foreign key and leaves every row in place (tested).

Business Rules:
- logical-data-model-ar.md §1.7 (password_hash never logged or audited)
- Owner decisions R1, R5, R19, R20, R26, R27, R30, R33, R34, R44; engineering
  decisions E1–E4, E7, E10, E16; contract v1.5 §3.5, §5.1, §7, §9.8, §10, §11

Dependencies:
- US-BE-006 (catalog seed)

Out of Scope:
- IPasswordHasher and its implementation, temporary-password generation,
  Party validity (E8) — Slice 4, where they are first used
- Sessions, refresh tokens, reset tickets, throttle store — Slice 2B
- audit_events — Slice 3; use cases — Slice 4; seeder — Slice 5; endpoints — 6/7

Must not change in this slice:
- The authorization catalog, its seed and its triggers; Party entities;
  either Flutter app.

Affected Layers:
- Domain / Infrastructure / Database / Tests / ADR
```

#### TASK-BE-026 — Domain: credential and security state on `User`

```
Add PasswordHash, MustChangePassword, TemporaryPasswordExpiresAt,
SecurityVersion and IsBootstrapAdmin; constructor changes; CreateBootstrapAdmin;
ChangePassword, BeginPasswordReset, ReissueTemporaryPassword; HasUsablePassword;
bootstrap guards. RED first.
Layer: Domain · Files: Contractra.Domain/Identity/User.cs, Domain tests
```

#### TASK-BE-027 — Domain: aggregate-only role/permission ending (R34)

```
Make UserRole.End() and UserPermission.Revoke() internal; add User.EndRole and
User.RevokePermission; bump security_version in every security-state mutator
(exactly once, never on a rejected call). RED first.
Layer: Domain · Files: User.cs, UserRole.cs, UserPermission.cs, Domain tests
```

#### TASK-BE-028 — Persistence: users columns, constraints, bootstrap index and trigger

```
Map the new properties in UserConfiguration; additive migration with the
backfill above, the CHECK constraints, the partial unique index and the
is_bootstrap_admin immutability trigger. Integration tests: columns, defaults
and backfill of an existing row, each CHECK, the 23505 on a second bootstrap
row, the trigger, down-and-up, no pending model changes.
Layer: Infrastructure / Database / Tests
```

#### TASK-BE-029 — R30 pre-check and `UNIQUE(party_id)` migration

```
Document the pre-check query. Additive migration creating
ux_user_party_links_party_id (unique) before dropping
ix_user_party_links_party_id. Integration tests: a second link to the same
party fails with 23505; Up fails without changing anything when duplicates
exist; down-and-up restores the non-unique index.
Layer: Infrastructure / Database / Tests
```

#### TASK-BE-030 — Update existing tests affected by Slice 2

```
Every existing Domain and integration test that (a) constructs User with the
old constructor, (b) calls UserRole.End() or UserPermission.Revoke()
directly, or (c) asserts that one party may be linked from several users
(reversed by R30) is updated to test the new rule, not deleted. The list of
changed tests is reported in the slice's commit message.
Layer: Tests
```

#### TASK-BE-031 — ADR 0003 extension

```
Record in docs/decisions/0003-user-identity-persistence.md §11: the columns,
constraints, index and trigger; the "!" marker; security-version semantics;
R34; the R30 reversal with the pre-check query; the backfill and the
migration/rollback policy (E16). Owner decisions are cited as owner
decisions, not confirmed rules.
Layer: Docs
```

#### TASK-BE-032 — Case-insensitive login-identifier lookup (E10)

```
Persistence query used later by login and reset completion. Integration
tests: mixed-case match, padded input, unknown identifier, stored casing
preserved, and the query can use ux_users_login_identifier_lower.
Layer: Infrastructure / Tests
```

#### TASK-BE-033 — Seed rollback safety test (E16)

```
Integration test: with a user holding a seeded role, migrating down past
SeedAuthorizationCatalog fails on the RESTRICT foreign key and every row
survives.
Layer: Tests
```

### US-BE-008 — Auth session, reset-ticket and throttle persistence (Slice 2B)

> Re-specified 2026-10-15 after the design review: engineering decisions E17–E25 (plan §1D), physical design in ADR 0005. The original text said "partial unique index" for one open ticket per user; it is a **deferred exclusion constraint** (ADR 0005 §3.3). The throttle **policy** is out of scope (contract §22, pending review).

```
As a: backend developer building the authentication foundation

I want: sessions, refresh tokens, password-reset tickets and throttle
counters to be stored with database-enforced invariants and safe concurrent
behaviour

So that: the Slice 4 use cases can revoke sessions, rotate refresh tokens and
issue/complete resets atomically, and Slice 6 can build the throttle policy on
a shared store

Acceptance Criteria — schema (ADR 0005 §3):
- Four additive migrations in order: AddAuthSessions, AddRefreshTokens,
  AddPasswordResetTickets, AddAuthThrottleCounters. Each migrates down and up
  on an ephemeral database leaving users and catalog data intact; the whole
  slice rolls back to MakeUserPartyLinkOnePerParty and forward again.
- Hashes only: every hash column is bytea of exactly 32 bytes; no column holds
  a raw token or code (each table's column list is asserted exactly).
- All foreign keys ON DELETE RESTRICT.
- auth_sessions: client_app (admin/employee), client_platform (web/mobile),
  absolute expiry after creation, last refresh not before creation,
  revocation fields set together, nine revoke reasons; xmin concurrency token.
- refresh_tokens: unique token_hash; at most one unconsumed token per session,
  enforced at commit (deferred exclusion constraint).
- password_reset_tickets: at most one open ticket per user, enforced at commit;
  consumed_at < expires_at; never both consumed and superseded.
- auth_throttle_counters: the five contract columns, primary key
  (scope, key_hash), failure_count >= 1, three scope codes.
- The EF model has no pending changes (ModelConsistencyTests).

Acceptance Criteria — Domain:
- AuthSession.Start creates a session with its first refresh token;
  Rotate consumes the presented unconsumed token, issues the replacement and
  updates last_refreshed_at, and refuses a revoked or expired session or an
  unknown/consumed token without changing anything; Revoke keeps the first
  revocation. All timestamps UTC; hashes exactly 32 bytes.
- PasswordResetTicket.Issue / Supersede / Consume: consumption only while open
  and before expiry; supersession only while open.

Acceptance Criteria — persistence and concurrency:
- Rotation is atomic: of N concurrent rotations of one token exactly one
  succeeds; the session ends with one consumed token and one replacement; a
  loser changes nothing. A revocation committed during a rotation makes the
  rotation fail; no revocation is lost.
- Revoke-all for a user (optionally excluding one session) touches only
  active sessions.
- Issuing a ticket supersedes the open ticket and inserts the new one in one
  transaction; concurrent issues leave exactly one open ticket; concurrent
  completions leave exactly one winner; a failed code check writes nothing.
- A failed SaveChanges inside a caller transaction is never followed by a
  commit (E25); tests prove the whole operation rolls back.
- Throttle store: ordered lock-or-create, write back, delete; N concurrent
  steps on one key lose no update; opposite input order does not deadlock;
  cleanup deletes at most a batch of expired, unblocked rows, skips locked
  rows and never deletes rows of a scope without a configured window.

Business Rules:
- Contract v1.5 §3.1, §3.3, §3.6, §5.6, §9.6, §10A, §14, §17 (Draft);
  owner decisions R5, R17, R21, R35, R36, R38, R44, R45; engineering
  decisions E1, E5, E11, E16, E17–E25.

Dependencies:
- US-BE-007 (Slice 2)

Out of Scope:
- The throttle policy and state machine, the Layer 3 mitigation, trusted
  devices (contract §22, OC-8; Slice 6)
- Hashing, token/code generation, clocks, use cases (Slice 4); audit (Slice 3)
- Any purge of session, token or ticket history (OC-9)

Must not change in this slice:
- users semantics and constraints from Slice 2; the authorization catalog;
  either Flutter app; contract §1–§21.

Affected Layers:
- Domain / Infrastructure / Database / Tests / ADR
```

#### TASK-BE-034 — Behaviour spikes (before any schema)

```
Integration tests, run on PostgreSQL 18.6 before the schema is written:
(1) xmin as a uint shadow row version: no xmin column in the generated DDL,
    a concurrent update raises DbUpdateConcurrencyException;
(2) deferred exclusion constraint: statement order inside a transaction does
    not matter; two transactions that each insert a conflicting row and
    commit concurrently — exactly one commits (loser's SQLSTATE recorded);
(3) nullable concurrency token: generated SQL uses IS NULL; a concurrent
    loser raises DbUpdateConcurrencyException; how a deferred violation
    surfaces from SaveChanges; a failed SaveChanges inside a user transaction
    rolls back only to a savepoint (E25);
(4) throttle lock-or-create: no lost update under concurrency; fixed lock
    order with opposite input order does not deadlock.
If a spike fails, stop and apply the ADR 0005 §6 fallback or ask for review.
Layer: Tests
```

#### TASK-BE-035 … 037 — Domain types

```
035 ClientApp, ClientPlatform, SessionRevokeReason.
036 AuthSession and RefreshToken (Start, Rotate, Revoke, IsActiveAt).
037 PasswordResetTicket (Issue, Supersede, Consume, IsOpen, IsValidAt).
RED first against a compiling skeleton.
Layer: Domain · Files: Contractra.Domain/Identity/*, Domain tests
```

#### TASK-BE-038 … 041 — Configurations and migrations

```
038 AddAuthSessions · 039 AddRefreshTokens (deferred exclusion) ·
040 AddPasswordResetTickets (deferred exclusion, CHECKs) ·
041 AddAuthThrottleCounters.
Scaffolded with dotnet ef (never applied to a shared database), hand-edited for
CHECKs, partial index and exclusion constraints. Schema and per-migration
down-and-up tests.
Layer: Infrastructure / Database / Tests
```

#### TASK-BE-042 … 044 — Persistence operations

```
042 Sessions: find by refresh-token hash (loads the presented and the live
    token only), revoke all active sessions of a user (optionally except one).
043 Tickets: issue (supersede open + insert, one transaction), find the open
    ticket of a user.
044 Throttle store: ordered lock-or-create / write / delete in one short
    transaction with a lock timeout; batched SKIP LOCKED cleanup. No policy.
Layer: Infrastructure / Tests
```

#### TASK-BE-045 … 048 — Concurrency, regression, documentation, verification

```
045 Concurrency suites (rotation, revocation, ticket issue/complete, throttle,
    cleanup), each with independent contexts released together.
046 SchemaConstraintNameTests updated; slice-level down-and-up; existing
    down-migrating tests still pass through the new Down steps.
047 Status lines (this file §A, ADR 0005 status and §7 spike results).
048 Build (0 warnings) and both suites on PostgreSQL 18.6; no pending model
    changes; no leftover contractra_test_* databases.
Layer: Tests / Docs
```

### US-BE-009 — Minimal `audit_events` (Slice 3)

```
Delivers: audit_events exactly as contract §14.1 (every logical-model §15.4
field plus actor_type), CHECKs linking actor_type/actor_user_id/
actor_role_code, an append-only trigger rejecting UPDATE and DELETE, and the
Application IAuditWriter with its Infrastructure implementation writing in
the caller's transaction.
Depends on: Slice 2.
Key acceptance: a forced audit-write failure rolls back the paired change
(BR-186); UPDATE/DELETE rejected by the database; System events have no user,
no role and source SystemJob; correlation_id NOT NULL.
Verify: PostgreSQL integration tests.
Must not change: Slice 2 schema.
```

### US-BE-010 — Application layer and use cases (Slice 4)

```
Delivers: IPasswordHasher over ASP.NET Core PasswordHasher<User> (shared
framework, no new package), ITemporaryPasswordGenerator (contract §10),
IResetCodeGenerator (§10A), IClock and business-date provider
(Business:TimeZone), IPasswordResetDelivery (no implementation registered:
"no channel"), user repository with the Slice 2 lookup; use cases CreateUser
(Party validity E8, R31, R43), DisableUser, ReactivateUser,
IssuePasswordReset (503 path when no channel), CompletePasswordReset,
ReissueInitialPassword, ChangePassword — each with bootstrap protection,
session revocation through the Slice 2B store and audit through Slice 3, in
one transaction.
Depends on: Slices 2, 2B, 3.
Key acceptance: every rule and error code of contract §5.5, §5.6, §9.1, §9.4–
§9.6, §9.8, §10, §10A, §11 has a test; the Application test project is added
in this slice.
Must not change: contract semantics; the apps.
```

### US-BE-011 — Bootstrap seeder and operator commands (Slice 5)

```
Delivers: the startup seeder (contract §11) and the operator commands
reset-bootstrap-password and issue-password-reset (§10A), each audited with
actor_type System and a reason, printing secrets only to the terminal.
Depends on: Slice 4.
Key acceptance: seeder is a no-op when an active Admin exists; never
overwrites a password; startup fails if its audit write fails; commands refuse
the protected cases.
```

### US-BE-012 — Auth endpoints and HTTP security pipeline (Slice 6)

```
Delivers: /api/v1/auth/* (contract §5), JWT issuance and validation with the
key ring (§3.2, §3.2.1), the app-audience policy (§4.1), the password-change
gate (§7), CSRF and CORS (§8), throttling (§17) over the Slice 2B store,
ProblemDetails (§12), correlation ID (§13), HTTPS/HSTS (§18), Serilog.
Depends on: Slice 4 (Slice 5 for end-to-end tests with a real admin).
Needs packages not yet in the solution (JwtBearer, Serilog, Mvc.Testing):
restore requires nuget.org access.
Key acceptance: WebApplicationFactory tests for every status and code in
§5, §6, §12 and every throttle layer in §17; tokens with a wrong alg, kid,
iss, aud, skew or app are rejected.
```

### US-BE-013 — Users endpoints (Slice 7)

```
Delivers: /api/v1/users and /api/v1/roles (contract §9) through the Slice 4
use cases, with the §9 check order.
Depends on: Slice 6.
Key acceptance: 401/403/404/409/503 and the bootstrap protections for each
endpoint; no endpoint returns a reset secret.
```

**Total: 8 user stories (US-BE-006 and US-BE-007 done; US-BE-008 in progress; US-BE-009…013 planned). Tasks: TASK-BE-024…033 done; TASK-BE-034…048 written for US-BE-008. Tasks for US-BE-009…013 are written when each slice starts.**

---

## D. FRONTEND ALIGNMENT

Both Flutter apps still follow the pre-contract mock design. Their alignment with contract v1.5 is planned in `docs/implementation/sprints/frontend-auth-contract-alignment-plan.md` and starts only after the matching backend endpoints exist (Slices 6–7). This sprint does not modify `apps/`.

---

## E. GIT / PROCESS RULES FOR THIS SPRINT

- Do not modify `apps/contractra_admin` or `apps/contractra_employee`.
- Do not touch unrelated concurrent-session work.
- PostgreSQL integration tests only against `CONTRACTRA_TEST_POSTGRES` ephemeral databases — never the development database. The project's test server is PostgreSQL 18 (ADR 0001 §14); on PostgreSQL 16 the nine `ON DELETE RESTRICT` tests report 23503 instead of 23001 (see §A), and new tests must not depend on that difference.
- No broad git commands (`git add .`, `clean`, `reset`, `stash`, `revert`); stage explicit paths.
- RED → GREEN → REFACTOR per task; commit only after the slice's tests are green; do not push unless explicitly asked.
