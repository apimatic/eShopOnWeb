# Subscription billing (Maxio Advanced Billing)

An **additive, parallel** capability alongside the existing one-time commerce flow. It lets a
logged-in shopper browse plans, subscribe, and see the subscription reflected in their account.
Maxio Advanced Billing is the system of record — eShopOnWeb stores no subscription state.

## Endpoints (JWT-authenticated; identity comes from the token)

| Method & route | Purpose |
| --- | --- |
| `GET  /api/subscription-plans` | Lists the plans (products in the configured Maxio product family). |
| `POST /api/subscriptions` | Subscribes the caller to a plan. Body: `{ "planHandle": "eshop-pro" }` (optional — defaults to the first advertised plan). Idempotent. |
| `GET  /api/my-subscriptions` | Lists the caller's subscriptions (empty if they never subscribed). |

Get a bearer token from `POST /api/authenticate` first (the storefront cookie does not work here).

## Idempotency (the hero flow)

`POST /api/subscriptions` guarantees a double-click never creates two customers or two subscriptions:

1. A Maxio customer is ensured for the eShopOnWeb user, keyed by the user's immutable id as the
   customer `reference` (Maxio enforces one customer per reference; a lost create race is recovered
   by re-reading).
2. If the user already has a live subscription to the requested plan, it is returned as-is
   (`alreadyExisted: true`).
3. Concurrent attempts for the same user are serialized by an in-process per-user lock so the
   "already subscribed?" check and the create cannot interleave.

## Architecture

- `ApplicationCore/Subscriptions` — domain models, the `IMaxioGateway` abstraction, and
  `SubscriptionService` (idempotency orchestration; gateway-agnostic and unit-tested).
- `Infrastructure/Maxio` — `MaxioGateway` (typed `HttpClient`, Basic auth, retry on 429/5xx),
  settings, and DI wiring (`AddMaxioBilling`).
- `PublicApi/SubscriptionEndpoints` — the three minimal-API endpoints and their DTOs.

## Configuration (`Maxio` section — supply via user-secrets / environment, never commit values)

| Key | Source env var | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | `MAXIO_API_KEY` | HTTP Basic username. |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Used to derive `https://{subdomain}.chargify.com/`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family that holds the plans. |
| `Maxio:BaseUrl` | — | Optional. When set, used verbatim as the API base address. |
| `Maxio:PaymentCollectionMethod` | — | Optional. Defaults to `remittance` (card-less invoice billing). Use `invoice` on legacy Statements sites, or `automatic` when capturing a card. |
