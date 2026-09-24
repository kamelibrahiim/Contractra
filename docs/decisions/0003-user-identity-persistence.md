# ADR 0003: Physical persistence design for the User identity slice

**Status:** Partially implemented. `User` is done (Domain + EF Core configuration + PostgreSQL constraints + tests). `UserRole`, `UserPermission` and `UserPartyLink` are not implemented yet; their sections below are reserved and will be filled when those entities are built. There is no Application layer, API, authentication, or audit infrastructure.

**Scope:** `users`, the first entity of the remaining W1-A identity work (`docs/mvp-entity-scope-matrix-ar.md` §4.1). The backend Sprint 1 plan is in `docs/implementation/backend/sprints/sprint-01.md`.

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

`User` has no delete method, and none is planned. `FR-AUTH-004` and `logical-data-model-ar.md` §1.5 both rule out deleting users. Foreign keys that later reference `users` (UserRole, UserPermission, UserPartyLink) are planned as `ON DELETE RESTRICT`; they will be recorded in §9 when built.

## 6. Out of scope: authentication

Authentication is **explicitly out of scope** for this slice and for Backend Sprint 1:
- no password or `password_hash` column, no hashing algorithm;
- no login flow, sessions, tokens or refresh tokens;
- no lockout policy.

The logical model lists `password_hash` as a sensitive field (§1.7), and adding it later is additive. Its format and algorithm are future authentication decisions.

## 7. Sprint boundaries

This ADR does not change the Backend Sprint 1 scope. UserRole, UserPermission and UserPartyLink are still to be built in their planned tasks, with no Application use cases, API endpoints or authentication.

The Sprint plan (US-BE-005) foresaw one combined migration for the whole slice. `AddUsers` was already published, so the slice now spans several additive migrations instead. Each one is additive and reversible on its own.

## 8. Audit boundary

**Confirmed requirement:** `UserCreated`, `UserDisabled` and `UserReactivated` are named audit events (`logical-data-model-ar.md` §1.8), written in the same transaction as the change (BR-186). `audit_events` belongs to W1-C and does not exist yet.

**Decision:** as in ADR 0001 §13 and ADR 0002 §10, no Application use case or API endpoint exposes user creation or status changes until the audit trail exists.

## 9. Reserved: `UserRole`, `UserPermission`, `UserPartyLink`

Not implemented. Each will get its own subsection here when built:
- **9.1 `UserRole`** — dated user-to-role assignment.
- **9.2 `UserPermission`** — direct per-user grants and revocations, including the only sanctioned path for `Payroll.View`. The cross-reference to ADR 0002 §6 (role path forbidden) belongs here.
- **9.3 `UserPartyLink`** — the user-to-party link, `UNIQUE(user_id)` (`logical-data-model-ar.md` §1.6).

## 10. Unresolved (not decided here)

1. **`user_party_links.party_id` uniqueness.** The logical model confirms `UNIQUE(user_id)` only. Its duplication-risk table (§1.9) handles "two users for one person" with `user_party_links` plus similarity detection on the party, which does not settle whether `party_id` must be unique. Not decided.
2. **Authentication credential format**: password storage, hashing algorithm, tokens, lockout (§6).
3. **Audit behavior** for user lifecycle events (§8).
4. **Login identifier character set and length**, including whether non-ASCII identifiers are allowed (§3.2 limitation).
5. **Whether "at least one role per user" (FR-AUTH-003) is a domain invariant** or an Application-layer rule. To be decided with UserRole.
