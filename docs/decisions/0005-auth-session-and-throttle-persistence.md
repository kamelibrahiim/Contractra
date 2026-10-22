# ADR 0005: Persistence for auth sessions, refresh tokens, password-reset tickets and throttle counters

**Status:** Implemented (Domain + EF Core configuration + four migrations + persistence operations + PostgreSQL integration tests), in the Slice 2B implementation commit that follows the design commit `b59d4c9`. Engineering decisions E17–E25, reviewer approval 2026-10-15. Backend Sprint 2, Slice 2B (US-BE-008, TASK-BE-034…048); `docs/implementation/backend/sprints/sprint-02.md` §A records the implementation state. The throttle **policy** remains unimplemented and pending review (§8).

**Scope:** four new tables — `auth_sessions`, `refresh_tokens`, `password_reset_tickets`, `auth_throttle_counters` — their Domain types (sessions, refresh tokens, reset tickets) and Infrastructure persistence operations. No change to `users`, the authorization catalog, the Application layer, the API or the Flutter apps.

Like ADRs 0001–0004, this is an engineering ADR. It is not authoritative over any document in `docs/` outside `docs/decisions/`. It separates **confirmed rules**, **owner decisions** and **engineering decisions**. The auth contract (`docs/implementation/backend/features/auth-and-users-api-contract.md`) is still **Draft v1.5**; the contract clarifications this ADR depends on are recorded there in §22 as **proposed, pending review**, not as approved policy.

---

## 1. Context

Disabling a user, issuing or completing a password reset, re-issuing an initial password and changing a password must revoke sessions (contract §3.3). Those use cases are built in Slice 4, before the endpoints (Slice 6), so the session store must exist first (audit finding F5). Reset completion needs stored tickets (contract §9.6, §10A, owner instruction R44), and brute-force protection needs a shared counter store (contract §17, owner instruction R45, engineering decision E5).

## 2. Decisions

| # | Decision | Basis |
|---|---|---|
| E17 | Four additive migrations, in this order: `AddAuthSessions`, `AddRefreshTokens`, `AddPasswordResetTickets`, `AddAuthThrottleCounters`. Each has its own Down/Up test. `users` and the authorization catalog are not changed. | Engineering (E16 migration policy); reviewer approval |
| E18 | No Application interfaces or use cases in Slice 2B; they are added with the first consuming use case. Domain: `AuthSession` (aggregate root, owns its `RefreshToken`s), `PasswordResetTicket`, enums `ClientApp`, `ClientPlatform`, `SessionRevokeReason`. Infrastructure: the throttle row type, EF configurations, persistence operations. | Engineering; reviewer approval |
| E19 | Token, code and throttle-key hashes are stored as `bytea` with `CHECK (octet_length(...) = 32)` (SHA-256). No table has a column for a raw token or code. | Contract §3.1, §10A, §17 (hash only) · engineering (type) |
| E20 | Sessions: optimistic-concurrency row version on `auth_sessions` mapped to PostgreSQL `xmin` (fallback in §6). Nine revoke reasons (§3). The first revocation is kept. Absolute expiry is derived from `absolute_expires_at` and is **not** written as a revocation. | Contract §3.3, §3.6 · engineering |
| E21 | Refresh tokens: `id` column; unique `token_hash`; at most one unconsumed token per session, enforced by a deferred exclusion constraint; `consumed_at` is an EF concurrency token. Rotation (consume old, insert new, update `last_refreshed_at`) is one transaction. | Contract §3.3 (R5, R35, E11) · engineering |
| E22 | Reset tickets: at most one open ticket per user (deferred exclusion constraint); `CHECK (consumed_at IS NULL OR consumed_at < expires_at)`; never both consumed and superseded; `consumed_at` and `superseded_at` are concurrency tokens. Issuing supersedes every open ticket, then inserts, in one transaction. A failed code check writes nothing to the ticket. | Contract §5.6, §9.6, §10A (R44, E1) · engineering |
| E23 | Throttle store: the contract's five columns, primary key `(scope, key_hash)`, `CHECK failure_count >= 1`. Generic operations only: lock-or-create the rows of one request in a fixed order inside a short transaction with a lock timeout, write back or delete, and batched cleanup with `FOR UPDATE SKIP LOCKED`. **The throttle policy (thresholds, windows, escalation, Layer 3 mitigation) is not implemented in Slice 2B**; it is proposed in contract §22 and decided in Slice 6. | Contract §17 · owner instruction R45 · reviewer decision |
| E24 | Retention: Slice 2B deletes only expired throttle rows. Session, refresh-token and reset-ticket history is kept. Any purge is a later owner/operations decision. | Reviewer decision (2026-10-15); contract §14 (session revocations are recorded only in the sessions table) |
| E25 | A failed `SaveChanges` inside a caller's transaction aborts the whole operation: the transaction is never committed after a failed save. (EF Core rolls a failed `SaveChanges` back only to an automatic savepoint, so a later commit would persist the earlier work.) | EF Core documentation, "Using Transactions — Savepoints" · engineering |

## 3. Schema

All timestamps are `timestamp with time zone`, written in UTC. All foreign keys are `ON DELETE RESTRICT`. Names follow ADR 0001–0003 (`pk_`, `fk_<child>_<parent>_<column>`, `ux_`/`ix_`, `ck_`, `ex_`).

### 3.1 `auth_sessions`

| Column | Rule |
|---|---|
| `id uuid` | `pk_auth_sessions`; UUIDv7, set by the Domain. Also the access token's `sid` claim (contract §3.2). |
| `user_id uuid` | `fk_auth_sessions_users_user_id` |
| `client_app text` | `ck_auth_sessions_client_app_valid_values`: `admin`, `employee` (contract §4) |
| `client_platform text` | `ck_auth_sessions_client_platform_valid_values`: `web`, `mobile` (contract §5.1, R38) |
| `created_at` | |
| `absolute_expires_at` | `ck_auth_sessions_absolute_expiry_after_creation`: `> created_at`. Computed by the caller (7 days, R5/R17). |
| `last_refreshed_at` | `ck_auth_sessions_last_refreshed_not_before_creation`: `>= created_at` (contract §3.6) |
| `revoked_at`, `revoke_reason` | `ck_auth_sessions_revocation_fields_together`: both null or both set · `ck_auth_sessions_revoked_not_before_creation` · `ck_auth_sessions_revoke_reason_valid_values` |
| `xmin` (system column) | Optimistic-concurrency token. Any update of the row, including a bulk revocation, changes it. |

Index `ix_auth_sessions_user_id_active` on `(user_id) WHERE revoked_at IS NULL`: revoking every session of a user touches only active sessions.

**Revoke reasons** (each tied to a contract event): `logout` (§5.3), `idle` (§3.6), `refresh_reuse` (§3.3, reuse after the 30-second grace), `app_access_lost` (§4, refresh), `user_disabled`, `password_reset_issued`, `password_reset_completed`, `initial_password_reissued`, `password_changed` (§3.3). Not included: absolute expiry (derived, never written) and the bootstrap break-glass command (whether it revokes sessions is not defined; Slice 5).

### 3.2 `refresh_tokens`

| Column | Rule |
|---|---|
| `id uuid` | `pk_refresh_tokens` (the contract lists no id; added so each token row is addressable) |
| `session_id uuid` | `fk_refresh_tokens_auth_sessions_session_id` |
| `token_hash bytea` | `ux_refresh_tokens_token_hash` (unique, includes consumed tokens: reuse detection looks them up) · `ck_refresh_tokens_token_hash_length` |
| `issued_at` | |
| `consumed_at` | `ck_refresh_tokens_consumed_not_before_issued`; EF concurrency token |

Index `ix_refresh_tokens_session_id` (the foreign key; loading a session's tokens). *Added during implementation:* EF Core creates a foreign-key index by convention; it is named explicitly rather than removed.

`ex_refresh_tokens_one_unconsumed_per_session`: `EXCLUDE USING btree (session_id WITH =) WHERE (consumed_at IS NULL) DEFERRABLE INITIALLY DEFERRED`. An exclusion constraint is used because PostgreSQL cannot defer a partial unique index, and EF Core does not order an `UPDATE` before an `INSERT` by a partial index's predicate; checking at commit makes the statement order irrelevant.

### 3.3 `password_reset_tickets`

| Column | Rule |
|---|---|
| `id uuid` | `pk_password_reset_tickets`; the audit event `PasswordResetIssued` references it (contract §9.6) |
| `user_id uuid` | `fk_password_reset_tickets_users_user_id` |
| `code_hash bytea` | `ck_password_reset_tickets_code_hash_length`. Not unique: completion finds the ticket by user and compares in constant time (contract §5.6). |
| `issued_at`, `expires_at` | `ck_password_reset_tickets_expires_after_issued` |
| `issued_by_user_id uuid NULL` | `fk_password_reset_tickets_users_issued_by_user_id`; null = issued by the System (operator command, contract §10A) |
| `channel text` | `ck_password_reset_tickets_channel_valid`: not empty, at most 32 characters. `operator` is the only channel defined so far (contract §19 OC-2). |
| `consumed_at` | `ck_password_reset_tickets_consumed_not_before_issued` · `ck_password_reset_tickets_consumed_before_expiry` (`consumed_at < expires_at`) |
| `superseded_at` | `ck_password_reset_tickets_superseded_not_before_issued` · `ck_password_reset_tickets_not_consumed_and_superseded` |

`ex_password_reset_tickets_one_open_per_user`: `EXCLUDE USING btree (user_id WITH =) WHERE (consumed_at IS NULL AND superseded_at IS NULL) DEFERRABLE INITIALLY DEFERRED`. Index `ix_password_reset_tickets_user_id_issued_at` on `(user_id, issued_at DESC)` for "latest ticket of a user" (`credentialState`, contract §9.2), and `ix_password_reset_tickets_issued_by_user_id` (the second foreign key; *added during implementation*, EF Core convention, named explicitly).

`code_hash` and `refresh_tokens.token_hash` are mapped to private backing fields; the Domain properties return copies, so a caller changing a returned array cannot change what is persisted.

**Open, valid, expired.** *Open* = not consumed and not superseded. *Valid at t* = open and `issued_at <= t < expires_at`. Expiry cannot appear in an index predicate, so an expired ticket stays *open* until it is superseded; issuing therefore always supersedes first. Expired tickets are kept: `credentialState = ResetRequired` is derived from them.

### 3.4 `auth_throttle_counters`

| Column | Rule |
|---|---|
| `scope text` | `ck_auth_throttle_counters_scope_valid_values`: `login_pair`, `identifier`, `change_password_user` (contract §17 layers 2, 3, 4). Slice 6 may extend the list with a migration. |
| `key_hash bytea` | `ck_auth_throttle_counters_key_hash_length` |
| `window_start` | |
| `failure_count integer` | `ck_auth_throttle_counters_failure_count_positive` (`>= 1`): a row exists only while it holds at least one counted attempt |
| `blocked_until NULL` | `ck_auth_throttle_counters_blocked_until_not_before_window_start` |

Primary key `pk_auth_throttle_counters (scope, key_hash)`. Index `ix_auth_throttle_counters_window_start` (cleanup).

## 4. Transactions and concurrency

- **Refresh rotation:** `AuthSession.Rotate` consumes the presented token, adds the replacement and updates `last_refreshed_at`; one `SaveChanges` (or the caller's transaction) writes all three. Two rotations of the same token serialize on the token row and on the session's `xmin`: the loser's `UPDATE` matches no row after the winner commits (PostgreSQL READ COMMITTED re-checks the `WHERE` on the new row version), EF raises `DbUpdateConcurrencyException`, and the loser's whole transaction — including its new token — rolls back. A revocation committed between a rotation's read and write changes `xmin`, so the rotation fails; a revocation that waits on a rotation's row lock re-checks `revoked_at IS NULL` and still revokes.
- **Reset tickets:** issue = bulk supersede of the open tickets + insert, in one transaction (the operation opens one if the caller has none). In Slice 4 the same transaction updates `users` (`BeginPasswordReset`), so two issues for one user serialize on `users.security_version` (concurrency token since Slice 2); the exclusion constraint is the backstop for any other path. Completion consumes the ticket through its concurrency tokens; a concurrent completion or supersession loses and rolls back. A wrong, expired, consumed or superseded code is a read-only outcome.
- **Throttle:** one short transaction per step, never held while hashing. Rows are locked in `(scope, key_hash)` order (bytes compared lexicographically), so two steps on overlapping keys cannot deadlock. A missing row is created with `INSERT … ON CONFLICT DO NOTHING`; if a concurrent transaction created it first, the step is retried (at most 3 attempts, then it fails, and Slice 6 fails closed). `SET LOCAL lock_timeout` bounds waiting. Cleanup deletes at most a batch of rows whose window has passed for their scope and that are not blocked, selected with `FOR UPDATE SKIP LOCKED` so it never waits on an admission; scopes without a configured window are never deleted.
- **Conflict classification:** conflicts are recognised by SQLSTATE anywhere in the exception chain — `23505` (unique), `23P01` (exclusion), `40P01` (deadlock between two deferred checks) — or by `DbUpdateConcurrencyException`.

## 5. Verified behaviour and implementation risks

Verified from documentation before implementation:
- Npgsql maps a `uint` property configured with `IsRowVersion()` to `xmin` (Npgsql EF Core docs, "Concurrency Tokens"; release notes 7.0).
- Every update of a row creates a new row version with a new `xmin` (PostgreSQL 18 docs, §5.6 System Columns).
- `EXCLUDE … WHERE (…) DEFERRABLE INITIALLY DEFERRED` is allowed with btree and is checked at the end of the transaction (PostgreSQL 18, `CREATE TABLE`).
- In READ COMMITTED, `UPDATE`/`DELETE`/`SELECT FOR UPDATE` wait for a concurrent updater and re-evaluate their `WHERE` on the updated row (PostgreSQL 18, §13.2.1).
- An inserter that meets an uncommitted conflicting unique entry waits for that transaction (PostgreSQL 18, §64.5).
- `SKIP LOCKED` skips rows that cannot be locked immediately (PostgreSQL 18, `SELECT`, The Locking Clause).
- A concurrency token adds a `WHERE` condition and zero affected rows raise `DbUpdateConcurrencyException`; `SaveChanges` without a user transaction is all-or-nothing; inside a user transaction a failed `SaveChanges` rolls back to a savepoint only (EF Core docs, "Handling Concurrency Conflicts", "Using Transactions").

Not documented — decided by the TASK-BE-034 spike tests before the schema was written (results in §7):
1. `xmin` as a shadow property, and no `xmin` column in a scaffolded migration.
2. Two transactions that each insert a conflicting row under a deferred exclusion constraint and commit concurrently: exactly one commits.
3. The SQL EF generates for a nullable concurrency token whose original value is null, and how a deferred violation surfaces from `SaveChanges`.
4. Throttle lock-or-create under concurrency and fixed lock order.

## 6. Fallbacks

- If `xmin` cannot be mapped as specified: an application-managed `row_version integer` column on `auth_sessions`, incremented by the Domain on every change and by the bulk-revocation SQL. Decided before `AddAuthSessions` is written.
- If two concurrent commits could both pass a deferred exclusion check: serialize on a parent row lock before inserting (the main flows already do, through `users.security_version` and the token row), and keep the constraint as a backstop. This would be reported for review.

## 7. Spike results (TASK-BE-034, PostgreSQL 18.6, Npgsql EF Core 10.0.3, EF Core 10.0.12)

`Contractra.Infrastructure.IntegrationTests/PersistenceBehaviorSpikeTests.cs` — 10 tests, all passing; no fallback from §6 was needed.

1. **`xmin`.** A `uint` shadow property configured with `IsRowVersion()` maps to `xmin` and is a concurrency token. A stale save raises `DbUpdateConcurrencyException`, also after a set-based `ExecuteUpdate`; the winner reads the new `xmin` back and can save again. *Observed:* the EF migration operation **does** list an `xmin` column (`xmin:xid`), and the scaffolded `AddAuthSessions` migration shows it; the Npgsql SQL generator leaves it out of the DDL, and the created table has only its real columns. The §5 wording "no `xmin` column in a scaffolded migration" was therefore inaccurate: the migration file lists it, the database never gets it.
2. **Deferred exclusion.** Statement order inside a transaction does not matter; two open rows are rejected at commit with `23P01` and the constraint's name. Two transactions that each inserted a conflicting row and committed concurrently: exactly one committed in each of 5 rounds; every loser got `23P01` (no `40P01` observed). This demonstrates, but cannot prove, the absence of a race; the main flows serialize on row locks first (§4).
3. **EF behaviour.** A nullable concurrency token whose original value is null generates `AND consumed_at IS NULL`; the concurrent loser raises `DbUpdateConcurrencyException` and the winner's value stays. A deferred violation surfaces as a **raw `PostgresException` (`23P01`)**, not wrapped in `DbUpdateException`, both from `SaveChanges` (auto transaction) and from `CommitAsync` of a user transaction; nothing is persisted. Inside a user transaction, committing after a failed `SaveChanges` **persists the earlier work**; disposing without commit rolls everything back (E25 confirmed).
4. **Throttle lock-or-create.** 50 concurrent steps on one key: no lost update (one retry after a concurrent insert observed). Fixed lock order with opposite input order: no deadlock.

**Test-environment finding.** The test server allows 100 connections (97 for non-superusers). Test classes that race up to 50 connections each, running in parallel, exhausted it (`53300`). They now run alone in a non-parallel xUnit collection (`ConcurrencyTestCollection`). This is not a defect of the store, but it confirms that every waiting throttle step holds a connection, which the lock timeout bounds (§4) and Slice 6 must size.

## 7A. Notes for the slices that use this persistence

- Conflicts surface as `DbUpdateConcurrencyException` or as a `PostgresException` with SQLSTATE `23505`, `23P01` or `40P01`, possibly unwrapped (§7.3); classify by SQLSTATE through the exception chain.
- `RevokeAllActiveAsync` and ticket supersession are set-based `UPDATE`s: entities already tracked by the context are not refreshed. A tracked entity changed afterwards fails its concurrency check rather than overwriting.
- Timestamps come from the caller's clock. `revoked_at >= created_at` and `superseded_at >= issued_at` can refuse a write when clocks of two instances disagree (for example a session created by another instance a moment "after" the revocation time). This fails safe (the change is refused, nothing is half-written) but is not reported as a conflict; contract §18 requires NTP.
- Issuing a ticket inside a caller transaction leaves the commit to the caller, which must not commit after any failure (E25).

## 8. Not decided here

- The throttle policy: windows, thresholds, escalation, source grouping, and the Layer 3 mitigation (contract §22, proposed; Slice 6).
- Whether a refresh inside the 30-second reuse grace updates `last_refreshed_at` (Slice 4).
- Whether the bootstrap break-glass command revokes sessions (Slice 5).
- The response to an optimistic-concurrency conflict on a users endpoint (Slice 4/7; the contract has no code for it).
- Retention and purge of session, token and ticket history (owner/operations).
