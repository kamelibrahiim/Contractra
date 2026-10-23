# ADR 0007 — Credential issuance, transactional outbox, sealed temporary copy and WhatsApp delivery

**Status:** Proposed (2026-10-23); backend implemented 2026-10-24 (commit `8e8b68a`, fake sender only). Written for owner/engineering review before PS-04a code is merged. Nothing in this ADR is applied to any shared database.
**Kind:** working document under `docs/decisions/**` (editable; not ranked by `docs/documentation-index-ar.md`).
**Implements:** owner decisions R49–R51 (plan §1F; contract §23). **Open owner items it depends on:** OD-18, OD-29, OD-30 (see §11). Where an item is undecided, this ADR states the default PS-04a builds, recorded provisionally on 2026-10-23 as "recommendation adopted by owner instruction; to be confirmed in the decision session".

## 1. Context

R49/R50 require the system to generate an employee's password and send the username and password to the employee's WhatsApp, with no forced change. R51 forbids plaintext storage, logging, auditing and any password in an ordinary API response, and requires safe retry and re-issue. The Admin never sees a password.

Retries must resend the *same* password: a new password per attempt lets a delayed earlier message carry a password that a later attempt already replaced (rejected, §9).

## 2. Decision summary

1. **One password per operation** (creation, reset or re-issue). Generated once, in the operation's transaction, from the existing CSPRNG generator.
2. Its **hash** is stored in the user record in that same transaction. The plaintext is held in a `SensitivePassword` value only for the duration of the transaction.
3. A **sealed copy** (AEAD) is written to the delivery outbox row in the same transaction. Only the delivery worker can `Open` it, only when about to send.
4. Retries of an operation send the same password. A retry never touches the user record.
5. The sealed copy is **purged** at the earliest terminal event or deadline; a CHECK makes "terminal ⇒ no ciphertext" a constraint.
6. If delivery is disabled/misconfigured **or the sealing key is absent**, create and reset return **503 `users.credential_delivery_unavailable`** and change nothing. No silent fallback.
7. **No new audit action codes** (reuse `UserCreated`, `RoleAssigned`, `PasswordResetIssued`).

## 3. Domain model

- `CredentialIssuance` — `id`, `user_id`, `kind` (`Initial` | `Reset`), `status` (`Open` | `Delivered` | `Failed` | `Superseded` | `Cancelled`), `issued_by_user_id`, `issued_at`, `ended_at`, `failure_category`. At most **one `Open` issuance per user** (partial unique index).
- **Implemented as one table, `credential_issuances`** (the delivery columns live on the issuance row; the 1:1 split below was folded together). Delivery columns - retries are attempts on the row: `issuance_id` (PK/FK), `status` (`Queued` | `Sending` | `Accepted` | `Sent` | `Delivered` | `Read` | `Failed` | `Unknown` | `Superseded` | `Cancelled`), `destination` (E.164 snapshot), `attempts`, `next_attempt_at`, `lease_owner`, `lease_until`, `provider_message_id`, `provider_status_at`, `failure_category`, **`sealed_bytes`, `nonce`, `key_id`, `sealed_at`, `purge_by`, `purged_at`**.
- `WhatsAppConsent` columns on the delivery destination record: affirmed-by user, affirmed-at, basis (`written` | `verbal`).
- `WhatsAppWebhookEvent` — `provider_message_id`, `status`, `provider_timestamp`, `error_code`, `error_title`; unique on the first three.
- **No plaintext column and no duplicate of the hash anywhere.**
- `CredentialStateRule` derives `PasswordSet` | `DeliveryPending` | `DeliveryFailed` from the user's **latest issuance** (contract §23.1a). A failure never reverts the stored hash.

## 4. Sealer

- **Port:** `ICredentialSealer { SealedSecret Seal(SensitivePassword, SealContext); SensitivePassword Open(SealedSecret, SealContext); }`. The request path is given only an `ICredentialSealing` view with `Seal`; `Open` is on a separate `ICredentialOpening` interface registered for the worker only.
- **Cipher:** AES-256-GCM (`System.Security.Cryptography.AesGcm`), 96-bit random nonce per record, 128-bit tag. Rejected: ASP.NET Data Protection (its key ring lives in a store we would have to keep outside the database anyway, with less control of key ids and rotation than the JWT-style ring we already operate).
- **Associated data:** `"contractra/credential-seal/v1" || 0x00 || issuance_id || user_id || key_id` (fixed-width binary). A ciphertext copied to another row, user or purpose fails authentication.
- **Stored bytes:** `ciphertext || tag`; nonce and key id in their own columns.
- **Key ring** (`CredentialSealing:Keys:<kid>` base64, `CredentialSealing:ActiveKeyId`), modelled on the JWT key ring (contract §3.2.1, E9): one active key, older keys decrypt-only; at least 256 random bits; loaded from user-secrets / environment / secret manager; never from the application database. Absent active key ⇒ the sealer reports *unavailable* and create/reset return 503.
- **Wrapper types:** `SensitivePassword` (no `ToString`, no serialization, no value equality printing) and `SealedSecret` (redacting `ToString`). Architecture tests fail if either appears in a DTO, ProblemDetails or log template.

## 5. Key custody, rotation and destruction

| Topic | Rule |
|---|---|
| Location | Outside the database and source. Development: user-secrets. Production: secret manager or KMS-backed store (**OD-30, owner decision pending**). Until decided PS-04a is delivered with a development key and the fake sender; **no production use**. |
| Access | API host runtime identity only. Migration/DB-admin accounts, operators and support cannot read it. No endpoint, health check, metric or log reveals more than a key id. |
| Rotation | Add a new key as active; older keys stay decrypt-only until the longest retention deadline has passed since retirement, then are destroyed. No re-encryption needed (copies live ≤ retention). |
| Destruction | Destroying a retired key makes ciphertext in old backups unreadable; the destruction date, not the backup expiry, bounds recoverability. |
| Emergency rotation | Operator command `purge-sealed-credentials`: (1) null every sealed copy, (2) mark every open issuance `Failed(key_unavailable)`, (3) replace the stored hash of every affected account with the unusable marker, (4) key replaced out of band; Admin re-issues. |

## 6. Retention and purge

- `purge_by = queued_at + SealedCopyRetention` (default **24 h**, configuration; OD-18).
- Purge (UPDATE nulling `sealed_bytes`, `nonce`, `key_id`; set `purged_at`) in the **same transaction** as the state change, at the earliest of: provider `Delivered`/`Read`; the user's first sign-in or password change after `issued_at`; terminal failure; supersession or cancellation; `purge_by`.
- `Accepted`/`Sent` alone do **not** purge (an asynchronous failure may follow).
- A hosted **sweeper** enforces `purge_by` (marks `Failed(expired_before_delivery)`) and catches rows the worker missed.
- CHECK `ck_credential_deliveries_terminal_has_no_ciphertext`: `status IN ('Delivered','Read','Failed','Superseded','Cancelled') ⇒ sealed_bytes IS NULL AND nonce IS NULL AND key_id IS NULL`. A second CHECK requires the three sealed columns to be all-null or all-non-null.
- The sealed copy is never exported or copied. Summary reads project explicit columns without the sealed ones; the entity is loaded only by the worker and, on the request path, by the supersede/cancel step that nulls it.

## 7. Worker

- Hosted service (`CredentialDeliveryWorker`) polling due rows; claim with a lease (`UPDATE … WHERE status='Queued' AND next_attempt_at <= now AND (lease_until IS NULL OR lease_until < now) RETURNING`), safe across instances.
- Per attempt: re-check issuance is `Open`/current, user Active, consent present, user has not signed in or changed password since `issued_at`, `purge_by` not passed → open sealed copy → send via `IWhatsAppSender` → record `Accepted` + provider message id, or a failure category.
- **Retries:** transient only (network, 5xx, 429 with `Retry-After`); default up to **5 attempts**, exponential back-off with full jitter starting at **1 minute** (OD-18). Definitive rejections (`invalid_number`, `not_on_whatsapp`, `template_unavailable`, `auth_or_permission`) are not retried.
- Key unavailable ⇒ `Failed(key_unavailable)`, no retry, purge, alert. Authentication failure on open ⇒ `Failed`, purge, security alert.
- The worker never generates, hashes or stores a password and never loads or changes the user record.
- Crash after open before send ⇒ lease expires, next attempt re-opens and resends the same password. Crash after provider accept before recording ⇒ duplicate message with the same password (harmless).
- Metrics (names only): queue depth, oldest-queued age, failures by category.

## 8. Reset semantics (OD-29)

Default built: **(a) immediate invalidation** — the reset transaction replaces the hash, revokes all sessions, bumps `security_version`, supersedes the earlier open issuance and purges its sealed copy. The employee has no working password until the new one arrives or an Admin re-issues. The Admin UI states this before confirmation. Alternative (b) (staged hash) is not built.

## 9. Alternatives

| Option | Verdict |
|---|---|
| **A. One password per operation, hash in user record, short-lived sealed copy, same password on retries** | **Chosen.** |
| B. New password per attempt | **Rejected:** a delayed message from attempt 1 arrives after attempt 2 changed the hash; the employee holds a dead password. |
| C. One attempt, no stored copy | **Fallback** if production custody (OD-30) is unavailable. No secret at rest; weaker delivery guarantees. Switching requires only `SealedCopyRetention = 0`-style configuration plus an in-request send; not built. |
| D. Inline send in the request | Variant of C. |
| E. Derive from id + server secret | Rejected: weakens randomness. |
| F. Return to the Admin | Excluded by R51. |

## 10. Other decisions

- **Audit:** no new action code. Creation: existing `UserCreated`, `RoleAssigned`; snapshot gains `credential_issuance_id`, `consent_affirmed`. Reset: `PasswordResetIssued` with `credential_issuance_id`, `channel='whatsapp'`, `issued_at`. Never captured: password, hash, destination, message text, provider secrets/payloads. Delivery status changes are operational data, not audit.
- **Fake sender:** `FakeWhatsAppSender` registers only when the host environment is Development or Testing; a startup check throws otherwise (tested). Exposes plaintext to tests only through an in-memory sink.
- **Webhook:** `GET` handshake (constant-time verify token); `POST` verifies HMAC-SHA-256 of the **raw body** (constant time) before parsing; body-size limit; idempotent on (message id, status, provider timestamp); statuses monotonic; unknown ids → 200; stores only id, status, timestamp, error code/title.
- **Password length:** generation stays at 12 characters (upper+lower+digit by rejection sampling). The password may never be changed, so the strength is that of ~71 bits for 12 chars over 62 symbols (62^12 ≈ 3.2×10^21); acceptable alongside login throttling (§17). Raising to 16 is a one-line change in the generator if the owner prefers.
- **Unconfigured behaviour:** `WhatsApp:Enabled=false`, missing sender configuration or missing sealing key ⇒ 503 on create/reset.

## 11. Consequences and open owner items

- Residual risks are those of plan §1F.6 (recoverable secret at rest for ≤ retention; replicas/backups inside the window; memory and crash dumps; key custody).
- **Pending owner decisions:** OD-30 (production key custody, destruction schedule, explicit acceptance of the encrypted copy), OD-29 (reset invalidation), OD-18 (parameters), OD-23 (Meta accepting a credential-bearing template; contingency).
- **Migration:** additive tables and CHECKs; written in PS-04a, tested up/down on an isolated database only, not applied to any shared database.
