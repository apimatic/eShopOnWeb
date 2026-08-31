---
name: sa-to-uc-migration
title: Secure Acceptance to Unified Checkout Migration
type: migration
description: Migrate a CyberSource Secure Acceptance (deprecated form-POST + HMAC signed fields) integration to Unified Checkout 1.0 (REST capture-context at /uc/v1/sessions + browser VAS.UnifiedCheckout SDK). Use when the user says "migrate from Secure Acceptance", "SA to UC", "replace silent/pay form", "Unified Checkout migration", or works in a codebase that posts to `secureacceptance.cybersource.com/silent/pay`.
keywords:
  - secure-acceptance
  - unified-checkout
  - sa-to-uc
  - migration
  - capture-context
---

# Secure Acceptance → Unified Checkout 1.0 Migration

Secure Acceptance is deprecated; clients are moving to Unified Checkout 1.0.

| | Secure Acceptance | Unified Checkout 1.0 |
|---|---|---|
| Auth | HMAC-signed hidden form fields | JWT v2 (`Authorization: Bearer <HS256 JWT>`) |
| Credentials | `access_key` + `profile_id` + SA secret | `merchant_id` + REST `key_id` + base64 secret (new — SA creds don't work) |
| Capture | Browser form POST to `secureacceptance.cybersource.com` | `POST /uc/v1/sessions` → capture-context JWT |
| Card UI | Hosted form on cybersource.com | Embedded `VAS.UnifiedCheckout(...).mount()` |
| Result | Signed POST/redirect back to merchant | `mount()` resolves with a complete-response JWT |

## How to work

This skill gives you the durable, hard-won facts and points you at the authoritative docs — it is **not** a script to execute blindly. Think, and lean on two things:

- **Use the skill's verified content first; confirm against a live source only when needed.** The capture-context schema is strict (`additionalProperties: false`), so a guessed or stale field is a hard 400. Order of authority: **this skill** (`references/unified-checkout.md` — the verified bodies + feature blocks) → **developer MCP** if installed, to find/confirm anything the skill doesn't cover → **official web docs** as the last resort. A live sandbox `400` is the runtime tiebreaker — it names the exact field. When you can't confirm a field, stop and flag it — don't infer its type/value by analogy to a neighbor, and don't "correct" a value you had right.
- **The developer is your collaborator.** Interview them; ask when unsure rather than assuming. In a large or unfamiliar codebase, ask where the payment/checkout code lives instead of searching at length. They know the environment and the merchant's CyberSource setup — you don't.

Iteration is normal. Real environments surface wrinkles (proxies, package restrictions, framework conventions, credential stores) this skill can't anticipate.

## Flow (adapt to the codebase — not rigid steps)

1. **Discover the SA integration.** Find the signing, the payload builder, the form, the reply handler, the success/error URLs, and any `merchant_defined_data`. Grep patterns and what to record are in `references/unified-checkout.md` ("Find the SA integration"). Save the inventory to a file **in the target project** (e.g. `SA_DISCOVERY.md` at its root), not in this skill.

2. **Interview the developer.** Having seen the code, ask what the code can't tell you. Start with the environment, verbatim:
   > "Any environmental constraints I should know about? For example a corporate/TLS-intercepting proxy, restrictions on installing packages, runtime or platform version requirements, or specific certificate authorities your HTTP clients must trust. Anything that affects outbound HTTPS calls or dependency installation is worth surfacing now."

   Then the current SA setup and UC target state: transaction type (authorize vs sale); which `merchant_defined_data` slots carry real signal; whether **Decision Manager, 3DS/Payer Auth, and TMS** are enabled today (these drive `completeMandate`, and none may be assumed); single vs multiple profiles/MIDs; currency/region. Don't skip the environment question just because discovery went smoothly.

3. **Set up REST auth.** JWT v2 (HS256) — full contract in `references/rest-api.md`. Prefer the official CyberSource SDK if one exists for the project's language (it builds the JWT and MLE for you); otherwise build the JWT independently per `references/rest-api.md`. Find how the project already stores credentials and mirror that; don't impose `.env`. Add placeholders with instructions if real credentials aren't available yet — that doesn't block the build; only the live call fails.

4. **Build the capture context and frontend.** Everything is in `references/unified-checkout.md` — start from its canonical body and adapt one to the merchant, using the per-feature blocks and the frontend `mount()` pattern. Configure `captureMandate`/`completeMandate` explicitly from the interview answers (enable what the merchant uses, explicitly disable what they don't; don't rely on defaults).

5. **Confirm feature coverage.** Check the SA→UC field map and feature blocks in `references/unified-checkout.md` (and the official migration guide it links) so every SA capability the merchant uses has a UC home — nothing orphaned.

6. **Retire SA safely.** UC becomes the single active flow; SA is never live alongside it. Prefer git history as the rollback; if the team wants an in-repo fallback, keep SA code dormant behind an off-by-default flag, not running. Don't delete SA in the same PR that adds UC — land UC, confirm it in production, then remove the SA signing, form, reply handler, and env vars in a follow-up.

7. **Hand off testing.** Do the verification you can: it compiles, the server boots, the capture-context payload follows the placement/field rules, credentials are wired, SA and UC aren't both live. The **live sandbox transactions and browser click-through are the developer's to run** — they need real credentials, network access, and a rendered page. Don't drive the browser yourself and don't loop retrying a live call that fails on credentials or a blocked network; produce a short test checklist and hand it over. Converging on capture-context field errors needs the response body visible — suggest adding request/response logging on the `/uc/v1/sessions` call (mirror the project's logging convention) and **ask how the developer wants to work the loop** (you iterate against the errors, or they paste them back). Swap the code; don't stand up test automation.

## Guardrails

- Never log raw PAN, CVV, or JWT bodies — treat as PCI-adjacent.
- Never hardcode SA or UC secrets in source.
- SA and UC must never both be live — UC is the sole active flow; the SA backup is git history (or dormant code behind an off-by-default flag).
- Don't delete SA in the same PR as UC. Two deploys minimum.
- HTTPS only — UC will not initialize over HTTP.
- No wildcard `targetOrigins`.

(UC schema specifics — the `data`-placement rule, `requestSaveCredentials`, DM/3DS/TMS not-assumed, the transient-flow amount match — live in `references/unified-checkout.md`.)

## Reference files

- `references/rest-api.md` — JWT v2 authentication and how to call the REST API
- `references/unified-checkout.md` — the SA→UC technical reference: discovery, verified capture-context bodies, SA→UC field map, per-feature blocks, complete-response JWT shape, frontend mount pattern, and how to confirm field detail (skill → MCP → docs)
