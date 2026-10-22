# ADR 0001: Physical persistence design for the Party identity slice

**Status:** Implemented (Domain + EF Core configuration + PostgreSQL constraints + tests). No Application layer, API, authorization, or audit infrastructure.

*Historical scope (clarified 2026-10-22).* The sentence above records the state of the Party identity slice when this ADR was written. Later Backend Sprint 2 work added an Application layer, audit persistence and authentication endpoints elsewhere (ADRs 0003 to 0006; status record: `docs/implementation/backend/sprints/sprint-02.md` §A), on feature branches that are not in `origin/main`. That work does not change this ADR's scope or decisions. At `1644077` (`backend/users-slice-7`), Application and Api contain no Party use case or endpoint; the only Party-named file there is the `IPartyRepository` read port, which user creation uses to validate the Party link.

**Scope:** `Party`, `PartyRole`, `PartyAlias`, `PartyIdentifier` — the W1-A core identity slice (`docs/mvp-entity-scope-matrix-ar.md`).

This document is an engineering ADR, not a business-rule document. It records physical-design decisions made to *implement* confirmed business rules, and is explicitly not authoritative over any document in `docs/` outside `docs/decisions/`. Where a decision below is not dictated by a confirmed rule, it is marked as an engineering choice.

---

## 1. Entity IDs

**Decision (engineering):** All four entities use `Guid` primary keys generated application-side via `Guid.CreateVersion7()` (UUID v7, time-ordered), mapped to PostgreSQL `uuid`, `ValueGeneratedNever()`. No sequential integers.

Not dictated by a specific business rule; chosen for index locality (time-ordered UUIDs avoid the b-tree fragmentation of random UUIDv4) while keeping ID generation out of the database.

## 2. PartyStatus

**Confirmed rule:** `database-design-ar.md` §6.2.1 — exactly three values: `Active`, `NeedsClassification`, `Inactive`. No hard delete.

**Decision (engineering):** Closed C# enum, persisted as PostgreSQL `text` via `HasConversion<string>()` (readable, not an integer or a native PostgreSQL enum type — native enums make adding a value a schema migration; storing as `text` keeps that cheap while a `CHECK` constraint (`ck_parties_status_valid_values`) still enforces the closed set at the database level). No `IsDeleted` column, no soft-delete query filter — `Party.Status` already carries the "still exists but not active" semantics the business needs; a second deletion flag would be a redundant, unconfirmed field. Status transition methods are not implemented: the transition graph (e.g. can `Inactive` return to `Active`?) is not confirmed anywhere in the source documentation. `Party`'s constructor requires the initial status as an explicit, non-defaulted parameter.

## 3. Deletion / cascade behavior

**Confirmed rule:** Parties are never hard-deleted (`database-design-ar.md` §6.2.1, §2.6).

**Decision (engineering):** All three parent→child relationships (`Party`→`PartyRole`/`PartyAlias`/`PartyIdentifier`) use `DeleteBehavior.Restrict`, which PostgreSQL enforces as `ON DELETE RESTRICT` (SQLSTATE `23001` on violation). No delete method is exposed on `Party` at the domain level. This was verified with an integration test that attempts a raw `DbSet.Remove` at the persistence layer and confirms it is rejected while children exist.

## 4. Party → PartyRole cardinality

**Confirmed rule (approved decision):** `Party` 1 → 0..N `PartyRole`. A party may exist with zero roles, supporting `NeedsClassification`/historical-import scenarios without inventing a role.

**Implementation:** no invariant forces a party to have at least one role; verified by a domain test (`Party_CanExist_WithZeroRoles`).

## 5. PartyRoleCode

**Finding (verification pass, prior session):** The documented role list (Engineer/Worker/Supplier/Contractor/Debtor/Creditor/Partner/Company, 8 values) is not confirmed exhaustive — `docs/logical-data-model-ar.md` §2.11 and `docs/data-dictionary-ar.md` §ب-4 both map 33 historical records to a role of "مندوب عهدة" (custody agent) that does not appear in that 8-value list, and no document states the list is closed.

**Decision (engineering):** `PartyRoleCode` is a string-backed value object (`readonly record struct`), not a C# enum and not a new lookup table in this slice. It only enforces non-null/non-empty/non-whitespace. No Arabic normalization is applied — normalization rules for role codes are not documented, and inventing one now would risk conflicting with rules `docs/confirmed-business-rules-ar.md` ح-59 defines specifically for party *names*, not role codes.

## 6. PartyIdentifierType

**Finding (verification pass, prior session):** Only three examples are documented (commercial registration, tax card, national ID) with no statement that the list is exhaustive.

**Decision (engineering):** Same treatment as `PartyRoleCode` — a string-backed value object, not an enum, not a lookup table in this slice.

## 7. PartyRole effective period

**Confirmed rule:** non-overlapping periods for the same `(party_id, role_code)` (`database-design-ar.md` §6.2.2, `database-integrity-rules-ar.md`). An empty/null end date means "active until now, open-ended" (`logical-model-correction-patch-v2.0.1-ar.md`).

**Decision (approved engineering):** `EffectiveFrom: DateOnly`, `EffectiveTo: DateOnly?`, both mapped to PostgreSQL `date`. Semantics are inclusive on both ends (`EffectiveTo` is the last active day). `PartyRole.Status` is **not** stored — no document names a stored status column for this table, only a date-derived lifecycle description, so `Active`/`Ended` is derived via `IsActiveOn(DateOnly asOf)`, which never reads the system clock (no `DateTime.Now`/`.Today` inside the entity — the caller supplies "as of" explicitly, keeping the entity deterministic and testable).

**Overlap enforcement (approved engineering, not specified by any source document down to the SQL mechanism):** a PostgreSQL `EXCLUDE` constraint using `btree_gist`:

```sql
CREATE EXTENSION IF NOT EXISTS btree_gist;

ALTER TABLE party_roles
    ADD CONSTRAINT ex_party_roles_no_overlapping_period
    EXCLUDE USING gist (
        party_id WITH =,
        role_code WITH =,
        daterange(effective_from, effective_to, '[]') WITH &&
    )
    DEFERRABLE INITIALLY IMMEDIATE;
```

`daterange(..., '[]')` matches the approved inclusive semantics; a `NULL` `effective_to` is treated by PostgreSQL's range types as unbounded, which is exactly the "open-ended" semantics required — no sentinel value needed. The documented `UNIQUE(party_id, role_code, effective_from)` constraint is retained alongside the `EXCLUDE` constraint (the former catches exact-start duplicates cheaply via a b-tree; the latter is the real overlap guarantee). The `CHECK (effective_to IS NULL OR effective_to >= effective_from)` constraint is also present. All constraints are explicitly, deterministically named (see §9).

## 8. PartyIdentifier lifecycle

**Confirmed rule:** `valid_to IS NULL` = currently valid; non-null = closed. `docs/confirmed-business-rules-ar.md` explicitly rejects the idea of a scheduled future expiry for this slice's purposes.

**Decision:** No `Status` column (same reasoning as `PartyRole`), no `valid_from` field (not confirmed for this table — see the prior verification pass), no expiry-date field, and no workflow method that would let a future-dated `valid_to` be set through this persistence slice — closing an identifier is left to the Application layer, which does not exist yet.

## 9. PartyIdentifier uniqueness

**Confirmed rule:** current identifiers are unique by `(identifier_type, identifier_value, jurisdiction_country, issuing_authority)`; `jurisdiction_country` and `issuing_authority` may both be `NULL`, and two current identifiers with the exact same scope — including matching `NULL`s — must conflict (BR-177).

**Decision (approved engineering):** PostgreSQL ≥ 15 is the pinned minimum version for this design specifically because of this requirement. A partial unique index using `NULLS NOT DISTINCT` (so `NULL` participates as a single value, not "distinct from every other `NULL`" as PostgreSQL's default unique-index semantics would otherwise treat it) is created via raw SQL, since neither `NULLS NOT DISTINCT` nor a scope-limited partial index is expressible through this EF Core provider version's fluent API:

```sql
CREATE UNIQUE INDEX ux_party_identifiers_current_scope
    ON party_identifiers (identifier_type, identifier_value, jurisdiction_country, issuing_authority)
    NULLS NOT DISTINCT
    WHERE valid_to IS NULL;
```

No `UNKNOWN`/`N/A`/`GLOBAL`/empty-string sentinel values are inserted in place of `NULL` — the index itself handles the NULL-equality problem, so the domain layer never needs to fabricate a placeholder.

## 10. Identifier normalization

**Decision (explicitly deferred, per approved scope):** `identifier_value` is stored exactly as given. No hyphen/space stripping, no leading-zero removal, no format assumptions are applied in this slice, for any `IdentifierType`. Type-specific canonicalization is Application-layer work that has not been designed or approved yet.

## 11. PartyAlias

**Confirmed rule:** `UNIQUE(party_id, alias)`, never global (BR-168 — the same alias text may legitimately belong to two different real parties).

**Decision:** stored as the literal historical spelling, no `normalized_alias` column in this slice — similarity detection belongs to the not-yet-built `party_similarity_candidates` work.

## 12. Party fields

`Id`, `Status`, `NormalizedName`, `Phone`, `Address` only — no `CreatedAt`/`UpdatedAt`/`IsDeleted`, none of which are confirmed in the source documentation for this table. No `MaxLength` is set on any string column (none is documented; PostgreSQL `text` has no inherent length penalty over `varchar(n)`, so leaving columns unbounded avoids inventing a limit). `NormalizedName` carries no unique constraint (BR-168).

## 13. Audit and Application boundary

No audit infrastructure, no Application-layer use cases, and no API surface are part of this slice. `docs/logical-data-model-ar.md` §2.9 names required audit events (`PartyCreated`, `PartyRoleAssigned`, `PartyRoleEnded`, `PartyIdentifierAdded`) for actions this persistence model makes possible — those events are not yet implemented, which is a deliberate, previously-reported scope boundary (see this project's planning history), not an oversight. Exposing Application-layer mutation use cases before that audit trail exists was assessed as producing a documented-rule violation and was explicitly avoided.

## 14. Test-database isolation (not a business rule; environment-specific)

Docker/Testcontainers is not available in this development environment. Integration tests instead use a dedicated, non-superuser PostgreSQL role (`contractra_test`, `CREATEDB` only) against the local PostgreSQL 18 service, reading its connection string exclusively from the `CONTRACTRA_TEST_POSTGRES` environment variable (never `ConnectionStrings:DefaultConnection`). Each test class gets its own ephemeral `contractra_test_<guid>` database, migrated and dropped automatically; every drop is guarded by a name-prefix check.
