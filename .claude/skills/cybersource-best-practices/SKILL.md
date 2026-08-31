---
name: cybersource-best-practices
title: CyberSource Integration Best Practices
type: concept
description: >
  CyberSource REST SDK integration guidance — authentication (JWT shared secret / P12),
  Unified Checkout v1 (/uc/v1/sessions), TMS token passthrough into UC,
  3DS/Payer Auth, digital wallets, webhooks, recurring billing, and error handling.
keywords:
  - authentication
  - jwt
  - mle
  - unified-checkout
  - tms
  - payer-auth
  - webhooks
  - recurring-billing
---

# CyberSource Best Practices

## Official Documentation

| Resource | URL |
|---|---|
| Cybersource Developer Portal | https://developer.cybersource.com |
| API Reference | https://developer.cybersource.com/api-reference-assets/index.md |
| Business Center (Sandbox) | https://businesscentertest.cybersource.com/ebc2/ |
| Customer Support | https://support.visaacceptance.com |
| SDK Samples (Java) | https://github.com/CyberSource/cybersource-rest-samples-java |
| SDK Samples (Node) | https://github.com/CyberSource/cybersource-rest-samples-node |
| SDK Samples (Python) | https://github.com/CyberSource/cybersource-rest-samples-python |
| SDK Samples (CSharp) | https://github.com/CyberSource/cybersource-rest-samples-csharp |
| SDK Samples (PHP) | https://github.com/CyberSource/cybersource-rest-samples-php |
| SDK Samples (Ruby) | https://github.com/CyberSource/cybersource-rest-samples-ruby |
| llms.txt (AI discovery index) | https://developer.cybersource.com/llms.txt |

---

## Authentication

**JWT (Shared Secret)** is the recommended auth method for new integrations. **JWT (P12)** is also fully supported. **HTTP Signature** is deprecated — don't implement it for new work; migrate any existing HTTP Signature integration to JWT Shared Secret (it reuses the same credentials). All JWT auth supports MLE; HTTP Signature does not.

### JWT (Shared Secret) — Recommended

Symmetric JWT signed with HMAC using a shared secret key. Supports MLE.

**SDK config keys:** `authenticationType=jwt`, `jwtKeyType=SHARED_SECRET`, `merchantID`, `merchantKeyId`, `merchantsecretKey`, `runEnvironment=apitest.cybersource.com`

**Key gotcha:** "The shared secret is shown only once when generated. Copy it immediately."

### JWT (P12 Certificate) — Recommended

Certificate-based authentication using a PKCS#12 file. Supports MLE.

**SDK config keys:** `authenticationType=jwt`, `merchantID`, `keysDirectory`, `keyFilename`, `keyPass`, `keyAlias`, `runEnvironment=apitest.cybersource.com`

**Obtaining your P12 certificate:**
1. Log into Business Center → Payment Configuration → Key Management
2. Generate new key → select P12
3. Download the `.p12` file — store securely, never commit to source control

### HTTP Signature — Deprecated

Signs each request with HMAC-SHA256. **Deprecated — do not implement for new integrations.** If you have an existing HTTP Signature integration, migrate to JWT Shared Secret: it reuses the same `merchantKeyId` + shared secret, so only `authenticationType` changes (add `jwtKeyType=SHARED_SECRET`). Does **not** support MLE.

**SDK config keys:** `authenticationType=http_signature`, `merchantID`, `merchantKeyId`, `merchantsecretKey`, `runEnvironment=apitest.cybersource.com`

**Key gotcha:** "The shared secret is shown only once when generated. Copy it immediately."

### OAuth 2.0

For ISV/partner delegation — allows one merchant to authorize another to act on their behalf. Not a general-purpose auth method. Uses separate endpoints: `api-matest.cybersource.com` (sandbox), `api-ma.cybersource.com` (production).

---

## Environments

| Environment | URL | Use |
|---|---|---|
| Sandbox | `https://apitest.cybersource.com` | Development and testing |
| Production | `https://api.cybersource.com` | Live transactions |
| OAuth Sandbox | `https://api-matest.cybersource.com` | OAuth testing |
| OAuth Production | `https://api-ma.cybersource.com` | OAuth live |

Never mix environments in the same code example. Sandbox test cards will fail on production and vice versa.

---

## Message Level Encryption (MLE)

Application-level payload encryption (request) and decryption (response). Supported **only with JWT authentication** — works with both JWT key types (`P12` and `SHARED_SECRET`). HTTP Signature does not support MLE.

**Enable globally:** set `enableRequestMLEForOptionalApisGlobally=true` in `merchantConfig` (replaces the deprecated `useMLEGlobally`).

**Request MLE certificate source by key type:**

| JWT key type | Where the MLE cert comes from |
|---|---|
| `P12` | Auto-extracted from the P12 file via `requestMleKeyAlias` (default alias `CyberSource_SJC_US`), or supplied via `mleForRequestPublicCertPath` |
| `SHARED_SECRET` | **Must** be supplied via `mleForRequestPublicCertPath` (no P12 to extract from) — download the cert from Business Center |

**Key gotchas:**
- Check each API's MLE enforcement level (Mandatory / Optional / Not Applicable)
- For `SHARED_SECRET`, `mleForRequestPublicCertPath` is required or request MLE cannot run
- Use `disableRequestMLEForMandatoryApisGlobally=true` to opt mandatory-MLE APIs out
- Default P12 key alias is `CyberSource_SJC_US` — only change if using a custom cert
- Up to 3 active Key-IDs for rotation

---

## Credential & Key Management

- **P12 certificates** — generated in Business Center, contain both signing key and MLE cert
- **Shared secrets** — shown once at generation; copy immediately
- Monitor expiration in Business Center → Key Management
- Maintain 2 active credential sets for seamless rotation
- Validate rotation in sandbox before promoting to production
- Revoke old credentials only after confirming new ones function correctly

---

## Core Payment Flows

### Key Endpoints

| Operation | Method | Endpoint |
|---|---|---|
| Authorization | POST | `/pts/v2/payments` |
| Capture | POST | `/pts/v2/payments/{id}/captures` |
| Void | POST | `/pts/v2/payments/{id}/voids` |
| Refund | POST | `/pts/v2/payments/{id}/refunds` |

### Auth-only vs Auth+Capture

| Use case | `capture` flag | Auth window |
|---|---|---|
| Physical goods (ship later) | `false` (auth-only) | 7 days Visa/MC, 30 days Amex |
| Digital goods (instant delivery) | `true` (auth+capture) | Immediate |
| Pre-auth (hotels, car rental) | `false` with incremental auth | Varies |

**Gotchas:**
- If the auth window expires, capturing is no longer possible — issue a new authorization
- "**Void** cancels before settlement (no money moves). **Refund** returns money after settlement (5-10 business days)."
- "**Never auto-retry a PROCESSOR_DECLINED (code 05).** This is an issuer decision. Retrying can trigger fraud flags."

---

## Tokenization (TMS)

Store payment credentials without handling raw PANs. Never store raw card numbers in your database.

### Token Types

| Type | Use case | Lifetime |
|---|---|---|
| Transient token (UC) | Single-use, created client-side | 15 minutes |
| TMS Payment Instrument | Stored card-on-file | Permanent |
| TMS Instrument Identifier | Network-agnostic token | Permanent |
| Network Token | Visa/MC provisioned token | Managed by network |

### Zero-Dollar Authorization (Store Card Without Charge)

POST `/pts/v2/payments` with `actionList: ["TOKEN_CREATE"]`, `capture: false`, `totalAmount: "0.00"`. Returns a TMS token for permanent storage.

---

## Unified Checkout (UC v1)

> **IMPORTANT:** Always use UC v1 (`/uc/v1/sessions`). Do NOT use the legacy Microform/Flex endpoint `/pts/v2/microform/capture-contexts` — that is a deprecated v0.x API.

Drop-in payment form handling card entry, digital wallets, and 3DS — all PCI-compliant.

### Setup Flow

1. **Server-side:** `POST /uc/v1/sessions` — creates a UC session and returns a `captureContext` JWT
2. **Client-side:** Load the UC JavaScript library with the `captureContext`
3. **On submit:** UC returns a transient token representing the entered card
4. **Server-side:** POST `/pts/v2/payments` with the transient token to authorize

Session lifetime is 15 minutes. Generate a fresh one per checkout session.

### Minimal Session Creation Request

```json
POST /uc/v1/sessions
{
  "targetOrigins": ["https://yoursite.com"],
  "allowedCardNetworks": ["VISA", "MASTERCARD", "AMEX"],
  "allowedPaymentTypes": ["PANENTRY", "SRC", "GOOGLEPAY", "APPLEPAY"]
}
```

Response includes `captureContext` — a JWT passed directly to the UC JS library.

### Passing a TMS Token into UC (Pre-fill Stored Card)

> **High-impact feature.** Include `paymentInformation` in the session creation request to pre-populate UC with a stored card from TMS. The customer sees their saved card without re-entering details.

**Option A — Pass a specific Payment Instrument ID:**

```json
POST /uc/v1/sessions
{
  "targetOrigins": ["https://yoursite.com"],
  "allowedCardNetworks": ["VISA", "MASTERCARD"],
  "allowedPaymentTypes": ["PANENTRY"],
  "paymentInformation": {
    "paymentInstrument": {
      "id": "<TMS_PAYMENT_INSTRUMENT_ID>"
    }
  }
}
```

**Option B — Pass a TMS Customer ID (loads all saved cards for that customer):**

```json
POST /uc/v1/sessions
{
  "targetOrigins": ["https://yoursite.com"],
  "allowedCardNetworks": ["VISA", "MASTERCARD"],
  "allowedPaymentTypes": ["PANENTRY"],
  "paymentInformation": {
    "customerId": "<TMS_CUSTOMER_ID>"
  }
}
```

**Key gotchas for TMS passthrough:**
- The TMS Payment Instrument ID must belong to the same `merchantID` making the session request
- The `captureContext` returned will contain masked card details; the full PAN is never exposed
- On form submit, UC returns a new transient token — do NOT pass the TMS ID directly to the payment API; always use the fresh transient token returned by UC
- If `paymentInstrument.id` is invalid or expired, UC falls back to blank card entry (no error thrown to the merchant)

### Key Gotchas (General UC v1)

- "`targetOrigins` must exactly match your domain (including port if non-standard)"
- The `captureContext` is a JWT — do not decode or modify it client-side
- 406 errors on session creation usually indicate a malformed request body
- Enable wallet types via `allowedPaymentTypes`: `PANENTRY`, `SRC`, `GOOGLEPAY`, `APPLEPAY`

---

## 3D Secure / Payer Authentication

Implements SCA (Strong Customer Authentication) for PSD2 compliance.

### When to Use

- **Required:** EU/EEA transactions (PSD2), high-risk transactions
- **Recommended:** Any card-not-present transaction (reduces chargebacks)
- **Not needed:** Recurring billing after initial enrollment, merchant-initiated transactions

### Frictionless vs Step-Up

| Flow | User experience | When |
|---|---|---|
| Frictionless | No challenge, instant auth | Low-risk transactions |
| Step-Up | Issuer challenge (OTP, biometric) | High-risk or issuer policy |

The issuer decides — frictionless cannot be forced.

---

## Digital Wallets

### Apple Pay

- Client-side: Apple Pay JS API → payment token
- Server-side: POST `/pts/v2/payments` with `paymentSolution: "001"`
- Requires an Apple Developer account and merchant ID registration

### Google Pay

Two paths:
- **With Unified Checkout:** Enable `GOOGLEPAY` in `allowedPaymentTypes` in the `/uc/v1/sessions` request — UC handles everything
- **Standalone:** Google Pay API → encrypted payment data → POST `/pts/v2/payments` with `paymentSolution: "012"`

### Click to Pay (SRC)

Enable `SRC` in `allowedPaymentTypes` in the `/uc/v1/sessions` request. No separate integration required.

---

## Webhooks

Subscribe to payment events via POST `/notification-subscriptions/v1/webhooks` with mutual trust security.

**Event types:** `payments.payments.authorized`, `payments.payments.captured`, `payments.payments.refunded`

**Gotchas:**
- Visa Acceptance webhook IPs must be whitelisted — check developer docs for current ranges
- "Respond with 200 OK within 10 seconds or the webhook will retry"
- Use `webhookId` for deduplication (idempotency)
- TLS 1.2+ required on your endpoint

---

## Recurring Billing

### Stored Credential Framework

| Transaction type | `initiator.type` | `storedCredentialUsed` | Notes |
|---|---|---|---|
| Initial (customer-initiated) | `customer` | `false` | Include `actionList: ["TOKEN_CREATE"]` |
| Subsequent (merchant-initiated) | `merchant` | `true` | Reference the stored token ID |

---

## Testing

### Sandbox Test Cards

| Card | Behavior |
|---|---|
| 4111111111111111 | Visa — approved |
| 5555555555554444 | Mastercard — approved |
| 4000000000000002 | Visa — declined |

Always use `https://apitest.cybersource.com`. Production credentials will not work in sandbox.

---

## Going Live Checklist

1. Get production credentials in Business Center (Production)
2. Change `runEnvironment` to `api.cybersource.com`
3. Replace sandbox credentials with production credentials
4. Same code — no logic changes needed between sandbox and production
5. Verify first production transaction with a real card (small amount, then void)
6. Enable webhook subscriptions on production
7. Set up monitoring and alerting on decline rates

---

## Error Handling

### Common Status Codes

| HTTP Code | Meaning | Action |
|---|---|---|
| 201 | Success (payment created) | Process normally |
| 400 | Invalid request | Check request body, fix validation errors |
| 401 | Unauthorized | Check credentials, key not expired |
| 403 | Forbidden | Check permissions, MLE config |
| 404 | Not found | Check endpoint URL |
| 429 | Rate limited | Back off and retry with exponential delay |
| 502/503 | Server error | Retry with idempotency key |

### Processor Response Codes

| Code | Meaning | Retry? |
|---|---|---|
| 100 | Approved | No — success |
| 05 | Do not honor (issuer decline) | **Never auto-retry** |
| 14 | Invalid card number | No — fix input |
| 51 | Insufficient funds | No — inform cardholder |
| 91 | Issuer unavailable | Yes — retry after delay |

---

## Supported SDKs

| Language | Package | GitHub |
|---|---|---|
| Java | `com.cybersource:cybersource-rest-client-java` | CyberSource/cybersource-rest-client-java |
| Node.js | `cybersource-rest-client-node` | CyberSource/cybersource-rest-client-node |
| Python | `cybersource-rest-client-python` | CyberSource/cybersource-rest-client-python |
| PHP | `cybersource/rest-client-php` | CyberSource/cybersource-rest-client-php |
| Ruby | `cybersource_rest_client` | CyberSource/cybersource-rest-client-ruby |
| .NET | `CyberSource.RestClient.DotNet` | CyberSource/cybersource-rest-client-dotnet |

All SDKs handle JWT/HTTP Signature generation automatically. Set `authenticationType` and credentials — the SDK handles the rest.
