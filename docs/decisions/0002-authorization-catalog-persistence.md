# ADR 0002: Physical persistence design for the Authorization Catalog slice

**Status:** Implemented (Domain + EF Core configuration + PostgreSQL constraints and triggers + tests). No Application layer, API, authorization policies, audit infrastructure, users, or seed data.

*Historical scope (clarified 2026-10-22).* The line above records the decision and its scope when this ADR was written. It is not the current state of the catalog. The catalog was later seeded by Backend Sprint 2, Slice 1 (`1e10742`, test fixtures `bc63d5f`): see [ADR 0004](0004-authorization-catalog-seed.md) and the dated note in §9. Users are covered by ADR 0003. Later Sprint 2 work (audit persistence, Application use cases, API) is recorded in `docs/implementation/backend/sprints/sprint-02.md` §A. The decisions in §1–§10, including the §6 `Payroll.View` guards, are unchanged.

**Scope:** `Role`, `Permission`, `RolePermission` — the authorization-catalog part of W1-A (`docs/mvp-entity-scope-matrix-ar.md` §4.1). `User`, `UserRole`, `UserPermission` and `UserPartyLink` are out of scope.

Like ADR 0001, this is an engineering ADR. It records physical-design decisions made to *implement* confirmed rules and is not authoritative over any document in `docs/` outside `docs/decisions/`. Every section below separates **confirmed rules** (with their source) from **engineering choices**.

---

## 1. Confirmed rules this slice implements

| Rule | Source | Implemented as |
|---|---|---|
| `roles` and `permissions` are reference data with a unique `code` | `logical-data-model-ar.md` §1.3, §1.6 · `database-integrity-rules-ar.md` §15 | `ux_roles_code_lower`, `ux_permissions_code_lower` |
| `role_permissions` is an N:M link owned by `roles`, unique per (role, permission) | `logical-data-model-ar.md` §1.3, §1.4, §1.6 | `ux_role_permissions_role_id_permission_id` |
| Lifecycle `Active → Deprecated`; never deleted while in use | `logical-data-model-ar.md` §1.5 · `database-design-ar.md` §6.1.2 | `Deprecate()` · `ck_*_status_valid_values` · FKs `ON DELETE RESTRICT` |
| No physical deletion of reference data in use; no cascades | `database-design-ar.md` §2.14, §3.3 | RESTRICT FKs; no delete operation in the domain |
| **`Payroll.View` is never granted through a role — Admin included; it is granted explicitly per user** | `confirmed-business-rules-ar.md` ح-235 · `monthly-financial-report-addendum-ar.md` §11 · spec BR-235, FR-AUTH-006 | Domain check in `Role.GrantPermission` **and** two database triggers (§6) |
| Rules are enforced in the backend **and the database**, not only the UI | spec BR-018, BR-138 | The database guard in §6 |
| Changes to roles and permissions are audited in the same transaction | `audit-and-operational-logging-addendum-ar.md` §3.10 · BR-186, BR-195 | **Not implemented** — see §10 |

## 2. Entity IDs

**Engineering choice (same as ADR 0001 §1):** `Guid.CreateVersion7()` generated in the application, PostgreSQL `uuid`, `ValueGeneratedNever()`.

## 3. Fields

**Confirmed:** `roles(id, code, status)`, `permissions(id, code, status)`, `role_permissions(id, role_id, permission_id)`.

**Engineering choice:** nothing else is stored.
- A role display name, a permission description and a per-role approval limit (FR-AUTH-011 / OQ-003, still 🟡 open) appear in `system-specification-and-team-reference-ar.md` but not in the logical data model, so they are left out. Adding any of them later is additive.
- No `created_at`, `updated_at` or `is_deleted` columns.
- `role_permissions` has **no dates, no status and no soft-delete**: the logical model dates `user_roles` and `user_permissions`, not `role_permissions`.

## 4. Status

**Confirmed:** `Active`, `Deprecated`.

**Engineering choice:** closed C# enums (`RoleStatus`, `PermissionStatus`) stored as `text` with `ck_roles_status_valid_values` and `ck_permissions_status_valid_values`, the same approach as `PartyStatus` (ADR 0001 §2).
- The initial status is a required constructor argument.
- `Deprecate()` is the only transition. It throws unless the current status is `Active`, so it records the documented transition and nothing else.
- There is no `Reactivate()`: reversal is documented for users (`Disabled → Active`) but not for roles or permissions.

## 5. Codes: format, casing, and identity

### 5.1 Why no segment pattern is enforced

**Finding (documentation contradiction):** `database-design-ar.md` §6.1.2 (priority 24) and `logical-data-model-ar.md` §1.3 (priority 16) describe the permission code format `<Domain>.<Entity>.<Action>` as mandatory. The permission codes confirmed by higher-precedence sources do not share that shape:
- `Payroll.View` has two segments (`confirmed-business-rules-ar.md` ح-235, priority 9; `monthly-financial-report-addendum-ar.md`, priority 2).
- `Reports.Monthly.View` has three segments (same sources).
- Most of the proposed (🔵) list in spec §7.3 has two segments, and it uses many actions outside the nine "approved actions" that section itself lists.

Under the precedence in `documentation-index-ar.md`, the higher sources govern. A three-segment rule would reject the confirmed `Payroll.View`.

**Decision (approved):** `RoleCode` and `PermissionCode` are string-backed value objects (`readonly record struct`), not enums, and enforce only:
- not null, not empty, not whitespace-only;
- **no leading or trailing whitespace**, which is rejected rather than trimmed, so an identifier is never silently changed.

No regex, segment count, character set or Arabic/text normalization is applied.

### 5.2 Case-insensitive identity, preserved casing

**Decision (approved):** codes are identifiers, so `Admin`, `admin` and `ADMIN` are the same code. The stored value keeps the casing it was given.

Database enforcement uses expression indexes, created with raw SQL because the EF Core fluent API cannot express them:

```sql
CREATE UNIQUE INDEX ux_roles_code_lower ON roles (lower(code));
CREATE UNIQUE INDEX ux_permissions_code_lower ON permissions (lower(code));
```

`citext` was not used. It would change the column type and add an extension, and the expression index meets the requirement without either. There is no plain (case-sensitive) unique index on `code` as well, because the `lower(code)` index is strictly stronger.

**Known limitation (engineering note):**
- The domain compares with `ToLowerInvariant()` and the database with `lower()`. The two agree for ASCII.
- For non-ASCII characters, PostgreSQL's `lower()` follows the database's locale, so exotic case mappings could differ.
- The documentation defines no character set for codes, and none is invented here. If a code catalog with non-ASCII codes is ever approved, revisit this.

### 5.3 Value-object equality

**Engineering choice:** the default record-struct equality (exact, ordinal) is kept. Case-insensitive identity is enforced where it matters, by the database uniqueness and by `PermissionCode.IsPayrollView`, rather than by overriding `Equals`. Codes cannot change after creation in the domain (no rename method), so EF change tracking is unaffected either way.

## 6. `Payroll.View`: enforced twice

**Confirmed rule:** ح-235 / BR-235 / FR-AUTH-006, as listed in §1.

**Why two levels (engineering):**
- The domain check gives an early, explicit error on the normal write path.
- BR-018 requires database enforcement, so that raw SQL, a future import tool, or a bug in some other code path cannot create the forbidden state.
- A CHECK constraint cannot read another table, so the database side is a trigger.

### 6.1 Domain

`Role.GrantPermission(Permission)`:
- rejects a permission whose code is `Payroll.View` in any casing (`PermissionCode.IsPayrollView`);
- rejects a second grant of the same permission already loaded on the role.

The `PermissionCode.PayrollView` constant holds the canonical spelling.

### 6.2 Database: grant guard

`fn_role_permissions_forbid_payroll_view()` runs through `trg_role_permissions_forbid_payroll_view`, `BEFORE INSERT OR UPDATE ON role_permissions FOR EACH ROW`.
- It reads the referenced permission's code and raises `check_violation` (SQLSTATE **23514**), with the trigger name as the error's constraint name, when `lower(code) = 'payroll.view'`.
- It also fires on UPDATE, so an existing grant cannot be re-pointed to `Payroll.View`.

### 6.3 Database: rename guard (the direct-SQL bypass)

Without this, a permission already granted to a role could simply be renamed to `Payroll.View` with a raw `UPDATE permissions SET code = ...`.

`fn_permissions_forbid_granted_rename_to_payroll_view()` runs through `trg_permissions_forbid_granted_rename_to_payroll_view`, `BEFORE UPDATE OF code ON permissions FOR EACH ROW WHEN (lower(NEW.code) = 'payroll.view')`. It raises SQLSTATE 23514 only when that permission has at least one row in `role_permissions`.

This is **deliberately not a general code-immutability rule**, which is not confirmed:
- renaming a granted permission to anything else is allowed;
- renaming an ungranted permission to `Payroll.View` is allowed.

Both are covered by tests.

### 6.4 Concurrency

Under READ COMMITTED, two separate transactions could otherwise both pass their checks: one granting permission P, the other renaming P to `Payroll.View`. Each would see only committed data and miss the other's uncommitted change.

To prevent that, the grant trigger reads the permission row **`FOR SHARE`**.
- If the rename comes second, it needs a row lock that conflicts with `FOR SHARE`, so it waits for the grant to commit. Its BEFORE-UPDATE trigger then sees the committed grant and rejects the rename.
- If the rename comes first, the grant trigger's `FOR SHARE` waits for it and then reads the committed new code.

Covered by `ConcurrentGrantAndRename_CannotTogetherGrantPayrollView`.

### 6.5 Limits (stated, not solved)

A database superuser can bypass any trigger, for example with `session_replication_role = replica` or by disabling triggers. That is outside what schema-level enforcement can prevent, and it belongs to operational access control.

## 7. `RolePermission`

**Confirmed:** `UNIQUE(role_id, permission_id)` and ownership by `roles`.

**Engineering choice:**
- Uniqueness is the unique index `ux_role_permissions_role_id_permission_id`, the same style as ADR 0001's `ux_*` indexes. Its leading `role_id` column also serves the role FK.
- `ix_role_permissions_permission_id` supports the permission FK and the rename guard's lookup.
- Both FKs are `ON DELETE RESTRICT`: `fk_role_permissions_roles_role_id` and `fk_role_permissions_permissions_permission_id`.
- `RolePermission` has an internal constructor and is created only through `Role.GrantPermission`.

### 7.1 Unresolved: removing a permission from a role

The documentation doesn't settle how this should work:
- `role_permissions` has no documented end date (§3).
- Yet `database-design-ar.md` §2.14 says there are no physical deletes in the system.
- And audit addendum §3.10 requires any change to role permissions to be audited.

Both a hard delete and an `ended_at` column would be invented behaviour, so **no removal operation exists in this slice**. This must be decided before a "revoke from role" use case is built. Adding `ended_at` later would also change the unique index into a partial one.

## 8. Unresolved: what `Deprecated` means for authorization

This slice only **records** the `Active → Deprecated` transition. It does not decide:
- whether a deprecated role or permission still grants anything;
- whether existing grants of a deprecated permission, or on a deprecated role, stay in effect;
- whether a deprecated permission may still be granted to a role;
- whether reactivation is allowed.

`GrantPermission` therefore does not look at either status. These are future authorization and Application decisions.

## 9. No seed data

> **Update (2026-10-01): the catalog is now seeded.** This section described the state at the time of this ADR. The seed was decided separately, as anticipated below, and implemented in Backend Sprint 2, Slice 1 (migration `SeedAuthorizationCatalog`, commit `1e10742 feat(auth-slice-1): seed authorization catalog`; the existing tests it required were fixed in `bc63d5f`): 4 roles, 6 permissions and 7 role grants, with `Payroll.View` granted to no role. See [ADR 0004](0004-authorization-catalog-seed.md). The guards in §6 are unchanged.

**Decision at the time (approved):**
- The four roles in spec §7.1 (`Engineer`, `OfficeStaff`, `Accountant`, `Admin`) are not seeded.
- The permission list in spec §7.3 is explicitly 🔵 proposed, and it has the format contradiction described in §5.1.

Populating the catalog, including how the first administrator is created, is a separate decision. Nothing here assumes `Payroll.View` exists in the table; the guards key on the code, whenever such a row is created.

## 10. Audit, Application and API boundary

**Confirmed requirement:** changing roles and permissions is in the mandatory audit scope (`audit-and-operational-logging-addendum-ar.md` §3.10, BR-195). The event is written inside the same transaction, and a failure to write it rolls the change back (BR-186). `audit_events` belongs to W1-C and does not exist yet.

**Decision:** no Application use case, API endpoint or authorization policy exposes catalog changes in this slice. This is the same boundary as ADR 0001 §13: exposing mutations before the audit trail exists would violate a confirmed rule.

## 11. Documentation defects observed (reported, not edited)

1. The permission-code format contradiction in §5.1.
2. Spec §7.4 row 8 cites **BR-243** for auditing role and permission changes. BR-243 covers the monthly report's audit events; the governing sources are the audit addendum §3.10 and BR-195.

## 12. Migration and tests

- **Migration** `AddAuthorizationCatalog` is additive:
  - three tables, their PK, CHECK and FK constraints, and two regular indexes;
  - raw SQL for the two `lower(code)` unique indexes and the two trigger/function pairs.
- **`Down()`** drops the triggers, functions and indexes, then the tables. It does not touch anything owned by `InitialPartySlice`, such as the `btree_gist` extension. A test migrates down to `InitialPartySlice` and back up to confirm this.
- **Tests** use the ephemeral-database strategy from ADR 0001 §14 (`CONTRACTRA_TEST_POSTGRES`, `contractra_test_<guid>` databases); the development database is never used. The raw-SQL guard tests run inside transactions that are rolled back.
