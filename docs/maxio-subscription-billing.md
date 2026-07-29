# Maxio Advanced Billing — recurring subscriptions

This is an **additive, parallel** capability alongside the existing one-time commerce flow
(Catalog → Basket → Order). It lets a logged-in shopper browse plans, subscribe, and see
their subscription — with **Maxio Advanced Billing** as the billing system of record.
eShopOnWeb stores **no** local billing state; customers and subscriptions live in Maxio and
are looked up by the user's stable reference.

## Endpoints (PublicApi, JWT-authenticated)

All routes require a bearer token from `POST /api/authenticate`. The caller's identity is
taken from the token — never from the request body.

| Method & route | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | List subscribable plans (products in the configured product family). |
| `POST /api/subscriptions` | Subscribe the caller to a plan. Body: `{ "planHandle": "eshop-pro" }`. |
| `GET /api/my-subscriptions` | List the caller's own subscriptions. |

`POST /api/subscriptions` is **idempotent**:

- It ensures a Maxio customer exists for the user (looked up / created by the user's
  Identity id as the Maxio `reference`).
- If the user already has a live subscription to the plan, it returns that subscription
  with `alreadySubscribed: true` and HTTP `200` — no duplicate is created.
- A brand-new subscription returns `alreadySubscribed: false` and HTTP `201`.
- Concurrent double-clicks for the same user+plan are serialized in-process, so only one
  subscription is ever created.

## Configuration

Settings are bound from the **`Maxio`** configuration section:

| Key | Source env var | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | `MAXIO_API_KEY` | HTTP Basic auth username (`{key}:X`). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Used to derive the API host `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family whose products are the plans. |
| `Maxio:BaseUrl` | `MAXIO_BASE_URL` (optional) | Explicit base-URL override; used verbatim when set instead of deriving from the subdomain. |

**Secrets never live in the repository.** Provide values via environment variables or
.NET user-secrets. `appsettings.json` contains an empty, documented placeholder section only.
The `MAXIO_*` environment variables are also mapped onto the `Maxio:*` keys at startup, so the
same build can target a different Maxio site/catalog purely by changing environment variables.

Maxio configuration is validated **lazily** (on first use of the billing service), so the
storefront and the rest of PublicApi start and run normally even when Maxio is not configured.

## Design

- `ISubscriptionBillingService` (ApplicationCore) is the abstraction; `MaxioBillingService`
  (Infrastructure) is the Maxio-backed implementation. The API layer never sees Maxio JSON.
- A typed `HttpClient` carries the Basic-auth header and base address. A retry
  `DelegatingHandler` backs off on HTTP 429 and transient 5xx/network faults (honoring
  `Retry-After`), matching Maxio's concurrency-based rate limiting guidance.
- The seeded demo plans do not require a stored payment method; subscriptions are created
  with `remittance` (invoice) collection so enrollment works without card capture / 3-DS.

## Local verification

See the "Verify it yourself" steps in the pull-request / handoff notes, or:

1. Load secrets into user-secrets for the PublicApi project (see the table above).
2. Run PublicApi with `DOTNET_ROLL_FORWARD=Major` and `UseOnlyInMemoryDatabase=true`.
3. `POST /api/authenticate` as `demouser@microsoft.com` / `Pass@word1` to get a token.
4. `GET /api/subscription-plans`, `POST /api/subscriptions {"planHandle":"eshop-pro"}`,
   then `GET /api/my-subscriptions`.
