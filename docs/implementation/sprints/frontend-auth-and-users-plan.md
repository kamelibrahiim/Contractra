# Frontend Plan — Login, Forced Password Change, Admin User Management

**Status:** Draft — awaiting approval. No code has been written for this plan yet.
**Doc status:** Derived planning document, not authoritative for business rules. If it conflicts with any source ranked in `docs/documentation-index-ar.md`, the source wins.
**Scope:** `apps/contractra_admin`, `apps/contractra_employee`. No backend changes.
**Tracked in:** `sprint-01.md`, extension stories US-008 to US-012 (TASK-023 to TASK-040).

---

## 1. Current state (verified 2026-09-26)

| Area | State |
|---|---|
| API contract `docs/implementation/backend/features/auth-and-users-api-contract.md` | **Does not exist.** The `features/` folder does not exist either. |
| Backend auth/users API | **Not implemented.** `Contractra.Api` has no controllers and no mapped endpoints. The backend has `User` persistence, dated role assignments, direct permission grants and user–party links, but the backend Sprint 1 excludes authentication (`backend/sprints/sprint-01.md` §69, §124). |
| Frontend Sprint 1 (US-001 to US-007) | Implemented in both apps, **uncommitted**. Includes the shared widgets in **both** apps (the prompt's note that the admin app has none is out of date), the navigation shells, a stub `LoginCubit` with the login layouts, and the Party Directory. |
| `lib/core/network` | Empty (`.gitkeep`). `dio` and `fast_dio` are already dependencies; neither is used. |
| Test tooling | `flutter_test` and `bloc_test` (added in Sprint 1). |

**Consequence:** following the prompt's fallback rule, everything is built against a **mock repository** behind the same interface as a Dio-backed implementation. The Dio implementation is written to the endpoint list in the prompt, but it **cannot be verified against a real server** until the contract and the backend exist.

---

## 2. Gaps between the prompt and the documentation

The prompt says these gaps must be reported, not worked around. Items marked **Decision needed** block nothing in mock mode, but they must be confirmed before the real integration.

| # | Gap | Source check | Proposed handling |
|---|---|---|---|
| G1 | **No API contract file.** | Missing. | Mock-first (per the prompt). Endpoints, DTO shapes and error codes come from the prompt and are kept in one file per repository, so aligning with the contract later is a local change. |
| G2 | **Default password `12345678`, `mustChangePassword`, and the forced change on first login are not documented** in any authoritative doc (searched `docs/**/*.md`). | Prompt only. | **Decision needed.** Implemented as specified by the prompt. The UI shows the default password **only** in the post-create and post-reset dialogs and hardcodes it nowhere else. Security note: one well-known default for every new or reset account is a weak point until the first login. A per-user temporary password is safer. That is a backend decision, flagged here and not changed. |
| G3 | **Token model** (30-minute access token, 7-day rotating refresh token, `/auth/refresh`, `/auth/logout`, `/auth/me`) is not documented. The backend sprint lists the token/session model as an open design decision. | Prompt only. | Implemented as specified by the prompt, behind the `AuthRepository` interface and the interceptor. |
| G4 | **Endpoints `reactivate` and `reset-password` are not documented.** FR-AUTH-004 documents disable only ("يعطّل … ولا يحذفه"). | Prompt only. | Implemented as specified by the prompt. There is **no delete** action anywhere (FR-AUTH-004, UC-002 exception flow). |
| G5 | **The `login_identifier` format is undecided** (username, email or phone). Backend sprint §H is marked `BLOCKED / REQUIRES BUSINESS DECISION`. | Open. | The client checks only that the field is not empty and applies no format validation. Label: "المعرّف". |
| G6 | **FR-AUTH-003 requires at least one role per user**, but the backend domain deliberately does not enforce it (ADR 0003 §5). | Documented FR. | The new-user form enforces at least one role on the client **and** maps the server's `users.role_required`. |
| G7 | **Which roles may use the Employee app** is not documented (§7.1 frames it as the Engineer's tool). | Open. | Any authenticated user can sign in. No role gate is added, because none is documented. **Decision needed.** |
| G8 | The prompt says the Employee app should "land on the existing home placeholder". The `home` module is empty. The Employee landing screen since Sprint 1 is the bottom-nav shell (My Sites). | Code. | After login, land on the shell at `/assignments`. |
| G9 | **Web token storage.** `flutter_secure_storage` on web uses WebCrypto with a key in `localStorage`, which is not protected against XSS. | Platform. | Use it on all platforms and document the trade-off in code. The admin app is web-first, so this matters: keep the access token in memory only and persist just the refresh token. **Decision needed** if an HttpOnly-cookie refresh flow is preferred; that would need a backend change. |
| G10 | **Error code catalogue.** Only the codes in the prompt are known. | Prompt only. | Map known codes to Arabic messages. Unknown codes fall back to a generic message and are never shown raw. |
| G11 | Sprint 1's constraint "no task calls a real API" and US-005's "stub cubit" are superseded by this work. | `sprint-01.md`. | Recorded as extension stories in `sprint-01.md`. The stub cubit is replaced, not kept alongside. |
| G12 | Users are listed through `Users.Manage`. UC-002 also lists `Permissions.Grant` (granting fine-grained permissions, `Payroll.View`). | UC-002. | Out of scope for this task: no permission-grant UI. Role assignment only happens at creation, as the prompt specifies. |

---

## 3. Packages to add (both apps unless noted)

| Package | Kind | Why |
|---|---|---|
| `flutter_secure_storage` | runtime | Token storage (see G9). |
| `mocktail` | dev | Mocking `Dio`, repositories and storage in unit tests. |

`dio` is already present. `fast_dio` remains unused. It is not removed, to keep scope small.

---

## 4. Architecture

```
lib/core/config/app_config.dart          API_BASE_URL + USE_MOCK_API via --dart-define (dev / prod)
lib/core/network/
  api_client.dart                        Dio factory (base URL, timeouts, JSON)
  auth_interceptor.dart                  attach access token · on 401 refresh once (single-flight) · retry · else logout
  token_storage.dart                     interface + secure-storage impl + in-memory impl for tests
  failures.dart                          sealed Failure types
  problem_details_mapper.dart            ProblemDetails `code` → typed Failure
lib/core/session/
  session_cubit.dart / session_state.dart  unknown → unauthenticated → authenticating → authenticated | passwordChangeRequired
lib/module/auth/
  login/        cubit · presentation (existing layouts, wired to SessionCubit via LoginCubit)
  change_password/ cubit · presentation
  data_layer/   models (Session, CurrentUser) · repository (AuthRepository, DioAuthRepository, MockAuthRepository) · web_services (AuthApi)
lib/module/users/            (admin only)
  cubit/        users_list_cubit · user_form_cubit · user_actions_cubit
  data_layer/   models (UserSummary, UserDetails, RoleOption, Page) · repository (UsersRepository, Dio…, Mock…) · web_services (UsersApi)
  presentation/ screens_layout (users_list_screen, new_user_screen, user_details_screen) · widgets (users_table, user_status_badge, confirm_dialog, default_password_dialog)
```

**Router guards.** A single `redirect` driven by `SessionCubit` (exposed to `GoRouter` as a `refreshListenable`):

- `unknown` goes to splash.
- `unauthenticated` goes to `/login`.
- `passwordChangeRequired` goes to `/change-password`; any other route redirects back there, and logout is the only way out.
- `authenticated` on `/login` or `/change-password` goes to the landing route.
- Admin only: `/users/**` without `Users.Manage` goes to the landing route, and the nav item is hidden.

**Mock mode** (`USE_MOCK_API=true`, the default until the backend exists) seeds in-memory users. The seed data is clearly marked as a placeholder and simulates `mustChangePassword`, `auth.password_change_required`, disabled accounts and paging, so every flow can be exercised without a server.

---

## 5. Ordered steps

Each step ends with `flutter analyze` and `flutter test` green in the affected apps, a short report, and one conventional commit (no push).

| Step | Story / Tasks | Files (main) | Tests |
|---|---|---|---|
| **0** | Commit the existing Sprint 1 work (US-001 to US-007), one commit per story, so this task starts from a clean base. | — | existing |
| **1** | US-008 · TASK-023…026: config, Dio client, token storage, ProblemDetails → failures, auth interceptor. Both apps. | `lib/core/config/*`, `lib/core/network/*`, `pubspec.yaml` | interceptor: token attached · 401 → one refresh → retry · concurrent 401s share one refresh · refresh fails → session cleared. Mapper: every known code. |
| **2** | US-009 · TASK-027…030: `AuthRepository` (Dio + Mock), `SessionCubit`, router guards, splash restores the session. Both apps. | `lib/module/auth/data_layer/**`, `lib/core/session/*`, `router_config.dart`, `splash_screen.dart` | repository with mocked Dio · SessionCubit transitions · guard redirect table (every state × protected/public route). |
| **3** | US-010 · TASK-031…032: login wired to the repository; replaces the stub `LoginCubit` from TASK-015 and TASK-018. Generic invalid-credentials message. Both apps. | `lib/module/auth/login/**` | cubit (bloc_test) · widget: loading, generic error, disabled-account message. |
| **4** | US-010 · TASK-033…034: change-password screen and cubit (min 8, ≠ `12345678`, confirm matches, server errors), plus logout (admin: shell header menu; employee: Profile tab). Both apps. | `lib/module/auth/change_password/**`, `app_shell_screen.dart`, profile placeholder | cubit · widget: each client rule, server error, success → landing · logout clears the session. |
| **5** | US-011 · TASK-035…037: `UsersRepository` (Dio + Mock), users list (table, search, status filter, pagination, empty/loading/error states), and the "المستخدمين" nav item gated by `Users.Manage`. Admin only. | `lib/module/users/**`, `app_shell_screen.dart`, `router_config.dart` | repository with mocked Dio · list cubit (search/filter/page) · nav hidden without the permission. |
| **6** | US-012 · TASK-038…040: new-user form (identifier + role multi-select from `GET /api/roles`, at least one role, no password field) with the default-password dialog; user details with disable / reactivate / reset-password behind confirmation dialogs. Admin only. | `lib/module/users/**` | form cubit + widget (role required, identifier taken, success dialog) · actions cubit (each action + confirm/cancel). |

---

## 6. Risks and open questions

1. **G2, G3, G4, G9 need confirmation** before the Dio implementations are switched on against a real backend.
2. When the contract lands, DTO field names and error codes may differ from the prompt. Each is isolated in `*_api.dart` and `problem_details_mapper.dart`.
3. The users table on narrow widths: the admin app is web-first, so below 600 px the table switches to a card list. This is a layout decision, not a new feature.
4. Existing Sprint 1 work is uncommitted. Step 0 commits it first so this task's commits stay focused.
