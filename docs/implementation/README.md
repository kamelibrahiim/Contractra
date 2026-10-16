# Working documents — what they are and which one wins

`docs/implementation/**` (plans, sprints, API contracts) and `docs/decisions/**` (ADRs) are **working documents**. They are not listed in `docs/documentation-index-ar.md`, which ranks the authoritative business sources; this page gives their order. It never overrides that index.

## 1. Order when documents disagree

1. **Authoritative sources** in the order of `docs/documentation-index-ar.md` (confirmed business rules, addenda, specification, logical model, …). A working document that contradicts one of them has a defect; the source wins.
2. **Owner decisions** recorded in `backend/features/admin-user-creation-plan.md` (C1–C6 §1, R1–R43 §1B, R44–R45 §1D), and R46–R47 (2026-10-16, audit vocabulary and production-data gate) recorded in repository ADR 0006 §3 (`../decisions/0006-audit-events-persistence.md`). Between owner decisions, a later one that explicitly addresses the same point supersedes the earlier one; the earlier text stays as history.
3. **`backend/features/auth-and-users-api-contract.md`** — the current version (v1.5) is the source of truth for the auth and user-management API, for the backend and both Flutter apps. Its **[N] / [O] / [E]** tags say where each rule comes from; [N] beats [O] beats [E].
4. **ADRs** (`docs/decisions/`) — how a rule is implemented physically. An ADR records what was decided *at its date*; later changes are added as dated notes, never by rewriting the history.
5. **Sprint files** — what is planned, in progress and done. The current-state section of the active sprint (`backend/sprints/sprint-02.md` §A) is the record of implementation status, checked against Git.
6. **Older plans** (`sprints/frontend-auth-and-users-plan.md`, frontend `sprints/sprint-01.md`) — history. Where they disagree with the contract, the contract wins; their banners say so.

A newer file date never decides precedence by itself (same rule as the index).

## 2. Map of files

| File | Kind | Status |
|---|---|---|
| `backend/features/auth-and-users-api-contract.md` | API contract | Current — v1.5 (Draft; owner confirmations in its §19; proposed v1.6 amendments in its §22, pending review and not applied) |
| `backend/features/admin-user-creation-plan.md` | Decision record and plan | Current — §1D / §1B binding; §5 Slices 2, 3, 4, 6, 7 superseded |
| `backend/sprints/sprint-02.md` | **Backend** Sprint 2 | Active |
| `backend/sprints/sprint-01.md` | **Backend** Sprint 1 | Closed. Its header still says "Draft for review" because owner decision C1 forbids editing it; its closure is recorded in `sprint-02.md` §A. |
| `sprints/sprint-01.md` | **Frontend** Sprint 1 (Admin and Employee apps) | Closed; auth stories superseded by the contract (banner) |
| `sprints/frontend-auth-and-users-plan.md` | Frontend plan (mock-first) | History; superseded by the contract (banner) |
| `sprints/frontend-auth-contract-alignment-plan.md` | Frontend plan against contract v1.5 | Current; starts after the matching backend slices |
| `../decisions/0001` … `0004` | ADRs | Implemented as each states |
| `../decisions/0005` | ADR — Slice 2B persistence (sessions, refresh tokens, reset tickets, throttle store) | Accepted for implementation; status recorded in `backend/sprints/sprint-02.md` §A |
| `../decisions/0006` | Repository ADR 0006 (not business ADR-006) — Slice 3 audit persistence, owner decisions R46–R47, Audit Completion slice and Gates A and B | Proposed — pending owner review; not implemented |

**Two files are called `sprint-01.md`**: the one under `backend/sprints/` is the backend sprint, the one under `sprints/` is the frontend sprint. Always cite the full path.

## 3. Implementation status is never inferred from a plan

A contract, plan or ADR describing something is not evidence that it exists. Status claims cite a commit or a test; anything else is "planned".
