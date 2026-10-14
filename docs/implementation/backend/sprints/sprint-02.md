# Backend Sprint 2 — Admin-Created User Accounts + Authentication

> This sprint restates the approved plan in
> `docs/implementation/backend/features/admin-user-creation-plan.md` as
> numbered user stories and tasks, following the exact structure of
> `sprint-01.md`. Sprint 1 stays closed and historical and is not modified
> by this sprint (Confirmed Decision C1). All backend-side scope decisions
> below were made explicitly by the project owner on 2026-09-26 and are
> recorded as Confirmed Decisions (C1–C6) in the plan document; this sprint
> file does not re-derive or re-litigate them.

Status: 🟡 Slice 1 done (US-BE-006, committed) · Slice 2 planned (US-BE-007, not started) · baseline: contract v1.4 and owner decisions R1–R43
Date: 2026-09-26 · updated 2026-09-27 · 2026-10-01
Scope: `Contractra_System` backend (.NET 10 / ASP.NET Core / EF Core / PostgreSQL, Clean Architecture)
Source: `docs/implementation/backend/features/admin-user-creation-plan.md`

---

## 0. Documentation basis

Same authority order as Sprint 1 (`docs/documentation-index-ar.md`), plus:

1. `docs/decisions/0003-user-identity-persistence.md` — the completed User/UserRole/UserPermission/UserPartyLink persistence this sprint builds on.
2. `docs/decisions/0002-authorization-catalog-persistence.md` §6 — the existing `Payroll.View` role-grant guard triggers this sprint's seed must not attempt to bypass.
3. `docs/implementation/backend/features/admin-user-creation-plan.md` — the plan. **§1B (R1–R43, 2026-09-27 … 2026-10-01) is binding and overrides §1 where they differ**; §1 (C1–C6) is binding except C4 (superseded) and C2 (amended); §4 (Q5, Q6) is explicitly deferred; §5 Slices 2, 4, 6 and 7 are superseded.
4. `docs/implementation/backend/features/auth-and-users-api-contract.md` (v1.4) — source of truth for the auth and user-management API.
5. `docs/decisions/0004-authorization-catalog-seed.md` — the Slice 1 seed.

**Cross-session reconciliation (resolved 2026-09-27).** The frontend (US-008…US-012, commits `384cf08`…`78de706`) was built against an auth contract that neither the documents nor this plan had settled. The owner answered each open question; the answers are recorded as R1–R14 in plan §1B. Notably, **R1 reinstates the forced password change, superseding C4**, and **R2 replaces the shared default password with a random temporary one**.

*Correction to an earlier version of this note:* it said commit `384cf08` added stories "directly to `sprint-01.md`". That commit edited the **frontend** sprint file, `docs/implementation/sprints/sprint-01.md`. The backend Sprint 1 file (`docs/implementation/backend/sprints/sprint-01.md`) was never modified, so C1 was not affected.

---

## A. CURRENT BACKEND STATE (verified 2026-10-01)

- **Done:** `User`, `UserRole`, `UserPermission`, `UserPartyLink` — Domain, EF configuration, PostgreSQL constraints, tests (Sprint 1, closed). Five migrations: `AddUsers`, `MakeUserLoginIdentifierCaseInsensitive`, `AddUserRoles`, `AddUserPermissions`, `AddUserPartyLinks`.
- `Role`/`Permission`/`RolePermission` exist (ADR 0002) with the `Payroll.View` role-grant guard (two database triggers). **Seeded by Slice 1** (`SeedAuthorizationCatalog`): 4 roles, 6 permissions, 7 role grants (ADR 0004).
- **Not done:** `Contractra.Application` has no files beyond the template. No authentication/JWT/password-hashing code. No `audit_events`. `Contractra.Api/Program.cs` still has the `/weatherforecast` template.

---

## B. CONFIRMED DECISIONS CARRIED FROM THE PLAN (binding for every task below)

| # | Decision | Effect on this sprint |
|---|---|---|
| C1 | Sprint 1 stays closed; this work is Sprint 2 | This file; `sprint-01.md` is never edited by this sprint |
| C2 | Seed only Identity-active permissions: `Users.Manage`, `Permissions.Grant`, `Roles.Manage`, `Payroll.View` — not the full §7.3 catalog. **Amended by R13:** plus `Apps.Admin.Access` and `Apps.Employee.Access` (6 permissions, 7 role grants) | TASK-BE-024 (done, amended) |
| C3 | Bootstrap admin gets `Payroll.View` as a direct `UserPermission`, never via `RolePermission` | TASK-BE-024 (seed excludes it from roles), TASK-BE-0xx bootstrap seeder slice |
| ~~C4~~ | ~~No `must_change_password`~~ — **superseded by R1–R3** | — |
| R1–R14 | Reconciliation decisions (plan §1B): forced password change; random 12-character temporary password returned once; reset uses the same mechanism; reactivate needs `Users.Manage`; JWT 30 min + rotating 7-day refresh, hashed storage, session revocation on disable/reset/password change, claims + per-user security version; web refresh token in an HttpOnly Secure SameSite cookie; `/api/v1`; optional client correlation ID; `roleCodes`; `/me` with effective permissions and `mustChangePassword`; generic response for disabled logins; `AccessDenied` audited and logged; app-access permissions; current password required to change it | Slices 2–7 |
| R15–R19 | Final owner decisions (plan §1B): reset needs `Users.Manage`; new password ≥ 8 characters and different from the current one, no default password anywhere; NFR-SEC-007 idle timeouts adopted (Engineer 8 h, others 60 min) on top of the 7-day maximum; mobile refresh token in the response body with secure device storage, web keeps the cookie; bootstrap admin exempt from the forced change | Slices 2–7 |
| R20–R25 | Final clarifications (plan §1B): bootstrap admin cannot be reset via `Users.Manage` (identified by a seeder-only flag); idle timeout checked and updated at refresh, no per-request write; `SameSite=Strict` for same-site deployment only; `X-Client-Platform` (`web`/`mobile`) is a transport hint, never used for authorization; app-access 403 after credential check; reactivation restores no session | Slices 2, 4, 6, 7 |
| R26–R27 | (2026-10-01) Bootstrap admin may change its own password via ChangePassword; it is protected server-side from reset, disable, reactivate, role changes and deletion by other users (409, Serilog only) | Slices 4, 5, 7 |
| R28–R29 | (2026-10-01) Bootstrap admin also protected from direct-permission grants/revokes, including its `Payroll.View`; no API recovery for its password — out-of-band operational procedure required | Future permission endpoints; operations |
| R30–R34 | (2026-10-01) One account per Party, `UNIQUE(party_id)` (reverses the 2026-09-26 decision); Engineer creation requires `partyId`; bootstrap break-glass (second Admin + operator-only CLI); `PasswordHasher<TUser>` with `users.password_hash text NOT NULL`; `UserRole.End()`/`UserPermission.Revoke()` internal, `User.EndRole`/`User.RevokePermission`, `security_version` bumped inside the aggregate | Slice 2 (R30 check + constraint, R33, R34), Slices 4–5 (R31, R32) |
| R35–R43 | (2026-10-01) 30-second refresh reuse grace; change-password consumes and reissues the refresh token; "never refresh" only for login/refresh/logout; `client_platform` stored on the session; server check order; `actor_type` + nullable `actor_user_id`, CSRF rejection Serilog-only; bootstrap seeder audits `RoleAssigned(Admin)` and `PayrollViewGranted` replaces `PermissionGranted`; protection checks the target; dummy hash on login; duplicate/deprecated role and disabled-reset rules | Slices 3–7 |
| Contract | `docs/implementation/backend/features/auth-and-users-api-contract.md` (v1.4) is the source of truth for the auth and user-management API (backend, Admin and Employee apps) | All auth/user slices |
| C5 | Audit event names are `UserDisabled` / `RoleEnded` (Logical Data Model §1.8), not `UserDeactivated` / `RoleRevoked` | Slice 3+ (not built in this sprint's Slice 1) |
| Q5 | Account lockout — deferred, not built | Out of scope, all slices |
| Q6 | Full §7.3 role↔permission mapping — deferred until each future module exists | Out of scope, all slices |

---

## C. SLICES → STORIES

This sprint currently defines **Slice 1** (US-BE-006, done) and **Slice 2** (US-BE-007, planned, not started). Slices 3–7 will each become their own user story (US-BE-008..US-BE-012) added to this file when that slice is re-planned against contract v1.4 — not created in advance.

### US-BE-006 — Roles & permissions catalog seed (Slice 1)

> ✅ **Amended for R13 (2026-09-27, owner-approved, uncommitted).** The seed now has **6 permissions and 7 role grants**: Admin ← `Apps.Admin.Access`, `Users.Manage`, `Permissions.Grant`, `Roles.Manage`; OfficeStaff ← `Apps.Admin.Access`; Accountant ← `Apps.Admin.Access`; Engineer ← `Apps.Employee.Access`. `Payroll.View` is still granted to no role. The acceptance criteria below that say "4 permissions" and "3 rows, all under Admin" are superseded by these numbers; the tests (`AuthorizationCatalogSeedTests`) assert the amended values.

```
As a: backend developer building the authentication foundation

I want: the Role/Permission/RolePermission reference data needed by
Identity/Authorization functionality that already exists, seeded via an
EF Core data migration

So that: CreateUser (Slice 4) and the bootstrap admin seeder (Slice 5) have
real roles and permissions to assign, without inventing the full business
permission catalog before the modules it describes exist

Business Value:
No user can be created with a role, and no permission can be granted,
until the reference data exists. This is the first unblocking step for the
whole sprint.

Acceptance Criteria:
- Exactly 4 roles exist after migration: Engineer, OfficeStaff, Accountant,
  Admin (spec §7.1).
- Exactly 4 permissions exist after migration: Users.Manage,
  Permissions.Grant, Roles.Manage, Payroll.View — no other permission from
  spec §7.3 is seeded (Confirmed Decision C2).
- role_permissions contains exactly 3 rows: Admin → Users.Manage,
  Permissions.Grant, Roles.Manage. No other role has any of these three.
- Payroll.View has ZERO role_permissions rows for any role (BR-235,
  Confirmed Decision C3) — proven by both an application-level assertion
  and confirmation that the existing ADR 0002 trigger would reject an
  attempted role grant.
- Migration is additive only; re-running it (or the whole migration chain)
  does not error or duplicate rows.

Business Rules:
- spec §7.1 (four roles), §7.3 (permission catalog, still 🔵 Proposed —
  only the four Identity-active permissions from Confirmed Decision C2 are
  treated as settled for this story)
- BR-235 / confirmed-business-rules-ar.md ح-235 (Payroll.View never via
  role) — enforcement already exists (ADR 0002 §6); this story must not
  attempt to bypass or duplicate it

Dependencies:
- Role, Permission, RolePermission (Sprint 1 / ADR 0002 — already built)

Blocked By:
- None.

Out of Scope:
- The other ~30 permissions in spec §7.3 (deferred per Q6, seeded when
  their modules exist).
- The bootstrap admin's Payroll.View grant itself (that is a per-user
  UserPermission created by the Slice 5 seeder, not reference data — this
  story only ensures Payroll.View exists as a permission and is excluded
  from every role's grants).
- Any Application-layer or API surface.

Affected Layers:
- Infrastructure / Database / Tests
```

#### TASK-BE-024 — Seed authorization catalog (roles, permissions, role_permissions)

```
Description:
Add an EF Core data-only migration that inserts the 4 roles, 4 permissions,
and 3 role_permissions rows described in US-BE-006's acceptance criteria,
using Guid.CreateVersion7()-shaped deterministic IDs so the migration is
reproducible.

Scope:
Migration only. No Domain or Application changes — Role, Permission, and
RolePermission already exist and are unchanged.

Expected Layer:
Infrastructure / Database

Likely Files / Areas:
- Contractra.Infrastructure/Migrations/<timestamp>_SeedAuthorizationCatalog.cs
- Contractra.Infrastructure/Migrations/<timestamp>_SeedAuthorizationCatalog.Designer.cs
- Contractra.Infrastructure/Migrations/ContractraDbContextModelSnapshot.cs (if
  EF regenerates it; a data-only migration should not change the snapshot's
  model shape)

Business Rules:
- spec §7.1, §7.3 (scoped per Confirmed Decision C2)
- BR-235 (Payroll.View excluded from role_permissions)

Validation / Constraints:
- Insert roles and permissions using the existing Role/Permission
  constructors' invariants (RoleCode/PermissionCode validation already
  enforced by those value objects — the migration must supply valid,
  already-approved code strings, not invent new ones).
- Do not attempt an INSERT into role_permissions for Payroll.View under any
  role — this would be rejected by the existing
  trg_role_permissions_forbid_payroll_view trigger (ADR 0002 §6.2); the
  migration must not even attempt it, since a caught/ignored trigger
  failure would be silent bypass-shaped code.
- Migration must be reversible: Down() removes exactly the rows this
  migration added and nothing else.

Testing:
- Covered by TASK-BE-025 (PostgreSQL integration tests).

Done When:
- Migration created (not applied to any shared/dev database without
  separate approval, per CLAUDE.md's migration-application rule); builds
  clean; reviewed against every constraint above.

Dependencies:
- None (Role/Permission/RolePermission already exist).
```

#### TASK-BE-025 — PostgreSQL integration tests for the authorization catalog seed

```
Description:
Write integration tests proving US-BE-006's acceptance criteria against a
real, ephemeral PostgreSQL database (existing PostgresTestDatabaseFixture /
CONTRACTRA_TEST_POSTGRES pattern — never the development database).

Scope:
Integration tests only.

Expected Layer:
Tests (PostgreSQL Integration)

Likely Files / Areas:
- Contractra.Infrastructure.IntegrationTests/AuthorizationCatalogSeedTests.cs

Business Rules:
- Same as TASK-BE-024.

Testing:
Test cases to cover:
- Exactly 4 roles exist after migration, with the expected codes.
- Exactly 4 permissions exist after migration, with the expected codes.
- Exactly 3 role_permissions rows exist, all under Admin, for
  Users.Manage/Permissions.Grant/Roles.Manage.
- Payroll.View has zero role_permissions rows.
- Migrating down then back up reproduces the same seeded state (no
  duplicate-key error, no leftover rows from a partial re-seed).
- The existing ADR 0002 PayrollViewGuardTests-style assertion still holds:
  attempting a raw-SQL role_permissions insert for Payroll.View still fails
  with the existing trigger's error, unaffected by this seed.

Done When:
- All listed tests exist and pass against the ephemeral test database, and
  the development database was never touched.

Dependencies:
- TASK-BE-024
```

### US-BE-007 — User credential and security state (Slice 2)

> **Not started.** Planned 2026-10-01 against contract v1.4 and owner decisions R30, R33, R34 (plan §1B). Replaces plan §5 Slice 2, which is superseded. Schema and Domain only: no Application use cases, no API, no audit table, no seeder (those are Slices 3–7).

```
As a: backend developer building the authentication foundation

I want: the users table and the User aggregate to hold the credential and
security state the auth contract depends on, with every security-relevant
change going through the User aggregate

So that: the later slices (audit, use cases, bootstrap seeder, auth
endpoints) can rely on password_hash, must_change_password,
security_version and is_bootstrap_admin existing with the right
invariants, and no role or permission can change without bumping the
security version

Business Value:
Without a stored credential no user can log in; without the security
version, permission changes would not invalidate issued tokens (R5).

Acceptance Criteria:
- users has password_hash text NOT NULL (R33). The Domain stores only the
  hash string it is given and never hashes or validates a password.
- users has must_change_password boolean NOT NULL (R1; contract §7).
- users has security_version integer NOT NULL, starting at a fixed initial
  value for new users (contract §3.5).
- users has is_bootstrap_admin boolean NOT NULL DEFAULT false, and a
  partial unique index allows at most one row where it is true (contract
  §11). A second true row fails with SQLSTATE 23505.
- The User constructor requires the password hash and the
  must_change_password value; is_bootstrap_admin can be set to true only
  through a dedicated creation path for the bootstrap account, and no
  method changes it afterwards.
- UserRole.End() and UserPermission.Revoke() are internal; the only public
  paths are User.EndRole(...) and User.RevokePermission(...) (R34).
- Each User mutator that changes security state increments
  security_version exactly once: AssignRole, EndRole, GrantPermission,
  RevokePermission, Disable, Reactivate, and the password-change and
  password-reset methods (contract §3.3, §3.5). A rejected call (for
  example an illegal transition) leaves security_version unchanged.
- A password-change method sets the new hash, sets must_change_password to
  false and bumps security_version; a password-reset method sets the new
  hash, sets must_change_password to true and bumps security_version.
- user_party_links has UNIQUE(party_id) (R30), replacing the non-unique
  ix_user_party_links_party_id. A second link to the same party fails with
  SQLSTATE 23505.
- Before the UNIQUE(party_id) migration is applied anywhere, a pre-check
  query reports zero party_id values with more than one link (R30).
- All new migrations are additive and reversible; each has a down-and-up
  test. has-pending-model-changes reports no drift.

Business Rules:
- logical-data-model-ar.md §1.7 (password_hash never logged or audited)
- Owner decisions R1, R5, R19, R20, R30, R33, R34; contract v1.4 §3.5, §7, §11

Dependencies:
- US-BE-006 (catalog seed)

Out of Scope:
- IPasswordHasher and its PasswordHasher<TUser> implementation (Slice 4,
  where it is first used), audit_events (Slice 3), use cases (Slice 4),
  bootstrap seeder (Slice 5), sessions/refresh tokens and endpoints
  (Slice 6).

Affected Layers:
- Domain / Infrastructure / Database / Tests / ADR
```

#### TASK-BE-026 — Domain: credential and security state on `User`

```
Add PasswordHash, MustChangePassword, SecurityVersion and IsBootstrapAdmin
to User; change the constructors to require the password hash and
must_change_password; add the bootstrap creation path; add the
password-change and password-reset methods. RED first: Domain tests for
every acceptance criterion above that concerns User.
Layer: Domain · Files: Contractra.Domain/Identity/User.cs, Domain tests
```

#### TASK-BE-027 — Domain: aggregate-only role/permission ending (R34)

```
Make UserRole.End() and UserPermission.Revoke() internal; add
User.EndRole(...) and User.RevokePermission(...); bump security_version in
every security-state mutator (exactly once, never on a rejected call).
RED first: tests that each mutator bumps the version once and that a
rejected call does not.
Layer: Domain · Files: User.cs, UserRole.cs, UserPermission.cs, Domain tests
```

#### TASK-BE-028 — Persistence: users columns and bootstrap index

```
Map the four new columns in UserConfiguration; additive migration adding
password_hash (text NOT NULL), must_change_password, security_version and
is_bootstrap_admin (DEFAULT false), plus the partial unique index on
is_bootstrap_admin WHERE is_bootstrap_admin. The NOT NULL add on
password_hash needs an explicit strategy for existing rows, stated in the
migration and the ADR (no real user rows exist outside development).
PostgreSQL integration tests: columns and defaults, the partial unique
index (23505 on a second bootstrap row), down-and-up.
Layer: Infrastructure / Database / Tests
```

#### TASK-BE-029 — R30 pre-check and `UNIQUE(party_id)` migration

```
Write the pre-check query (party_id values with more than one row in
user_party_links) and record its result before the migration is applied to
any non-test database. Additive migration replacing
ix_user_party_links_party_id with ux_user_party_links_party_id (unique).
Integration tests: second link to the same party fails with 23505;
down-and-up restores the non-unique index.
Layer: Infrastructure / Database / Tests
```

#### TASK-BE-030 — Update existing tests affected by Slice 2

```
Identify, at slice start, every existing Domain and integration test that
(a) constructs User with the old constructor, (b) calls UserRole.End() or
UserPermission.Revoke() directly, or (c) asserts that one party may be
linked from several users (reversed by R30), and update each one so it
tests the new rule rather than being deleted. Report the list of changed
tests.
Layer: Tests
```

#### TASK-BE-031 — ADR 0003 extension

```
Record the implemented changes in docs/decisions/0003-user-identity-persistence.md:
password_hash (R33), must_change_password, security_version,
is_bootstrap_admin, R34, and the R30 reversal with the pre-check result.
Owner decisions are cited as owner decisions, not confirmed rules.
Layer: Docs
```

**Total so far: 2 user stories (US-BE-006 done, US-BE-007 planned), 8 tasks (TASK-BE-024…025 done, TASK-BE-026…031 planned).**

---

## D. FUTURE SLICES (not yet started — placeholders only, per §C)

Slices 3–7 of the plan (minimal `audit_events`; the
`CreateUser`/`DisableUser`/`ReactivateUser`/`ResetPassword`/`ChangePassword`
Application layer; the bootstrap admin seeder and break-glass; auth
endpoints + JWT; user management endpoints) will each be added here as
US-BE-008 through US-BE-012 when that slice begins, each following its own
RED→GREEN→REFACTOR cycle and its own report. They are planned against
contract v1.4 and R1–R43, not against plan §5 (whose Slices 4, 6 and 7 are
superseded). Do not treat their absence here as forgotten scope.

---

## E. GIT / PROCESS RULES FOR THIS SPRINT

Same as Sprint 1's discipline, restated:
- Do not modify `apps/contractra_admin` or `apps/contractra_employee`.
- Do not touch unrelated concurrent-session work.
- PostgreSQL integration tests only against `CONTRACTRA_TEST_POSTGRES`
  ephemeral databases — never the development database.
- No broad git commands (`git add .`, `clean`, `reset`, `stash`, `revert`).
- Commit only after each slice's tests are green; do not push unless
  explicitly asked.
