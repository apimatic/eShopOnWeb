---
name: unified-checkout-reference
title: Unified Checkout 1.0 Technical Reference
type: concept
description: Unified Checkout 1.0 technical reference — how UC works, the capture-context request bodies and field-placement rules, per-feature blocks (3DS, Decision Manager, TMS, shipping), decoding the /uc/v1/sessions response JWT, the complete-response JWT, the browser mount() pattern, the transient-token flow, and the Secure Acceptance → UC field map for migrations.
keywords:
  - unified-checkout
  - capture-context
  - uc-sessions
  - mount
  - transient-token
  - sa-to-uc
---

# Unified Checkout 1.0 — the SA→UC technical reference

Everything for building the UC side and mapping from Secure Acceptance: how to get field detail, how UC works, SA discovery, the verified capture-context bodies, the SA→UC field map, per-feature blocks, the browser mount pattern, and the gotchas. Auth (JWT v2) is in `rest-api.md`.

> **Verified against** the CyberSource REST SDK `Ucv1sessions*` model family (SDK v0.0.77), captured 2026-07-22. If a field here ever returns a `400`, the schema has moved — fix the call and update this doc + the stamp.

## Where field detail comes from (order of authority)

1. **This doc first.** The canonical body and feature blocks below are verified — start here and adapt one to the merchant. Most migrations need nothing else.
2. **Developer MCP, if installed** (`mcp__cybersource-developer-mcp__*`) — fall back to it to find or confirm a field this doc doesn't cover. Reach the right models by **entering from the endpoint, not guessing model names**: list the APIs → `/uc/v1/sessions` → its request model (`GenerateUnifiedCheckoutV1CaptureContextRequest`) → follow its children (the `Ucv1sessions*` family). The SDK also ships a **deprecated pre-1.0 family** (`/up/v1` — boolean `consumerAuthentication`, `requestSaveCard`); a name-guess can land on it, so always navigate down from the request model.
3. **Official web docs** — last resort / for concepts. Roots: `https://developer.cybersource.com/docs/cybs/en-us/unified-checkout/developer/all/rest/unified-checkout/uc-about-guide.html` and the hub `https://developer.cybersource.com/docs`. Prefer a page's Markdown export (swap `.html`→`.md`); the nav is JavaScript, so follow links in the page. If a URL 404s, go up to a root and navigate, search, or ask the developer.

**Runtime ground truth:** a live sandbox `400` overrides all of the above — it names the exact field and expected values. If it contradicts this doc, the schema moved: fix the call and update this doc.

**When you can't confirm a field, stop and flag it — never guess.** Don't infer a field's type/value by analogy to a neighbor (adjacent fields can be a boolean vs a string enum vs an object), and don't "correct" a product-specific format to a convention you assume. Leave a visible `TODO`/comment and ask the developer.

**Verify loop.** Converging on field errors means seeing the capture-context response body — suggest adding request/response logging to the `/uc/v1/sessions` call (mirror the project's logging convention) so the 400 detail is visible, then **ask the developer how they want to run the loop** (you iterate against the errors, or they paste them back). Don't assume the environment or stand up automated testing — the skill swaps the code, it doesn't own a test harness.

**Watch the surface.** This skill targets **UC 1.0 `POST /uc/v1/sessions`** with the `VAS.UnifiedCheckout(...).createCheckout().mount()` SDK. Older docs describe the **pre-1.0 `/up/v1/capture-contexts`** surface (`up.show()`/`up.complete()`, a `clientVersion` field, boolean `consumerAuthentication`, `requestSaveCard`) — those are wrong for `/uc/v1/sessions`. If you see `up.show()` or `clientVersion`, you're on the wrong surface.

## How UC works

1. **Server** creates a capture context: `POST /uc/v1/sessions` → a signed capture-context JWT + a per-session `clientLibrary` SDK URL (see `rest-api.md`).
2. **Browser** loads the `clientLibrary` script, then `VAS.UnifiedCheckout(captureContext).createCheckout().mount(...)` renders the card form.
3. On pay, `mount()` resolves with a **complete-response JWT** (auth already done) or a **transient token**, depending on the flow.
4. **Server** records the result (complete-response) or calls `/pts/v2/payments` with the token (transient).

UC requires **HTTPS** even in local dev — it will not initialize over plain HTTP.

## Official SA→UC migration guide

CyberSource publishes an SA→UC migration guide (Feature Comparison, Migration Process, prerequisites, FAQ) — the authoritative map for feature parity and the SA-reply-NVP → UC-JSON change. Root: `https://developer.cybersource.com/docs/cybs/en-us/sa-uc/migration/all/na/sa-uc/sa-uc-about-guide.html`.

## Find the SA integration (discovery)

Grep the codebase (adjust globs to the languages in use):
```
secureacceptance.cybersource.com      # the form action — the smoking gun
signed_field_names                     # the signed-field manifest
access_key | profile_id | transaction_uuid | signed_date_time | unsigned_field_names
hmac | HMAC                            # the signing logic
```
Record, for each hit: the signing function (what it signs; where the secret comes from), the payload builder (source of amount/currency/bill-to/ship-to/reference), the form template (hidden inputs; signed vs unsigned fields), the reply/return handler, success/cancel/error URLs, any `merchant_defined_data*` slots and their meaning (ask the merchant), Decision Manager fields, and whether there are multiple SA profiles. Save this to a file **in the target project** (e.g. `SA_DISCOVERY.md` at its root), never in the skill — you'll need it at cleanup.

Watch for: client-side signing (check the templates); `merchant_defined_data*` referenced from *outside* the code (DM rules, BI jobs) — confirm before dropping; legacy `silent/pay` iframe flows with `postMessage` listeners that must go; and any proprietary shim capturing raw PAN before the SA post — stop and escalate.

## Rules that don't change

- **Endpoint:** `POST /uc/v1/sessions`; returns a raw capture-context JWT (see `rest-api.md`).
- **JSON is camelCase** (SDK model attrs are snake_case; the wire format is camelCase — `requestSaveCredentials`, `consumerAuthentication`, `totalAmount`).
- **Placement:** UC-control fields at the **root**; everything payment-shaped under **`data`** (mirrors the `/pts/v2/payments` body). Schema is `additionalProperties: false` — an unknown or misplaced field is a hard 400.
- **Configure explicitly, don't rely on defaults** — set each feature to a deliberate value.

## Canonical body — complete-response / authorize (the common SA replacement)

Card entry, authorize on pay, backend just parses the result. Adjust per the feature blocks below.

```json
{
  "targetOrigins": ["https://your-domain:port"],
  "allowedCardNetworks": ["VISA", "MASTERCARD", "AMEX", "DISCOVER"],
  "allowedPaymentTypes": ["PANENTRY"],
  "country": "US",
  "locale": "en_US",
  "captureMandate": {
    "billingType": "FULL",
    "requestEmail": true,
    "requestPhone": false,
    "requestShipping": false,
    "showAcceptedNetworkIcons": true,
    "requestSaveCredentials": false
  },
  "completeMandate": {
    "type": "AUTH",
    "decisionManager": false,
    "consumerAuthentication": "NONE"
  },
  "data": {
    "clientReferenceInformation": { "code": "your-order-id" },
    "orderInformation": {
      "amountDetails": { "totalAmount": "100.00", "currency": "USD" },
      "billTo": {
        "firstName": "Jane", "lastName": "Doe", "email": "jane@example.com",
        "phoneNumber": "5551234567",
        "address1": "1 Market St", "locality": "San Francisco",
        "administrativeArea": "CA", "postalCode": "94105", "country": "US"
      }
    }
  }
}
```
- `targetOrigins` — exact `scheme://host:port`; must match `window.location.origin` incl. port; no wildcards; `https` only.
- `country` — 2-char ISO 3166. `locale` — `<ISO 639>_<ISO 3166>` with an **underscore** (`en_US`, not `en-US`). Match the merchant's region.
- `completeMandate.type` — `AUTH` (authorize only) vs `CAPTURE` (sale) vs `PREFER_AUTH`. Map from SA `transaction_type`.
- Omit `clientVersion` — UC uses the latest and returns the matching SDK URL.

## Field reference (SA → UC path)

| SA field | UC path (camelCase) | Type / values |
|---|---|---|
| `amount` | `data.orderInformation.amountDetails.totalAmount` | string |
| `currency` | `data.orderInformation.amountDetails.currency` | string |
| `reference_number` | `data.clientReferenceInformation.code` | string (echoes back in the response JWT) |
| `bill_to_forename` / `_surname` | `data.orderInformation.billTo.firstName` / `lastName` | string |
| `bill_to_email` | `data.orderInformation.billTo.email` | string |
| `bill_to_phone` | `data.orderInformation.billTo.phoneNumber` | string |
| `bill_to_address_line1..4` | `data.orderInformation.billTo.address1..4` | string |
| `bill_to_address_city` | `data.orderInformation.billTo.locality` | string |
| `bill_to_address_state` | `data.orderInformation.billTo.administrativeArea` | string |
| `bill_to_address_postal_code` | `data.orderInformation.billTo.postalCode` | string |
| `bill_to_address_country` | `data.orderInformation.billTo.country` | string (2-char ISO) |
| `ship_to_*` | `data.orderInformation.shipTo.*` | same field names |
| `merchant_defined_data1..100` | `data.merchantDefinedInformation` | `[{ "key": "1".."100", "value": "..." }]` |
| `transaction_type` | `completeMandate.type` | `AUTH` \| `CAPTURE` \| `PREFER_AUTH` |
| `signed_field_names`/`signature`/`signed_date_time` | — | gone; replaced by JWT v2 request signing |

## Feature blocks — add to the canonical body only when the merchant uses them

Each is separately provisioned on the merchant; enabling one they lack errors out. Confirm from the SA flow or ask; set the value explicitly either way.

**Sale (capture immediately) instead of authorize** — `"completeMandate": { "type": "CAPTURE" }`

**3D Secure** — `consumerAuthentication` is a **string enum**, not a boolean:
```json
"completeMandate": { "type": "AUTH", "consumerAuthentication": "3DS" }
```
Values: `NONE` (off) · `3DS` · `PASSKEY`.

**Decision Manager (fraud)** — boolean: `"completeMandate": { "type": "AUTH", "decisionManager": true }`

**TMS tokenization / save card** — the TMS block *and* the save-card opt-in:
```json
"captureMandate": { "requestSaveCredentials": true },
"completeMandate": {
  "type": "AUTH",
  "tms": { "tokenCreate": true, "tokenTypes": ["customer", "paymentInstrument", "instrumentIdentifier", "shippingAddress"] }
}
```
`requestSaveCredentials` **defaults to `true`** — always send it explicitly; set `false` (as in the canonical body) whenever TMS is not in the flow, or the form offers "save card" with nowhere to save it.

**Shipping capture** — `"captureMandate": { "requestShipping": true, "shipToCountries": ["US","CA"] }` plus `data.orderInformation.shipTo`.

**Merchant-defined data** (SA `merchant_defined_data*`) — under `data`:
```json
"data": { "merchantDefinedInformation": [ { "key": "1", "value": "loyalty-123" } ] }
```
Confirm which slots DM rules or BI jobs depend on before dropping any.

## The `/uc/v1/sessions` response — decode it to get the SDK URL

The response is a **raw capture-context JWT** (media type `application/jwt`), **not JSON** — don't `.json()` it. base64url-decode the middle segment; the browser SDK loader lives *inside* the payload at `ctx[0].data`:
```
ctx[0].data.clientLibrary            ← the per-session SDK <script> URL (unique per session; never hardcode)
ctx[0].data.clientLibraryIntegrity   ← its SRI integrity hash
```
Return `{ captureContext: <the raw JWT>, clientLibrary, clientLibraryIntegrity }` to the browser (the frontend injects `<script src=clientLibrary integrity=clientLibraryIntegrity>` — see the mount pattern below). The decoded payload also has a sibling top-level `flx` block (Flex microform / transient-token material) and standard JWT claims — the SDK loader is specifically under `ctx[0].data`; don't pull from `flx`.

**Failure signature:** if `clientLibrary` comes through `undefined`, the browser injects `<script src="undefined">`, requests `/undefined`, gets the HTML 404 page, and refuses to run it (`Refused to execute script … MIME type 'text/html'`). That means the server didn't extract `ctx[0].data` — usually because it `.json()`'d the raw JWT, read a nonexistent top-level `clientLibrary`, or pulled from the wrong block. (This shape isn't in the SDK models — the endpoint returns the JWT string — so it's **confirmed against a real `/uc/v1/sessions` response**, not the MCP.)

## Complete-response JWT (what `mount()` returns on the AUTH flow)

Decode the JWT payload (middle segment, base64url). **`id` / `status` / `outcome` / `message` are top-level**; the rest is under `details`. Reading `details.status` is the classic bug — status is top-level; `details.status` is null.
```
id, status, outcome, message                          ← top level
details.orderInformation.amountDetails.authorizedAmount
details.clientReferenceInformation.code               ← your reference_number, echoed back
details.processorInformation.approvalCode
details.consumerAuthenticationInformation.eci         ← present when 3DS ran
details.tokenInformation.{customer,paymentInstrument,instrumentIdentifier}.id  ← present when TMS ran
```

## Frontend mount pattern

```js
// 1. Get the capture context from your server
const { captureContext, clientLibrary } = await fetch('/sessions', { method: 'POST' }).then(r => r.json());
// 2. Inject the SDK — the URL is unique per session, never hardcode it
await loadScript(clientLibrary);
// 3. Mount (complete-response flow)
const client = await VAS.UnifiedCheckout(captureContext);
const checkout = await client.createCheckout();
const completeResponseJwt = await checkout.mount({ paymentSelection: '#container', paymentScreen: '#container' });
// 4. Hand the result to your server (record-keeping, not re-processing)
await fetch('/complete', { method: 'POST', body: JSON.stringify({ completeResponseJwt }) });
// 5. Always clean up
checkout.destroy(); client.destroy();
```
Forward the framework's CSRF token on the `/sessions` and `/complete` fetches. Verify exact method signatures against the JavaScript API Reference if needed.

## Transient-token flow (only if the merchant needs server-side logic between capture and auth)

Omit `completeMandate` server-side **and** call `createCheckout({ autoProcessing: false })` in the browser (both halves must flip together). `mount()` then returns a transient token; POST it to `/pts/v2/payments`. The capture-context `totalAmount` and the `/pts/v2/payments` `totalAmount` **must match**, or auth silently uses the capture-context amount.

## Quick gotcha table

| Symptom | Cause / fix |
|---|---|
| Form never renders | `targetOrigins` doesn't exactly match `window.location.origin` (scheme+host+**port**). |
| UC won't initialize | Served over HTTP — UC needs HTTPS even in dev. |
| `400 … ADDITIONAL_PROPERTIES` | Unknown/misplaced field. Move payment fields under `data`; confirm the name (order of authority above). |
| `401` | Auth — usually a credential name/value or env-vs-host mismatch, not the request body (see `rest-api.md`). |
| `GET /undefined` 404 + "Refused to execute script … MIME type 'text/html'" | `clientLibrary` came through undefined — the server didn't decode `ctx[0].data` from the capture-context JWT (don't `.json()` the raw JWT). |
| `/sessions` or `/complete` 403 | CSRF token not forwarded on the fetch. |
| `mount()` returns the wrong type | `completeMandate` (server) and `autoProcessing` (browser) don't match — flip both together. |
| Authorized wrong amount | Amount mismatch across displayed total / capture context / `/pts/v2/payments`. |
