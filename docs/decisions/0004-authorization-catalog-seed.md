# ADR 0004: Authorization catalog seed

**Status:** Implemented (EF Core data migration + PostgreSQL integration tests). Backend Sprint 2, Slice 1 (US-BE-006, TASK-BE-024/025).

**Scope:** the reference rows in `roles`, `permissions` and `role_permissions` (tables from ADR 0002). No user, no user-level grant, no schema change.

Like ADRs 0001–0003, this is an engineering ADR. It is not authoritative over any document in `docs/` outside `docs/decisions/`. It separates **confirmed rules** (with their source) from **owner decisions**. The owner decisions are recorded in `docs/implementation/backend/features/admin-user-creation-plan.md` §1 and §1B.

---

## 1. Context

ADR 0002 §9 left the catalog empty and said that populating it, including how the first administrator is created, was a separate decision. Creating users with roles (Slice 4) and the bootstrap admin (Slice 5) need real rows to point at.

`system-specification-and-team-reference-ar.md` §7.3 is the only permission catalog in the documentation, and it is marked 🔵 Proposed. Seeding all of it would treat a proposed catalog as confirmed, for modules that do not exist yet.

## 2. Decisions

| Decision | Basis |
|---|---|
| Four roles: `Engineer`, `OfficeStaff`, `Accountant`, `Admin`. | **Confirmed:** spec §7.1 |
| Seed only the permissions that implemented functionality uses: `Users.Manage`, `Permissions.Grant`, `Roles.Manage`, `Payroll.View`. Not the full §7.3 catalog; each future module seeds its own permissions in its own migration. | **Owner decision C2** (2026-09-26); codes from spec §7.3 |
| Two app-access permissions are added: `Apps.Admin.Access` and `Apps.Employee.Access`. They are not in spec §7.3. | **Owner decision R13** (2026-09-27), amending C2 |
| Role grants (7 rows): Admin ← `Apps.Admin.Access`, `Users.Manage`, `Permissions.Grant`, `Roles.Manage`; OfficeStaff ← `Apps.Admin.Access`; Accountant ← `Apps.Admin.Access`; Engineer ← `Apps.Employee.Access`. | Identity permissions to Admin only: spec §7.3 (that narrow mapping is unambiguous) and owner decision C2 · app-access grants: **owner decision R13** |
| `Payroll.View` exists as a permission but is granted to **no** role, Admin included. The bootstrap admin receives it as a direct `UserPermission` (Slice 5), never through a role. | **Confirmed:** BR-235 / ح-235, FR-AUTH-006 · **owner decision C3** |

## 3. Implementation

- Migration `20260926091750_SeedAuthorizationCatalog`: data only, `InsertData` with fixed, deterministic UUIDv7-shaped ids (`01930000-0000-7000-8000-0000000000xx`), so the migration is reproducible.
- The migration never attempts a `Payroll.View` role grant. The ADR 0002 §6 trigger would reject it, and a caught failure would be bypass-shaped code.
- `Down()` removes exactly the seeded rows, in dependency order: `role_permissions`, then `permissions`, then `roles`.
- The model snapshot is unchanged (data-only migration).

## 4. Tests

`Contractra.Infrastructure.IntegrationTests/AuthorizationCatalogSeedTests.cs`, against ephemeral PostgreSQL databases (`CONTRACTRA_TEST_POSTGRES`; never the development database):
- exactly the 4 roles, 6 permissions and 7 role grants above;
- `Payroll.View` has zero `role_permissions` rows, and a raw-SQL attempt to add one is still rejected by the trigger;
- migrating down to `AddAuthorizationCatalog` removes all seeded rows, and migrating up restores them.

Existing tests that created their own catalog rows were changed to use unique codes, so they no longer collide with the seed.

## 5. Not decided here

- The role↔permission mapping for the rest of spec §7.3 (plan §4 Q6). It is decided module by module, when each module is built.
- What `Deprecated` means for authorization in general (ADR 0002 §8). For user creation, owner decision R43 says a Deprecated role cannot be assigned.
