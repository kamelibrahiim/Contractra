# Backend Sprint 2 — Admin-Created User Accounts + Authentication

> This sprint restates the approved plan in
> `docs/implementation/backend/features/admin-user-creation-plan.md` as
> numbered user stories and tasks, following the structure of the backend
> Sprint 1 file (`docs/implementation/backend/sprints/sprint-01.md`). Backend
> Sprint 1 stays closed and historical and is not modified by this sprint
> (Confirmed Decision C1). Scope decisions are the owner's (C1–C6, R1–R45)
> and engineering's (E1–E16), recorded in the plan; this file does not
> re-derive them.

Status: 🟡 Slices 1, 2, 2B, 3, 4 and 5 done and committed (see §A) · Slice 6 implemented and committed (`fa62a98`), with further uncommitted changes in the working tree; **implementation-complete for team handoff (2026-10-21); not owner-accepted, not production-approved** (deferred items tracked in §A) · Slice 7 planned and may proceed · (Slice 3's design, repository ADR 0006, is proposed and pending owner review) · baseline: **contract v1.5** (Draft; §22 proposals pending review), owner decisions R1–R47 plus the unnumbered decisions of 2026-10-18 (OC-6) and 2026-10-19 (`ThrottleCleared`), engineering decisions E1–E25 (E26–E44 proposed in ADR 0006)
Date: 2026-09-26 · updated 2026-09-27 · 2026-10-01 · 2026-10-15 · 2026-10-16 · 2026-10-18 · 2026-10-19
Scope: `Contractra_System` backend (.NET 10 / ASP.NET Core / EF Core / PostgreSQL, Clean Architecture)
Source: `docs/implementation/backend/features/admin-user-creation-plan.md`

---

## 0. Documentation basis

Same authority order as Sprint 1 (`docs/documentation-index-ar.md`), then the working-document order in `docs/implementation/README.md`:

1. `docs/decisions/0003-user-identity-persistence.md` — the User/UserRole/UserPermission/UserPartyLink persistence this sprint builds on.
2. `docs/decisions/0002-authorization-catalog-persistence.md` §6 — the `Payroll.View` role-grant guard triggers the seed must not bypass.
3. `docs/implementation/backend/features/admin-user-creation-plan.md` — the plan. **§1D (R44–R45, E1–E16) and §1B (R1–R43) are binding and override §1 where they differ**; §1 (C1–C6) is binding except C4 (superseded) and C2 (amended); §4 Q6 is deferred and Q5 is a non-goal (R45); §5 Slices 2, 3, 4, 6 and 7 are superseded (Slice 3 since 2026-10-16, by US-BE-009 below and repository ADR 0006).
4. `docs/implementation/backend/features/auth-and-users-api-contract.md` (**v1.5**) — source of truth for the auth and user-management API. Its §19 lists the owner confirmations still open; none blocks Slice 2.
5. `docs/decisions/0004-authorization-catalog-seed.md` — the Slice 1 seed.
6. `docs/decisions/0005-auth-session-and-throttle-persistence.md` — the Slice 2B persistence design (E17–E25).
7. `docs/decisions/0006-audit-events-persistence.md` — **repository ADR 0006** (not business ADR-006): the Slice 3 audit design (proposed E26–E44, pending owner review), owner decisions R46–R47, the Audit Completion slice and Gates A and B.

**Two files are called `sprint-01.md`.** `docs/implementation/sprints/sprint-01.md` is the **frontend** Sprint 1 (Admin and Employee apps). `docs/implementation/backend/sprints/sprint-01.md` is the **backend** Sprint 1. This file only ever refers to the backend one as "Sprint 1".

**Cross-session reconciliation (resolved 2026-09-27).** The frontend (US-008…US-012, commits `384cf08`…`78de706`) was built against an auth contract that neither the documents nor this plan had settled. The owner answered each open question; the answers are recorded as R1–R14 in plan §1B. Notably, **R1 reinstates the forced password change, superseding C4**, and **R2 replaces the shared default password with a random temporary one**.

*Correction to an earlier version of this note:* it said commit `384cf08` added stories "directly to `sprint-01.md`". That commit edited the **frontend** sprint file, `docs/implementation/sprints/sprint-01.md`. The backend Sprint 1 file (`docs/implementation/backend/sprints/sprint-01.md`) was never modified, so C1 was not affected.

---

## A. CURRENT BACKEND STATE (verified 2026-10-15 against Git and a clean clone)

- **Backend Sprint 1 — closed.** `User`, `UserRole`, `UserPermission`, `UserPartyLink` (Domain, EF configuration, PostgreSQL constraints, tests) and the authorization catalog (`Role`, `Permission`, `RolePermission`, ADR 0002) are implemented. The Sprint 1 file still reads "Draft for review / planning document only" because C1 forbids editing it; its stories are complete as described in ADRs 0002 and 0003. This line is the record of its closure.
- **Slice 1 (US-BE-006) — done and committed.** `1e10742 feat(auth-slice-1): seed authorization catalog` (migration `SeedAuthorizationCatalog`, `AuthorizationCatalogSeedTests`, ADR 0004) and `bc63d5f test(auth-slice-1): keep catalog fixtures clear of the seeded codes` (the three existing test classes the seed required; without them a clean checkout of `1e10742` fails 12 integration tests). 4 roles, 6 permissions, 7 role grants, `Payroll.View` granted to no role.
- **Slice 2 (US-BE-007) — done and committed** in `feat(auth-slice-2): user credential and security state` (the commit after `3307bd0`): credential and security-state columns with their constraints, bootstrap index and trigger, backfill of existing rows, aggregate-only security mutations (R34) with security-version semantics (E7), one account per Party (R30), case-insensitive login lookup (E10), seed-rollback safety test. Details: ADR 0003 §11. Tests on PostgreSQL 16.15: Domain 215/215; Integration 206/215 — the same 9 environment-specific RESTRICT tests as below fail; every other test passes, including the 38 new integration test cases (177 → 215) and the 58 new Domain test cases (157 → 215).
- **Slice 2B (US-BE-008) — done and committed** in `feat(auth-slice-2b): session, refresh-token, reset-ticket and throttle persistence` (the commit after the design commit `b59d4c9`). Four additive migrations (`AddAuthSessions`, `AddRefreshTokens`, `AddPasswordResetTickets`, `AddAuthThrottleCounters`); Domain `AuthSession`/`RefreshToken`, `PasswordResetTicket`, `ClientApp`, `ClientPlatform`, `SessionRevokeReason`; Infrastructure session lookup and revoke-all, atomic ticket issue, and the policy-free throttle store with batched cleanup. Spike results and implementation notes: ADR 0005 §7, §7A. Tests on **PostgreSQL 18.6**: build 0 warnings / 0 errors; Domain **267/267** (+52: `AuthSessionTests` 31, `PasswordResetTicketTests` 21); Integration **341/341** (+126: spikes 10, schema 82, per-migration down-and-up 4, slice down-and-up 1, sessions 9, tickets 9, throttle store 11), stable over 6 consecutive full runs; `ModelConsistencyTests` passing; no leftover `contractra_test_*` database. The throttle **policy** is not implemented (contract §22, pending review).
- **Slice 3 (US-BE-009) — done and committed** in `50832d1` and `d5c4c26` (`audit_events`, append-only triggers and privileges, `IAuditWriter`, privilege self-check). Its design, repository ADR 0006 (E26–E44), is still **proposed, pending owner review**.
- **Slice 4 (US-BE-010) — implemented; owner-approved 2026-10-17 with open items below.** The seven use cases (CreateUser, ChangePassword, DisableUser, ReactivateUser, IssuePasswordReset, CompletePasswordReset, ReissueInitialPassword), their Application ports, the Infrastructure implementations (`UnitOfWork` with a single save-and-commit exit, user/role/party repositories, session and reset-ticket stores, password hasher over `PasswordHasher<TUser>`, temporary-password and Crockford reset-code generators, clock, business date) and the new `Contractra.Application.Tests` project. `RevokeAllActiveAsync` and `IssuePasswordResetTicketAsync` now throw without an open transaction (ADR 0006 §6, §12). Tests on **PostgreSQL 18**, final working tree: build 0 warnings / 0 errors; Application **70/70**; Domain **359/359**; Integration **515/515** (the 7 added in the acceptance fixes prove that a failing audit save rolls back IssuePasswordReset, CompletePasswordReset and ChangePassword completely, and that the reset code is sent only after the commit). Total 944.
- **Slice 4 open items (recorded, not resolved):**
  1. **OC-6** — at the time of this Slice 4 approval, `actor_role_code` was written as null on every Slice 4 audit event pending owner confirmation of contract §14.1's rule. **OC-6 was confirmed 2026-10-18** (see Slice 6 below) and applied to all seven Slice 4 use cases: `ChangePassword`/`CompletePasswordReset` (no permission required) resolve to the actor's first active role; `CreateUser`/`DisableUser`/`ReactivateUser`/`IssuePasswordReset`/`ReissueInitialPassword` (all require `Users.Manage`) resolve to the active role through which the actor held it, falling back to the first active role when none grants it directly. `AuditAssert.ActorRoleCodeIsUnresolvedPendingOc6` is no longer referenced by any Slice 4 use case's own test; it remains available for any future audit-writing path that still needs the interim-null assertion.
  2. **Delivery-failure outcome** — contract §9.6 step 6 says a failed delivery is logged and leaves the committed ticket valid, but not which HTTP status the caller gets. `IssuePasswordResetResult.DeliveryFailed` carries the fact; Slice 6/7 maps it once the owner decides.
  3. **OC-2** — no delivery channel exists; every reset returns `users.reset_delivery_unavailable` (503).
  4. **ADR 0006 and contract §22** remain proposed; Slice 4 depends on the Slice 3 design but does not approve it.
  5. **Slice 6 concern** — `ChangePassword` commits and leaves the current session active so Slice 6 can rotate its refresh token afterwards; Slice 6 must either rotate in the same transaction or treat a post-commit rotation failure as recoverable (documented on `ChangePasswordUseCase`).
  6. **Follow-ups** — concurrent CreateUser/CompletePasswordReset races surface as raw database exceptions rather than the contract's 409 / generic error (map in Slice 6/7); `ChangePassword` does not check temporary-password expiry (login blocks an expired one, §10); `CompletePasswordReset` validates the new password before the code (contract §5.6 orders it last).
- **Slice 5 (US-BE-011) — done and committed; owner-approved 2026-10-17 including the generated-password design of `reset-bootstrap-password`.** `SeedBootstrapAdminUseCase` (no-op when an Active user holds `Admin`; never overwrites; creates the bootstrap admin with its direct `Payroll.View` grant and writes `UserCreated`, `RoleAssigned`, `PayrollViewGranted` — System actor, source `SystemJob`, fixed reason, one correlation id per run, keys `<action>:bootstrap:<user id>` — in one transaction, and fails startup if the audit save fails); `StartupInitializer` running the audit privilege self-check (order 0) before the seeder (order 100); the operator commands `issue-password-reset --login --reason` and `reset-bootstrap-password --reason` (`OperatorCommandRunner`, entered from `Program.cs`, no web host; System actor, reason, key `<action>:command:<run correlation id>`; the secret is printed once and never logged or audited). Tests on **PostgreSQL 18**, working tree: build 0 warnings / 0 errors; Application **111/111**; Domain **359/359**; Integration **552/552** (37 added: seeder create / repeat / missing configuration / audit-failure rollback / concurrent start, self-check policy and real-database ordering, command end-to-end, rollback, refusals, usage). Total 1022. Not run: a real `dotnet run` of the web host (needs a database; only the command usage path was smoke-tested).
- **Slice 5 open items (recorded):**
  1. **`reset-bootstrap-password` design (R32, contract §11 delegate it to Slice 5) — approved by the owner 2026-10-17:** the new password is *generated* (contract §10 generator) and printed once on the terminal, like the §10A reset code; the bootstrap admin stays exempt from the forced change (R19). Session revocation reuses reason `PasswordChanged` because the nine reasons are fixed by a database CHECK.
  2. **Privilege self-check scope:** per ADR 0006 §7.3 it fails closed only in Production and warns elsewhere (development and tests connect as the table owner). The compliant-role case cannot be produced by the tests (roles are cluster-wide; ADR 0006 §7.3), so compliance is covered by policy unit tests on hand-built reports; the non-compliant case is covered against the real database.
  3. **Commands also run the self-check first** (they write audit events); the contract does not say so — a safety choice.
  4. **A database that is unreachable or not migrated now stops application startup** (the self-check and seeder need it). Operators of a bare `dotnet run` without a database see a startup failure.
  5. **Operator-account restriction** (§18 "run only on the server, by an operator account") and the "do not redirect the output" rule are operational: the code documents them in the usage text and cannot enforce them.
  6. **Concurrent starts:** the loser's insert is refused by the database and, when an Active Admin then exists, treated as a no-op; no audit idempotency is relied on for this (the keys contain the new user id).
  7. OC-2, the reset-delivery-failure HTTP outcome, ADR 0006 and contract §22 remain as listed under Slice 4. **OC-6 is now confirmed (2026-10-18, below) and no longer an open item.**
- **Slice 6 (US-BE-012) — implemented and committed (`fa62a98`); test evidence passing; not yet formally accepted.** `/api/v1/auth/*` endpoints, JWT issuance/validation with the key ring, app-audience enforcement, the password-change gate, CSRF/CORS, throttling over the Slice 2B store, ProblemDetails, correlation ID, HTTPS/HSTS, Serilog. Includes corrective fixes made and verified in this working tree: password-reset validation ordering (newPassword's 128-character maximum as a §1 pre-lookup transport check; its 8-character minimum as a §5.6 business rule checked last), change-password's check ordering (`currentPassword` correctness and expiry before the new-password length rules), and the PostgreSQL test-fixture connection-pool fix (`PostgresTestDatabaseFixture.DisposeAsync` now clears only its own Npgsql pool before its preserve-by-default teardown). Tests: Application **175/175**; Domain **359/359**; API **170/170**; Integration **584/584** (no PostgreSQL 53300 connection-exhaustion recurrence after the pool-clear fix). Full counts and dates: this session's working-tree history.
- **OC-6 resolved (2026-10-18), applied consistently across every existing identity audit-writing path.** The owner confirmed the §14.1 `actor_role_code` rule exactly as drafted (contract §19 OC-6, §21). First applied to `AccessDenied` (already resolving it since Slice 6 was first implemented, ADR 0006 E36), `PasswordChanged` and `PasswordResetCompleted`. On review, the scope restriction that initially left the five Slice 4 users-endpoint use cases unchanged (on the reasoning that their HTTP endpoints are Slice 7) was found inconsistent with the decision itself: OC-6 confirms a value for `actor_role_code`, not a scope limited to Slice 6's own endpoints, and these use cases already exist, are tested, and write real audit rows today. `CreateUserUseCase`, `DisableUserUseCase`, `ReactivateUserUseCase`, `IssuePasswordResetUseCase`, `ReissueInitialPasswordUseCase` were updated to resolve `actorRoleCode` against `Users.Manage` (the permission each already required, per their own prior comments and contract §9's header), using the same `IUserAccessReader` + `ActorRoleCodeRule.Resolve` mechanism. No HTTP endpoint was implemented or wired for these use cases; their Slice 7 exposure is unaffected. New tests: 10 added (2 per use case: the qualifying-role-wins case and the no-qualifying-role-falls-back-to-first-active-role case); each use case's existing audit-shape test now asserts its no-active-role `null` result explicitly as the confirmed rule's own output, not an unresolved placeholder; the rule's own unit tests (`ActorRoleCodeRuleTests`) already covered every branch and needed no change beyond their doc comment. Application tests: **185/185** (175 + 10 new).
- **Slice 6 remaining acceptance conditions (not resolved by OC-6):**
  1. This file's own tracking (this edit) was the first update recording Slice 6 as implemented; before this, §A and §C both still read "Planned" despite `fa62a98` already being on this branch — a documentation lag, not a functional gap.
  2. ADR 0006 and contract §22 (including I1–I20, the throttle-policy proposals A–E/H, and the OC-8 M1/M3/M5 mitigation choice) remain **proposed, not accepted** — OC-6's confirmation does not extend to any of them (contract §21, 2026-10-18 row).
  3. ~~M1 and M5 (contract §22, "required in Slice 6") — verify against the actual throttle implementation before treating Slice 6 as covering them.~~ **Implemented 2026-10-19** (see below); still part of §22, which stays proposed.
  4. OC-1/OC-2/OC-3/OC-4/OC-7 remain open but are Slice 7 (`/users/*`)-scoped, not Slice 6 blockers.
- **Slice 6 handoff (2026-10-21) — implementation-complete; the team can proceed to Slice 7.** This records a handoff status only. It is **not** production approval and **not** owner approval of ADR 0006, contract §22, OC-8, OC-9, the E27 deviation, or Slice 6 itself; ADR 0006 and §22 stay Proposed.
  - **Scope delivered:** the auth endpoints and HTTP pipeline (`fa62a98`), plus the uncommitted follow-ups in this working tree: OC-6 `actor_role_code` applied across the identity audit paths, M1 source grouping, M5 `clear-throttle` (atomic with its `ThrottleCleared` audit event; target `OperatorCommandRun` + the run's UUID; no identifier or hash in the event, CLI output or its logs), the 16th audit code and its migration, and the related tests and documentation.
  - **Test evidence (recorded; not re-run for the handoff):** Domain 360/360, Application 201/201, `ClientSourceTests` 8/8; and, on an isolated PostgreSQL 18.6 instance with all exit codes 0 and nothing failed or skipped, the PostgreSQL groups listed in the 2026-10-20 entries below and in `backend/reviews/slice-06-acceptance-review-2026-10-19.md` (Addenda 2 and 3). **Limits:** other Infrastructure classes were not run on this tree; the `Down` of `AddThrottleClearedAuditAction` was exercised only without `ThrottleCleared` rows; other migrations' `Down` round trips were not run; nothing was run against a real target database.
  - **Deferred, tracked, not blocking development:** OC-8 (accept the Layer 3 starvation risk with the existing slow-mode warning log and `clear-throttle`, or require M3 trusted devices); OC-9 (retention of session, token and reset-ticket history); the E27 migration deviation (`AddThrottleClearedAuditAction` drops and re-adds the CHECK instead of `NOT VALID` then `VALIDATE`) and whether the migration was ever applied to a real database; the E33/E36 deviation of the `OperatorCommandRun` audit target; whether the login-path `Auth.Throttled` logs keep the identifier key hash (contract §17); the unrecorded `AccessDenied` target decision; no alert or runbook is wired to the slow-mode warning (operational readiness of the OC-8 mitigation); formal acceptance of ADR 0006, contract §22 and Slice 6.
  - **Residual risks:** a coordinated attacker (about 30+ sources) can slow or block one account's logins and reset completions until an operator clears it; the new migration's behaviour on a database that already holds `ThrottleCleared` rows is untested.
  - **Working-tree note:** the Slice 6 follow-ups above are uncommitted on `backend/auth-slice-3`; they must be committed by the owner of the branch before other developers can build on them.
- **M1/M5 implemented (2026-10-19); contract §22 itself remains proposed, unaffected by this change.** M1 (source grouping): `ClientSource.ThrottleGroupOf` groups one IPv6 /64 (`IPNetwork`, byte-level, not string truncation) or one IPv4 address, wired into the shared throttle's two HTTP call sites (login, reset-completion); layer 1/5's in-memory limiter still uses the exact address via the unchanged `ClientSource.Of`. M5 (audited operator clear): command `clear-throttle --identifier <text> --reason <text>` (`ClearThrottleUseCase`, wired into `OperatorCommandRunner`); clears layer 3's identifier-wide state only (layer 2's per-source pairs are not enumerable by identifier alone); audits a new `ThrottleCleared` action (migration `AddThrottleClearedAuditAction`, additive to the action-type CHECK constraint; **not applied by any of these working sessions — whether it has been applied to any database elsewhere is not known**).
- **M5 atomicity redesign (2026-10-19, supersedes the same day's earlier partial-success-reporting note below it).** The original M5 implementation cleared the throttle row through `IAuthThrottle.ClearIdentifierAsync`, which (like every other `IAuthThrottle` member) ran in its own DI scope and its own short transaction against its own `ContractraDbContext` instance (`AuthThrottle`'s own doc comment: "each step runs in its own DI scope, so its short transaction never joins a use case's"; `AuthThrottleStore.ApplyAsync` actively throws if it detects an ambient transaction). That meant the delete and the `ThrottleCleared` audit write were on two different connections and could never share one PostgreSQL transaction - a genuine atomicity gap, not merely a reporting gap. *(Corrected 2026-10-19: this is not the same case as ADR 0006 §6's "two existing helpers", which break atomicity when no transaction is open; the throttle store is designed the other way — it refuses to join any transaction — which is why the original delete could not share the audit transaction.)*
  - **Design chosen (smallest sound option; two alternatives considered and rejected):** a new, narrow Application port, `IThrottleIdentifierEraser` (`Contractra.Application/Identity/Auth/AuthPorts.cs`), deliberately **not** part of `IAuthThrottle` and not routed through `AuthThrottleStore.ApplyAsync`. Its Infrastructure implementation, `ThrottleIdentifierEraser` (`Contractra.Infrastructure/Persistence/Throttling/ThrottleIdentifierEraser.cs`), takes the same scoped `ContractraDbContext` the use case's `IUnitOfWork` already uses and issues `AuthThrottleCounters.Where(...).ExecuteDeleteAsync(...)`, which joins the ambient transaction EF Core opens via `BeginTransactionAsync`, exactly like `IAuditWriter`/`AuditWriter` already do for the audit row. Rejected: (a) threading an optional ambient transaction through `AuthThrottleStore.ApplyAsync` itself - larger change, touches the invariant every other throttle operation (admit/release) relies on to never hold a transaction open behind a password hash; (b) a compensating/manual-undo action - the task directs against this when both writes can be coordinated in the same database, and they can.
  - **`ClearThrottleUseCase` rewritten:** `BeginTransactionAsync` now happens first; `IThrottleIdentifierEraser.ClearIdentifierAsync` and `IAuditWriter.Stage` both run inside it; one `SaveChangesAndCommitAsync` commits both or an exception rolls back both (the use case no longer calls `_unitOfWork` only after the clear - the clear is now inside the transaction, not before it). The distinct `ClearedButAuditFailedCode`/"partial success" result from the 2026-10-19 reporting fix is **removed**: with real atomicity there is no longer a partial-success state to report, so every failure path returns the ordinary `ThrottleStoreUnavailableCode` and "nothing was changed" is now always true, not just usually true. `IAuthThrottle.ClearIdentifierAsync` was removed from the interface (it had no other caller); the operator-command host's throttle-only DI slice (`AddAuthThrottleOnly`) is removed along with it, since `clear-throttle` no longer needs `IAuthThrottle` at all - only `ContractraDbContext`, already available via `AddInfrastructure`.
  - **Tests:** `OperatorCommandUseCaseTests.cs`'s `ClearThrottle_*` tests rewritten against a new `FakeThrottleIdentifierEraser` (replacing `FakeAuthThrottle.ClearIdentifierAsync`); the old partial-success test is replaced by `ClearThrottle_WhenTheAuditWriteFails_ReportsTheOrdinaryRefusalCode_NeverAPartialSuccess`, plus a no-row-existed case and an eraser-failure case. **New, not yet executed** (requires `CONTRACTRA_TEST_POSTGRES`): `Contractra.Infrastructure.IntegrationTests/OperatorCommandIntegrationTests.cs`'s `ClearThrottleIntegrationTests` - in particular `ClearThrottleCommand_WhenTheAuditSaveFails_RollsBackTheDelete_LeavingTheRowIntact`, which is the actual database-level proof of atomicity (the fake-based Application tests can only prove the use case's own orchestration, not that PostgreSQL really rolls both writes back together).
  - **Still not verified (as of 2026-10-19; partly superseded by the 2026-10-20 PostgreSQL note below):** the integration tests above had not been run against a real database; the `AddThrottleClearedAuditAction` migration has been reviewed statically but has no executed `Up`/`Down` round trip. Until the integration tests run, "real atomicity" rests on the code-level transaction-sharing argument above, not on an observed database rollback.
- **`ThrottleCleared` owner decisions applied (2026-10-19; Slice 6 acceptance review D1, D4).** `ThrottleCleared` is now the 16th audit code in contract §14 and ADR 0006 §5.3. Data minimization: `ClearThrottleUseCase` returns no value (the `ClearThrottleResult` that carried the key hash is removed), its Serilog messages carry only the correlation id, and `clear-throttle` prints "Throttle cleared." without the key. The Domain now requires a reason for `ThrottleCleared` (`AuditEvent.ForSystem`, ADR 0006 E34). Tests: Domain **360/360** (one new reason case), Application **199/199** at that point (log tests now assert the hash is absent), Api `ClientSourceTests` **8/8**; the PostgreSQL integration test `ClearThrottleCommand_DeletesTheIdentifierRow_AndAuditsInOneCommit` gained output/log assertions but has **not been run**.
  - **`entity_id` resolved (2026-10-19, follow-up):** the event now targets `entity_type = "OperatorCommandRun"`, `entity_id` = the run's UUID — the per-run GUID generated by `OperatorCommandRunner` (also the correlation id and the idempotency key's operation part; no separate run record exists and none was invented). `ClearThrottleUseCase` parses it and refuses a non-UUID or empty value with `validation.failed`. The target login identifier and its hash are no longer stored in the audit event, printed, or logged by the command. No schema change, no nullable `entity_id`. Deviation from ADR 0006 E33/E36 recorded in the ADR and contract. Application tests now **201/201**; the PostgreSQL integration test was updated to read the run id from the CLI output and has **not been run**.
  - **Blocked — migration (D2):** `AddThrottleClearedAuditAction` drops and re-adds the CHECK instead of ADR 0006 E27's `NOT VALID` then `VALIDATE`. Not edited, because whether it has been applied to any database is not known.
- **PostgreSQL verification (2026-10-20), isolated instance only.** Four authorized test groups ran on `Contractra.Infrastructure.IntegrationTests` against a dedicated PostgreSQL 18.6 instance (`127.0.0.1:55432`, fresh `contractra_test_<guid>` databases, drop disabled): the four `ClearThrottleIntegrationTests` (success and audit, audit-failure rollback leaves the throttle row intact, no-row, layer-2/other identifiers) **4 passed**; `AddAuditEventsMigrationTests` **4 passed**; `AuditEventsSchemaTests` **109 passed**; `ModelConsistencyTests` **1 passed** - **118 passed, 0 failed, 0 skipped**, all exit codes 0. This database-verifies M5's atomicity and the 16-code catalog CHECK, and exercised the new migration's `Up` on fresh databases. **Still open:** the migration's `Down` was exercised only by the `AddAuditEventsMigrationTests` down-and-up test, on a fresh database with no `ThrottleCleared` rows (*corrected 2026-10-20*; `Down` with such rows is untested); whether it was applied to any real database is unknown; the E27 deviation is unresolved; OC-8 and OC-9 are undecided; the rest of the Integration project and the PostgreSQL-backed API tests had not been run at that point (later runs are in the next entry); ADR 0006 and section 22 stay Proposed; Slice 6 is not accepted. Details: `backend/reviews/slice-06-acceptance-review-2026-10-19.md`, Addendum 2.
- **Further PostgreSQL verification (2026-10-20, later runs), same isolated instance.** Passed with 0 failed and 0 skipped, all exit codes 0: `AuthThrottleStoreTests` 11, `AuthThrottleIntegrationTests` 7, `AuthThrottlePolicyTests` 17 cases; API `ThrottlingTests` 16, `RateLimitLayerTests` 3, `CompositionTests` 2; the remaining 11 API classes 149 (`AudienceAndGateTests`, `AccessDeniedWriteFailureTests`, `ChangePasswordEndpointTests`, `JwtValidationTests`, `LoginEndpointTests`, `LogoutAndMeEndpointTests`, `PasswordResetCompletionEndpointTests`, `PipelineTests`, `HttpsOnlyTests`, `RefreshEndpointTests`, `SecretLeakTests`; API-host classes ran with User Secrets isolated, verified by a boolean probe); `AuditEventsAppendOnlyTests` 13, `AuditPrivilegeSelfCheckTests` 11, `AuditPrivilegePolicyTests` 9, `AuditEventsAtomicityTests` 14. `AddAuditEventsDownGuardTests.Down_Refuses_WhenTheTableHoldsRows_AndNothingIsLost` also **passed (1/1)** after a test-only rewrite (uncommitted) that first rolls the empty database back to `AddAuditEvents` so only that migration's own `Down` guard is attempted; its earlier form was never run. **Still open:** `AddThrottleClearedAuditAction.Down` with `ThrottleCleared` rows present is untested; other rollback scenarios (the other migrations' `Down` round trips) are untested; the remaining Infrastructure classes were not run; the migration's status on any real database is unknown; the E27 deviation is unresolved; OC-8 and OC-9 are undecided; ADR 0006 and section 22 stay Proposed; Slice 6 is not accepted. Details: `backend/reviews/slice-06-acceptance-review-2026-10-19.md`, Addendum 3.
- **Not done:** no users endpoints (Slice 7). `Contractra.Api/Program.cs` still has the `/weatherforecast` template.
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
| **R46** | (2026-10-16; owner decision recorded in repository ADR 0006 §3 and README §1, not in the plan) Permanent audit vocabulary: Employee application source **`EmployeeApp`**; user-disable event **`UserDisabled`**; role-ending event **`RoleEnded`** (repository ADR 0006 §3) | Slice 3 catalogs; contract §22 I1, I2; upstream corrections ADR 0006 §10 |
| **R47** | (2026-10-16; owner decision recorded in repository ADR 0006 §3 and README §1, not in the plan) No production launch with real business or user data until the Audit Completion slice is implemented, tested, reviewed and approved; development and automated testing continue (repository ADR 0006 §3, §8.3) | Gate B (§C below) |
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
| 2B | US-BE-008 | Persistence for sessions, refresh tokens, reset tickets and the throttle store | Slice 2 | ✅ Done (see §A; ADR 0005) |
| 3 | US-BE-009 | `audit_events` with explicit event context in its snapshot field (contract §14, §22 I; repository ADR 0006) — the explicit business-event level only | Slice 2 | ✅ Done (`50832d1`, `d5c4c26`); design (ADR 0006) still proposed, pending owner review |
| 4 | US-BE-010 | Application layer: hashing, temporary passwords, clock, use cases | 2, 2B, 3 | ✅ Done (see §A; open items listed there) |
| 5 | US-BE-011 | Bootstrap seeder; operator commands | 4 | ✅ Done (owner-approved 2026-10-17; open items in §A) |
| 6 | US-BE-012 | Auth endpoints, JWT, app audience, throttling, HTTP pipeline | 4 (5 for end-to-end tests) | Implementation-complete for team handoff (2026-10-21; `fa62a98` plus the uncommitted follow-ups in §A); test evidence passing (see §A); **not owner-accepted — deferred items tracked in §A** |
| 7 | US-BE-013 | Users endpoints | 6 | Planned; **may start (handoff 2026-10-21)** — Slice 6's deferred items (§A) stay tracked and do not block development. (This replaces this record's earlier "do not start before" note; it is a handoff decision recorded here, not an owner approval.) |
| AC | — (story written when scheduled) | **Audit Completion** (mandatory): generic change capture into `audit_event_changes` (genuine field changes only), masking before writing, `audit_event_links`, further snapshots where required, idempotency construction, retention and archival per ح-190 with `audit_retention_policies` (repository ADR 0006 §8.2) | 3 | Not scheduled in Sprint 2; **Gate A and Gate B depend on it** |

**Release gates (repository ADR 0006 §8.3).** Slice 3 alone does not satisfy the generic data-change level of ح-185 / FR-AUD-003.
- **Gate A — financial-module gate:** no migration introducing a financial table or financial module may be merged until Audit Completion is implemented, tested, reviewed and approved (ح-185, FR-AUD-003, FR-AUD-005, IMM-02).
- **Gate B — production-data gate (owner decision R47):** no production deployment may manage real business or user data until Audit Completion is complete and its acceptance criteria have passed — including the ح-190 retention criterion (ADR 0006 §8.2).
- Neither gate stops Slices 4–7, other development or automated testing.

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

### US-BE-008 — Auth session, reset-ticket and throttle persistence (Slice 2B) — ✅ done

> *Deviations recorded at implementation (2026-10-15):* the new constraint and index names are asserted by the new `AuthPersistenceSchemaTests` rather than added to `SchemaConstraintNameTests` (TASK-BE-046), which is unchanged; EF Core's convention foreign-key indexes `ix_refresh_tokens_session_id` and `ix_password_reset_tickets_issued_by_user_id` are kept and named; the scaffolded `AddAuthSessions` migration lists `xmin`, which the Npgsql DDL generator leaves out (ADR 0005 §7); the four concurrency-heavy test classes run in a non-parallel collection because together they exceeded the test server's connection limit.

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

### US-BE-009 — `audit_events` with explicit event context (Slice 3)

> *Re-specified 2026-10-16* after the read-only Slice 3 review: design in **repository ADR 0006** (proposed E26–E44, pending owner review; not business ADR-006), owner decisions R46–R47, contract wording proposed in contract §22 I (pending review). The earlier text of this story, kept below for history, said "exactly as contract §14.1" and "UPDATE and DELETE"; it is superseded by the text after it.
>
> *Corrected 2026-10-16 (documentation correction pass, ADR 0006 revision note):* event context moved from `audit_event_changes` rows to the logical model's snapshot field, so Slice 3 creates only `audit_events`; idempotency keys required on `SystemJob`, `OfflineSync` and `Import` events; reason required for operator-command events (seeder: fixed reason), not for every `System` event; the self-check covers `TRIGGER`; retention (ح-190) added to Audit Completion.
>
> *Superseded text (2026-10-15):* "audit_events exactly as contract §14.1 (every logical-model §15.4 field plus actor_type), CHECKs linking actor_type/actor_user_id/actor_role_code, an append-only trigger rejecting UPDATE and DELETE, and the Application IAuditWriter with its Infrastructure implementation writing in the caller's transaction."

```
Delivers (repository ADR 0006 §4, §5):
- Domain: AuditEvent (immutable, UUIDv7) with ForUser / ForSystem factories,
  AuditActorType, AuditSource, the action-type catalog, the snapshot key
  lists per action type (ADR 0006 §5.2).
- Application: IAuditWriter (stages only, no EF Core reference).
- Infrastructure: scoped writer adding to the caller's ContractraDbContext
  (never SaveChanges), configuration, one additive migration after
  AddAuthThrottleCounters creating audit_events, the privilege self-check
  query.
- Catalogs: the 15 contract §14 action codes, with UserDisabled and RoleEnded
  (R46); sources Web, EmployeeApp (R46), OfflineSync, Import, SystemJob.
- Not columns: client_event_id, ip_address, user_agent, payload.
- Not tables in this slice: audit_event_changes, audit_event_links,
  audit_retention_policies (Audit Completion).
Depends on: Slice 2.
Key acceptance:
- A forced audit-write failure rolls back the paired change, including a bulk
  ExecuteUpdate run inside the caller's explicit transaction (ح-186).
- UPDATE, DELETE, a zero-row UPDATE, TRUNCATE, TRUNCATE ... CASCADE and
  INSERT ... ON CONFLICT DO UPDATE are rejected on audit_events (SQLSTATE
  42501, trigger name); INSERT succeeds.
- System events have no user, no role and source SystemJob; operator-command
  events carry the operator's reason and seeder events a fixed reason;
  correlation_id NOT NULL and in the contract §13 format.
- SystemJob, OfflineSync and Import events cannot be written without an
  idempotency_key (database CHECK); the key is always
  <action_type>:<operation key>[:<sub-identifier>] and starts with the row's
  action type (database CHECK); seeder and command keys follow ADR 0006 §5.3.
- Every CHECK, the RESTRICT foreign keys (23001 or 23503) and the global
  partial unique index on idempotency_key are tested by name; the catalog
  CHECKs equal the C# catalogs.
- Snapshots record the affected assignment, grant or ticket (ids, codes and
  timestamps) at the time of the event, only on the listed action types and
  only with their keys; no password, hash, reset code, token, token hash or
  login identifier can be written.
- The self-check query reports UPDATE, DELETE, TRUNCATE and TRIGGER
  privileges, ownership and superuser.
- Down refuses to drop a non-empty audit_events; per-migration and whole-chain
  down-and-up tests pass; existing down-migrating tests still pass;
  ModelConsistencyTests passes.
Verify: Domain tests and PostgreSQL 18 integration tests (repository ADR 0006 §9).
Must not change: Slice 2 / 2B schema; contract §1–§21 (proposals stay in §22).
Not in this slice (Audit Completion, ADR 0006 §8.2): generic change capture
into audit_event_changes, masking before writing, audit_event_links, further
snapshots, replay key construction for sync and import, retention and
archival (ح-190).
Slice 3 alone does NOT satisfy the generic level of ح-185 / FR-AUD-003;
Gates A and B (§C) apply.
Obligations handed on (ADR 0006 §12): Slice 4 - unit of work with a single
save-and-commit exit, transaction opened before the first write,
RevokeAllActiveAsync and IssuePasswordResetTicketAsync refuse to run without
an open transaction; Slice 5 - privilege self-check at startup before the
seeder (fails closed in Production), correlation id per run, reasons and
idempotency keys on seeder and command events; Slice 6 -
correlation id at the HTTP boundary, AccessDenied in its own scope and its
target.
```

### US-BE-010 — Application layer and use cases (Slice 4) — ✅ done (owner-approved 2026-10-17; open items in §A)

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

### US-BE-011 — Bootstrap seeder and operator commands (Slice 5) — ✅ done (owner-approved 2026-10-17; details and open items in §A)

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

**Total: 8 user stories (US-BE-006, US-BE-007 and US-BE-008 done; US-BE-009…013 planned). Tasks: TASK-BE-024…048 done. Tasks for US-BE-009…013 are written when each slice starts.**

---

## D. FRONTEND ALIGNMENT

Both Flutter apps still follow the pre-contract mock design. Their alignment with contract v1.5 is planned in `docs/implementation/sprints/frontend-auth-contract-alignment-plan.md` and starts only after the matching backend endpoints exist (Slices 6–7). This sprint does not modify `apps/`.

---

## E. GIT / PROCESS RULES FOR THIS SPRINT

- Do not modify `apps/contractra_admin` or `apps/contractra_employee`.
- Do not touch unrelated concurrent-session work.
- PostgreSQL integration tests only against `CONTRACTRA_TEST_POSTGRES` ephemeral databases — never the development database. The project's test server is PostgreSQL 18 (ADR 0001 §14); on PostgreSQL 16 the nine `ON DELETE RESTRICT` tests report 23503 instead of 23001 (see §A), and new tests must not depend on that difference.
- `PostgresTestDatabaseFixture` preserves each run's ephemeral `contractra_test_*` database by default — it is not dropped automatically. Set `CONTRACTRA_TEST_ALLOW_DATABASE_DROP=true` to opt into automatic teardown; any other value, or leaving it unset, preserves the database for inspection.
- No broad git commands (`git add .`, `clean`, `reset`, `stash`, `revert`); stage explicit paths.
- RED → GREEN → REFACTOR per task; commit only after the slice's tests are green; do not push unless explicitly asked.
