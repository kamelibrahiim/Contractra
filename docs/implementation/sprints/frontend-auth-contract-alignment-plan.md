# Frontend Plan — Aligning both apps with auth contract v1.5

**Status:** Planned (2026-10-15). No app code has been changed for this plan.
**Doc status:** Derived planning document; the contract (`docs/implementation/backend/features/auth-and-users-api-contract.md`, v1.5) is the source of truth, and its §16 is the checklist this plan orders. Precedence of working documents: `docs/implementation/README.md`.
**Scope:** `apps/contractra_admin`, `apps/contractra_employee`, and the planned shared package `packages/contractra_core`. No backend changes.
**Replaces for future work:** `frontend-auth-and-users-plan.md` (mock-first, pre-contract; kept as history).

> **Update 2026-10-21.** (1) Scheduling of the phases below now lives in `docs/implementation/roadmap.md` (FA-0 … FA-3 and FA-5 in mock mode → PS-03; real integration FA-4/FA-6 → PS-04). This file remains the detailed phase checklist. (2) The state table in §1 was re-checked and still holds: both apps use `/api/auth/*` without `/v1`, still contain `12345678`, and no frontend commit exists after `78de706`. (3) **Owner decisions R49–R51 (2026-10-21, `admin-user-creation-plan.md` §1F; they supersede the earlier R48)** change what is built in FA-3, FA-4 and FA-5: the system generates the password and sends the username and password to the employee's WhatsApp, for creation and for reset; **no screen, dialog, clipboard action or log in either app ever shows a password**; **no forced-change screen and no first-login prompt** (voluntary change-password stays); the Admin create form gains the employee's name, an optional Latin spelling, a server-suggested editable English username, the WhatsApp number entered twice and a consent checkbox; the Admin gets a delivery-status panel (timeline, failure category, stalled flag, re-issue) after create and after reset; the users list gets `credentialState` and a "needs attention" filter. The public "I have a reset code" screen (FA-3, FA-5) is **not to be built** (roadmap OD-2), and there is **no activation route** (the activation-link idea was withdrawn). Contract v1.6 restates §16 items 12 and 18 accordingly (contract §23.10). (4) Scheduling: roadmap PS-03 (decisions, contract shapes, mock mode) → PS-04a (real, fake sender) → PS-04b (live Meta). Username-policy details beyond "English-only, suggested from the name, editable" stay Proposed until the owner confirms roadmap OD-19.

---

## 1. Current state (verified 2026-10-15 against the code)

| Area | Admin app | Employee app |
|---|---|---|
| Base paths | `/api/auth/*`, `/api/users*`, `/api/roles` — **no `/v1`** (`auth_api.dart`, `users_api.dart`) | `/api/auth/*` — no `/v1` |
| Login body | `loginIdentifier`, `password` — **no `clientApp`** | same |
| Headers | No `X-Client-Platform`, `X-CSRF-Protection`, `X-Correlation-ID` | same |
| Token storage | Access token in memory; **refresh token in `flutter_secure_storage` on every platform, web included** (contract: cookie on web) | Access in memory, refresh in secure storage (matches contract for mobile) |
| Refresh | `DioAuthRepository.refresh` casts `data['refreshToken'] as String` — breaks on web (cookie) and inside the 30-s grace (no new token) | casts the same way — breaks inside the grace |
| 401 handling | `AuthInterceptor`: one shared refresh, retry once; requests flagged `isAuthRequest` (login, refresh) never refresh. `logout` is not flagged, so a 401 on logout triggers a refresh (contract: never) | same |
| Change password | `defaultPassword = '12345678'` rule; response tokens discarded; route reachable only when forced | same |
| Users (Admin only) | Create shows the default-password dialog; reset shows it too, even on failure; no `partyId`; `RoleOption` expects a display name | — |
| Mocks | `USE_MOCK_API` defaults to `true`; mock accounts use `12345678`; mock returns `users.disabled` | same |
| Shared code | Networking and auth data layer duplicated byte-for-byte in both apps. `packages/contractra_core` exists on disk with an empty skeleton but is **ignored by Git** (`.gitignore` line 208, `**/[Pp]ackages/*` from the Visual Studio template), so nothing placed there would be committed. | same |

## 2. Principles

- **The server decides.** Route guards and hidden buttons are convenience only; every permission, app-audience, bootstrap and password rule is enforced by the backend (contract §4.1, §9, §11). The apps must handle the server's refusal even when the UI should have prevented the call.
- **One change, one place.** Contract changes to networking and the auth data layer are made in `contractra_core` once it exists (FA-0), never twice by hand.
- **No cross-contamination.** The Admin app keeps the users module; the Employee app never gains admin screens. Shared code is limited to networking, session and auth data — no UI, router, theme or i18n.
- **Mocks follow the contract.** Mock repositories are updated to the v1.5 shapes and codes in the same step as the real ones, so mock mode stays a faithful rehearsal.
- **Backend first for real integration.** A phase that calls a real endpoint starts only after that endpoint exists and passes its own tests (backend Slices 6–7).

## 3. Phases

| Phase | Content | Starts when | Done when |
|---|---|---|---|
| **FA-0 Shared core** | (1) Fix `.gitignore` so `packages/contractra_core/` is tracked (narrow exception such as `!/packages/contractra_core/`, keeping the NuGet rule for everything else). (2) Do the behaviour-preserving extraction described in `claude-code-prompt-04-frontend-core-package.md` (FE-1): move only byte-identical networking/auth-data code, with its unit tests. | Now (independent of the backend) | Both apps build, analyze clean, test counts unchanged; `git status` shows the package as tracked |
| **FA-1 Networking** | In `contractra_core`: `/api/v1` prefix in one place; `X-Correlation-ID` per user action (UUID) and surfacing the server's ID in error UI; `X-Client-Platform` (`web` only for the Admin browser build, `mobile` otherwise); ProblemDetails mapper for every v1.5 code (§12) including 429 with `Retry-After`; "never refresh" for login, refresh, logout and reset completion; refresh-once for `/auth/me` and change-password. | After FA-0; mock-only until Slice 6 | Unit tests per code and per rule (see §4) |
| **FA-2 Session and tokens** | Mobile: refresh token in secure storage. **Web: no stored refresh token**; Dio web adapter with credentials so the HttpOnly cookie flows; `X-CSRF-Protection: 1` on refresh and logout. Accept a refresh response without `refreshToken` (grace) and keep the old one. Store the tokens returned by change-password (§5.5). Restore at app start only; never refresh on a timer. On 403 `auth.app_access_denied` from a normal call, clear the session and return to login with a message. | Mock now; real after Slice 6 | Session tests for both platforms; web-cookie flow verified against the real backend |
| **FA-3 Auth screens (both apps)** | Login sends `clientApp` (`admin` / `employee`); drop the "account disabled" message; generic invalid-credentials text; 429 lockout-free UI (disable submit for `Retry-After`). Change password: 8–128 characters, different from current; remove `defaultPassword`; route reachable voluntarily by any signed-in user. **New public screen "I have a reset code"** (identifier, code in 4×4 groups, new password, confirm) → `POST /auth/password-reset/complete`, then back to login. | Mock now; real after Slice 6 | Widget tests for each rule and code |
| **FA-4 Admin user management** | Users list with `credentialState` badge; create form with Arabic role labels from codes, temporary password **and its expiry** shown once; reset action → 202 "code sent via <channel>, valid until …" or 503 "resets are done by the system operator for now" — **never a password dialog**; initial-password re-issue only for `TemporaryPasswordIssued` / `InitialPasswordRequired`; disable / reactivate; hide protected actions when `isBootstrapAdmin`, still handle 409. **Engineer creation needs a Party picker, which is blocked**: the contract has no Party lookup endpoint yet (contract §19 OC-7). Until it exists, the form cannot offer Engineer creation except with a known Party id. | Mock now; real after Slice 7 (and OC-7 for the picker) | Widget tests for every action and code; no test asserts `12345678` |
| **FA-5 Employee app** | Login as `employee`; profile/logout; reset completion screen; handle `auth.app_access_denied` (account without `Apps.Employee.Access`). No admin features. | Mock now; real after Slice 6 | Widget tests |
| **FA-6 Real integration** | Builds with `USE_MOCK_API=false` and `API_BASE_URL` per environment; a scripted run against a local backend (ephemeral database, seeded bootstrap admin from Slice 5) covering: create user → first login → forced change; reset issued via operator command → completion → login; disable → refresh fails; app-audience refusal; throttling. Mock mode stays available for UI work. | After Slices 5–7 | The scripted run passes for both apps, web and Android |

## 4. Tests that must exist when each phase closes

- Interceptor: token attached; one shared refresh for concurrent 401s; no refresh on login/refresh/logout/reset completion; refresh once on `/auth/me` and change-password; 429 never retried; correlation ID present on every request.
- Mapper: one test per v1.5 code (§12), including `reason` on `users.party_invalid` and `Retry-After` on 429.
- Session: web never writes a refresh token to storage; mobile does; grace response keeps the old token; change-password stores new tokens; `app_access_denied` clears the session.
- Screens: the change-password rules; reset completion success and each error; Admin reset 202 and 503 paths; initial re-issue visibility by `credentialState`; bootstrap actions hidden yet 409 handled.
- A repository-wide check (test or CI grep) that `12345678` appears nowhere under `apps/` or `packages/`.

## 5. Must not change

- The backend, the contract, or `docs/` outside `docs/implementation/**` — mismatches found while implementing are reported for a contract revision.
- Unrelated UI, theme, i18n or modules in either app.
- The separation of the two apps (no shared UI package, no admin code in the Employee app).

## 6. Open dependencies

| Dependency | Blocks |
|---|---|
| Backend Slice 6 (auth endpoints) | Real-mode FA-1 … FA-3, FA-5 |
| Backend Slice 7 (users endpoints) | Real-mode FA-4 |
| Backend Slice 5 (seeder, operator commands) | FA-6 end-to-end run |
| Contract §19 OC-1 (reset policy) | Final wording of the Admin reset messages in FA-4 |
| Contract §19 OC-2 (delivery channel) | A real "code sent via …" path; until then the 503 path is the only real one |
| Contract §19 OC-7 (Party lookup and permission) | The Engineer Party picker in FA-4 |
