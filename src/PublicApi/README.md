# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Recurring subscriptions (Maxio Advanced Billing)

An additive capability that runs in parallel to the one-time Catalog → Basket → Order flow. Maxio
Advanced Billing is the system of record: eShopOnWeb stores no subscription state of its own.

| Endpoint | Purpose |
|----------|---------|
| `GET /api/subscription-plans` | The plans on offer - the products of the configured Maxio product family. |
| `POST /api/subscriptions` | Subscribes the caller to a plan. Idempotent. |
| `GET /api/my-subscriptions` | The caller's subscriptions. Read-only; never creates a billing record. |

All three require a bearer token from `POST /api/authenticate`. The caller's identity comes from the
token: the user name claim is the stable key, so the mapping to a Maxio customer survives restarts
even when the app runs on the in-memory database.

### The contract

Every Maxio interaction is built against `maxio-spec/openapi.yaml`. Six operations are used, and
each method of `IMaxioApiClient` names the `operationId` it implements:

| operationId | Request |
|-------------|---------|
| `listProductsForProductFamily` | `GET /product_families/handle:{family}/products.json` |
| `readCustomerByReference` | `GET /customers/lookup.json?reference=…` |
| `createCustomer` | `POST /customers.json` |
| `listCustomerSubscriptions` | `GET /customers/{customer_id}/subscriptions.json` |
| `findSubscription` | `GET /subscriptions/lookup.json?reference=…` |
| `createSubscription` | `POST /subscriptions.json` |

Authentication is HTTP Basic with the API key as the user name and `x` as the password, exactly as
the specification's `BasicAuth` security scheme states. The base address comes from the
`x-server-configuration` block: `https://{site}.chargify.com` for the US environment and
`https://{site}.ebilling.maxio.com` for the EU one.

### How subscribing stays idempotent

A double-click must not produce two customers or two subscriptions. Three mechanisms combine:

1. **The customer reference.** Every eShopOnWeb user maps to a Maxio customer whose `reference` is
   derived deterministically from their user name (`eshop-demouser@microsoft.com`). Maxio enforces
   that references are unique per site, so the shopper resolves to the same customer on every call,
   from any host. A lost race surfaces as a 422 and is settled by re-reading the winner's customer.
2. **The live-subscription check.** Before enrolling, the caller's subscriptions are read and matched
   on plan handle plus a live state. A match is returned as-is with `created: false` and HTTP 200.
   End-of-life states (`canceled`, `expired`, …) do not match, so re-subscribing after cancelling works.
3. **A per-shopper lock.** Concurrent requests for the same shopper are serialised in-process so the
   check-then-create sequence cannot interleave with itself.

Callers that want cross-process replay safety can pass an `idempotencyKey`; it is stored as the
subscription's `reference` and looked up with `findSubscription` before anything else happens.

### Configuration

Bound from the `Maxio` section. Secrets never live in the repository - load them into user-secrets:

```powershell
dotnet user-secrets set "Maxio:ApiKey"              $env:MAXIO_API_KEY               --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           $env:MAXIO_SITE_SUBDOMAIN        --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi
dotnet user-secrets set "Maxio:Environment"         $env:MAXIO_ENVIRONMENT           --project src/PublicApi
```

| Key | Default | Notes |
|-----|---------|-------|
| `Maxio:ApiKey` | – | Required. Basic-auth user name. |
| `Maxio:Subdomain` | – | Required unless `Maxio:BaseUrl` is set. |
| `Maxio:ProductFamilyHandle` | – | Required. The family whose products become plans. |
| `Maxio:BaseUrl` | – | Optional override, used verbatim when set. |
| `Maxio:Environment` | `US` | `US` or `EU`; selects the server template. |
| `Maxio:PaymentCollectionMethod` | `remittance` | Empty falls back to the site default. See below. |
| `Maxio:CustomerReferencePrefix` | `eshop-` | Prefix on the customer reference. |
| `Maxio:RequestTimeoutSeconds` | `30` | Per HTTP attempt. |
| `Maxio:MaxRetryAttempts` | `3` | Retries after the first attempt. |
| `Maxio:PlanCacheSeconds` | `60` | `0` disables plan caching. |

`PaymentCollectionMethod` defaults to `remittance` because eShopOnWeb enrolls shoppers without
capturing a payment method; `automatic` collection fails at signup on a Relationship Invoicing site
when no payment profile is on file. Switch it to `automatic` once the storefront captures payment
profiles.

The host stays startable without these settings: registration never fails, and the three endpoints
answer `503` naming the keys that are missing.

### Failure handling

`ExceptionMiddleware` maps the billing exception hierarchy onto status codes: `404` for an unknown
plan, `400` for a request Maxio rejected as invalid, `503` when this host has no billing credentials,
and `502` for any other upstream failure. Transient faults are retried before that: `429` for any
method (honouring `Retry-After`) and `5xx` or transport faults for safe methods only - a `POST` is
never replayed, because that could enroll a shopper twice.
