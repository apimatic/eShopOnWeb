# Subscription billing with Maxio Advanced Billing

eShopOnWeb's storefront sells one-time orders (Catalog → Basket → Order). This capability adds
**recurring subscriptions** alongside it, with **Maxio Advanced Billing** as the system of record.
Nothing in the existing cart or checkout flow changes.

Everything the integration does over the wire is defined by the OpenAPI specification in
[`maxio-spec/`](../maxio-spec/openapi.yaml): paths, query and path parameters, request and response
schemas, the `BasicAuth` security scheme and the server templates all come from there.

## Endpoints

All three live on `src/PublicApi` and require a JWT bearer token; the shopper is taken from the
token, never from the request body.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | The plans a shopper can subscribe to (the products of the configured Maxio product family). |
| `POST /api/subscriptions` | Subscribe the caller to a plan. Body: `{ "planHandle": "eshop-pro" }`. Optional `firstName`, `lastName`, `organization` are used only when the billing customer is created. |
| `GET /api/my-subscriptions` | The subscriptions the caller holds, most recent first. |

`POST /api/subscriptions` answers **201 Created** when it enrolls the shopper and **200 OK** with
`"alreadySubscribed": true` when they already hold that subscription.

Status codes: `401` without a token, `404` for an unknown plan handle, `400` when the request cannot
be fulfilled as asked (no plan handle while several plans are published, or a payload Maxio
rejects), `502` when Maxio is unreachable, `503` when Maxio is not configured.

## Configuration

Settings are bound from the `Maxio` section. No value is ever hard-coded, and none belongs in the
repository - use user-secrets locally, and environment variables or a secret store elsewhere.

| Key | Meaning |
| --- | --- |
| `Maxio:ApiKey` | API key, sent as the basic-auth user name with the password `x`, per the specification's `BasicAuth` scheme. |
| `Maxio:Subdomain` | The Maxio site subdomain, substituted into the specification's server template `https://{site}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | The product family whose products are published as plans. A numeric family id also works. |
| `Maxio:BaseUrl` | Optional. When set it is used verbatim as the API base address, instead of deriving one from the subdomain. |
| `Maxio:Environment` | Optional, `US` (default) or `EU`. `EU` selects the `https://{site}.ebilling.maxio.com` server from the specification's environment list. Ignored when `Maxio:BaseUrl` is set. |

Loading the sandbox credentials into user-secrets (values come from the environment, so they never
appear in a file):

```powershell
dotnet user-secrets set "Maxio:ApiKey"              $env:MAXIO_API_KEY             --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain"           $env:MAXIO_SITE_SUBDOMAIN      --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi/PublicApi.csproj
```

User-secrets are only read in the `Development` environment. In any other environment supply the
same keys as environment variables (`Maxio__ApiKey`, `Maxio__Subdomain`, …).

If the section is missing or incomplete the rest of the API keeps working: start-up logs a warning
and the three endpoints answer `503 Service Unavailable`.

## How it works

### Layers

| Where | What |
| --- | --- |
| `src/ApplicationCore/Subscriptions`, `Interfaces/ISubscriptionService.cs` | The capability's vocabulary - plans, subscriptions, states - with no knowledge of Maxio. |
| `src/Infrastructure/Billing/Maxio` | `MaxioApiClient` (one method per specification operation), the options, the authentication and retry handlers, and `MaxioSubscriptionService`, which orchestrates them. |
| `src/PublicApi/SubscriptionEndpoints` | The three endpoints, following the project's existing endpoint conventions. |

### Operations used

| Specification operation | Used for |
| --- | --- |
| `listProductsForProductFamily` — `GET /product_families/{product_family_id}/products.json` | Publishing plans, and validating that a requested plan belongs to the configured family. |
| `readCustomerByReference` — `GET /customers/lookup.json` | Finding the shopper's billing customer. |
| `createCustomer` — `POST /customers.json` | Creating it on first subscribe. |
| `listCustomerSubscriptions` — `GET /customers/{customer_id}/subscriptions.json` | Reading what the shopper holds, and recognising a repeat subscribe. |
| `createSubscription` — `POST /subscriptions.json` | Enrolling the shopper. |
| `readSite` — `GET /site.json` | The currency prices are quoted in, and the invoicing architecture. |

### Idempotency

A double-click, a client retry or a second browser tab must not produce two customers or two
subscriptions.

* The billing customer carries a **deterministic reference** derived from the eShopOnWeb user name
  (`eshoponweb:demouser@microsoft.com`). Subscribe looks the customer up by that reference and only
  creates one when there is none. Maxio permits a single customer per reference, so if a concurrent
  request wins the race the `422` is caught, the customer is looked up again, and the winner is
  reused.
* Before enrolling, the shopper's existing subscriptions are checked for one to the same plan that
  has not ended. If there is one it is returned as-is, with `alreadySubscribed: true` and `200 OK`.
* Requests for the same shopper are serialised within the process, so the read-then-write above is
  not interleaved.
* The subscription itself gets a readable reference (`<customer reference>:<plan handle>`). If the
  shopper previously held - and ended - a subscription to that plan, the new one is given a distinct
  suffix rather than colliding with the old reference.

### Payment collection

The seeded plans do not require a payment method and eShopOnWeb does not capture card details, so an
automatic charge at signup would fail for want of a card. New subscriptions are therefore invoiced,
using the specification's `Collection-Method` values: `remittance` on Relationship Invoicing sites
and `invoice` on legacy Statements sites (`readSite` says which). A plan that *does* require a
payment method is left on `automatic`, so Maxio charges whatever profile the customer already has
and reports plainly when there is none.

### Reliability

* Reads are retried (up to three attempts, exponential backoff with jitter, `Retry-After` honoured)
  on transport faults, timeouts and transient statuses. Writes are retried **only** on `429`, which
  means the request was throttled before it was processed - repeating any other failed write could
  create a duplicate customer or subscription.
* Each attempt has its own timeout, inside an overall budget for the call.
* The plan catalogue is cached for a minute and the site for ten, so browsing and subscribing do not
  re-read the same catalogue on every request.
* The API key is read per request, so a rotated secret takes effect without a restart. It is never
  logged, and query strings - which can carry a customer reference - are stripped from log messages.

### State

Nothing about a shopper's billing is stored in the eShopOnWeb database; `my-subscriptions` reads
Maxio each time. That matters on this machine, where the app runs with
`UseOnlyInMemoryDatabase=true`: subscriptions are still there after a restart.

## Verifying it

Prerequisites: the .NET SDK, a trusted HTTPS dev certificate (`dotnet dev-certs https --check`), and
the user-secrets above. The SDK on this machine is .NET 10 while the projects target .NET 8, so
`global.json` rolls forward and `DOTNET_ROLL_FORWARD=Major` is set.

1. **Run the API** (ports come from `launchSettings.json`):

   ```powershell
   $env:DOTNET_ROLL_FORWARD="Major"; $env:UseOnlyInMemoryDatabase="true"
   dotnet run --project src/PublicApi/PublicApi.csproj --launch-profile PublicApi
   ```

   The log should read `Maxio subscription billing is configured against https://<site>.chargify.com
   for product family <family>`.

2. **Get a bearer token** (the storefront cookie does not work here):

   ```bash
   TOKEN=$(curl -sk -X POST https://localhost:26183/api/authenticate \
     -H "Content-Type: application/json" \
     -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)
   ```

3. **Browse the plans** - expect the product family's plans with prices and billing period:

   ```bash
   curl -sk https://localhost:26183/api/subscription-plans -H "Authorization: Bearer $TOKEN"
   ```

4. **Subscribe** - expect `201` and a subscription with plan, price, `state: active` and
   `nextBillingAt` one period out:

   ```bash
   curl -sk -X POST https://localhost:26183/api/subscriptions -H "Authorization: Bearer $TOKEN" \
     -H "Content-Type: application/json" -d '{"planHandle":"eshop-pro"}'
   ```

5. **Subscribe again** (the double-click) - expect `200`, the *same* subscription id, and
   `"alreadySubscribed": true`.

6. **See it on the account** - expect the subscription to be listed:

   ```bash
   curl -sk https://localhost:26183/api/my-subscriptions -H "Authorization: Bearer $TOKEN"
   ```

7. **Check the error paths**: no token → `401`; `{"planHandle":"does-not-exist"}` → `404` naming the
   available plans; `{}` while several plans are published → `400`.

8. **Confirm in Maxio** that the shopper has exactly one customer record and at most one *live*
   subscription per plan - subscriptions cancelled earlier stay on the account as history - in the
   Maxio UI, or:

   ```bash
   curl -s -u "$MAXIO_API_KEY:x" \
     "https://$MAXIO_SITE_SUBDOMAIN.chargify.com/customers/lookup.json?reference=eshoponweb%3Ademouser%40microsoft.com"
   ```

9. **Run the tests**: `dotnet test tests/UnitTests/UnitTests.csproj` covers the client, the retry
   policy, the options and the idempotency rules against a stub transport;
   `dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj` covers the routing,
   the bearer-token requirement and the unconfigured-billing behaviour. Neither suite touches Maxio.
