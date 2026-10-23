# Backend Sprint 3 (PS-03) — identity closure, credential-policy alignment and the delivery design

**Status:** 🟡 Backend tracks done on 2026-10-23 as **documents and verification only**; owner confirmation outstanding (see §2). Frontend Track F lives in the frontend worktrees and is **not** started here. Roadmap: `docs/implementation/roadmap.md` §7 PS-03. This file records PS-03's backend side; it approves nothing.
**Rule kept:** PS-03 adds no code for R49–R51 (all of it is PS-04a, `sprint-04.md`).

## 1. Task status (backend side)

| Task | Result | Evidence |
|---|---|---|
| PS3-BE-01 full suites on isolated PostgreSQL | Done 2026-10-23 | `sprint-02.md` §A "Verification pass (2026-10-23)": Domain 360, Application 216, Api.Tests 265, Infrastructure 588; `a97706a`, `fb0ad18` |
| PS3-BE-02 Slice 7 acceptance review | Done | `backend/reviews/slice-07-acceptance-review-2026-10-23.md` |
| PS3-BE-03 impact inventory | Done | §3 below |
| PS3-BE-09 stale `CLAUDE.md` state | **Not changed** — `CLAUDE.md` is the owner's file; the stale line ("no Application or API logic yet") is listed here for the owner to correct |
| PS3-X-02 decision outcomes recorded | Done, **provisional** | §2; plan §1H |
| PS3-X-05 contract v1.6 + acceptance rows | Done | contract §23.12 (rows with named PS-04a tests) |
| PS3-X-06 ADR 0007 | Done, **Proposed** | `docs/decisions/0007-credential-issuance-outbox-and-whatsapp-delivery.md` |
| PS3-X-07 username policy v1 + golden vectors | Done, **Proposed**; Arabic outputs need owner review | `backend/features/username-policy-v1.md` |
| PS3-X-08 Meta setup / early template check | **Not started** — requires the business owner (Business Portfolio, WABA, number, template submission). Nothing external was contacted. | §4 |
| PS3-X-09 draft `sprint-04.md` | Done | `backend/sprints/sprint-04.md` |
| PS3-X-03 / PS3-X-10 frontend alignment plan, consistency sweep | Alignment-plan update **not done** (frontend repo); sweep partly done in §3 (code) | — |

## 2. Decision-session record (provisional)

On 2026-10-23 the owner instructed: follow the roadmap's recommendations as provisional decisions and proceed. That is **not** the decision session; each row stays "to confirm" until the owner records it. "PS-04a builds" is the default if nothing changes.

| ID | Provisional outcome | PS-04a builds |
|---|---|---|
| OD-1 | (a) allow Admin reset of other Admins/`Payroll.View` holders (audited risk); (b) no self-reset; (c) bootstrap excluded; (d) number change is a separate audited action — **deferred** | Reset for non-bootstrap, non-self accounts; no number-change endpoint |
| OD-2 | Remove reset-code subsystem from the v1.6 API surface; keep code/tables dormant; operator `reset-bootstrap-password` stays | Unmap `POST /auth/password-reset/complete`; keep the rest |
| OD-3 | Accept E8 as written | Unchanged |
| OD-4 | Seed `Parties.View`/`Parties.Manage`; Admin may create a minimal person Party inline | `PS4-BE-01..03` |
| OD-18 | 5 attempts, exponential back-off with jitter from 1 min; ≥ 60 s between re-issues, ≤ 5 per 24 h; sealed-copy retention 24 h (configuration) | As proposed |
| OD-19 | Confirm plan §1G as `username-policy-v1.md`; opaque `LoginIdentifier` kept; creation-time validator | Validator, suggestions endpoint |
| OD-20 | Recommended (a) correction while never signed in | **Default (c)**: not built until confirmed |
| OD-21 | Party phone as editable default, snapshotted; E.164; number entered twice (UI); consent basis `written`/`verbal` | As proposed |
| OD-22 | Keep delivery/consent rows with the account history; no raw payloads | As proposed |
| OD-23 | Owner-led Meta setup | **Not started**; PS-04a uses the fake sender |
| OD-25 | No expiry (R49/R50) | No expiry |
| OD-26 | Recommended: all API-created accounts, number required, contact record for non-Engineers | **Engineers only** until confirmed (roadmap default); other roles → 400 `validation.failed` |
| OD-27 | No in-band fallback | None |
| OD-28 | Short addendum to protected documents — **not edited** (protected) | — |
| OD-29 | (a) immediate invalidation, stated on the Admin screen | (a) |
| OD-30 | Option A with secret-manager/KMS key | Development key + fake sender only; **not for production** |

## 3. Impact inventory (PS3-BE-03, read-only)

Every code path embedding behaviour that R49–R51 supersede, and what PS-04a does to it. (Test files are listed with their production file.)

| Superseded behaviour | Where | PS-04a change |
|---|---|---|
| Temporary password in create response, `temporaryPasswordExpiresAt` | `UsersEndpoints.CreateAsync`; `CreateUserResult`; `CreateUserUseCase`; tests `CreateUserUseCaseTests`, `UsersEndpointTests` | New create use case; response carries `credentialDelivery`, no password |
| `must_change_password = true` at creation/reset; 72 h expiry | `User.cs` (credential state), `UserConfiguration`, `ITemporaryPasswordGenerator`, `LoginUseCase`/`ChangePasswordUseCase` (expiry paths) | Creation/reset never set it; gate stays dormant and tested |
| `initial-password` endpoint and `ReissueInitialPasswordUseCase`, `User.ReissueTemporaryPassword`, audit `InitialPasswordReissued` | `UsersEndpoints.ReissueInitialPasswordAsync`, use case + result/request, `UserCredentialTests`, `UsersEndpointTests` | Endpoint unmapped (404); use case code retired; audit code stays valid (history) |
| Reset via ticket + code: `IssuePasswordResetUseCase`, `User.BeginPasswordReset`, `PasswordResetTicket*`, `IPasswordResetDelivery`, operator `issue-password-reset` | Application, Domain, Infrastructure persistence, `OperatorCommandRunner` | Reset endpoint re-pointed to the new use case; ticket machinery dormant, not deleted |
| `POST /auth/password-reset/complete` and its throttle layer | `AuthEndpoints`, `ThrottledPasswordResetCompletion`, `CompletePasswordResetUseCase` | Unmapped from the API surface (OD-2); code dormant |
| `credentialState` values `TemporaryPasswordIssued` / `InitialPasswordRequired` / `ResetPending` / `ResetRequired` | `CredentialStateRule`, `UserDirectory`, `GetUserQuery`, `ListUsersQuery`, tests | Add `DeliveryPending` / `DeliveryFailed`; legacy values still produced for legacy rows |
| `SecretLeakTests` aimed at create / initial re-issue | `Contractra.Api.Tests` | Re-aimed at create, reset, worker, webhook, status endpoint |
| Contract §9.1, §9.6, §9.8, §10, §10A, §16 items 12/18, `12345678` | `auth-and-users-api-contract.md` | §23 + acceptance rows (contract §23.12) |

## 4. Meta checklist (PS3-X-08) — status: not started

| Item | Owner | Status |
|---|---|---|
| Business Portfolio, business verification | business owner (to be named) | not started |
| WABA and business number | business owner | not started |
| Arabic template drafted and submitted early (does Meta accept a credential-bearing template, which category?) | business owner + engineering | not started; wording to be drafted by the owner |
| System-user token, app secret, webhook verify token in a secret store | engineering | not started |
| Public HTTPS webhook endpoint | engineering/hosting (OD-15) | not started |
| Verified unit price recorded | business owner | not started |

No external system was contacted and no claim is made about Meta's acceptance of the template.

## 5. Updates after PS-04a backend work (2026-10-24)

- **OD-26 built as recommended**, not the Engineers-only default: every API-created account uses the credential flow; the number lives on the issuance (no contact table). **OD-2** is only partly done: the reset-code endpoint `POST /auth/password-reset/complete` is still mapped (dormant) because removing it means rewriting its endpoint and throttling tests; it is not removed yet.
- **Not done and why:** frontend tracks (other worktrees); Meta setup, template, live sender, real key custody (owner: OD-23, OD-30); the emergency `purge-sealed-credentials` operator command (ADR 0007 section 5); Party endpoints and the `Parties.View/Manage` seed (PS4-BE-01..03); OD-1(d) number-change action; OD-20 username correction; `must_change_password` gate remains dormant.
- Contract status and the named tests per endpoint: `auth-and-users-api-contract.md` section 23.12.
