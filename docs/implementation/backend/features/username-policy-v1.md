# Username policy v1 (UN-1) — specification and golden vectors

**Status:** Proposed (2026-10-23). The three points the owner confirmed under R49 are firm: usernames are **English-only**, **suggested from the employee's name**, and **editable by the Admin before creation**. Every other rule below is the working text from plan §1G, adopted provisionally on 2026-10-23 (owner instruction: follow the roadmap recommendations) and **to be confirmed in the decision session (OD-19)**. The Arabic dictionary and dialect choices below are an engineering seed; **the owner must review the Arabic outputs** before any of this is treated as agreed.
**Kind:** working document under `docs/implementation/**`.
**Amends (dated note, history unchanged):** ADR 0003 §3.1 — the login identifier stays an opaque value object (`LoginIdentifier`); this policy is a **creation-time validator** applied by the create use case and the suggestion endpoint. Existing and bootstrap identifiers are not re-validated.

## 1. Canonical form and validation

A candidate is validated in this order; the first failing rule gives the `reason`.

1. `whitespace` — contains any whitespace (including leading/trailing).
2. `too_short` — fewer than 3 characters. `too_long` — more than 32.
3. Uppercase ASCII letters are lowercased (canonicalization, not an error). Then `bad_characters` — anything outside `a–z 0–9 . _ -`.
4. `bad_shape` — does not start with a letter, does not end with a letter or digit, or has two separators in a row.
5. `phone_like` — a run of 6 or more digits.
6. `reserved` — see §4.

The stored value is the canonical lowercase form. Login matching stays case-insensitive.

## 2. Suggestion pipeline (policy version `v1`)

Input: `name` (any script) and optional `latinName`. `latinName`, when present and usable, is used **instead of** transliterating `name`.

1. Unicode NFKC; remove bidi/zero-width controls (U+200B–U+200F, U+202A–U+202E, U+2066–U+2069, U+FEFF); remove Arabic diacritics (U+064B–U+065F, U+0670) and tatweel (U+0640); collapse whitespace; trim.
2. Tokenize on whitespace. **Connectors** attach to the following token: Arabic `عبد`, `ابو`/`أبو`, `بن`, `ابن`; Latin `abd`, `abdel`, `abdul`, `abu`, `bin`, `ibn`, `al`, `el`. (`عبد الله` is one token.)
3. Latin tokens: NFD, drop combining marks, lowercase, drop every character outside `a–z 0–9`.
4. Arabic tokens: dictionary lookup first (§3); otherwise the letter map (§3). Tokens with no usable letters are dropped.
5. Join the **first two** surviving tokens with `.` (one token → that token). Apply the validation rules (§1) to the result; if it is unusable (fewer than 2 usable characters, or still invalid) → **no suggestion** (`name_unusable`). The service never returns an empty value or a placeholder.
6. **Collision:** first free of `base`, `base2`, `base3`, … compared case-insensitively against **all** accounts (disabled included). The base is shortened so base + suffix fits 32 characters. At most three free alternatives are returned.

## 3. Data (seed, to be reviewed)

**Dictionary** (Arabic token → Latin; undiacritized, alef forms normalized to `ا` for lookup):

| Arabic | Latin | Arabic | Latin |
|---|---|---|---|
| محمد | mohamed | احمد | ahmed |
| محمود | mahmoud | مصطفى | mostafa |
| ابراهيم | ibrahim | اسماعيل | ismail |
| علي | ali | حسن | hassan |
| حسين | hussein | خالد | khaled |
| عمر | omar | عثمان | osman |
| يوسف | youssef | كريم | karim |
| طارق | tarek | سعيد | said |
| سيد | sayed | كامل | kamel |
| هاني | hany | ياسر | yasser |
| عادل | adel | ماجد | maged |
| سامي | samy | رمضان | ramadan |
| صلاح | salah | جمال | gamal |
| فتحي | fathy | عبد الله | abdullah |
| عبد الرحمن | abdelrahman | عبد العزيز | abdelaziz |
| عبد الحميد | abdelhamid | عبد الرحيم | abdelrahim |

**Letter map** (fallback; low quality — Arabic omits short vowels, so the UI labels it "check the spelling"):

`ا أ إ آ ٱ`→`a` · `ب`→`b` · `ت`→`t` · `ث`→`th` · `ج`→`j` · `ح`→`h` · `خ`→`kh` · `د`→`d` · `ذ`→`dh` · `ر`→`r` · `ز`→`z` · `س`→`s` · `ش`→`sh` · `ص`→`s` · `ض`→`d` · `ط`→`t` · `ظ`→`z` · `ع`→`a` · `غ`→`gh` · `ف`→`f` · `ق`→`q` · `ك`→`k` · `ل`→`l` · `م`→`m` · `ن`→`n` · `ه`→`h` · `ة`→`a` · `ى`→`a` · `ء ئ ؤ`→dropped · `و`→`w` at token start, `u` elsewhere · `ي`→`y` at token start, `i` elsewhere. `عبد` + `ال…` (not in the dictionary) → `abdel` + map(rest without `ال`); `عبد` + other → `abd` + map(rest).

Dialect choices (`ج`→`j`, `ق`→`q`) are the owner's.

## 4. Reserved names

Exact, case-insensitive: `admin`, `administrator`, `root`, `system`, `support`, `contractra`, `owner`, `accountant`, `engineer`, `officestaff`, `office`, `staff`, `service`, `bot`, `noreply`, `api`, `null`, `undefined`, `me`; every role code in the roles table; the configured bootstrap identifier. For `admin`, `administrator`, `root`, `system`, `support` a **trailing number is also rejected** (`admin2`).

## 5. Golden vectors (the fixture of PS4a-BE-01 and its tests)

| # | Input | Expected |
|---|---|---|
| G1 | name `Ahmed Mohamed` | `ahmed.mohamed` |
| G2 | name `  AHMED   MOHAMED ` | `ahmed.mohamed` |
| G3 | name `أحمد محمد` | `ahmed.mohamed` |
| G4 | name `عبد الله حسن` | `abdullah.hassan` |
| G5 | name `José Núñez` | `jose.nunez` |
| G6 | name `محمد` | `mohamed` |
| G7 | name `???` | no suggestion (`name_unusable`) |
| G8 | name `O'Brien Patrick` | `obrien.patrick` |
| G9 | name `أحمد مُحَمَّد` (diacritics) | `ahmed.mohamed` |
| G10 | name `أحمـد محمد` (tatweel) | `ahmed.mohamed` |
| G11 | name `عبد الرحمن علي` | `abdelrahman.ali` |
| G12 | name `سلمى` (not in dictionary, letter map; short vowels are not written in Arabic, so the result is low quality and the UI says "check the spelling") | `slma` |
| G13 | name `ندى أحمد` (map + dictionary) | `nda.ahmed` |
| G14 | name `أحمد محمد`, latinName `Ahmed Mohammed` | `ahmed.mohammed` (latinName wins) |
| G15 | name `A` | no suggestion |
| G16 | name `Ahmed Mohamed` when `ahmed.mohamed` and `ahmed.mohamed2` exist | `ahmed.mohamed3` |
| G17 | name `Ahmed Mohamed` when `Ahmed.Mohamed` (any case) exists | `ahmed.mohamed2` |
| G18 | 20-letter given + 20-letter second name | base cut so base + suffix ≤ 32 |
| V1 | candidate `Ahmed.Mohamed` | valid → `ahmed.mohamed` |
| V2 | `ab` | `too_short` |
| V3 | 33 characters | `too_long` |
| V4 | `ahmed mohamed` / ` ahmed` | `whitespace` |
| V5 | `ahmed@x.com`, `أحمد` | `bad_characters` |
| V6 | `1ahmed`, `a..b`, `ahmed-` | `bad_shape` |
| V7 | `user123456789` | `phone_like` |
| V8 | `admin`, `Admin`, `admin2`, `engineer`, `root` | `reserved` |
| V9 | `admin_x`, `engineers` | valid |

## 6. Stability

The username is fixed at creation; no rename endpoint exists. Mistaken-username recovery is OD-20 (recommended: a narrow, audited correction while the account has never signed in). **PS-04a builds the default (c): no correction path** until the owner decides.
