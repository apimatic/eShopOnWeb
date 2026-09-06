# Subscription billing (Maxio Advanced Billing)

Recurring-subscription billing for eShopOnWeb, running **in parallel** to the one-time
Catalog → Basket → Order flow. **Maxio Advanced Billing is the system of record**: eShopOnWeb
stores no local copy of customers, plans or subscriptions.

Every Maxio interaction is built against the OpenAPI specification in [`maxio-spec/`](../../../maxio-spec)
— endpoints, parameters, schemas, error models, the `BasicAuth` security scheme and the
`x-server-configuration` server templates all come from there.

## Endpoints

All three require a JWT bearer token from `POST /api/authenticate`; the shopper's identity is taken
from the token, never from the request body.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/subscription-plans` | Plans published in the configured product family. |
| `POST` | `/api/subscriptions` | Subscribe the caller to a plan. Body: `{ "planHandle": "eshop-pro" }`. |
| `GET` | `/api/my-subscriptions` | The caller's own subscriptions, newest first. |

`POST /api/subscriptions` answers **201 Created** when it enrolls the shopper and **200 OK** with
`alreadySubscribed: true` when a live subscription for that plan already exists.

## How the pieces fit

| Layer | Type | Role |
| --- | --- | --- |
| ApplicationCore | `ISubscriptionBillingService` | The port the endpoints depend on. |
| ApplicationCore | `Subscriber`, `SubscriptionPlan`, `CustomerSubscription`, `SubscriptionStates` | Provider-agnostic models. |
| Infrastructure | `MaxioApiClient` | Typed `HttpClient` over the specified operations. |
| Infrastructure | `MaxioSubscriptionBillingService` | Idempotent enrollment, plan catalogue, caching. |
| Infrastructure | `MaxioRetryHandler` | Transient-failure retries with back-off and `Retry-After`. |
| PublicApi | `SubscriptionEndpoints/*` | HTTP surface and DTO mapping. |

## Identity and idempotency

The link between an eShopOnWeb shopper and a Maxio customer is the customer **`reference`**,
`eshoponweb-{user name}`. Maxio enforces uniqueness on it, so "ensure a customer exists" is a safe
upsert: look up by reference, create when absent, and re-read if a concurrent caller won the race.

Subscribing is idempotent per (shopper, plan). Concurrent attempts are serialised per shopper, and
the flow checks the customer's existing subscriptions for a live one on the requested plan before
creating anything. A double-click therefore produces one customer and one subscription.

## Payment collection

eShopOnWeb never captures card or bank details. The site default collection method (`automatic`)
would reject a priced signup with *"No payment method was on file"*, so subscriptions are created
for invoice-style collection instead — `remittance` on Relationship Invoicing sites, `invoice` on
legacy Statements sites, decided from `GET /site.json`. A plan whose product has
`require_credit_card: true` is rejected up front with **422** rather than half-provisioned.

## Configuration

Bound from the `Maxio` configuration section. **Never commit these values** — use user-secrets in
development and environment variables (`Maxio__ApiKey`, …) elsewhere.

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Basic-auth user name; the password is the literal `x`. |
| `Maxio:Subdomain` | yes, unless `BaseUrl` is set | Substituted into the specification's `{site}` server template. |
| `Maxio:ProductFamilyHandle` | yes | Only this family's products are published as plans. |
| `Maxio:BaseUrl` | no | Absolute override; used verbatim when set. |
| `Maxio:Environment` | no | `US` (default) or `EU`, matching the specification's `x-server-configuration`. |
| `Maxio:TimeoutSeconds` | no | Default `30`. |
| `Maxio:MaxRetryAttempts` | no | Default `3`. |
| `Maxio:RetryBaseDelayMilliseconds` | no | Default `250`. |
| `Maxio:CatalogCacheSeconds` | no | Plan/site cache lifetime; default `60`, `0` disables. |

Settings are validated at start-up, so a misconfigured deployment fails immediately rather than on
the first shopper's request.

```bash
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets --project src/PublicApi set "Maxio:Environment"         "$MAXIO_ENVIRONMENT"
```

## Failure mapping

`ExceptionMiddleware` translates billing failures into statuses a caller can act on:

| Condition | Status |
| --- | --- |
| Unknown plan handle | `404` |
| Plan requires a stored payment method | `422` |
| Maxio rejected the payload (`400`/`422`) | `422`, with Maxio's messages |
| Maxio rate limited us (`429`) | `503` |
| Bad credentials, outage, unreachable | `502` |
