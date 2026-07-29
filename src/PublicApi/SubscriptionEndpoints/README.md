# Subscription billing (Maxio Advanced Billing)

Additive, recurring-subscription capability layered onto eShopOnWeb. It runs in parallel to
the existing one-time Catalog → Basket → Order flow and does not replace it. **Maxio Advanced
Billing (Chargify) is the system of record** — subscriptions are not stored in the local
database.

## Endpoints (JWT-authenticated)

| Method & route | Purpose |
|----------------|---------|
| `GET  /api/subscription-plans` | List the plans available to subscribe to (the products in the configured Maxio product family). |
| `POST /api/subscriptions` | Subscribe the authenticated user to a plan. Body: `{ "planHandle": "eshop-pro" }`. Returns **201 Created** for a new subscription, or **200 OK** with `alreadySubscribed: true` when the user already has a live subscription to that plan. |
| `GET  /api/my-subscriptions` | List the authenticated user's subscriptions. |

The caller's identity is taken from the JWT `name` claim — never from the request body.

## Configuration

Settings are bound from the `Maxio` configuration section. Provide them via **.NET
user-secrets** (or environment configuration) — **never commit the values**:

| Key | Source env var | Notes |
|-----|----------------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Used as the HTTP Basic username (password is the literal `x`). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Site handle; base URL is `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Which product family's products are offered as plans. |
| `Maxio:BaseUrl` | — | Optional. When set, used verbatim instead of deriving from the subdomain. |

```bash
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

## Design

- **Layering** — the contract (`IMaxioBillingService`) and models live in
  `ApplicationCore/Subscriptions`; the implementation (`MaxioBillingService`, a typed
  `HttpClient` using `System.Text.Json` — no third-party SDK) lives in `Infrastructure/Maxio`;
  it is registered in `Program.cs`.
- **User ↔ customer mapping** — the eShopOnWeb user id (JWT name) is stored as the Maxio
  customer `reference` (`eshoponweb:{userId}`). Because Maxio is the store of record, this
  mapping survives even though the local database is in-memory in this environment.
- **Idempotency** (Maxio has no Idempotency-Key support):
  - Customers: lookup-by-reference then create; a duplicate-reference `422` race falls back to
    a re-lookup.
  - Subscriptions: before creating, the customer's existing non-terminal subscriptions are
    checked for the requested plan and returned if present.
  - A per-reference in-process lock serializes concurrent subscribe calls so a double-click
    (even truly simultaneous) cannot create duplicates.
- **Errors** — an unknown plan handle → `400`; upstream Maxio failures → `502` (ProblemDetails).
