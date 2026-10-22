# Working documents — what they are and which one wins

`docs/implementation/**` (plans, sprints, API contracts) and `docs/decisions/**` (ADRs) are **working documents**. They are not listed in `docs/documentation-index-ar.md`, which ranks the authoritative business sources; this page gives their order. It never overrides that index.

## 1. Order when documents disagree

1. **Authoritative sources** in the order of `docs/documentation-index-ar.md` (confirmed business rules, addenda, specification, logical model, …). A working document that contradicts one of them has a defect; the source wins.
2. **Owner decisions** recorded in `backend/features/admin-user-creation-plan.md` (C1–C6 §1, R1–R43 §1B, R44–R45 §1D, R48 §1E — superseded the same day by R49–R51 §1F, which also supersede R1–R3, R16, E3, E4 and the reset half of R44 in their credential parts), and R46–R47 (2026-10-16, audit vocabulary and production-data gate) recorded in repository ADR 0006 §3 (`../decisions/0006-audit-events-persistence.md`), as are the unnumbered decisions of 2026-10-19 on the `ThrottleCleared` audit event (also in the contract's §21). Between owner decisions, a later one that explicitly addresses the same point supersedes the earlier one; the earlier text stays as history.
3. **`backend/features/auth-and-users-api-contract.md`** — the current version (v1.5) is the source of truth for the auth and user-management API, for the backend and both Flutter apps. Its **[N] / [O] / [E]** tags say where each rule comes from; [N] beats [O] beats [E].
4. **ADRs** (`docs/decisions/`) — how a rule is implemented physically. An ADR records what was decided *at its date*; later changes are added as dated notes, never by rewriting the history.
5. **Sprint files** — what is planned, in progress and done. The current-state section of the active sprint (`backend/sprints/sprint-02.md` §A) is the record of implementation status, checked against Git.
6. **Older plans** (`sprints/frontend-auth-and-users-plan.md`, frontend `sprints/sprint-01.md`) — history. Where they disagree with the contract, the contract wins; their banners say so.

A newer file date never decides precedence by itself (same rule as the index).

## 2. Map of files

| File | Kind | Status |
|---|---|---|
| `roadmap.md` | **Master roadmap and sprint plan** (all apps): baseline, decision register, sprint sequence PS-03 onward, next-sprint handoff | Current (2026-10-21; revised the same day for the confirmed WhatsApp credential delivery R49–R51 and the PS-04a/PS-04b split; corrected 2026-10-22 for the one-password-per-operation retry design, which is recommended but not yet approved, OD-30 — roadmap §13). The one place for cross-application sequencing; sprint files hold the detail of one sprint and link here. It approves nothing. |
| `backend/features/auth-and-users-api-contract.md` | API contract | Current — v1.5 (Draft; owner confirmations in its §19; proposed v1.6 amendments in its §22, pending review and not applied). **Its §5.6, §7, §9.1, §9.6, §9.8, §10 and §10A credential design is superseded by owner decisions R49–R51 (2026-10-21; derived amendments in its §23, not yet applied); the text is to be rewritten in v1.6, which is planned (roadmap PS-03) and has not been issued.** |
| `backend/features/admin-user-creation-plan.md` | Decision record and plan | Current — §1D / §1B binding; §5 Slices 2, 3, 4, 6, 7 superseded. **§1F (2026-10-21) records the CONFIRMED owner decisions R49–R51** (system-generated password sent by WhatsApp for creation and reset, no forced change; they supersede §1E/R48); **§1G** holds the username policy — English-only confirmed, details Proposed; the contract's §23 carries the derived API shapes. None of it is implemented. |
| `backend/sprints/sprint-02.md` | **Backend** Sprint 2 | Active |
| `backend/sprints/sprint-01.md` | **Backend** Sprint 1 | Closed. Its header still says "Draft for review" because owner decision C1 forbids editing it; its closure is recorded in `sprint-02.md` §A. |
| `sprints/sprint-01.md` | **Frontend** Sprint 1 (Admin and Employee apps) | Closed; auth stories superseded by the contract (banner) |
| `sprints/frontend-auth-and-users-plan.md` | Frontend plan (mock-first) | History; superseded by the contract (banner) |
| `sprints/frontend-auth-contract-alignment-plan.md` | Frontend plan against contract v1.5 | Current; starts after the matching backend slices |
| `../decisions/0001` … `0004` | ADRs | Implemented as each states |
| `../decisions/0005` | ADR — Slice 2B persistence (sessions, refresh tokens, reset tickets, throttle store) | Implemented in Backend Sprint 2, Slice 2B (the ADR's own status line; its throttle **policy** remains unimplemented and pending review, ADR 0005 §8); status recorded in `backend/sprints/sprint-02.md` §A |
| `../decisions/0006` | Repository ADR 0006 (not business ADR-006) — Slice 3 audit persistence, owner decisions R46–R47 and those of 2026-10-19, Audit Completion slice and Gates A and B | Proposed — pending owner review; implemented in `50832d1` and `d5c4c26` (status recorded in `backend/sprints/sprint-02.md` §A) |

**Two files are called `sprint-01.md`**: the one under `backend/sprints/` is the backend sprint, the one under `sprints/` is the frontend sprint. Always cite the full path.

## 3. Implementation status is never inferred from a plan

A contract, plan or ADR describing something is not evidence that it exists. Status claims cite a commit or a test; anything else is "planned".

## 4. Historical statements about passwords

ADRs, sprint files and acceptance reviews describe what was decided or built **at their date**. Their statements that a new or reset account gets a temporary password, that the Admin is shown it, that a change is forced at the next sign-in, or that a reset code is used, are **superseded in those parts by owner decisions R49–R51** (2026-10-21, `backend/features/admin-user-creation-plan.md` §1F) and are not rewritten, in keeping with the history rule above. For the planned behaviour read the plan's §1F and the contract's §23; for what the code does today read the contract's v1.5 text and `backend/sprints/sprint-02.md` §A.
