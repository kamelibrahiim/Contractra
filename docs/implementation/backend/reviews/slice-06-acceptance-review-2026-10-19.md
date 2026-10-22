# Slice 6 acceptance review — ADR 0006 / Contract §22 matrix (2026-10-19)

**Kind:** working document under `docs/implementation/**` (`docs/implementation/README.md` lines 3, 5–14). Not ranked in `docs/documentation-index-ar.md`. It is an evidence record only: it changes no approval status, makes no owner decision and does not request owner approval.

**Statuses this file does not change:** repository ADR 0006 is **Proposed** (`docs/decisions/0006-audit-events-persistence.md` line 3). Contract §22 is **proposed, not applied, not approved policy** (`docs/implementation/backend/features/auth-and-users-api-contract.md` line 820). OC-8 is **undecided** (contract line 781). OC-6 is confirmed (contract line 779) — and that confirmation covers the one §14.1 `actor_role_code` rule only, **not** ADR 0006 or Contract §22 as a whole.

**Revision:** corrected 2026-10-19 (second pass) — see "Corrections made in this revision" at the end.

**Line references (noted 2026-10-22):** line numbers in this file refer to the files as they stood when the review was written. Several no longer match the current files. In the contract, OC-6, OC-8 and §22 have moved after later edits. Use the section references (OC-6 and OC-8 in contract §19; contract §22; ADR 0006 §§ as cited) to locate the text. The evidence and statuses recorded here are unchanged.

## Status legend

| Term | Meaning in this file |
|---|---|
| **Implemented** | Code exists; the file and symbol are cited. |
| **Unit-tested** | A database-free test covers it. The row says whether that test was run on 2026-10-19 (Domain.Tests 359/359, Application.Tests 199/199, Api.Tests filtered to `ClientSourceTests` 8/8) or was not run. |
| **Integration-tested** | A PostgreSQL test covers it. The row says whether it was last run before the current code (stale) or never. |
| **Database-verified** | That PostgreSQL test passed against the current working tree. **As of the 2026-10-19 snapshot, no item was database-verified**: no PostgreSQL-dependent test had been run since the OC-6 extension, M1 and M5 changes (the last recorded full counts, API 170/170 and Integration 584/584, predate them — `docs/implementation/backend/sprints/sprint-02.md` line 63). **Since 2026-10-20 a limited set of items is database-verified** (118 tests, four named groups, one isolated PostgreSQL 18.6 instance): see Addendum 2 at the end for exactly which, and Addendum 3 for the larger set that passed in later runs the same day. Anything not named in Addendum 2 or 3 keeps its "stale" or "never run" status. |
| **Owner-approved** | An explicit owner decision recorded in a source document. |

Test totals above come from the verification run of 2026-10-19 that preceded this documentation-only pass; no test was re-run for this revision. **Reading note (added 2026-10-20):** the tables below are the 2026-10-19 snapshot and are kept as written. Their "stale", "never run" and "not run" wording is historical; where the PostgreSQL results of 2026-10-20 change a status, Addendum 2 (first run) and Addendum 3 (later runs) say so. Those totals (Domain 359, Application 199) are also historical: the later current-tree counts are Domain 360/360 and Application 201/201 (recorded in `sprint-02.md`).

---

## Part 1 — Owner decisions recorded in ADR 0006 §3

| # | Title | Source | Requirement (source terms) | Status | Implementation | Tests | Evidence | Gap / ambiguity | Next action · owner? |
|---|---|---|---|---|---|---|---|---|---|
| R46 | Permanent audit vocabulary | ADR 0006 §3, line 43; precedence line 46 | Employee app source = `EmployeeApp`; user-disable event = `UserDisabled`; role-ending event = `RoleEnded`; Admin app keeps `Web` | **Owner-approved** (2026-10-16) · Implemented · Unit-tested (run) | `Contractra.Domain/Audit/AuditSource.cs` line 14 (`EmployeeApp`); `Contractra.Domain/Audit/AuditActionType.cs` | `Contractra.Domain.Tests/Audit/AuditEventTests.cs` lines 66–69 (`SourceCatalog_UsesEmployeeAppAndNotPwa`) and `ActionCatalog_IsTheSixteenContractCodes_WithTheOwnerSelectedNames` | Both tests in the 359/359 run | Protected documents still use `PWA` / `UserDeactivated` / `RoleChanged`; correction proposals U1, U2 (ADR 0006 §10) are pending with their owner (line 46) | None for the code. Upstream corrections U1/U2 need the protected documents' owner. |
| R47 | Production-data gate | ADR 0006 §3, line 44; §8.3 line 241 | No production launch with real business or user data until Audit Completion is implemented, tested, reviewed and approved; development and automated testing continue | **Owner-approved** (2026-10-16) = Gate B | N/A — release gate | N/A | ADR 0006 line 44 | None | None now; applies at production release (Part 8). |

## Part 2 — ADR 0006 §4 decisions E26–E44

The ADR states that E26–E44 "are not approved until the owner accepts this ADR" (line 3). Each row has its own basis (line 52: [C] confirmed requirement, [O] owner decision, [D] draft contract text, [E] engineering decision); they are not all [E]. **Every row below is Proposed as a decision**, whatever its implementation status.

| # | Title | Source · basis | Requirement (summary) | Status | Implementation | Tests | Evidence | Gap / ambiguity | Next action · owner? |
|---|---|---|---|---|---|---|---|---|---|
| E26 | `occurred_at_utc` | line 56 · [C] + [E] | Application clock, UTC, read once per use case, shared with the business change | Implemented · Unit-tested (run) | e.g. `ClearThrottleUseCase.cs` line 80 (`var now = _clock.UtcNow;`) | Application.Tests | Read once in `ClearThrottleUseCase` (checked); not re-checked use case by use case in this review | None found | ADR acceptance — owner |
| E27 | `action_type` / `source` catalogs | line 57 · [C] + [E] | Text + CHECK generated from one C# catalog each; a test compares them; a module that adds events replaces the CHECK in its own migration (`NOT VALID` then `VALIDATE`) | Implemented · Unit-tested (run) for the C# catalog · Integration-tested, **never run against the 16-value catalog** | `AuditActionType.cs` (16 values); `Migrations/20261018164129_AddThrottleClearedAuditAction.cs` | `AuditEventTests` catalog test (run); `Contractra.Infrastructure.IntegrationTests/AuditEventsSchemaTests.cs` lines 175–188 `CatalogChecks_EqualTheDomainEnums` (not run) | Migration lines 21–28 and 34–41 use `DropCheckConstraint` + `AddCheckConstraint` | **Discrepancies D1, D2** (catalog count; `NOT VALID`/`VALIDATE` not used) | Resolve D1/D2 — documentation owner / engineering; run the catalog test |
| E28 | Actor fields | line 58 · [O] R40, [D] §14.1, [C] | `actor_role_code` NULL for `System`; for `User`, NULL only when no active role | Implemented · Unit-tested (run) · Integration-tested, stale | `AuditEvent.ForUser`/`ForSystem`; `ActorRoleCodeRule`; CHECK `ck_audit_events_actor_consistency` | Application.Tests (OC-6 tests); `AuditEventsSchemaTests.cs` lines 92, 251, 335 (not run) | — | None found | ADR acceptance — owner; re-run integration tests |
| E29 | `schema_version` | line 59 · [D] + [E] | integer ≥ 1, per action type, 1 for Slice 3 events | Implemented · Unit-tested (run) | `AuditEvent` (`SchemaVersion = CurrentSchemaVersion`, `AuditEvent.cs` line 250) | Domain.Tests | Individual test-to-rule mapping not re-audited | None found | ADR acceptance — owner |
| E30 | Idempotency (two levels, one column) | line 60; §5.1 line 102; §5.3 lines 156–172 · [C] + [E] | Key `<action_type>:<operation key>[:<sub-identifier>]`; required for `OfflineSync`/`Import`/`SystemJob`; operator commands use `command:<the run's correlation id>` (§5.3 line 161) | Implemented · Unit-tested (run) · Integration-tested, not run for `ThrottleCleared` | `AuditEvent.BuildIdempotencyKey`, `AuditEvent.cs` lines 259–299 (required sources line 265; composition line 284) | See Part 2a | See Part 2a — conforms | `OfflineSync`/`Import` have no producer yet (future modules); Sprint 2 online Identity events carry no key, by design (line 60) | ADR acceptance — owner |
| E31 | `managed_company_id`, `operation_id`, `site_id` | line 61; §11 line 270 · [C] + [E] | NULL; temporary CHECK keeps all three NULL | Implemented (schema) · Integration-tested, stale | `AddAuditEvents` migration; CHECK `ck_audit_events_context_not_yet_defined` | `AuditEventsSchemaTests.cs` lines 96, 274–276 (not run) | — | Target of `managed_company_id` undefined (§11) — deferred by the ADR itself, not a Slice 6 item | None for Slice 6 |
| E32 | `approved_by_user_id` | line 62; §11 line 271 · [C] + [E] | FK RESTRICT; temporary CHECK keeps it NULL | Implemented (schema) · Integration-tested, stale | CHECK `ck_audit_events_approved_by_not_yet_defined` | `AuditEventsSchemaTests.cs` lines 101, 277 (not run) | — | Meaning undefined (§11) — deferred to the first module with an approval step | None for Slice 6 |
| E33 | `correlation_id` / `causation_id` | line 63 · [D] §13, [C], [E] | `correlation_id` NOT NULL, `^[A-Za-z0-9._-]{1,64}$`, never generated by the Domain; `causation_id` not written in Slice 3 | Implemented · Unit-tested (run) · endpoint behaviour Integration-tested, stale | `AuditEvent.cs` lines 215–220 | Domain.Tests; Api.Tests endpoint tests (PostgreSQL, not run) | — | None found | ADR acceptance — owner |
| E34 | `reason` | line 64 · [D], [C] IMM-03, [E] | 1–1000 characters, no surrounding whitespace; required for operator-command events; "enforced in the Domain" | Implemented · Unit-tested (run) | Domain: `AuditEvent.ForSystem` lines 159–164 (only `PasswordResetIssued`, `BootstrapPasswordReset`); Application: `ClearThrottleUseCase.cs` line 60 (`OperatorReason.IsValid`) | `OperatorCommandUseCaseTests` `ClearThrottle_RequiresAValidReason_…` | — | **Discrepancy D3** (`ThrottleCleared` reason enforced in Application only) | Decide whether the Domain rule should cover `ThrottleCleared` — engineering decision, then ADR update |
| E35 | `previous_status` / `new_status` | line 65; §5.3 line 174 · [D] + [E] | Both NULL or both set, different, non-empty; values `Active`, `Disabled` | Implemented · Unit-tested (run) | `AuditEvent` `ValidateStatuses` (line 231); `DisableUserUseCase`, `ReactivateUserUseCase` | Domain.Tests, Application.Tests | — | None found | ADR acceptance — owner |
| E36 | Event target | line 66; §11 line 273 · [C] DD, [D] §14.1 | Identity events target `User` + user id; `AccessDenied` target decided in Slice 6 | Implemented for Identity events and `AccessDenied` | `ScopedAccessDeniedAuditor.cs` line 56 (`entityType: "User"`) | Api.Tests `AudienceAndGateTests.cs` (PostgreSQL, not run) | Code read | **Discrepancy D4** — the `ThrottleCleared` target is not covered by E36 | Owner/document decision on D4 |
| E37 | Snapshot = context | line 67; §5.2 lines 110–134 · [C] + [E] | Fixed non-sensitive key list per action type; never in `audit_event_changes` | Implemented · Unit-tested (run) | `AuditEvent.BuildSnapshot` (line 249) | Domain.Tests, Application.Tests (`AuditAssert.SnapshotKeys`) | — | `ThrottleCleared` carries no snapshot (consistent with "All others: none", line 128) | ADR acceptance — owner |
| E38 | No `ip_address` / `user_agent` | line 68 · [C] + [E] | Not columns | Implemented (absence) · Integration-tested, stale (column-list test) | `AddAuditEvents` migration | `AuditEventsSchemaTests.cs` (not run) | — | None | ADR acceptance — owner |
| E39 | Append-only triggers | line 69; §5.4 line 178 · [C] + [E] | Statement-level triggers reject `UPDATE`, `DELETE`, `TRUNCATE` with 42501 | Implemented · Integration-tested, stale | `AddAuditEvents` migration | Integration suite (not run) | — | None specific to Slice 6 | Re-run integration tests |
| E40 | `Down` refuses with rows | line 70 · [E] | The Slice 3 migration's `Down` refuses when `audit_events` has rows | Implemented for the Slice 3 migration · Integration-tested, stale | `AddAuditEvents` `Down()` | Integration suite (not run) | — | E40 names the Slice 3 migration only. `AddThrottleClearedAuditAction.Down` (lines 32–42) re-adds the 15-value CHECK; *inference, untested:* PostgreSQL validates existing rows when a CHECK is added without `NOT VALID`, so this `Down` would fail if a `ThrottleCleared` row exists. No `Up`/`Down` round trip has been executed. | Migration round trip on a disposable database |
| E41 | Transactional persistence | line 71; §6 lines 180–193 · [C] ح-186, [D] §14.2, [E] | Writer stages only; caller owns the transaction; single save-and-commit exit | Implemented · Unit-tested (run) · Integration test written for `ClearThrottleUseCase`, **never run** | See Part 3 | See Part 3 | See Part 3 | Not database-verified | Run `ClearThrottleIntegrationTests` |
| E42 | Privilege self-check | line 72; §7.3 lines 210–212 · [C], [D] §18, [E] | Checks `UPDATE`/`DELETE`/`TRUNCATE`/`TRIGGER`, ownership, superuser; fails closed in Production | Implemented · Integration-tested, stale | `AuditPrivilegeStartupStep` (registered `DependencyInjection.cs` line 86); operator commands run it first (`OperatorCommandRunner.cs` line 71) | Integration suite (not run); policy unit tests on hand-built reports per `sprint-02.md` line 57 | — | The compliant-role case cannot be produced by tests (roles are cluster-wide; ADR §7.3, `sprint-02.md` line 57) — documented limitation | Deployment verification |
| E43 | Separate migration / application roles | line 73; §7.2 lines 203–208 · [C], [D] §18 | Application never runs migrations | Implemented in code (no `Migrate`/`MigrateAsync` call found in `Contractra.Api`) | — | N/A | Search on 2026-10-19 | Role separation is deployment work, not provable from code | Deployment documentation |
| E44 | Ids and timestamps | line 74 · [E] | UUIDv7 set by the Domain; no database defaults; `timestamp with time zone` | Implemented · Unit-tested (run) | `AuditEvent.cs` line 235 (`Guid.CreateVersion7()`) | Domain.Tests | — | None found | ADR acceptance — owner |

### Part 2a — `ClearThrottleUseCase` idempotency key (E30, §5.3)

| Check | Evidence | Result |
|---|---|---|
| Value passed by the use case | `Contractra.Application/Identity/Operations/ClearThrottleUseCase.cs` line 94: `idempotencyOperationKey: $"command:{request.CorrelationId}"` | Operation key = `command:<run correlation id>` |
| Composition in the Domain | `Contractra.Domain/Audit/AuditEvent.cs` line 284: `var key = $"{actionType}:{operationKey}";` (no sub-identifier passed) | Stored `idempotency_key` = **`ThrottleCleared:command:<run correlation id>`** |
| Required for the source | `ForSystem` always uses `AuditSource.SystemJob` (line 168); `BuildIdempotencyKey` requires a key for `SystemJob` (line 265) | Present, as required (E30; §5.1 `ck_audit_events_idempotency_key_required_for_source`, line 102) |
| Format rule | ADR §5.3 line 161 (operator commands: `command:<the run's correlation id>`); line 170 (`<action_type>:<operation key>[:<sub-identifier>]`; database checks the action-type prefix); same pattern as `OperatorIssuePasswordResetUseCase.cs` line 117 and `ResetBootstrapPasswordUseCase.cs` line 95 | **Conforms.** One event per run, so no sub-identifier is needed (line 170). |
| Tests | `OperatorCommandUseCaseTests.cs` lines 372 and 386 (asserts `ThrottleCleared:command:run-7`, `…run-1`, `…run-2`; run, passing); `OperatorCommandIntegrationTests.cs` line 438 (PostgreSQL; never run) | Unit-tested; not database-verified |
| Code vs. documentation | The use case's own doc comment (line 27), the ADR's operator-command row and the tests agree | **No discrepancy found for the key.** |

## Part 3 — ADR 0006 §6 (transactions and failure model) and M5

| # | Source | Requirement (source terms) | Status | Implementation | Tests | Evidence | Gap | Next action · owner? |
|---|---|---|---|---|---|---|---|---|
| §6 item 1 | ADR 0006 line 182 | "One scope and one DbContext per unit of work. The use case opens the transaction before its first database write." | `ClearThrottleUseCase` previously did not meet this (its delete ran in a separate scope and transaction); redesigned 2026-10-19 · Implemented · Unit-tested (run) · Integration test written, **never run** | `ClearThrottleUseCase.cs` lines 84 (`BeginTransactionAsync`), 86 (`IThrottleIdentifierEraser.ClearIdentifierAsync`), 88–95 (`IAuditWriter.Stage`), 97 (`SaveChangesAndCommitAsync`). `ThrottleIdentifierEraser.cs` lines 30–32 (`ExecuteDeleteAsync` on the injected `ContractraDbContext`). Both ports registered scoped next to `IUnitOfWork` (`DependencyInjection.cs` lines 44–45). | `OperatorCommandUseCaseTests.cs` `ClearThrottle_*` (run, passing — these prove the use case's ordering and results, **not** database rollback); `OperatorCommandIntegrationTests.cs` `ClearThrottleIntegrationTests`, including `ClearThrottleCommand_WhenTheAuditSaveFails_RollsBackTheDelete_LeavingTheRowIntact` (never run) | Repository evidence: shared scoped `ContractraDbContext`; `UnitOfWork.DisposeAsync` disposes an uncommitted transaction. *Technical inference, not repository evidence:* EF Core's `ExecuteDeleteAsync` executes on the context's connection inside its current transaction (EF Core 10.0.12 per `Contractra.Infrastructure.csproj`). | Not database-verified | Run the integration tests with `CONTRACTRA_TEST_POSTGRES` against a disposable database — engineering, no owner decision |
| §6 helpers paragraph | ADR 0006 line 191 | `RevokeAllActiveAsync` and `IssuePasswordResetTicketAsync` can break atomicity when no transaction is open; Slice 4 makes both throw without one | Not re-inspected in this review | `AuthSessionPersistence.cs`, `PasswordResetTicketPersistence.cs` (unchanged in this working tree's M5 work) | Not re-checked | — | The throttle store is a **different** case from these two helpers: `AuthThrottleStore.ApplyAsync` (lines 78–82) deliberately refuses to join any open transaction and always runs its own (ADR 0005 §3.4), and `AuthThrottle.ApplyAsync` opens a new DI scope per call (`AuthThrottle.cs` line 197). That design — not a missing guard — is why M5's original delete could not share the audit transaction. Whether any other such path exists was not searched in this review. | None for M5 |
| `AccessDenied` scope | ADR 0006 line 193 | Written in its own scope and DbContext | Implemented | `ScopedAccessDeniedAuditor.cs` | Api.Tests (PostgreSQL, not run) | — | — | None |

## Part 4 — Contract §22 A–H (throttle policy)

Contract line 820: A–E, H and M1–M5 "are not final until reviewed together with OC-8"; F and G "are approved for implementation as persistence design" (E20–E22, ADR 0005) and become contract wording only when the revision is accepted.

| # | Source | Requirement | Status | Implementation | Tests | Gap | Next action · owner? |
|---|---|---|---|---|---|---|---|
| A | line 824 (policy change) | Anchored fixed window | Proposed · Implemented | `AuthThrottlePolicy.cs` lines 15–18, 41–46, 83–91 | `Contractra.Infrastructure.IntegrationTests/AuthThrottlePolicyTests.cs` — **database-free** pure-function tests, but in the Integration project, **not run** in this review | §22 not accepted | Owner review of §22 with OC-8 |
| B | line 831 (clarification, material effect) | Layer 2: base 30 s, doubling, cap 900 s; ends after 15 min without a failure or on success | Proposed · Implemented | `AuthThrottlePolicy.AdmitPair`, `PairDelay` (lines 38–74) | `AuthThrottlePolicyTests` (not run) | Same | Same |
| C | line 837 (clarification) | Layer 3 slot enforced at admission; a success does not end slow mode | Proposed · Implemented | `AuthThrottlePolicy.AdmitIdentifier` (lines 78–107), `ReleaseIdentifier` (lines 115–141) | `AuthThrottlePolicyTests` (not run); `AuthThrottleIntegrationTests` (PostgreSQL, not run) | Same | Same |
| D | line 843 (clarification) | Counted at admission; success releases its own count | Proposed · Implemented | `AuthThrottle.cs` lines 56–72, 99–123 | Same as C | Same | Same |
| E | line 849 (policy change) | One source = one IPv4 address or one IPv6 /64, after trusted-proxy rules | Proposed · Implemented · Unit-tested (run) | `Contractra.Api/Http/ClientSource.cs` `ThrottleGroupOf`; call sites in `AuthEndpoints.cs` | `Contractra.Api.Tests/ClientSourceTests.cs` (8/8, run) | Applied to layers 2–3 only; layer 1/5 keeps the exact address (implementation note, line 880). §22 E's own consequence text mentions layers 1, 2 and 5; whether layers 1/5 must also group is not recorded as an owner decision in any source reviewed | Same |
| F | line 855 (clarification, implemented in Slice 2B) | Sessions and refresh tokens | Approved for implementation as persistence design; contract wording not accepted | Slice 2B (not re-inspected) | Not re-verified | — | Contract revision acceptance — owner |
| G | line 859 (clarification, implemented in Slice 2B) | Reset tickets | Same as F | Slice 2B (not re-inspected) | Not re-verified | — | Same |
| H | line 863 (correction) | About 30 or more coordinated sources can keep the real user waiting indefinitely | Proposed correction; no code | N/A | N/A | Premise of OC-8 | OC-8 — owner |
| Limits | lines 883–889 | Derived maximum attempts after A–E and M1 | Proposed; derived figures | N/A | Not independently recomputed in this review | — | Owner review of §22 |

## Part 5 — Contract §22 layer-3 mitigation options M1–M5 (lines 867–881)

"Proposed: M1 and M5 required in Slice 6; M3 is the owner decision OC-8." (line 877)

| # | Requirement | Status | Implementation | Tests | Gap | Next action · owner? |
|---|---|---|---|---|---|---|
| M1 | E + layer 2 checked in the same admission + B + rejected attempts never take a slot | Proposed · Implemented | E: `ClientSource.cs`; same admission and rejected-attempt rule: `AuthThrottle.cs` lines 56–72 | E: `ClientSourceTests` (run); the rest: `AuthThrottleIntegrationTests` (PostgreSQL, not run) and `AuthThrottlePolicyTests` (not run) | Only the E part is covered by a test run in this review | Owner review of §22 with OC-8 |
| M2 | k > 1 slots per period | Not recommended by the table; not implemented | — | — | — | None |
| M3 | Trusted devices (new `trusted_devices` table, both apps) | **Undecided — this is OC-8** | — | — | — | **Owner decision (OC-8)** |
| M4 | CAPTCHA / proof-of-work | Not recommended by the table; not implemented | — | — | — | None |
| M5 | Operator command to clear an identifier's throttle (with reason, audited) + Serilog alert when slow mode starts | Proposed · Implemented · Unit-tested (run) · Integration tests written, never run | `ClearThrottleUseCase.cs`, `ThrottleIdentifierEraser.cs`, `OperatorCommandRunner.cs`; slow-mode alert `AuthThrottle.cs` lines 83–88 | Application.Tests (run); `ClearThrottleIntegrationTests` (never run) | Not database-verified; discrepancies D1–D4 concern its audit event | Run integration tests; resolve D1–D4 |

## Part 6 — Contract §22 I1–I20 traced to their sources

I1–I20 do **not** map one-to-one onto E26–E44. Mapping below uses each I-item's own "Classification · basis" column (contract lines 897–916), checked against the ADR text.

| I | Contract line · section | Topic | Traced source | Mapping type | Status |
|---|---|---|---|---|---|
| I1 | 897 · §14.1 `source` | `EmployeeApp` replaces `PWA`; `System` actor implies `SystemJob` | **R46** (ADR line 43); correction U1 (§10). The one-way `System`→`SystemJob` rule is in ADR §5.3 line 154 and CHECK `ck_audit_events_system_actor_source` (§5.1 line 101), not in R46 | Owner decision + ADR §5 detail, no E-item | Vocabulary owner-approved (R46); the rest of the wording proposed. Implemented (`AuditSource.cs`). |
| I2 | 898 · §14 event table | `UserDisabled`, `RoleEnded` confirmed | **R46**; correction U2; closes OC-5(a) for the contract | Owner decision, no E-item | Owner-approved names; implemented |
| I3 | 899 · §14 catalog | "exactly those 15 codes", CHECK-enforced; codes permanent; a rename adds a value | **E27** (line 57); catalog ADR §5.3 line 140; permanence ADR §3 line 48 | One-to-one with E27, plus §3/§5.3 detail | Proposed. **Conflicts with the 16-value code — D1.** |
| I4 | 900 · §14.1 `occurred_at_utc` | Application server clock | **E26** | One-to-one | Proposed |
| I5 | 901 · §14.1 `actor_*` | `actor_role_code` NULL only with no active role | **E28** (basis includes owner decision R40); correction U3 | One-to-one | Proposed. The deterministic role rule itself is **OC-6** (§19), owner-confirmed 2026-10-18 — a separate item |
| I6 | 902 · §14.1 target | `User` + user id; `AccessDenied` target decided in Slice 6 | **E36**; references I7/E37 | One-to-one | Proposed; `AccessDenied` target implemented. **D4** (`ThrottleCleared` target) |
| I7 | 903 · §14.1 `snapshot` | Context snapshot with key lists | **E37** and ADR §5.2 | One-to-one (plus §5.2 key lists) | Proposed |
| I8 | 904 · §14.1 `idempotency_key` | One column; key format; required for sync/import/job | **E30** and ADR §5.3; correction U5 | One-to-one (plus §5.3) | Proposed; `ThrottleCleared` key conforms (Part 2a) |
| I9 | 905 · §14.1 `schema_version` | integer ≥ 1 | **E29**; correction U4 | One-to-one | Proposed |
| I10 | 906 · §14.1 context ids | Temporary all-NULL CHECK | **E31**, ADR §11 | One-to-one | Proposed |
| I11 | 907 · §14.1 `approved_by_user_id` | FK, temporary NULL CHECK | **E32**, ADR §11 | One-to-one | Proposed |
| I12 | 908 · §14.1 `causation_id` | Opaque operation id; not written in Sprint 2 | **E33** (causation part) | Partial — E33 is split between I12 and I16 | Proposed |
| I13 | 909 · §14.1 `reason` | Length and whitespace; required for operator commands | **E34** | One-to-one | Proposed. **D3** |
| I14 | 910 · §14.1 statuses | Pair rule; `Active`/`Disabled` | **E35**; values ADR §5.3 line 174 | One-to-one (plus §5.3) | Proposed |
| I15 | 911 · §14.1 `ip_address`, `user_agent` | Not columns | **E38** | One-to-one | Proposed |
| I16 | 912 · §13, §14.1 `correlation_id` | Format CHECK; generation at HTTP boundary and per command run; replaced client value logged by length and hash | **E33** (correlation part) and ADR §12 Slice 6 obligation (line 285) | Partial — second half of E33, plus §12 | Proposed |
| I17 | 913 · §14.1 append-only | Triggers incl. `TRUNCATE`; revocation; non-empty table cannot be dropped | **E39 + E40** (and §5.4, §7.1) | Two E-items combined | Proposed |
| I18 | 914 · §18 database roles | No migrations at startup; privilege self-check | **E42 + E43** and ADR §7 | Two E-items combined | Proposed |
| I19 | 915 · §14 (new) scope and gates | Slice 3 is the explicit business-event level only; Gates A and B; retention criteria | **ADR §8** (8.1–8.3) and **R47** | No E-item; ADR section + owner decision | Gate B owner-approved (R47); Gate A and the rest proposed |
| I20 | 916 · §19 OC-5 | OC-5(a)/(b) decided for the implementation; (c) unchanged | **R46** and **OC-5** (contract line 778); corrections U1/U2 | No E-item; owner decision + §19 register | R46 portion owner-approved; protected-document corrections pending |

**E-items with no I counterpart:** E41 (transactional persistence — the contract already states it in v1.5 §14.2, which E41 cites as [D]) and E44 (repository convention). **Owner decisions in the I-table:** R46 (I1, I2, I20), R47 (I19). Contract line 893: only R46 and R47 are decided; every other I-item is a proposal pending review.

## Part 7 — §19 owner-confirmation register (contract lines 772–782)

| # | Source | Question | Status | Gap | Next action · owner? |
|---|---|---|---|---|---|
| OC-6 | line 779 | `actor_role_code` for users with several roles | **Owner-confirmed** (2026-10-18), applied to nine action types per that row · Implemented · Unit-tested (run) for `ActorRoleCodeRule` and the Application use cases | `AccessDenied` coverage is in `Application.Tests/Identity/Auth/AuthRulesTests.cs` (run) and `Api.Tests/AudienceAndGateTests.cs` (PostgreSQL, not run) | None. **Does not approve ADR 0006 or Contract §22 as a whole.** |
| OC-8 | line 781 | Accept layer-3 starvation by about 30+ coordinated sources as a documented limitation (with operator alerting and an operator unblock command), or require trusted devices (§22 M3). Relates to owner instruction R45 | **"Undecided; the throttle policy (§17, §22) is not finalized until this is reviewed. Does not block Slice 2B."** | M5 now exists as the "operator unblock command" the first option mentions; that does not decide OC-8 | **Owner decision required.** Not decided here. |
| OC-5 | line 778 | Upstream documentation defects (a), (b), (c) | (a) and (b) decided for the implementation by R46 (I20); (c) unchanged | Protected documents not yet corrected (U1, U2) | Protected documents' owner |
| OC-9 | line 782 | Retention of session, refresh-token and reset-ticket history | Undecided; current behaviour: "Nothing is deleted except expired throttle rows (Slice 2B, ADR 0005 E24)." | **D7** | Owner |
| OC-1, OC-2, OC-3, OC-4, OC-7 | lines 774–777, 780 | Reset delivery, contact data, creation exposure, Engineer party validity, Party lookup | Open | Slice 7 (`/users/*`) scope per `sprint-02.md` line 69 | Owner, before Slice 7 |

## Part 8 — Release gates (ADR 0006 §8.3, lines 237–243; contract I19)

| Gate | Requirement | Status | Scope |
|---|---|---|---|
| Gate A | No migration introducing a financial table or financial module is merged before Audit Completion is implemented, tested, reviewed and approved (line 239) | Proposed (ADR); not triggered — no financial module in this branch | Merge gate for financial modules, not a Slice 6 acceptance item |
| Gate B | No production deployment with real business or user data before Audit Completion passes its criteria (line 241) | Owner-approved (R47); not triggered | Production gate, not a Slice 6 acceptance item |

Line 243: "Neither gate stops ordinary work."

## Part 9 — "B1/B2/B3" search

**Command** (run 2026-10-19 from the repository root):

```
git grep --untracked -c -I -w -E "B[123]"
```

**What it searched:** every tracked file and every untracked, non-ignored file in the working tree (`--untracked`), text files only (`-I`), whole-word matches of `B1`, `B2` or `B3` (`-w`). Ignored paths (for example build output) were not searched. A case-insensitive run (`-i`) returned the same files and counts.

**Results:**

| File | Matches | What they are |
|---|---|---|
| `docs/contracts-and-payments-addendum-ar.md` | 3 (lines 68–70) | Mermaid node ids `B1`, `B2`, `B3` for payment-basis options ("إجمالي المستخلص", "صافي المستخلص", "قيمة أمر الدفع") |
| `docs/data-dictionary-ar.md` | 1 (line 774) | Spreadsheet cell reference `B3` (sheet name → `parties` + `subcontract_agreements`) |
| `docs/data-dictionary-ar.html` | 1 (line 442) | Generated presentation counterpart of the line above |
| `docs/excel-source-mapping-ar.md` | 7 (lines 19, 26, 27, 32, 33, 38, 293) | Spreadsheet cell ranges (`B2:C8`, `B3:E370`, `B1:I27`, …) and the same `B3` sheet-name reference |
| this file | 2 | This section's own text |

**Conclusion:** no match in ADR 0006, the auth and users contract, `docs/implementation/README.md` or any code file. The labels B1, B2 and B3 are absent from the sources relevant to this review. No meaning is inferred for them. Contract §22 has a single item "B" (line 831), which has no sub-items.

---

## Discrepancies found — reported, not resolved

| # | Discrepancy | Evidence | Who must resolve |
|---|---|---|---|
| D1 | **Catalog count.** The code has 16 action types (`ThrottleCleared` added). ADR §5.3 (line 140) lists 15; contract I3 (line 899) proposes "exactly those 15 codes"; `ThrottleCleared` is not in the ADR catalog and appears in the contract only in the M5 implementation note (line 881). The migration comment (`AddThrottleClearedAuditAction.cs` lines 12–14) and that note justify the addition with I3's "a later rename adds a value", which concerns renames; the clause that covers adding events is E27's "A module that adds events replaces the CHECK in its own migration". | `AuditActionType.cs`; ADR line 140; contract lines 881, 899 | Document owner — contract §14/§22 I3 and ADR §5.3 need a decision on the 16th code |
| D2 | **Migration mechanism.** E27 says an added event replaces the CHECK with `NOT VALID` then `VALIDATE`; the migration drops and re-adds the CHECK in one step. | ADR line 57; migration lines 21–28 | Engineering; production migration not changed in this review |
| D3 | **Where the reason rule is enforced.** E34 says the rule is enforced in the Domain; `AuditEvent.ForSystem` enforces a reason only for `PasswordResetIssued` and `BootstrapPasswordReset`; `ThrottleCleared`'s reason is enforced only in the Application layer. E34's list of commands predates `clear-throttle`. | ADR line 64; `AuditEvent.cs` lines 159–164; `ClearThrottleUseCase.cs` line 60 | Engineering, then ADR update |
| D4 | **SUPERSEDED — historical finding as first recorded; the current implementation no longer does this (see the Addendum below: the event now targets `OperatorCommandRun` + the run's UUID).** **`ThrottleCleared` target.** `entity_type = "AuthThrottleIdentifier"`, `entity_id` = the first 16 bytes of the identifier's SHA-256 key. E36/I6 cover only `User` targets and `AccessDenied`. *Observation, not decided:* the value derives from an unsalted hash of a lower-cased login identifier and is stored permanently in an append-only table; no reviewed source addresses whether this is acceptable under contract §17's hashing rule or ح-191. | Historical: `ClearThrottleUseCase.cs` lines 79, 89 as of the first review (the code those lines described was replaced by the owner-decision follow-up of 2026-10-19; current locations: target at lines 90–91, run-id check at line 74); ADR line 66; contract line 902 | Owner / document owner — decided; see Addendum |
| D5 | **Documentation lag on implementation status.** ADR 0006 line 3 says "Not implemented"; README §2 (line 29) says "not implemented"; the audit persistence it describes exists in code. | ADR line 3; README line 29 | Documentation update (outside this file's scope) |
| D6 | **Reference error.** The contract's M5 note (line 881) cites "§14 I3"; I3 is in §22 (line 899). | Contract line 881 | Documentation update (outside this file's scope) |
| D7 | **OC-9 wording.** OC-9's current behaviour says nothing is deleted except expired throttle rows. `clear-throttle` deletes a non-expired identifier row (`ThrottleIdentifierEraser.cs` lines 30–32), and a layer-2 success already deletes the pair row (`AuthThrottle.cs` line 109). OC-9's subject is session, refresh-token and reset-ticket history, so this may be a wording-scope issue rather than a rule conflict; not decided here. | Contract line 782 | Owner |

## Slice 6 acceptance, future slices and release gates — kept separate

- **Remaining Slice 6 acceptance items** (`sprint-02.md` lines 65–69): ADR 0006 and Contract §22 — including I1–I20, A–E/H and the OC-8 choice — remain proposed, not accepted; M1/M5 implemented; M5's atomicity, success and no-row paths passed their PostgreSQL integration tests on 2026-10-20 (Addendum 2), but the migration's `Down` has been exercised only without `ThrottleCleared` rows (with such rows it is untested; Addendum 2); further PostgreSQL groups passed in later runs (Addendum 3), and classes not named in Addendum 2 or 3 have not been re-run; discrepancies D1–D4 concern the Slice 6 audit event; formal owner acceptance of Slice 6 not given.
- **Future-slice work:** OC-1, OC-2, OC-3, OC-4, OC-7 (Slice 7); the Audit Completion slice (ADR §8.2) — generic change capture, `audit_event_changes`, `audit_event_links`, retention policies.
- **Release gates, not local acceptance items:** Gate A and Gate B (ADR §8.3).

## Corrections made in this revision (2026-10-19, second pass)

1. **I1–I20:** the earlier text said they "mirror the ADR 0006 engineering decisions … one-to-one". Replaced by the traced table in Part 6: I1, I2 and I20 trace to R46 (I20 also to OC-5), I19 to ADR §8 and R47, I17 and I18 each combine two E-items, E33 is split across I12 and I16, and E41 and E44 have no I-item.
2. **Part 2 heading** said every E-item was basis [E]; each row now carries its basis from ADR line 52 onward.
3. **Throttle policy A–D** were described as unit-tested in Application.Tests; their pure-function tests are `AuthThrottlePolicyTests` in the Integration project and were not run.
4. **M1** was described as covered by `ClientSourceTests`; that covers only its E part.
5. **§6 helpers row** said the throttle store "never received the equivalent guard"; the store's design is the opposite (it refuses to join any transaction). Also removed the unsupported claim that no other such helper exists.
6. **OC-5** was listed as unresolved; (a) and (b) are decided for the implementation by R46.
7. **E42** cited `OperatorCommandRunner` as the self-check; the check is `AuditPrivilegeStartupStep`, which operator commands call first.
8. **E36 / E40 / E41 / E26** wording narrowed to what was actually checked.
9. **The contract note at line 881** was said to sit in "the I-section"; it is in the mitigation-options section.
10. **B1/B2/B3:** the earlier text claimed a search of `docs/` and then results "anywhere in the repository". Now records the exact command, scope and per-file results.
11. **Added:** idempotency-key verification (Part 2a), the limits table, OC-9, the `AccessDenied` scope row, and discrepancies D1–D7.
12. **EF Core transaction behaviour** is now labelled as technical inference, not repository evidence.

## Addendum (2026-10-19, after owner decisions D1 and D4)

The tables above are the review snapshot and are not rewritten. Status of the discrepancies after the owner's decisions and the follow-up change:

| # | Status | Where |
|---|---|---|
| D1 | **Resolved in documentation** — `ThrottleCleared` is the 16th code by owner decision; contract §14, §14.1, §21, §22 I3/M5 and ADR 0006 §3/§5.3 updated | Contract, ADR 0006 |
| D2 | **Open — blocked** on whether `AddThrottleClearedAuditAction` has been applied to any database; migration not edited; ADR E27 dated note added | ADR 0006 §4 dated notes |
| D3 | **Resolved in code** — `AuditEvent.ForSystem` requires a reason for `ThrottleCleared`; Domain test added | `AuditEvent.cs`, `AuditEventTests.cs` |
| D4 | **Resolved in code and documentation** — the hash is gone from the use-case result, Serilog messages and CLI output, and the audit event now targets `OperatorCommandRun` + the run's UUID (the per-run GUID from `OperatorCommandRunner`; no schema change). Deviation from ADR E33/E36 recorded. Not database-verified | `ClearThrottleUseCase.cs`, `OperatorCommandRunner.cs`; contract §22 M5 note; ADR 0006 §3 |
| D5 | **Resolved** — ADR 0006 status line (struck, dated note) and README row | ADR 0006 line 3, README |
| D6 | **Resolved** — the M5 note now cites ADR E27 for adding events and §22 I3 by its section | Contract §22 M5 note |
| D7 | **Resolved** — OC-9's current-behaviour cell clarified to ADR 0005 E24's scope; OC-9 stays undecided | Contract §19 |

Unchanged: ADR 0006 and Contract §22 are **Proposed**; OC-8 is undecided; Slice 6 is not accepted. (As written on 2026-10-19: "no item is database-verified" — superseded in part by Addendum 2 below.)

## Addendum 2 (2026-10-20) — PostgreSQL verification on an isolated instance

Documentation of an authorized test run. It changes no approval status, makes no owner decision, and authorizes no database mutation or migration round trip.

**Environment (non-secret metadata only).** A dedicated PostgreSQL 18.6 instance, separate from the existing local PostgreSQL service: `127.0.0.1:55432`, data directory `D:/pg-contractra-test/data`, test role `contractra_test` (LOGIN and CREATEDB, not a superuser). The session was identified from the restricted role (database `postgres`, user `contractra_test`, address and port as above, `is_superuser=off`, version 18.6); the postmaster process (PID 36228) and its data directory were confirmed from Windows process metadata and `postmaster.pid`, because the restricted role cannot read `data_directory`. The existing Windows service `postgresql-x64-18` stayed stopped and was never contacted. The test fixture created fresh `contractra_test_<guid>` databases (seven exist now) and applied all 16 migrations to each; database dropping was disabled, so they are preserved. No credentials are recorded in this repository.

**Results.** Seven invocations of `dotnet test --no-build` on `Contractra.Infrastructure.IntegrationTests`, run sequentially; every exit code was 0. Total **118 passed, 0 failed, 0 skipped**.

| # | Filter (`FullyQualifiedName` ...) | Result |
|---|---|---|
| 1 | `=...ClearThrottleIntegrationTests.ClearThrottleCommand_DeletesTheIdentifierRow_AndAuditsInOneCommit` | 1 passed |
| 2 | `=...ClearThrottleIntegrationTests.ClearThrottleCommand_WhenTheAuditSaveFails_RollsBackTheDelete_LeavingTheRowIntact` | 1 passed |
| 3 | `=...ClearThrottleIntegrationTests.ClearThrottleCommand_WhenNoRowExists_StillSucceedsAndAudits` | 1 passed |
| 4 | `=...ClearThrottleIntegrationTests.ClearThrottleCommand_DoesNotTouchLayer2PairsOrOtherIdentifiers` | 1 passed |
| 5 | `~...AddAuditEventsMigrationTests` | 4 passed |
| 6 | `~...AuditEventsSchemaTests` | 109 passed |
| 7 | `~...ModelConsistencyTests` | 1 passed |

**What these results establish, and only this.**
- **M5 (ADR 0006 E41 / section 6 item 1; contract section 22 M5):** a successful `clear-throttle` removes the identifier's throttle row and writes one `ThrottleCleared` audit event targeting `OperatorCommandRun` plus the run's UUID. When the audit save is forced to fail (an FK violation), the command exits 1 with "Nothing was changed", the throttle row is still present, and no audit event exists. This is database-level evidence that the delete and the audit write are atomic. A clear with no existing row still succeeds and audits. Layer-2 pairs and other identifiers are untouched. In the success test the CLI output, the captured log entries and the audit event did not contain the identifier or its hash.
- **E27 catalog (rows E27, I3):** `AuditEventsSchemaTests.CatalogChecks_EqualTheDomainEnums` passed, so the database CHECK lists match the C# catalogs, now 16 action types. This verifies catalog consistency only. It does **not** establish compliance with E27's migration strategy: ADR 0006 E27 says a module that adds events replaces the CHECK in its own migration using `NOT VALID` then `VALIDATE`, whereas `AddThrottleClearedAuditAction` drops and recreates the CHECK in one step. That E27 migration-design deviation remains **unresolved** (see D2 and "What they do not establish" below).
- **Schema rows E28, E31, E32, E38:** the schema test class those rows cite passed in full.
- **Migrations:** the fixture applied the `AddThrottleClearedAuditAction` **Up** path to fresh, empty databases. The migration-order test passed (`AddAuthThrottleCounters` -> `AddAuditEvents` -> `AddThrottleClearedAuditAction`), and the `AddAuditEvents` down-and-up test passed. `ModelConsistencyTests` found no pending model changes.

**What they do not establish (still open).**
- `AddThrottleClearedAuditAction`'s **`Down`** has been exercised in one scenario only. `AddAuditEventsMigrationTests.DownAndUp_AddsAndRemovesTheTableFunctionAndTriggers_LeavingOtherDataIntact` (run 5 above) migrates from the latest schema down to `AddAuthThrottleCounters` — which runs `AddThrottleClearedAuditAction.Down` and then `AddAuditEvents.Down` — and back up, on a fresh test database with **no `ThrottleCleared` rows**; it passed and unrelated data was preserved. *(Corrected 2026-10-20: this line previously said no test exercises it.)* `Down` with `ThrottleCleared` rows present remains **untested**; *inference, untested:* it would fail when the narrower CHECK is re-added. This does not cover every rollback scenario.
- Whether the migration has been applied to **any real target database is unknown**; no such database was identified or contacted.
- The E27 deviation (drop and re-add instead of `NOT VALID` then `VALIDATE`) is unresolved.
- *(Status as of the first 2026-10-20 run. Superseded in part by Addendum 3: later runs of the append-only, down-guard, privilege self-check, atomicity, throttle and API classes named in this bullet passed. The remaining classes are still not run.)* Not run on this tree, so their rows keep "stale": `AuditEventsAppendOnlyTests` (E39), `AddAuditEventsDownGuardTests` (E40), `AuditPrivilegeSelfCheckTests` (E42), `AuditEventsAtomicityTests`, `AuthThrottleIntegrationTests`, `AuthThrottlePolicyTests`, the remaining integration tests, and all PostgreSQL-backed API tests (`Contractra.Api.Tests` apart from the database-free `ClientSourceTests`, 8/8). **This is not a full Slice 6 integration run.**
- OC-8 and OC-9 remain undecided. ADR 0006 and Contract section 22 remain **Proposed**. Slice 6 acceptance remains pending. A passing test group is not owner approval.
- The 2026-10-20 results do not themselves authorize any database mutation, migration round trip, or further test run.

## Addendum 3 (2026-10-20, later runs) — additional PostgreSQL results on the same isolated instance

Documentation of further authorized test runs. Like Addendum 2, it changes no approval status, makes no owner decision, and authorizes nothing further. The environment is the one in Addendum 2: a dedicated PostgreSQL 18.6 instance at `127.0.0.1:55432`, restricted role `contractra_test`, fresh `contractra_test_<guid>` databases, drop disabled. For every API-host class the child process had its `APPDATA` redirected to an empty directory after a probe showed that the API project's User Secrets key `ConnectionStrings:DefaultConnection` is present with the normal `APPDATA` and absent with the redirected one (booleans only; no value was read or recorded). Every run below exited 0 with 0 failed and 0 skipped, and every run created only new `contractra_test_<guid>` databases; none was removed. No credentials are recorded here.

| Group | Class (project) | Passed |
|---|---|---|
| Throttle store and policy | `AuthThrottleStoreTests` (Infrastructure.IntegrationTests) | 11 |
| | `AuthThrottleIntegrationTests` (same) | 7 |
| | `AuthThrottlePolicyTests` (same; database-free, run with the database variable removed) | 17 test cases from 11 methods |
| API throttling and composition | `ThrottlingTests` (Api.Tests) | 16 |
| | `RateLimitLayerTests` | 3 |
| | `CompositionTests` | 2 |
| Remaining API classes | `AudienceAndGateTests` 12; `AccessDeniedWriteFailureTests` 2; `ChangePasswordEndpointTests` 19; `JwtValidationTests` 25; `LoginEndpointTests` 23; `LogoutAndMeEndpointTests` 7; `PasswordResetCompletionEndpointTests` 16; `PipelineTests` 18; `HttpsOnlyTests` 4; `RefreshEndpointTests` 21; `SecretLeakTests` 2 | 149 |
| Audit protections (Infrastructure.IntegrationTests) | `AuditEventsAppendOnlyTests` 13; `AuditPrivilegeSelfCheckTests` 11; `AuditPrivilegePolicyTests` 9 (database-free); `AuditEventsAtomicityTests` 14 | 47 |
| Migration down-guard | `AddAuditEventsDownGuardTests.Down_Refuses_WhenTheTableHoldsRows_AndNothingIsLost` | 1 |

The first five groups total 252 passed; with the down-guard test, 253. These are separate from the 118 in Addendum 2. `ClientSourceTests` (8/8) was run earlier and not repeated.

**The down-guard test was changed before it ran.** Its body was rewritten (test code only, uncommitted, in `AddAuditEventsMigrationTests.cs`) so that it first rolls the empty database back to the `AddAuditEvents` level, then inserts one `UserCreated` row and attempts the rollback to `AddAuthThrottleCounters`, which runs only `AddAuditEvents.Down`. It asserts SQLSTATE `23001` with the "audit history" message; that the audit table, the row and both non-internal triggers remain; and that the migration history still ends at `AddAuditEvents` with `AddThrottleClearedAuditAction` as the only pending migration. The previous form of that test was never run, so no statement is made about how it would have behaved.

**Snapshot rows that now have passing PostgreSQL evidence on this tree** (the snapshot wording in Parts 2–5 is left as written):
- E39 append-only protections: `AuditEventsAppendOnlyTests`.
- E40: the rewritten down-guard test above, for `AddAuditEvents.Down` only.
- E41 transactional persistence (other use cases): `AuditEventsAtomicityTests`. M5's own atomicity is in Addendum 2.
- E42: `AuditPrivilegeSelfCheckTests` and `AuditPrivilegePolicyTests`. The compliant-role case still cannot be produced by tests (roles are cluster-wide).
- §22 A–D and M1 (throttle policy and shared store): `AuthThrottlePolicyTests`, `AuthThrottleStoreTests`, `AuthThrottleIntegrationTests`, and the layer 2–5 HTTP tests in `ThrottlingTests` and `RateLimitLayerTests`. The derived "limits" figures in the contract were not recomputed.
- `AccessDenied` behaviour (E36 row, and the failure path): `AudienceAndGateTests` and `AccessDeniedWriteFailureTests`.
- The HTTP endpoint classes listed above, which include the login, refresh, logout, change-password and password-reset paths.

**Still not verified or open:**
- `AddThrottleClearedAuditAction.Down` **with `ThrottleCleared` rows present** remains untested; *inference, untested:* it would fail when the narrower CHECK is re-added.
- Other rollback scenarios remain untested unless named above, including the `Down` round trips of the other migrations (`AuthPersistence*MigrationTests`, `SeedAuthorizationCatalogRollbackTests`, and the user, party and role migration tests), which were not run.
- Classes not named in Addendum 2 or 3 were not run on this tree: the remaining Infrastructure classes (for example the other migration tests, the user, party, role and permission integrity tests, the bootstrap-seeder and operator-command classes, the startup self-check classes, `UseCaseIntegrationTests`, and `PayrollViewGuardTests`, which is outside the audit scope).
- Whether `AddThrottleClearedAuditAction` has been applied to any real target database is unknown; the E27 deviation (drop and re-add instead of `NOT VALID` then `VALIDATE`) is unresolved.
- OC-8 and OC-9 remain undecided. ADR 0006 and Contract section 22 remain **Proposed**. Slice 6 acceptance remains pending. Passing tests are not owner approval.
