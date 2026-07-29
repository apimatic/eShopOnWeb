# Maxio Advanced Billing — subscription integration

Adds recurring-subscription billing to eShopOnWeb with **Maxio Advanced Billing** as the system of
record. This is an **additive, parallel** capability exposed on the **PublicApi** project; it does not
touch the existing Catalog → Basket → Order flow.

## Endpoints (JWT-authenticated; identity comes from the token)

| Method & route | Purpose |
|----------------|---------|
| `GET  /api/subscription-plans` | Lists the active plans in the configured Maxio product family. |
| `POST /api/subscriptions`      | Subscribes the caller to a plan (`{ "planHandle": "eshop-pro" }`). Ensures a Maxio customer exists and enrolls them — **idempotent**. |
| `GET  /api/my-subscriptions`   | Lists the caller's subscriptions (empty if never enrolled; never creates anything). |

Get a bearer token from `POST /api/authenticate` first (the storefront cookie does not work here).

## Design

- **`Maxio/`** — a self-contained module:
  - `MaxioClient` — a typed `HttpClient` wrapping the documented Maxio REST endpoints (customers, products,
    subscriptions), with HTTP Basic auth (`apiKey:X`) and retry on `429`/transient errors.
  - `SubscriptionService` — orchestration + idempotency (the hero guarantee).
  - `MaxioIdempotencyGuard` — a per-user (`SemaphoreSlim`) lock so concurrent double-clicks serialize.
- **`SubscriptionPlanEndpoints/` and `SubscriptionEndpoints/`** — the three HTTP endpoints, following the
  project's existing `MinimalApi.Endpoint` (`IEndpoint`) conventions.

### Idempotency (double-click safe)

1. All subscribe work runs under a per-user lock (keyed on the Maxio customer *reference*).
2. The customer *reference* is the eShop user id; Maxio enforces one customer per reference. Ensure =
   lookup-by-reference, else create (a 422 reference race falls back to a re-lookup).
3. Before creating a subscription, existing live subscriptions to the same plan are checked; if one
   exists it is returned unchanged (`alreadyExisted: true`, HTTP 200). A fresh enrollment is HTTP 201.
4. A `uniqueness_token` guards internal retries; a `409` is reconciled by re-reading.

### Card-less plans

The seeded plans require no payment method, so subscriptions are created with **`remittance`** (invoice)
collection. With the default `automatic` collection Maxio would try to capture the plan price at signup
and reject the subscription for having no card on file.

## Configuration

Bound from the `Maxio` configuration section (values supplied via **.NET user-secrets**, never committed):

| Key | Source env var | Notes |
|-----|----------------|-------|
| `Maxio:ApiKey`              | `MAXIO_API_KEY`               | HTTP Basic username (password is `X`). |
| `Maxio:Subdomain`           | `MAXIO_SITE_SUBDOMAIN`        | Used to derive `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY`| Product family whose products are the plans. |
| `Maxio:BaseUrl`             | —                            | Optional; when set, used verbatim instead of the derived host. |

Load the secrets (from the host environment variables) with:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

> **Handles are stable; numeric ids are not.** Maxio reassigns product ids on re-seed, so always
> subscribe by handle (e.g. `eshop-pro`), never by id.
