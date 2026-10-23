# Slice 7 (US-BE-013) — acceptance review, 2026-10-23

**Scope:** `/api/v1/users` (8 routes) and `/api/v1/roles`, commit `1644077` plus fixes `a97706a`, `fb0ad18`; contract v1.5 §9. **Not an acceptance decision** — the owner accepts or defers. Evidence: `sprint-02.md` §A "Verification pass (2026-10-23)" (build 0/0; Domain 360, Application 216, Api.Tests 265, Infrastructure 588 on PostgreSQL 18.6).

## 1. Requirement → code → test → status

| Contract §9 requirement | Code | Test | Status |
|---|---|---|---|
| Check order 1–4 (authn, password-change gate, audience `admin`, `Users.Manage` → `AccessDenied`) before any handler | `UsersEndpoints.MapUsersEndpoints` (`WithAudience`, `RequiresPermission`) + `AudienceGateMiddleware` | `UsersEndpointTests`, `AudienceAndGateTests` | Met |
| Step 5 target exists → 404 `users.not_found`; non-GUID `{id}` → 404 | `TryParseId`, use cases | `UsersEndpointTests` | Met (non-GUID is an engineering choice) |
| Step 6 bootstrap protection (409) before state checks, for disable / reactivate / reset / initial-password | Use cases test `IsBootstrapAdmin` before `Status` / `MustChangePassword` | `UsersEndpointTests`, use-case tests | Met |
| Step 7 validation and state conflicts (`already_disabled`, `already_active`, `initial_password_pending`, `initial_password_not_reissuable`) | Use cases | Use-case + endpoint tests | Met |
| 201 + `Location` + `Cache-Control: no-store` on create; `no-store` on every response | `CreateAsync` and the other handlers | `UsersEndpointTests` | Met |
| Unique-index races map to 409 | `UniqueViolationCode` | `UsersEndpointTests` | Met |
| `GET /users` paging, search, status filter, sort | `ListAsync`, `ListUsersQuery` | `UsersEndpointTests`, `UserQueriesTests` | Met |
| Reset returns only `channel`, `expiresAt` | `ResetPasswordAsync` | `UsersEndpointTests`, `SecretLeakTests` | Met — **no endpoint returns a reset code/secret** |
| `GET /roles` active roles sorted, `{code}` only | `ListRolesAsync` | `UsersEndpointTests` | Met |

## 2. Deviations (engineering choices not in the contract)

1. Out-of-range `page`/`pageSize`, unknown `status`, `search` > 256 characters → 400 `validation.failed` (not clamped).
2. Non-GUID route id → 404 `users.not_found`.
3. Wrong-typed body field → 400 `validation.failed`; absent field reaches the use case (`login_identifier_invalid`, `role_required`).
4. NUL characters in `search`, `loginIdentifier`, role codes → 400 (previously a 500; fixed in the Slice 7 review).
5. Reset returns 202 even if post-commit delivery fails (§9.6 step 6 defines no other outcome; **OC-2**, owner decision).

## 3. Defects found in this pass (fixed)

- `a97706a` — `IUserAccessReader` was registered only by `AddAuthInfrastructure`; the operator-command host could not resolve the user use cases (12 integration tests failed on it).
- `fb0ad18` — test race in `ConcurrentCompletionsOfOneTicket_ExactlyOneConsumesIt`.

## 4. Behaviour that R49–R51 replace (accept Slice 7 knowing it will change)

Create still returns `temporaryPassword`; `initial-password` still returns one; `reset-password` still uses the v1.5 reset-code design (503 until a channel exists); `credentialState` still uses the v1.5 values. All are superseded by contract §23 and are rebuilt in PS-04a (`sprint-04.md`).

## 5. Open items for the owner

OC-2 (post-commit delivery failure outcome), OC-4 (Party validity, E8), OC-7 (no Party lookup; addressed by PS4-BE-02), plus the Slice 6 deferred items (OC-8, OC-9, E27, E33/E36, `AccessDenied` target, slow-mode alerting) — unchanged. One unexplained, non-reproduced run of 115 Infrastructure integration failures (~1 ms each) is recorded in `sprint-02.md` §A.
