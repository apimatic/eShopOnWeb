# Recurring subscriptions (Maxio Advanced Billing)

eShopOnWeb's storefront sells one-time orders (Catalog → Basket → Order). This is a **separate, parallel
capability**: a shopper can subscribe to a recurring plan, with **Maxio Advanced Billing as the system of
record**. Nothing in the existing cart or checkout flow is changed or replaced — no plan, subscription or
customer is stored in the eShopOnWeb database.

## Endpoints

All three live on **`src/PublicApi`** and require a JWT bearer token from `POST /api/authenticate`. The
subscriber's identity always comes from the token, never from the request body.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET`  | `/api/subscription-plans` | Plans available in the configured product family |
| `POST` | `/api/subscriptions`      | Subscribe the caller to a plan (idempotent) |
| `GET`  | `/api/my-subscriptions`   | The caller's subscriptions |

`POST /api/subscriptions` takes an optional body: `{ "planHandle": "eshop-pro" }`. Omit it (or send `{}`)
to use `Maxio:DefaultPlanHandle`. It answers **`201 Created`** when it enrolled the caller and **`200 OK`
with `alreadySubscribed: true`** when the caller already held a live subscription on that plan.

Failures are RFC 7807 problem responses: `400` for a provider rejection, `404` for an unknown plan
handle, `503` when Maxio is unreachable, `502` when the outcome could not be determined, `500` for a
misconfigured integration. Provider and framework exception text is logged, never returned.

## Configuration

Bound from the `Maxio:` configuration section.

| Key | Required | Meaning |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Maxio API key. **Secret** — supply via user-secrets, environment variable or a vault. |
| `Maxio:Subdomain` | yes | Maxio site subdomain, e.g. `cp-exp-2`. |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family holding the plans, e.g. `eshop-subscribe`. |
| `Maxio:BaseUrl` | no | API base address override. When set it is used **verbatim** instead of deriving one from the subdomain. |
| `Maxio:DefaultPlanHandle` | no | Plan used when a subscribe request names none. Falls back to the first plan in the family. |
| `Maxio:PaymentCollectionMethod` | no | Overrides the collection method (see below). |
| `Maxio:CallBudget` | no | Total budget for one provider call including retries. Default 30s. |
| `Maxio:AttemptTimeout` | no | Bound on a single HTTP attempt. Default 10s. |
| `Maxio:MaxRetries` | no | Retries beyond the first attempt. Default 2, minimum 1. |
| `Maxio:CatalogCacheDuration` | no | How long the resolved family id and site facts are cached. Default 30m. |
| `Maxio:LogRequests` | no | Logs each Maxio request's verb, URL and status at `Debug`. Never logs headers or bodies. |

No value is hard-coded, and no catalog id appears anywhere in the build — the product family is resolved
**by handle**, because Maxio reassigns numeric ids when a catalog is re-seeded. A missing required setting
fails at startup rather than on the first shopper's subscribe.

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets set "Maxio:DefaultPlanHandle"   "eshop-pro"
```

## How it is put together

```
ApplicationCore   ISubscriptionBillingService + SubscriptionPlan / CustomerSubscription /
                  Subscriber / SubscribeResult + BillingProviderException   (no SDK types)
Infrastructure    Maxio/  — the AsadAli.AdvancedBilling.Sdk implementation, DI wiring,
                            HTTP handlers, options
PublicApi         SubscriptionEndpoints/ — the three endpoints and their DTOs
```

The domain layer never sees an SDK type, and `PublicApi` never references the SDK. Swapping billing
providers is a change confined to `src/Infrastructure/Maxio`.

### Subscribing is idempotent

A double-click must not enroll a shopper twice. Four mechanisms, each covering a case the others do not:

1. **A derived customer reference.** The Maxio customer is keyed on a reference derived deterministically
   from the caller's identity (`eshoponweb-<email>`, digested if unusually long). Maxio permits only one
   customer per reference, so a repeat can never create a second customer — even from another process.
2. **Look up before creating.** The caller's existing subscriptions are read first; a live one on the same
   plan is returned as-is. Only `canceled`, `expired` and `failed_to_create` count as finished — a state
   this SDK version does not recognise is treated as live, so an unknown state never causes a second
   enrollment.
3. **A single-flight gate per subscriber**, so concurrent requests from one user do not race the check
   above inside this process. It is striped, so memory does not grow with the user count.
4. **At-most-one-send on writes.** The SDK retries a transport failure (connection reset, dropped socket)
   on *any* verb, and that cannot be switched off — a reset thrown after the bytes reached Maxio is
   indistinguishable from one thrown before. `MaxioSingleSendHandler` refuses the re-send, so the write
   reaches Maxio at most once; the flow then **re-reads provider state** to establish what actually
   happened rather than assuming nothing did.

The same reconciliation covers an unreadable response: "I could not read the answer" is never treated as
"this user has no customer", because that would turn a corrupt response into a duplicate enrollment.

### Why subscriptions are created with an invoiced collection method

The seeded plans report `require_credit_card: false`, but that flag governs Maxio's own hosted signup
form — not how the API collects a balance. Left to the site default (`automatic`), Maxio tries to charge a
card for the first period at creation time and rejects the call with
*"No payment method was on file for the $299.00 balance"*.

This API captures no card, so it sets `payment_collection_method` explicitly. Which value is valid depends
on the site's billing architecture, so it is **read from the site** rather than assumed: `remittance` when
`relationship_invoicing_enabled` is true, `invoice` on the legacy Statements architecture. Override with
`Maxio:PaymentCollectionMethod` if a site needs something else.

## Tests

`tests/UnitTests/Maxio` covers the integration through the SDK's `HttpClient` seam — no network. Among
them: plan projection and currency, family resolution by handle, the exact subscribe payload (asserting no
payment fields are sent), the idempotency guard, write-once under a dropped connection, reconciliation
after an unknown write outcome, the failure-kind mapping, and the `Maxio:BaseUrl` override.

```bash
dotnet test tests/UnitTests/UnitTests.csproj --filter FullyQualifiedName~Maxio
```

## Troubleshooting

Turn on wire logging to see exactly what is sent — the SDK surfaces neither URL nor status on success, so
a wrong path or verb otherwise only shows up as a runtime 404:

```bash
Maxio__LogRequests=true "Logging__LogLevel__Microsoft.eShopWeb.Infrastructure.Maxio=Debug" dotnet run --project src/PublicApi
```

- **401 from Maxio** — check `Maxio:ApiKey`.
- **Every call hits `https://subdomain.chargify.com`** — `Maxio:Subdomain` is unset.
- **`500` with "not configured correctly"** — no product family on the site matches
  `Maxio:ProductFamilyHandle`.
