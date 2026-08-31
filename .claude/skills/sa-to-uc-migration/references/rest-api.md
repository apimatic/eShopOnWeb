---
name: cybersource-rest-api-auth
title: Authenticating and Calling the CyberSource REST API (JWT v2)
type: concept
description: How to authenticate CyberSource REST calls with JWT v2 (HS256 shared secret) — the per-request JWT contract (header, claims, digest, signing), hosts and environments, when to prefer the official SDK vs. build it yourself, Message-Level Encryption (MLE), calling /uc/v1/sessions, and corporate TLS proxy handling.
keywords:
  - authentication
  - jwt
  - shared-secret
  - hmac
  - mle
  - rest-api
  - uc-sessions
  - tls-proxy
---

# Authenticating and calling the CyberSource REST API

Unified Checkout is driven by CyberSource REST calls. Auth is **JWT v2 (HS256 shared secret)** — a symmetric JWT signed with your REST shared secret. This is the auth method to implement; don't reach for anything else. (The older HTTP Signature scheme is **deprecated** — don't implement it. JWT v2 shared secret uses the same `key_id` + shared-secret credentials, so there's nothing extra to obtain.)

## Two ways to do the auth — pick per the project

The auth applies only to the **backend REST calls** (`/uc/v1/sessions`, `/pts/v2/payments`). The browser side (`VAS.UnifiedCheckout(...).mount()`) is hand-written JavaScript either way — no SDK replaces it.

- **Official CyberSource SDK (prefer when it fits).** If the project is in a language with an official SDK (Java, Python, Node.js, PHP, Ruby, .NET), use it: configure `authenticationType=jwt` + `jwtKeyType=SHARED_SECRET` and it builds the v2 JWT (header, claims, digest) and MLE for you, and tracks scheme changes so you don't. To find the SDK and its setup for a given language, use the developer MCP if available, otherwise the [CyberSource developer portal](https://developer.cybersource.com). Prefer this whenever the project can take the dependency.
- **Integrate independently (fallback).** When there's no official SDK for the runtime, dependency policy forbids adding one, the footprint matters (serverless/edge), or you only need a single endpoint — build the JWT yourself. The auth is standard HMAC/SHA-256/base64url, so no third-party JWT library is required. The exact contract is below.

## Credentials (three values)

| Value | What it is |
|---|---|
| `MERCHANT_ID` | The merchant ID (often the same as the SA merchant). |
| `KEY_ID` | REST **Shared Secret** key id (a UUID). Goes in the JWT header as `kid`. |
| `SECRET_KEY` | Base64-encoded REST shared secret. **Base64-decode it** to get the HMAC key. |

Generate them in Business Center → Payment Configuration → Key Management → Generate Key → **REST – Shared Secret**. The secret is shown once. These are *different* from SA's `access_key`/`profile_id`/SA-secret — SA credentials do not work for REST, so generate new REST shared-secret keys. This one key pair is the standard credential for all REST calls — there's nothing extra to maintain.

Load them however the project already loads config (see SKILL.md — mirror the existing store; don't impose `.env`). Placeholders are fine to build against; only the live call fails until real values are present.

## The JWT (per request)

**Header:**
```
{ "typ": "JWT", "alg": "HS256", "kid": "<KEY_ID>" }
```

**Payload claims:**
```
iat                     current unix time (seconds)
exp                     iat + 120        (short-lived; regenerate per request)
iss                     <MERCHANT_ID>
jti                     a fresh UUID per request
request-host            the API host (see below), no scheme
request-method          the HTTP method, lowercase (e.g. "post")
request-resource-path   the path only, e.g. "/uc/v1/sessions"
v-c-jwt-version         "2"              (string)
v-c-merchant-id         <MERCHANT_ID>
```
For any request **with a body** (all POSTs), also add:
```
digest                  base64( SHA-256(raw request body bytes) )
digestAlgorithm         "sha-256"
```
The `digest` must be computed over the exact bytes you send — build the body string once, hash that, and send that same string. (CyberSource's own examples show the `digestAlgorithm` value as both `sha-256` and `SHA-256`; either is accepted, and the official SDK sets it for you — one more reason to prefer the SDK.)

**Sign:** `signing_input = base64url(header) + "." + base64url(payload)` (base64url, no `=` padding). `signature = HMAC_SHA256(key = base64_decode(SECRET_KEY), msg = signing_input)`. Token = `signing_input + "." + base64url(signature)`.

**Send:** header `Authorization: Bearer <token>`, `Content-Type: application/json`.

## Hosts / environment

| Env | Host |
|---|---|
| Sandbox/test | `apitest.cybersource.com` |
| Production | `api.cybersource.com` |

`request-host` and the request URL must use the host that matches the credentials. A **401** on an otherwise-correct call almost always means credentials and host are from different environments.

## Message-Level Encryption (MLE)

MLE encrypts the request and/or response payload at the application layer (on top of TLS), and works only with JWT auth — the method used here. **Whether MLE applies is decided by each API endpoint's spec, not by the merchant or MID:** for a given endpoint, request MLE and response MLE are each either *mandatory* (then you must use it), *optional*, or *not supported*. So check the spec for the endpoints you actually call (`/uc/v1/sessions`, `/pts/v2/payments`): use the developer MCP's per-API MLE detail if available, otherwise the [CyberSource developer portal](https://developer.cybersource.com) API reference. On an official SDK, enabling MLE is a config flag; integrating independently, follow the spec's contract for any endpoint that mandates it.

## Calling `POST /uc/v1/sessions`

This endpoint returns a **raw JWT string** (media type `application/jwt`), *not* JSON — don't `.json()` it. Decode the JWT payload (middle segment, base64url) to read `ctx[0].data.clientLibrary` and `ctx[0].data.clientLibraryIntegrity`; hand `{ captureContext: <the raw JWT>, clientLibrary, clientLibraryIntegrity }` to the browser. (Detect it: a JWT is three dot-separated base64url segments and doesn't start with `{`.)

`/pts/v2/payments` (transient-token flow only) returns normal JSON.

## Corporate TLS proxy

Enterprise networks often run a TLS-intercepting proxy whose CA the default trust store doesn't know, so the first CyberSource call fails with `CERTIFICATE_VERIFY_FAILED`. Fix by pointing the HTTP client at the corporate CA bundle:
- Python (`requests`/`urllib`): `REQUESTS_CA_BUNDLE` / `SSL_CERT_FILE`.
- Node: `NODE_EXTRA_CA_CERTS`.

Dev-only last resort: a `VERIFY_SSL=false` style flag that disables verification. **Never in production.**
