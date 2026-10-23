# ADR 0006: Persistence for the financial audit trail — `audit_events` with explicit event context

**Status:** **Accepted as written (owner decision, 2026-10-23).** ~~Proposed — pending owner review of this document.~~ ~~**Not implemented.**~~ *Implementation note (2026-10-19): the Slice 3 design is implemented — commits `50832d1` and `d5c4c26`, recorded in `docs/implementation/backend/sprints/sprint-02.md` §A. Implemented does not mean accepted: this ADR's status is unchanged.* Backend Sprint 2, Slice 3 (US-BE-009). Records the owner decisions R46 and R47 of 2026-10-16 and the unnumbered owner decisions of 2026-10-19 on `ThrottleCleared` (§3; the E33/E36 target deviation recorded there stays open), and proposes engineering decisions E26–E44 (§4), which are not approved until the owner accepts this ADR. The contract wording they need is proposed in `docs/implementation/backend/features/auth-and-users-api-contract.md` §22 I (pending review, not applied).

*Revised 2026-10-16 (documentation correction pass): precedence of R46/R47 stated (§3); E30 reconciled with ح-188, business ADR-018 and PRV-08; ح-190 retention added to Audit Completion and Gate B (§8); E34 corrected to follow the contract's reason rule; the privilege self-check covers `TRIGGER` (§7.3); explicit Identity detail moved from `audit_event_changes` to the logical model's optional snapshot field, so that `audit_event_changes` holds only genuine field changes (E37, §5.2). All still proposed.*

**Scope:** one new table, `audit_events` (including the logical model's optional snapshot field), its Domain types, the Application `IAuditWriter` and its Infrastructure implementation, the append-only protection, and the privilege model the deployment must provide. Not in scope: generic change capture and `audit_event_changes`, field masking, `audit_event_links`, `audit_retention_policies` and audit read/export — they belong to the mandatory **Audit Completion** slice (§8).

**Repository ADR 0006 is not business ADR-006.** This file is the repository's sixth engineering ADR (`docs/decisions/`). `docs/architecture-decision-records-ar.md` contains a different, authoritative **ADR-006** ("سجل تدقيق مالي منفصل عن Serilog" — the audit trail is separate from Serilog). Always cite this file as "repository ADR 0006" and the other as "business ADR-006".

Like ADRs 0001–0005, this is an engineering ADR. It is not authoritative over any document in `docs/` outside `docs/decisions/`, and the contract outranks it (`docs/implementation/README.md` §1): where this ADR differs from contract v1.5 §14, the v1.5 text stands until the matching §22 I proposal is accepted. It separates **confirmed requirements** (authoritative documents, with their source), **owner decisions**, **draft contract text** and **engineering decisions**. Where it disagrees with an authoritative document, §10 records the disagreement as a correction proposal for that document's owner; nothing in the authoritative documents is changed by this ADR.

---

## 1. Context

Every state change of the Identity slice must write an audit event in the same database transaction, and the change must roll back if the audit write fails (contract §14, §14.2; ح-186). Slice 4 (use cases) depends on this slice. The design was reviewed read-only on 2026-10-16; this ADR records the final recommendation of that review and the owner's answers to its two questions.

## 2. Sources and their authority

Order of authority: `docs/documentation-index-ar.md` §1, then `docs/implementation/README.md` §1.

| Source | Status | Used for |
|---|---|---|
| `audit-and-operational-logging-addendum-ar.md` §4, §5, §6, §7, §9, §10, §11, §14 | Confirmed (rank 5) | Event content (conceptual list), two audit levels, append-only, atomicity, prohibited data, retention, acceptance cases ق-1…ق-15 |
| `confirmed-business-rules-ar.md` ح-182…ح-195 | Confirmed (rank 9) | ح-185 (two mandatory levels), ح-186 (atomicity), ح-187 (append-only), ح-188 (idempotency), ح-189 (content, conceptual), ح-190 (retention), ح-191 (prohibited data, masking before write), ح-192 (read is the highest permission) |
| `system-specification-and-team-reference-ar.md` FR-AUD-001…009, FR-AUTH-007, NFR-SEC-009, UC-002 | Authoritative derived (rank 15) | FR-AUD-003 (both levels), FR-AUD-005 (correction linked to the original), NFR-SEC-009 (database permissions prevent UPDATE/DELETE) |
| `logical-data-model-ar.md` §1.7, §1.8, §15.3–§15.12 | Authoritative derived (rank 16) | Fields (§15.4), one row per changed field (§15.5), governing rules (§15.6), keys (§15.7) |
| `logical-model-patch-v1.2.1-ar.md` §6 | Normative for what it covers (rank 15) | Derivation of an audit event's idempotency key |
| `data-dictionary-ar.md` §0.2, §6 | Authoritative derived (rank 17) | Logical types and required flags |
| `database-integrity-rules-ar.md` IMM-02, IMM-03, PRV-08, layer 4 | Authoritative derived (rank 18) | Corrections via `audit_event_links`; reason on corrections; idempotency on sync/import/job records |
| `architecture-decision-records-ar.md` business ADR-006, ADR-018, ADR-033 | Approved | Separation from Serilog; idempotency on every external input; `PWA` naming deferred to the audit design |
| `database-design-ar.md` §6.15 | Authoritative derived (rank 24) | `SystemJob` without a user (line 1686) |
| `mvp-entity-scope-matrix-ar.md` | **Proposed** (wave order not approved) | Not relied on for any gate in this ADR |
| Contract v1.5 §13, §14, §18, §19 | **Draft** working document | Field rules, fail-closed behaviour, operational requirements |

## 3. Owner decisions (2026-10-16)

These are owner decisions in the sense of `docs/implementation/README.md` §1 item 2. They were made after the read-only Slice 3 review.

| # | Decision | Effect |
|---|---|---|
| **R46** | **Permanent audit vocabulary.** The Employee application's audit source is **`EmployeeApp`**. The user-disable event is **`UserDisabled`**. The role-ending event is **`RoleEnded`**. | Resolves, for the implementation, the naming that business ADR-033 deferred to the audit design (`PWA`), and confirms C5 against spec UC-002 (`UserDeactivated` / `RoleChanged`). The authoritative documents still use the old terms; their correction is a proposal to their owners (§10). The Admin application keeps `Web`. |
| **R47** | **Production-data gate.** Contractra is not launched in production with real business or user data until the mandatory Audit Completion slice (§8) has been implemented, tested, reviewed and approved. Development, implementation and automated testing continue normally. | Gate B (§8.3). It is a release gate, not a stop on development. |

**Precedence of R46 and R47.** Both are explicit owner decisions and govern the implementation's behaviour: the stored codes are `EmployeeApp`, `UserDisabled` and `RoleEnded`, and Gate B applies, even where older text says otherwise. They outrank the Draft contract's v1.5 wording (`docs/implementation/README.md` §1 ranks owner decisions above the contract), so they apply before the §22 I revision is accepted; the contract's status (Draft v1.5) and the proposal status of every other §22 I item are unchanged. Against the protected authoritative documents the position is different: `docs/implementation/README.md` §1 ranks those documents above owner decisions, so R46 is recorded here as an **explicit owner-directed exception** for the terminology it names, not as a precedence result. For `PWA` the exception exercises a deferral that the approved business ADR-033 itself made ("وتسميتها قرار مؤجَّل إلى تصميم التدقيق (W1-C)"); for `UserDeactivated`/`RoleChanged` it overrides spec UC-002, which ranks above the logical model's `UserDisabled`/`RoleEnded`. The protected documents keep their current text until their owner applies the corrections in §10 (U1, U2); until then the discrepancy stays recorded, not resolved. R47 conflicts with no source: it adds a release gate.

**Owner decisions of 2026-10-19 (Slice 6 acceptance review D1, D4; no number assigned).** (1) `ThrottleCleared` is the 16th audit action type, written by the operator command `clear-throttle` (contract §22 M5). (2) Its audit event records the actor, time and reason; the event, the command's output and its Serilog messages must not carry the cleared login identifier or its unsalted hash. The event targets the operation, not the cleared identifier: `entity_type = "OperatorCommandRun"`, `entity_id` = the run's UUID (the per-run GUID the operator-command runner generates; also the run's correlation id). No schema change and no nullable `entity_id`; this replaces the nullable-`entity_id` option considered earlier the same day, which was not implemented. It deviates from E36 (Identity events target the aggregate root) and from E33's placement of operation ids in `causation_id`, because `entity_id` is NOT NULL and the only other identity available is the identifier's hash, which the decision forbids storing. These decisions do not accept this ADR.

**Why the vocabulary had to be decided now.** `audit_events` is append-only: a code written into a production row stays there. A later rename can only add a new value; every historical value must remain valid and keep its documented meaning. A compatibility change of that kind is a migration that widens a CHECK — never an `UPDATE` of history.

## 4. Decisions

Basis: **[C]** confirmed requirement · **[O]** owner decision · **[D]** draft contract text · **[E]** engineering decision (proposed in this ADR).

| # | Decision | Basis |
|---|---|---|
| E26 | `occurred_at_utc` comes from the **application** clock in UTC, read once per use case and shared with the business change it records. "Server" in LM §15.4 and DD §6 means server as opposed to client; NTP is required (contract §18). No database skew check: with fail-closed audit it would turn clock drift into rolled-back legitimate changes. Neither `occurred_at_utc` nor UUIDv7 orders events across instances; no source requires that. | [C] server time, not client time · [E] application server |
| E27 | `action_type` and `source` are text with CHECK constraints generated from one C# catalog each (the `AuthCodes` pattern, ADR 0005), so code and database cannot drift; a test compares them. No PostgreSQL enum type. A module that adds events replaces the CHECK in its own migration (`NOT VALID` then `VALIDATE`). | [C] DD `ENUM` = closed documented list · [E] mechanism |
| E28 | `actor_type` ∈ {`User`, `System`}. `actor_user_id` is set exactly when `actor_type = 'User'`. `actor_role_code` is always NULL for `System`; for `User` it follows contract §14.1 and is NULL only when the actor holds no active role (possible: `User.EndRole` does not protect the last role). No fake system user and no invented role. | [O] R40 · [D] §14.1 · [C] DB design line 1686 · DD conflict in §10 |
| E29 | `schema_version` is `integer NOT NULL CHECK (schema_version >= 1)`, versioned per action type; every Slice 3 event writes 1. DD §0.2 has no integer logical type, so its `TEXT` describes, it does not prescribe. | [D] §14.1 · [E] |
| E30 | **Two levels, kept apart.** *Business-operation idempotency* is the defence against a repeated operation: the key belongs to the business record or operation (`expenses.client_generated_id`, `financial_events.idempotency_key`, import row keys; ح-103, business ADR-018 "وهذا — لا تفرّد المصدر — هو خط الدفاع ضد الترحيل المكرر"), and a repeat is answered from it without writing anything (patch v1.2.1 §6: "بنفس المفتاح ⟹ لا شيء يُنشأ من جديد — والاستجابة نفسها تُعاد"). *Audit-event deduplication* is the backstop that stops a second audit row for the same event (ح-188: "لا يُنشئ … حدث تدقيق مكررًا"). One column, `idempotency_key`; no `client_event_id` column — a client event ID is stored **as** the key. Global partial unique index `WHERE idempotency_key IS NOT NULL`. Key = `<action_type>:<operation key>` (patch v1.2.1 §6: `CustodyDisbursed` + the same key), plus a mandatory `:<stable sub-identifier>` when one operation produces several events of the same action type (§5.3 format rule). **Required** (database CHECK) for every event whose source is `OfflineSync`, `Import` or `SystemJob`: ح-188 covers sync and import, and PRV-08 / integrity layer 4 require a key on "كل سجل من مزامنة أو استيراد أو مهمة" with `audit_events` among the mandatory entities. **Online events** (`Web`, `EmployeeApp`) carry the key whenever their business operation has one: business ADR-018 lists "إعادة إرسال الطلب عند ضعف الشبكة" among the sources it covers, so every online operation with a financial effect has a business key and its audit events derive from it. This is not an exemption for online requests: the Sprint 2 Identity endpoints carry no key only because no source requires one for them — they are neither sync, import nor job (ح-188, PRV-08), and ADR-018's context is limited to sources "تُنتج أثرًا ماليًا". Their repeats are already safe without one (§5.3). | [C] ح-188, ح-103, business ADR-018, PRV-08, LM §15.7, DD §6 · [C] patch v1.2.1 §6 · [E] operation keys for System events, sub-identifier |
| E31 | `managed_company_id`, `operation_id`, `site_id`: uuid NULL, no foreign key yet, and a temporary CHECK that all three are NULL, dropped by the migration that adds their foreign keys. `operation_id` is included (LM §15.4). The referenced target of `managed_company_id` is **not defined** in any source (§11). | [C] LM §15.4 · [E] temporary CHECK |
| E32 | `approved_by_user_id`: uuid NULL, FK → `users` `ON DELETE RESTRICT`, and a temporary CHECK that it is NULL until its meaning is defined (§11). | [C] LM §15.4 · [E] |
| E33 | `correlation_id`: NOT NULL, not unique, CHECK `^[A-Za-z0-9._-]{1,64}$`. The Domain validates and never generates; generation happens at the HTTP boundary (Slice 6) and per run in the seeder and operator commands (Slice 5). `causation_id`: NULL, same format; it holds the opaque identifier of the operation that caused this one (an import batch, a job run, a command). Causality between two audit events is an `audit_event_links` row of type `CAUSED_BY` (Audit Completion). Slice 3 writes no `causation_id`. | [D] §13 · [C] DD `causation_id TEXT` · [E] meaning |
| E34 | `reason`: NULL or 1–1000 characters with no leading or trailing whitespace. **Required for the events of operator commands** — `issue-password-reset` (`PasswordResetIssued` with a `System` actor) and `reset-bootstrap-password` (`BootstrapPasswordReset`): contract §14.1 ("Required for operator commands (§10A, §11); optional otherwise"), §10A (`--reason <text>`), §18. **Seeder events** (`UserCreated`, `RoleAssigned`, `PayrollViewGranted`) carry a fixed descriptive reason: contract §11 does not ask for one, but US-BE-011 says the seeder and the commands are "each audited with actor_type System and a reason", and "optional otherwise" permits it. **No rule applies to `System` events in general**: no source says whether future scheduled jobs need a reason, so each module that adds one decides it. Corrections require a reason (IMM-03), enforced by the modules that write them. The rule is per action and origin, enforced in the Domain; no database CHECK ties `reason` to `actor_type`. A reason must never contain a secret. | [D] §10A, §14.1, §18, US-BE-011 · [C] IMM-03 · [E] length |
| E35 | `previous_status` and `new_status` are both NULL or both set, different from each other, and non-empty. Which events must carry them is checked per action type in the Domain. | [D] §14.1 · [E] |
| E36 | Identity events target the aggregate root: `entity_type = 'User'`, `entity_id` = the user's id (contract §14.1). `entity_id` is NOT NULL (DD). What `AccessDenied` targets is decided in Slice 6; relaxing NOT NULL later loses no data. | [C] DD · [D] §14.1 |
| E37 | **Which role, permission or reset ticket an event concerns is recorded in the event's optional snapshot field (§5.2)**, not in `audit_event_changes`. LM §15.3 defines `audit_event_changes` as "القيمة قبل وبعد لكل حقل تغيّر" (the value before and after for each field that changed); a reference such as `password_reset_ticket_id` on `PasswordResetCompleted`, or a role code on `RoleEnded`, is context, not a changed value. LM §15.5 provides the place for context: "حقل لقطة اختياري على الحدث للحالات التي تتطلب صورة كاملة", available "لأحداث محدَّدة فقط". The snapshot is the image of the affected child record at the time of the event, from an approved, non-sensitive key list per action type, versioned by `schema_version`. `audit_event_changes` is created by Audit Completion and holds only genuine before/after values. | [C] LM §15.3, §15.5 · [E] key lists |
| E38 | **No `ip_address` or `user_agent` columns** in the initial design. LM §15.4 marks them "عند الحاجة فقط" (only when needed) and DD §6 marks them sensitive "بضوابط الخصوصية"; contract §14.1 does not store them in Sprint 2. A column that does not exist cannot collect data; adding one later is a metadata-only change. | [C] LM §15.4, DD §6 · [E] |
| E39 | Append-only protection on `audit_events` (and, from Audit Completion, on every audit table): statement-level `BEFORE UPDATE OR DELETE` and `BEFORE TRUNCATE` triggers raising `insufficient_privilege` (42501), plus revocation at creation time (§7.1). Deliberately not `check_violation`: a refused audit change is "not permitted", not "invalid data". | [C] ح-187, LM §15.6 rule 1, NFR-SEC-009 · [E] mechanism |
| E40 | `Down` of the Slice 3 migration refuses to run when `audit_events` has rows, so a rollback cannot silently destroy audit history (plan E16: fail rather than lose). Down-and-up tests use empty tables. | [E] |
| E41 | Transactional persistence (§6): the writer stages only; the caller owns the transaction, saves and commits through a single exit; any failure discards the transaction and the DbContext. | [C] ح-186 · [D] §14.2 · [E] |
| E42 | The privilege self-check query is delivered in Slice 3 with its test; it runs at startup from Slice 5 (the first slice with startup work, before the seeder writes) and fails closed in Production. It checks exactly the privileges the migration revokes — `UPDATE`, `DELETE`, `TRUNCATE` and `TRIGGER` — plus ownership and superuser (§7.3). | [C] NFR-SEC-009 · [D] §18 · [E] placement |
| E43 | Deployment uses a migration role and a separate application role; the application never runs migrations (§7). | [C] NFR-SEC-009 · [D] §18 |
| E44 | `id` is UUIDv7 set by the Domain. No column has a database default; every row states every value. All timestamps are `timestamp with time zone`. Names follow ADRs 0001–0005. | [E] (repository convention) |

*Dated notes (2026-10-19), added after the Slice 6 acceptance review; the decisions above are unchanged and still proposed:*
- *E27:* the first migration to add an event, `AddThrottleClearedAuditAction` (`ThrottleCleared`), drops and re-adds the CHECK in one step instead of `NOT VALID` then `VALIDATE`. This is an open deviation. Whether that migration has been applied to any database is not known, so it has not been edited; resolving it (rewrite before first application, or a follow-up migration) needs that fact first.
- *E34:* `clear-throttle` is a third operator command. The Domain now requires a reason for `ThrottleCleared` exactly as for `PasswordResetIssued` and `BootstrapPasswordReset`.
- *E33 / E36:* `ThrottleCleared` deviates from both: its target is `OperatorCommandRun` + the run's UUID rather than an aggregate root, and the operation id sits in `entity_id` (it is also the correlation id and the idempotency key's operation part), not in `causation_id`. See the owner decisions of 2026-10-19 in §3 for the reason.

## 5. Schema

### 5.1 `audit_events`

| Column | Type | Null | Rule |
|---|---|---|---|
| `id` | uuid | no | `pk_audit_events` |
| `occurred_at_utc` | timestamptz | no | E26 |
| `actor_type` | text | no | `ck_audit_events_actor_type_valid_values`: `User`, `System` |
| `actor_user_id` | uuid | yes | `fk_audit_events_users_actor_user_id` `ON DELETE RESTRICT`; `ix_audit_events_actor_user_id` |
| `actor_role_code` | text | yes | `ck_audit_events_actor_role_code_valid`: NULL, or non-empty with no surrounding whitespace (role codes have no length limit, ADR 0003) |
| *(actor rule)* | | | `ck_audit_events_actor_consistency`: `(actor_type = 'User' AND actor_user_id IS NOT NULL) OR (actor_type = 'System' AND actor_user_id IS NULL AND actor_role_code IS NULL)` |
| `action_type` | text | no | `ck_audit_events_action_type_valid_values` (§5.3) |
| `entity_type` | text | no | `ck_audit_events_entity_type_valid`: `^[A-Z][A-Za-z0-9]{0,63}$` |
| `entity_id` | uuid | no | No foreign key: a generic target (integrity rules: «مقصود — يبقى») |
| `business_document_number` | text | yes | `ck_audit_events_business_document_number_valid`: NULL or 1–128 characters, trimmed |
| `managed_company_id` | uuid | yes | E31 |
| `operation_id` | uuid | yes | E31 |
| `site_id` | uuid | yes | E31 · `ck_audit_events_context_not_yet_defined`: all three NULL (temporary) |
| `previous_status` | text | yes | E35 |
| `new_status` | text | yes | E35 · `ck_audit_events_status_pair`, `ck_audit_events_status_changed`, non-empty checks |
| `reason` | text | yes | `ck_audit_events_reason_valid` (E34) |
| `approved_by_user_id` | uuid | yes | `fk_audit_events_users_approved_by_user_id` `ON DELETE RESTRICT`; `ix_audit_events_approved_by_user_id`; `ck_audit_events_approved_by_not_yet_defined`: NULL (temporary) |
| `correlation_id` | text | no | `ck_audit_events_correlation_id_format` |
| `causation_id` | text | yes | `ck_audit_events_causation_id_format` |
| `source` | text | no | `ck_audit_events_source_valid_values` (§5.3); `ck_audit_events_system_actor_source`: `actor_type <> 'System' OR source = 'SystemJob'` |
| `idempotency_key` | text | yes | `ck_audit_events_idempotency_key_format`: `^[!-~]{1,256}$`; `ck_audit_events_idempotency_key_required_for_source`: `source NOT IN ('OfflineSync', 'Import', 'SystemJob') OR idempotency_key IS NOT NULL` (E30); `ck_audit_events_idempotency_key_prefix`: `idempotency_key IS NULL OR idempotency_key LIKE action_type || ':%'` (E30); `ux_audit_events_idempotency_key` UNIQUE `WHERE idempotency_key IS NOT NULL` |
| `snapshot` | jsonb | yes | LM §15.5 optional snapshot field (E37, §5.2). `ck_audit_events_snapshot_valid`: NULL, or a JSON object of at most 8 KiB (`jsonb_typeof(snapshot) = 'object' AND octet_length(snapshot::text) <= 8192`). Which action types may carry one, and with which keys, is enforced by the Domain. |
| `schema_version` | integer | no | `ck_audit_events_schema_version_positive`; identifies, per action type, the shape of the snapshot |

Indexes besides the primary key, the unique index and the two foreign-key indexes: `ix_audit_events_entity (entity_type, entity_id, occurred_at_utc)` for the history of one record (addendum ق-11). Correlation and context indexes come with the audit read/export slice; on a large table they must be created `CONCURRENTLY` (a migration step outside a transaction).

Deliberately absent: `client_event_id` (E30), `ip_address` and `user_agent` (E38), a `payload` column (the legacy plan's; genuine field changes go to `audit_event_changes` from Audit Completion, context to the source-defined snapshot field), and any amount or balance field (ح-184, business ADR-006).

### 5.2 Changes versus context: `audit_event_changes` and the snapshot field

**Two different things** (LM §15.3, §15.5):

| | Holds | Example | Where | When |
|---|---|---|---|---|
| **Field change** | The value before and after of a field the action changed — "القيمة قبل وبعد لكل حقل تغيّر" | `user_roles.ended_at`: NULL → `2026-…` | `audit_event_changes` (one row per field, `UNIQUE(event_id, field_name)`, LM §15.7) | Audit Completion (generic capture writes it) |
| **Context** | What the event concerns, as it was at the time: the affected record's identity and state, including codes that did not change | The assignment id and role code on `RoleEnded`; the ticket id on `PasswordResetCompleted` | `audit_events.snapshot` — the LM §15.5 optional snapshot field for specific events | Slice 3 |

Recording context as a "change" would misstate history: on `RoleEnded` the role code did not change to NULL, and on `PasswordResetCompleted` the ticket id did not appear from NULL. Context therefore never goes into `audit_event_changes`, and `audit_event_changes` is not created until Audit Completion, where it holds genuine before/after values only.

**Snapshots in Slice 3** — the image of the affected child record **after** the action, from an approved key list per action type (`schema_version` 1). The Domain refuses any other key and refuses a snapshot on any other action type.

| Action types | Snapshot keys (v1) |
|---|---|
| `RoleAssigned`, `RoleEnded` | `user_role_id`, `role_id`, `role_code`, `assigned_at`, `ended_at` |
| `PermissionGranted`, `PermissionRevoked`, `PayrollViewGranted`, `PayrollViewRevoked` | `user_permission_id`, `permission_id`, `permission_code`, `granted_at`, `revoked_at` |
| `PasswordResetIssued`, `PasswordResetCompleted` | `password_reset_ticket_id`, `issued_by_user_id`, `channel`, `issued_at`, `expires_at`, `consumed_at` (ADR 0005 §3.3: the event references the ticket; `code_hash` is never included) |
| All others | none (`UserDisabled` and `UserReactivated` record their change in `previous_status`/`new_status`) |

**Never captured, in any field or snapshot:** passwords, temporary passwords, password hashes, reset codes, access or refresh tokens, any token or code hash, and login identifiers (LM §1.7, ح-191, contract §1). Masking before writing (ح-191) needs no rule in Slice 3 because only the keys above are accepted; Audit Completion introduces field classification before generic capture can see any column.

**What the snapshot does and does not prove.** Codes and timestamps are captured as they were when the event was written, so a later rename of a role or permission code (possible by direct SQL; ADR 0002 deliberately does not make codes immutable) does not change history. The assignment, grant and ticket ids are **references, not evidence**: those rows are never deleted (LM §1.5, RESTRICT foreign keys), but their columns are not protected against direct SQL, so the immutable historical value is what the snapshot captured, not the referenced row. The values are only as correct as the use case that writes them; Slice 4 tests assert them. LM §15.5 (option B, ❌) rejects a full JSON snapshot as the *primary* store for field changes because it is hard to query, large and hard to mask; that objection does not apply to a small, fixed, non-sensitive key list per action type, which is what LM §15.5 permits for specific events.

**Slice 3 does not provide generic change capture.** The snapshot is explicit business context. The generic data-change level required by ح-185 and FR-AUD-003 is not implemented until Audit Completion (§8).

### 5.3 Catalogs

**Action types** (contract §14, as confirmed by R46):

`UserCreated` · **`UserDisabled`** (R46) · `UserReactivated` · `RoleAssigned` · **`RoleEnded`** (R46) · `PermissionGranted` · `PermissionRevoked` · `PayrollViewGranted` · `PayrollViewRevoked` · `PasswordChanged` · `PasswordResetIssued` · `PasswordResetCompleted` · `InitialPasswordReissued` · `BootstrapPasswordReset` · `AccessDenied`

Not included: `PasswordReset` (replaced in contract v1.5 by `PasswordResetIssued` and `PasswordResetCompleted`), `LoginFailed` (Serilog only, LM §1.8).

*Added 2026-10-19 (owner decision, §3):* · **`ThrottleCleared`** — operator `clear-throttle` (contract §22 M5); `System` actor, source `SystemJob`, operation key `command:<the run's correlation id>`, target `OperatorCommandRun` + the run's UUID, a reason required, no snapshot. The catalog now has 16 codes.

**Sources:**

| Value | Used for | Basis |
|---|---|---|
| `Web` | The Admin application | [C] LM §15.4 · [D] §14.1 mapping |
| **`EmployeeApp`** | The Employee application, on any platform | **[O] R46** (replaces `PWA`; §10) |
| `OfflineSync` | Records synchronised from offline drafts (future) | [C] LM §15.4 |
| `Import` | Historical import (future) | [C] LM §15.4 |
| `SystemJob` | Startup seeding, operator commands, background jobs | [C] LM §15.4 · [D] §14.1 |

The rule is one-way: a `System` actor implies `SystemJob`; nothing in the sources says that `SystemJob` implies a `System` actor.

**Idempotency keys by origin (E30).**

| Origin | Source | Operation key | Full `idempotency_key` (example) | Basis |
|---|---|---|---|---|
| Bootstrap seeder | `SystemJob` | `bootstrap:<bootstrap user id>` | `UserCreated:bootstrap:<id>`, `RoleAssigned:bootstrap:<id>`, `PayrollViewGranted:bootstrap:<id>` | PRV-08 (job) · [E] key |
| Operator commands | `SystemJob` | `command:<the run's correlation id>` | `PasswordResetIssued:command:<correlation id>` | PRV-08 (job) · [E] key |
| Background jobs (future) | `SystemJob` | Job type + period + scope + version, as for the report job | `<action_type>:<job type>:<period>:<scope>:<version>` | DB design §2.11 · PRV-08 |
| Offline sync (future) | `OfflineSync` | The record's `client_generated_id` | `<action_type>:<client_generated_id>` | ح-188 · business ADR-018 |
| Import (future) | `Import` | Batch, sheet and row, with `source_record_hash` | `<action_type>:<batch>:<sheet>:<row>` | ح-103 · business ADR-018 |
| Online operations with a business key (future financial modules) | `Web`, `EmployeeApp` | The operation's own idempotency key | `<action_type>:<operation key>` | business ADR-018 (request resend) · patch v1.2.1 §6 |
| Sprint 2 Identity endpoints | `Web`, `EmployeeApp` | none | none | No source requires one (E30) |

A repeated Sprint 2 Identity request is either refused by a state check — create (409 `users.login_identifier_taken`), disable, reactivate (409), change password (400, the current password has changed), reset completion (400, the ticket is consumed) — or deliberately performs and audits a **new** operation: a repeated reset issue or initial re-issue replaces the earlier ticket or temporary password and writes its own event. No duplicate audit event of one operation can arise. The sub-identifier (E30) is never needed in Sprint 2: each origin above produces at most one event per action type.

**Format rule (E30).** The stored key is always `<action_type>:<operation key>[:<sub-identifier>]`, composed by the Domain from the event's own action type, so two events of different action types can never collide even when they share an operation key (the seeder writes `UserCreated`, `RoleAssigned` and `PayrollViewGranted` under one `bootstrap:<id>`), and the database checks that the key starts with the row's `action_type` and a colon (`ck_audit_events_idempotency_key_prefix`). The sub-identifier is **mandatory** whenever one operation produces more than one event of the **same** action type, and is a stable value of the thing the event concerns (for example the assignment id); the producer of such events owns its uniqueness. Sprint 2 never needs one: the seeder assigns one role and one permission, and a command run writes one event.

**Duplicate behaviour.** A repeat with the same business key is answered from the business record before anything is written. If an audit insert nevertheless meets `ux_audit_events_idempotency_key` (23505), the whole transaction rolls back and the caller resolves the outcome from the business record; the writer never uses `ON CONFLICT DO NOTHING`.

**Status values** are the literal `users.status` codes: `Active`, `Disabled`.

### 5.4 Append-only protection

One function, `fn_audit_append_only()`, raises `insufficient_privilege` with the trigger's name as the constraint name. Triggers on `audit_events` in Slice 3, and on every audit table Audit Completion adds (`audit_event_changes`, `audit_event_links`): `trg_<table>_forbid_update_delete` (`BEFORE UPDATE OR DELETE … FOR EACH STATEMENT`) and `trg_<table>_forbid_truncate` (`BEFORE TRUNCATE … FOR EACH STATEMENT`). Statement-level triggers fire even when no row matches, and for `INSERT … ON CONFLICT DO UPDATE` and `MERGE`; `TRUNCATE` triggers fire for tables reached through `TRUNCATE … CASCADE` (PostgreSQL documentation; each case is a required test, §9). The writer never uses `ON CONFLICT DO NOTHING`: that could commit a business change without its audit row.

## 6. Transactions and failure model

1. One scope and one DbContext per unit of work. **The use case opens the transaction before its first database write.**
2. The clock is read once.
3. Domain changes; bulk operations (`RevokeAllActiveAsync`, ticket issue) run inside the open transaction.
4. The audit event, with its snapshot where one applies, is staged with `IAuditWriter` (Application interface, no EF Core reference; the scoped Infrastructure implementation adds them to the same `ContractraDbContext` and never calls `SaveChanges`).
5. Save, then commit, through a single exit (Slice 4's unit of work), so a staged-but-unsaved event or a commit after a failed save cannot happen (E25).
6. Deferred constraints are checked at commit (ADR 0005 §7.3); a violation there rolls back the audit rows with everything else.

**Failure:** no commit; the transaction is disposed (rollback); the DbContext and scope are discarded, because the change tracker still holds the staged rows. HTTP callers get 500 `server.error`, operator commands exit non-zero, and the seeder stops startup (contract §11, §14.2). A retry starts in a new scope with new event ids. If an EF retry strategy is ever enabled, the whole unit of work must run inside `CreateExecutionStrategy().ExecuteAsync`.

**Two existing helpers can break atomicity if no transaction is open:** `RevokeAllActiveAsync` executes its `UPDATE` immediately (`AuthSessionPersistence.cs`), and `IssuePasswordResetTicketAsync` commits its own transaction when the caller has none (`PasswordResetTicketPersistence.cs`). Every caller of both is an audited use case, so **Slice 4 makes both throw when no transaction is open**. No commit interceptor is added: the single exit and these guards prevent the same failures more simply.

**`AccessDenied`** (Slice 6) is written in its own scope and DbContext, so it can never save unrelated pending changes; if it fails, the request is still refused and Serilog records `Audit.WriteFailed` (contract §14.2).

## 7. Database privileges

### 7.1 Portable migration behaviour (the same in every environment)

- Creates `audit_events`, its constraints, indexes, function and triggers (Audit Completion repeats the same steps for each audit table it adds).
- Immediately after each audit `CREATE TABLE`, revokes `UPDATE`, `DELETE`, `TRUNCATE` and `TRIGGER` from every non-owner grantee present in the table's ACL (`pg_class.relacl`, PUBLIC included). This names no role and cancels privileges granted automatically by `ALTER DEFAULT PRIVILEGES`.
- Names no environment role and creates no role.

### 7.2 Environment-specific provisioning (deployment documentation, not migrations)

- A **migration role** owns the schema and applies migrations with an EF Core migration bundle or idempotent script. **The application never runs migrations at startup** (true today: `Program.cs` does not call `Migrate`).
- An **application role** that does not own the tables, is not a superuser, and holds only `SELECT` and `INSERT` on `audit_events` and on each audit table Audit Completion adds (plus what it needs on other tables), and no `TRIGGER` privilege on any of them.
- Default privileges, if used, are followed by an explicit revoke of `UPDATE`, `DELETE`, `TRUNCATE` and `TRIGGER` on every audit table.
- After any restore: re-run provisioning, run the self-check, and record the restore outside the database.

### 7.3 Runtime self-check (E42)

A query reports, for every audit table, whether the current role holds `UPDATE`, `DELETE`, `TRUNCATE` or `TRIGGER` on it (`has_table_privilege`) — the same four privileges the migration revokes (§7.1) — whether it owns it, and whether it is a superuser. `TRIGGER` is included because a role holding it could attach its own trigger to the table, which is outside the append-only model. In **Production**, any of these stops startup. In other environments it logs a warning: development and the integration tests connect as the table owner (the tests use a non-superuser role with only `CREATEDB` that owns each ephemeral database, ADR 0001 §14), which is expected there. The integration tests cannot create roles, and roles are cluster-wide objects that would outlive the ephemeral database, so grants are verified by the self-check and deployment validation, not by tests.

### 7.4 What the schema cannot guarantee

The table owner or a superuser can disable or drop the triggers or drop the tables; a restore can rewind history; ح-192 (the technical team does not read financial event content) cannot be enforced against database administrators. These need operational controls: restricted DBA access, an out-of-database record of every privileged break-glass action (who, why, when), and the restore procedure above. Retention, archival and erasure: §11.

## 8. Slice 3 scope, Audit Completion and the release gates

### 8.1 What Slice 3 delivers — and what it does not

Slice 3 delivers the explicit business-event level: `audit_events` with the snapshot context of §5.2, the writer and its transactional rules, append-only protection, the privilege self-check query and the tests in §9. **It does not satisfy ح-185 / FR-AUD-003 on its own**: the generic data-change level ("تدقيق عام لتغيّر البيانات يلتقط أي تغيّر") is missing until Audit Completion, and changes made before it exists cannot be back-filled (LM §15.10: no invented historical events).

### 8.2 Audit Completion slice (mandatory)

| Item | Requirement |
|---|---|
| Generic change capture | ح-185, FR-AUD-003, addendum §5.1, §5.3. Mechanism is a physical-design decision (LM §15.11, OQ-010). An EF Core `SaveChanges` interceptor does not see `ExecuteUpdate`/`ExecuteDelete`, so bulk writes need explicit handling. |
| `audit_event_changes` | LM §15.3, §15.5, §15.7: one row per **changed** field with its before and after values, `UNIQUE(event_id, field_name)`; written by generic capture; never used for context (§5.2). Append-only like `audit_events`. |
| Field classification and masking before writing | ح-191, FR-AUD-008, FR-AUD-009, addendum §9.2: every column of every table is classified before generic capture can see it; credential columns are never captured; snapshots follow the same rules. |
| `audit_event_links` | LM §15.7 (`CORRECTION_OF`, `REVERSAL_OF`, `CAUSED_BY`; `UNIQUE(source_event_id, target_event_id, link_type)`), FR-AUD-005, IMM-02, ح-187. Append-only like `audit_events`. |
| Further snapshots | LM §15.5 — a new action type gets a snapshot only where a confirmed requirement needs a full picture (for example a document approval), with its own key list and `schema_version`. |
| Idempotency key construction and replay | E30; ح-188 (including recording the source, the movement date and the receipt date separately for sync), business ADR-018, addendum ق-8. |
| **Retention and archival (ح-190)** | ح-190: "سياسة الاحتفاظ والأرشفة قابلة للضبط، ولا يُحذف سجل تدقيق مالي دون سياسة معتمدة موثَّقة بمعتمِدها وتاريخها. والأرشفة نقل لا إتلاف"; addendum §11; LM §15.6 rule 9, §15.3, §15.7. **Acceptance criterion:** (1) no path deletes or archives audit rows — triggers and privileges prove it for every audit table; (2) `audit_retention_policies` exists as LM §15.7 defines it (`UNIQUE(event_type)`; `approved_by` and `approved_at` mandatory), so a policy per event type can be configured with its approver and date, and creating or changing a policy writes an audit event; (3) an event type without an approved policy is retained indefinitely — nothing is ever deleted or archived without one; (4) no archival mechanism is delivered without an approved policy, and any later one moves rows to readable storage and never destroys them. Retention durations and who approves deletion or archival (OQ-001, addendum §11: "يحتاج قرار المدير + المحاسب") are business decisions; they are not a prerequisite for this criterion because (3) keeps everything until they are made. The proposed MVP matrix classes `audit_retention_policies` as Post-MVP; it is included here because ح-190 is confirmed and the matrix is only proposed. |
| Tests | Business change and both audit levels commit or roll back together (ق-2, ق-14); no secret or prohibited value reaches any audit table or snapshot (ق-15); bulk writes covered; a correction is linked to its original (ق-5, ق-11); a replay with the same key creates no duplicate (ق-8); the retention criterion above (no delete or archive path; policy rows require approver and date; policy changes are audited). |

### 8.3 Gates

**Gate A — financial-module gate.** No migration that introduces a financial table or a financial module may be merged until the Audit Completion slice is implemented, tested, reviewed and approved. Basis: ح-185 and FR-AUD-003 (both levels mandatory together), FR-AUD-005 and integrity rule IMM-02 (corrections linked through `audit_event_links`), ح-186. The proposed MVP matrix (W1-C before any financial movement) agrees but is not the basis, because its wave order is not approved.

**Gate B — production-data gate (owner decision R47).** No production deployment may manage real business or user data until Audit Completion is complete and its acceptance criteria (§8.2) have passed. It protects ح-185 for every kind of data, Identity included, and — because the retention criterion is one of those acceptance criteria — ح-190 as well. No separate retention gate is needed. Production without that criterion would need an explicit, approved exception to ح-190, which nothing here grants.

**Neither gate stops ordinary work.** Slices 4–7, other development and automated testing continue normally.

## 9. Tests required for Slice 3

- **Domain:** each factory rule (`ForUser`, `ForSystem`): UTC only, correlation format, System → `SystemJob`, a reason on operator-command and seeder events (E34), an idempotency key on `SystemJob`, `OfflineSync` and `Import` events, composed as `<action_type>:<operation key>[:<sub-identifier>]` from the event's own action type, with two action types never producing the same key (E30, §5.3), status pair, schema version ≥ 1, catalogs, snapshot only on the listed action types and only with their keys (§5.2), empty ids rejected, whitespace rules.
- **Schema:** exact column list and types of `audit_events`; every CHECK accepted and rejected with its constraint name, including `ck_audit_events_idempotency_key_required_for_source`, `ck_audit_events_idempotency_key_prefix` and `ck_audit_events_snapshot_valid`; RESTRICT foreign keys asserted as SQLSTATE 23001 or 23503 (sprint §E: new tests must not depend on the PostgreSQL 16/18 difference); the partial unique index (23505; several NULLs allowed); the catalog CHECKs equal the C# catalogs.
- **Append-only:** `UPDATE`, `DELETE`, a zero-row `UPDATE`, `TRUNCATE`, `TRUNCATE users CASCADE` and `INSERT … ON CONFLICT DO UPDATE` are refused with 42501 and the trigger name; `INSERT` succeeds.
- **Atomicity** (forced failure: an `actor_user_id` that matches no user): a tracked change plus a failing audit row persists nothing; `RevokeAllActiveAsync` plus a failing audit row inside an explicit transaction leaves the sessions active; ticket issue plus audit commit together or not at all; staging alone writes nothing; a deferred-constraint failure at commit removes the audit row too.
- **Migration:** down-and-up of the new migration; `Down` refuses with rows present; the whole chain down and up; existing down-migrating test classes still pass; `ModelConsistencyTests`.
- **Self-check query:** detects that the test role owns `audit_events`, and reports each of `UPDATE`, `DELETE`, `TRUNCATE` and `TRIGGER` separately.

## 10. Upstream documentation corrections (proposals for the documents' owners)

These documents are protected and are not edited. Each item is a correction proposal requiring approval by the owner of that document.

| # | Document and location | Current text | Proposed correction | Reason |
|---|---|---|---|---|
| U1 | `logical-data-model-ar.md` §15.4 (line 2188) · `data-dictionary-ar.md` §6 (line 264) · `audit-and-operational-logging-addendum-ar.md` §4 (line 125) · `system-specification-and-team-reference-ar.md` (line 2957) · `conceptual-data-model-ar.md` (line 404) | `PWA` / "Web · PWA · Offline Sync · Import · System Job" | `EmployeeApp` | R46; business ADR-033 superseded the React PWA (ADR-004) and deferred this name to the audit design. Business ADR-033's note that the naming is deferred can record that it was decided by R46. |
| U2 | `system-specification-and-team-reference-ar.md` UC-002 (line 1816) | `UserDeactivated` · `RoleChanged` | `UserDisabled` · `RoleAssigned` + `RoleEnded`, and add `UserReactivated` and `AccessDenied` (both in LM §1.8) | R46 confirms C5; the specification ranks above the logical model (index §1), so it must match. This closes contract §19 OC-5(a). |
| U3 | `data-dictionary-ar.md` §6 (lines 253–254) | `actor_user_id` and `actor_role_code` Required ✅ | Required when the actor is a user (`actor_role_code`: when the user holds an active role); add `actor_type` (`User`/`System`) | A system job has no user (DB design line 1686; the same row lists `SystemJob`); the bootstrap seeder runs before any user exists. R40 added `actor_type`, which neither LM §15.4 nor DD §6 lists. |
| U4 | `data-dictionary-ar.md` §6 (line 267), §0.2 | `schema_version` `TEXT`; no integer logical type | An integer logical type, or a note that `schema_version` is a positive integer | E29. |
| U5 | `logical-data-model-ar.md` §15.4 (line 2189) | `idempotency_key` / `client_event_id` | `idempotency_key` (a client event ID, when one exists, is stored as the key) | §15.7, DD §6 and the integrity rules make only `idempotency_key` unique; two columns would leave `client_event_id` unique nowhere (ح-188). |
| U6 | `logical-data-model-ar.md` §1.8 · spec UC-002 | No password events | Add `PasswordChanged`, `PasswordResetIssued`, `PasswordResetCompleted`, `InitialPasswordReissued`, `BootstrapPasswordReset` | They exist as owner and engineering decisions (D6, R32, R44, E1, E3; contract §14) without an authoritative source. |
| U7 | `documentation-index-ar.md` | Does not list `docs/decisions/**` or `docs/implementation/**` | Already reported as contract §19 OC-5(c) | Unchanged. |

## 11. Not decided here

- **Referenced target of `managed_company_id`.** LM line 276 calls `managed_company_links` "a dated relationship, not an identity table", which suggests the company's party, but no document states the target. It must be decided once for every table that carries the column; the temporary NULL-only CHECK stays until then.
- **Meaning of `approved_by_user_id`.** "عند اختلافه عن المنشئ" (when different from the creator) is ambiguous: in an approval event the actor is the approver. Defined by the first module with an approval step (IMM-04 requires an approver on corrections).
- **Sub-identifier for replays** that produce several events of the same type (E30): defined by the first sync or import module.
- **Target of `AccessDenied`** (`entity_type`/`entity_id`): Slice 6.
- **Generic capture mechanism, masking rules, snapshot use:** Audit Completion.
- **Retention durations and approvers (OQ-001).** The mechanism and its acceptance criterion are part of Audit Completion (§8.2); the durations per event type and who approves deletion or archival are for the manager and the accountant (addendum §11). Until they decide, nothing is deleted or archived.
- **Erasure.** Erasure requests versus append-only are not addressed by any source; storing ids rather than personal data and bounding `reason` are the mitigations. If the tables are later partitioned by time, the primary key must include `occurred_at_utc`, which needs a privileged copy migration.
- **Release scheduling** of Audit Completion (R47 says only that it precedes production).

## 12. Obligations handed to later slices

| Slice | Obligation |
|---|---|
| 4 | Unit of work with a single save-and-commit exit; transaction opened before the first write; `RevokeAllActiveAsync` and `IssuePasswordResetTicketAsync` throw without an open transaction; each use case asserts its audit rows and snapshots. |
| 5 | Run the privilege self-check at startup before the seeder; generate a correlation id per seeder and command run; operator-command events carry the operator's `--reason`, seeder events a fixed reason (E34); seeder and command events carry the idempotency keys of §5.3. |
| 6 | Correlation id at the HTTP boundary (contract §13); a rejected client value is logged by length and hash, never raw; `AccessDenied` in its own scope; decide its target. |
| Audit Completion | §8.2; then lift Gates A and B. |
