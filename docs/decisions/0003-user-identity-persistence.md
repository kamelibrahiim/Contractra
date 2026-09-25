# ADR 0003: Physical persistence design for the User identity slice

**Status:** Partially implemented. `User`, `UserRole` and `UserPermission` are done (Domain + EF Core configuration + PostgreSQL constraints + tests). `UserPartyLink` is not implemented yet; its section below is reserved and will be filled when it is built. There is no Application layer, API, authentication, or audit infrastructure.

**Scope:** `users`, `user_roles` and `user_permissions`, part of the remaining W1-A identity work (`docs/mvp-entity-scope-matrix-ar.md` §4.1). The backend Sprint 1 plan is in `docs/implementation/backend/sprints/sprint-01.md`.

Like ADR 0001 and ADR 0002, this is an engineering ADR. It records physical-design decisions made to *implement* confirmed rules and is not authoritative over any document in `docs/` outside `docs/decisions/`. Every section below separates **confirmed rules** (with their source) from **owner decisions** and **engineering choices**.

---

## 1. Confirmed rules this slice implements

| Rule | Source | Implemented as |
|---|---|---|
| `users` has business key `login_identifier`, unique | `logical-data-model-ar.md` §1.6 | `ux_users_login_identifier_lower` (§3.2) |
| Lifecycle `Active → Disabled`; `Disabled` may return to `Active`; no deletion | `logical-data-model-ar.md` §1.5 | `Disable()` / `Reactivate()` (§4) · `ck_users_status_valid_values` |
| A user is disabled, never deleted, so their history stays linked | spec FR-AUTH-004 | No delete operation in the domain (§5) |
| `password_hash` is never written to audit or Serilog | `logical-data-model-ar.md` §1.7 | Not applicable yet: no credential is stored (§6) |
| Creating, disabling and reactivating users are audited in the same transaction | `logical-data-model-ar.md` §1.8 · BR-186 | **Not implemented** — see §8 |
| `users → user_roles` is `1 : N` and dated | `logical-data-model-ar.md` §1.3, §1.4 | `UserRole` owned by `User` (§9.1) |
| `user_roles` lifecycle `Active → Ended` by setting `ended_at`; no deletion | `logical-data-model-ar.md` §1.5 | `UserRole.End()` (§9.1) · FKs `ON DELETE RESTRICT` |
| No uniqueness on `(user, role)`, because a role may be assigned, ended and assigned again; a partial index `WHERE ended_at IS NULL` | `logical-data-model-ar.md` §1.6 | `ux_user_roles_user_id_role_id_active` (§9.1) |
| `user_permissions` grants a specific permission on top of the role; lifecycle `Granted · Revoked`, both audit events | `logical-data-model-ar.md` §1.3, §1.5 · `database-design-ar.md` §6.1.3 | `UserPermission` owned by `User` (§9.2) |
| A permission is granted, revoked and granted again; a partial index on `(user, permission) WHERE ended_at IS NULL` | `logical-data-model-ar.md` §1.6 · `database-design-ar.md` §6.1.3 | `ux_user_permissions_user_id_permission_id_active` (§9.2) |
| **`Payroll.View` is granted explicitly per user, never through a role — Admin included** | `confirmed-business-rules-ar.md` ح-235 · spec BR-235, FR-AUTH-006 · `logical-data-model-ar.md` §1.9 | Allowed by `User.GrantPermission` and by `user_permissions`; forbidden on the role path by ADR 0002 §6 (§9.2) |

## 2. Entity ID and fields

**Engineering choice (same as ADR 0001 §1 and ADR 0002 §2):** `Guid.CreateVersion7()` generated in the application, PostgreSQL `uuid`, `ValueGeneratedNever()`.

**Stored today:** `users(id, login_identifier, status)`. Nothing else. No `password_hash` (§6), no `created_at`/`updated_at`, no soft-delete column.

## 3. `LoginIdentifier`

### 3.1 Format: opaque

**Owner decision (2026-09-24):** `LoginIdentifier` is an **opaque** login identifier. It is **not** defined as an email address, a username or a phone number, and no such format is validated.

The value object (`Contractra.Domain/Identity/LoginIdentifier.cs`, a `readonly record struct`) enforces only the rules shared with `RoleCode` and `PermissionCode` (ADR 0002 §5.1):
- not null, not empty, not whitespace-only;
- **no leading or trailing whitespace**, which is rejected rather than trimmed, so the identifier is never silently changed.

No regex, character set, length limit, case folding or normalization is applied, in the domain or in the database. The column is `text` with no maximum length, since none is confirmed.

### 3.2 Case-insensitive identity, preserved casing

**Owner decision (2026-09-24):** `Kamel`, `kamel` and `KAMEL` are the **same login identity** and cannot belong to two users. The stored value keeps the casing it was given.

**Engineering choice:** this follows the established Contractra identity-code convention for `roles.code` and `permissions.code` (ADR 0002 §5.2): a unique expression index, created with raw SQL because the EF Core fluent API cannot express it.

```sql
CREATE UNIQUE INDEX ux_users_login_identifier_lower ON users (lower(login_identifier));
```

- The rule is enforced **at the PostgreSQL level**, so every write path is covered: EF Core, raw SQL, or a future import tool. This is tested for both EF Core and raw SQL.
- There is **no** plain (case-sensitive) unique index as well, because the `lower()` index is strictly stronger (same reasoning as ADR 0002 §5.2).
- `UserConfiguration` declares no index on `LoginIdentifier`. That keeps the EF model snapshot from containing a case-sensitive index that a future migration could re-create.
- `citext`, custom collations and generated columns were not used, for the same reasons as ADR 0002 §5.2: they change the column type or add schema objects, and the expression index meets the requirement without them.
- The domain does not lower-case anything. Value-object equality stays exact (ordinal), as in ADR 0002 §5.3. There is no domain-level uniqueness check; the database is the guard.

**Note for future lookups:** any query that finds a user by login identifier must compare with `lower(login_identifier) = lower(@input)`, not with an exact EF Core equality on the value object. An exact match would miss `kamel` when `Kamel` is stored, and would not use `ux_users_login_identifier_lower`. No such query exists yet; this belongs to the authentication design (§6).

**Known limitation (same as ADR 0002 §5.2):** for non-ASCII characters, PostgreSQL's `lower()` follows the database locale, so exotic case mappings may differ from .NET's. No character set is confirmed for login identifiers; revisit this if one is defined.

### 3.3 History of this decision

`AddUsers` first created a plain, case-sensitive `ux_users_login_identifier`, because no case rule had been decided at that point. That migration was already committed and shared, so it is not edited. The additive migration `MakeUserLoginIdentifierCaseInsensitive` replaces the index:

- **`Up()`** creates `ux_users_login_identifier_lower` **first**, then drops `ux_users_login_identifier`, so uniqueness is never absent. If case-only duplicates already exist, the index creation fails and the migration rolls back. Rows are never merged or rewritten.
- **`Down()`** re-creates `ux_users_login_identifier` and drops `ux_users_login_identifier_lower`. It cannot fail on existing data, because data that is unique by `lower()` is also unique by exact value.
- No other table or object is touched.
- Tests cover both directions: one migrates down to `AddUsers` and back up, and checks both index states and that a mixed-case user row survives unchanged. Another seeds case-only duplicates at `AddUsers` and checks that `Up()` fails with 23505, leaves the case-sensitive index in place, and leaves both rows unchanged.

## 4. Status and lifecycle

**Confirmed:** `Active`, `Disabled` (§1).

**Engineering choice:** a closed C# enum (`UserStatus`) stored as `text` with `ck_users_status_valid_values`, the same approach as `PartyStatus`, `RoleStatus` and `PermissionStatus`. The initial status is a required constructor argument.

**Decision (approved): transitions are guarded, not idempotent**, matching `Role.Deprecate()` and `Permission.Deprecate()`:

| Call | From | Result |
|---|---|---|
| `Disable()` | `Active` | `Disabled` |
| `Disable()` | `Disabled` | `InvalidOperationException` |
| `Reactivate()` | `Disabled` | `Active` |
| `Reactivate()` | `Active` | `InvalidOperationException` |

A rejected call leaves the status unchanged. Neither method records a timestamp or raises an event (§8).

## 5. No hard delete

`User` has no delete method, and none is planned. `FR-AUTH-004` and `logical-data-model-ar.md` §1.5 both rule out deleting users. Every foreign key that references `users` is `ON DELETE RESTRICT`: today `fk_user_roles_users_user_id` (§9.1) and `fk_user_permissions_users_user_id` (§9.2); UserPartyLink will follow the same rule.

## 6. Out of scope: authentication

Authentication is **explicitly out of scope** for this slice and for Backend Sprint 1:
- no password or `password_hash` column, no hashing algorithm;
- no login flow, sessions, tokens or refresh tokens;
- no lockout policy.

The logical model lists `password_hash` as a sensitive field (§1.7), and adding it later is additive. Its format and algorithm are future authentication decisions.

## 7. Sprint boundaries

This ADR does not change the Backend Sprint 1 scope. UserPartyLink is still to be built in its planned tasks, with no Application use cases, API endpoints or authentication.

The Sprint plan (US-BE-005) foresaw one combined migration for the whole slice. `AddUsers` was already published, so the slice now spans several additive migrations instead: `AddUsers`, `MakeUserLoginIdentifierCaseInsensitive`, `AddUserRoles` and `AddUserPermissions` so far. Each one is additive and reversible on its own, and each has a test that migrates down past it and back up.

## 8. Audit boundary

**Confirmed requirement:** `UserCreated`, `UserDisabled` and `UserReactivated` are named audit events (`logical-data-model-ar.md` §1.8), written in the same transaction as the change (BR-186). `audit_events` belongs to W1-C and does not exist yet.

**Decision:** as in ADR 0001 §13 and ADR 0002 §10, no Application use case or API endpoint exposes user creation or status changes until the audit trail exists. The same applies to assigning and ending roles and to granting and revoking permissions: `RoleAssigned`, `RoleEnded`, `PermissionGranted`, `PermissionRevoked`, `PayrollViewGranted` and `PayrollViewRevoked` are named audit events in the same section.

## 9. Related entities

### 9.1 `UserRole` — implemented

**Timestamps, not dates (owner decision, 2026-09-25).** `AssignedAt: DateTimeOffset` and `EndedAt: DateTimeOffset?`, stored as `timestamp with time zone` in `assigned_at` and `ended_at`.
- The Sprint plan proposed `DateOnly`, like `PartyRole`. The owner chose instants instead: an ended role stops at that exact moment, whereas with whole days it would stay active until the end of its end date.
- This also matches the data dictionary's naming convention, where every `_at` column is `TIMESTAMP_UTC`.
- Only UTC values (offset `00:00`) are accepted. Other offsets are rejected, not converted, so a caller's value is never silently shifted.
- PostgreSQL stores microseconds, so sub-microsecond precision is not preserved on save.
- The start column `assigned_at` is not named in the logical model, which names only `ended_at`. It pairs with `ended_at` and with the `RoleAssigned` audit event.

**Domain (engineering choices):**
- `UserRole` has an internal constructor. It is created only through `User.AssignRole(role, assignedAt, asOf)` and exposed through the read-only `User.Roles`, the same ownership pattern as `Role.GrantPermission` and `Party.Roles`.
- `IsActiveAt(asOf)`: active from `AssignedAt` (inclusive) until `EndedAt` (exclusive).
- The entity never reads the system clock. The caller supplies "now" as `asOf`, and `assignedAt`/`endedAt` must not be after it: they record facts, not scheduled changes. A future start would otherwise block that role until it began, with no way to end it.
- `End(endedAt, asOf)` records `Active → Ended`. It rejects an end before `AssignedAt`, and a second `End()`, so a recorded end is never rewritten. `EndedAt == AssignedAt` is allowed (a zero-length assignment, for example an immediate correction).
- `AssignRole` rejects a new assignment that overlaps a loaded assignment of the same role, whether that one is still open or ends after the new start. Only loaded assignments are checked; the database is the final guard. This is stricter than the plan, which put no uniqueness check in the domain; review found that a backdated reassignment could otherwise overlap an ended one.
- There is no delete or unassign method.

**Database:**

| Object | Purpose | Basis |
|---|---|---|
| `pk_user_roles` | primary key | convention |
| `ux_user_roles_user_id_role_id_active` | unique `(user_id, role_id) WHERE ended_at IS NULL`: at most one open assignment per user and role | **confirmed**, `logical-data-model-ar.md` §1.6 |
| `ex_user_roles_no_overlapping_period` | `EXCLUDE USING gist (user_id WITH =, role_id WITH =, tstzrange(assigned_at, ended_at, '[)') WITH &&)`, `DEFERRABLE INITIALLY IMMEDIATE` | **engineering**, same approach as `ex_party_roles_no_overlapping_period` (ADR 0001 §7) |
| `ck_user_roles_ended_at_after_assigned_at` | `ended_at IS NULL OR ended_at >= assigned_at` | engineering, mirrors `party_roles` |
| `fk_user_roles_users_user_id`, `fk_user_roles_roles_role_id` | both `ON DELETE RESTRICT` | no deletion (§5) |
| `ix_user_roles_role_id` | supports the role foreign key | engineering |

- **Why both the index and the `EXCLUDE` constraint:** the documented partial index only sees open rows. It cannot stop a backdated reassignment from overlapping an assignment that has already ended. The `EXCLUDE` constraint is the real non-overlap guarantee; its `'[)'` range matches `IsActiveAt`, so a reassignment starting at the exact instant the previous one ended is allowed. This pairing of a documented cheap constraint with an `EXCLUDE` constraint is the ADR 0001 §7 precedent.
- **Two error codes for one rule:** a second open assignment fails with 23505 on the partial index, and an overlap with an ended assignment fails with 23P01 on the `EXCLUDE` constraint. A future Application layer must map both.
- The `btree_gist` extension is owned by `InitialPartySlice`; `AddUserRoles` neither creates nor drops it.
- The migration is the additive `AddUserRoles`. `Down()` drops the constraint and the table, and nothing else.
- **Not enforced (open, see §10):** whether a user must hold at least one role, and whether a deprecated role may be assigned or a disabled user may receive one.

### 9.2 `UserPermission` — implemented

**Meaning of "revoke".** `database-design-ar.md` §6.1.3 describes the table as granting or revoking a specific permission on top of the role, and explains its partial index as "a permission is granted, then revoked, then granted again". So a row is a direct grant, and revoking **ends that grant**. It is not a deny rule that removes a permission a role would otherwise give; no document defines such a rule, and none is implemented.

**Same design as `UserRole` (§9.1)**, including the owner's timestamp decision:
- `GrantedAt: DateTimeOffset` and `RevokedAt: DateTimeOffset?`, UTC only, stored as `timestamp with time zone`.
- Columns `granted_at` and `ended_at`. The end column keeps the **documented** name `ended_at` (`logical-data-model-ar.md` §1.6, `database-design-ar.md` §6.1.3), not the `revoked_at` the Sprint plan suggested. The domain calls the same moment `RevokedAt`, after the `Revoked` lifecycle state.
- Created only through `User.GrantPermission(permission, grantedAt, asOf)` (internal constructor, read-only `User.Permissions`). Grants in the future are rejected, and so is a grant that overlaps a loaded grant of the same permission.
- `UserPermission.Revoke(revokedAt, asOf)` records `Granted → Revoked`; it rejects a revocation before `GrantedAt` or after `asOf`, and a second revocation. The Sprint plan placed revoke on `User`; it sits on the grant itself, matching `UserRole.End`.
- `IsActiveAt(asOf)`: active from `GrantedAt` (inclusive) until `RevokedAt` (exclusive).

**Database:** `pk_user_permissions`; the documented partial unique index `ux_user_permissions_user_id_permission_id_active` on `(user_id, permission_id) WHERE ended_at IS NULL`; the engineering `EXCLUDE` constraint `ex_user_permissions_no_overlapping_period` (same approach and reasons as §9.1); `ck_user_permissions_ended_at_after_granted_at`; `fk_user_permissions_users_user_id` and `fk_user_permissions_permissions_permission_id`, both `ON DELETE RESTRICT`; `ix_user_permissions_permission_id`. The additive migration is `AddUserPermissions`.

#### 9.2.1 `Payroll.View`: forbidden on the role path, allowed on the user path

**Confirmed rule:** ح-235 / BR-235 / FR-AUTH-006 — `Payroll.View` is never granted through a role, Admin included, and is granted explicitly per user.

The rule has two halves, enforced in two places:

| Path | Behaviour | Where |
|---|---|---|
| Role → `role_permissions` | **Forbidden.** `Role.GrantPermission` rejects it, and two triggers reject it in the database, including the direct-SQL rename bypass | ADR 0002 §6 |
| User → `user_permissions` | **Allowed — the only sanctioned path.** `User.GrantPermission` accepts it in any casing, and the table has **no** Payroll.View trigger | this section |

- The missing trigger on `user_permissions` is deliberate, not an oversight. A schema test asserts that the table has no user-defined trigger, and integration tests prove a `Payroll.View` grant is accepted through both EF Core and raw SQL.
- The ADR 0002 rename guard checks only `role_permissions`. A permission granted to users only may still be renamed to `Payroll.View`, which is consistent: that path is allowed.
- `PayrollViewGuardTests` from ADR 0002 run unchanged alongside the new tests.
- Computing a user's **effective** permissions (role-derived plus direct grants) is not implemented; it belongs to the Application layer (Sprint plan §H.7).

### 9.3 `UserPartyLink` — reserved

The user-to-party link, `UNIQUE(user_id)` (`logical-data-model-ar.md` §1.6).

## 10. Unresolved (not decided here)

1. **`user_party_links.party_id` uniqueness.** The logical model confirms `UNIQUE(user_id)` only. Its duplication-risk table (§1.9) handles "two users for one person" with `user_party_links` plus similarity detection on the party, which does not settle whether `party_id` must be unique. Not decided.
2. **Authentication credential format**: password storage, hashing algorithm, tokens, lockout (§6).
3. **Audit behavior** for user lifecycle events (§8).
4. **Login identifier character set and length**, including whether non-ASCII identifiers are allowed (§3.2 limitation).
5. **"At least one role per user" (FR-AUTH-003).** Not enforced in the domain or the database: a `User` may have zero roles, following the `Party` precedent (ADR 0001 §4), and the Sprint plan (§H.3) defers the rule to the Application layer. Whether it should ever become a domain invariant is still open.
6. **Role and user status on assignment.** `AssignRole` does not check whether the role is `Deprecated` or the user is `Disabled`. What those states mean for authorization is undecided (ADR 0002 §8).
