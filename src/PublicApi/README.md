# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

---

# Subscription billing (Maxio Advanced Billing)

eShopOnWeb's original flow is one-time commerce: Catalog → Basket → Order. Subscription billing is an
**additive, parallel** capability that does not touch it. **Maxio Advanced Billing** is the system of
record: plans, customers and subscriptions live there, and this application stores none of them
locally.

## Endpoints

All three are JWT-authenticated. Get a token from `POST /api/authenticate` first; the storefront's
cookie will not work here. The caller's identity is taken from the token — no request field can
change whose subscription is read or created.

| Route | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Plans on offer, taken from the configured Maxio product family. |
| `POST /api/subscriptions` | Subscribe the caller to a plan. Idempotent. |
| `GET /api/my-subscriptions` | The caller's subscriptions, newest first. |

`POST /api/subscriptions` takes `{ "planHandle": "eshop-pro", "idempotencyKey": "optional" }` and
answers `201 Created` for a new enrollment or `200 OK` with `"created": false` when an equivalent
one already existed.

## Configuration

Bound from the `Maxio` configuration section:

| Key | Required | Meaning |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Maxio API key. Sent as the HTTP Basic user name with the documented fixed password `x`. |
| `Maxio:Subdomain` | yes* | Site subdomain, e.g. `acme` in `acme.chargify.com`. |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Overrides the API base address; used verbatim when set. |
| `Maxio:PaymentCollectionMethod` | no | Defaults to `remittance`. See below. |
| `Maxio:Timeout` | no | Per-attempt request timeout, default `00:00:30`. |
| `Maxio:MaxRetryAttempts` | no | Transient-failure retries, default `3`. |
| `Maxio:RetryBaseDelay` | no | Backoff base, default `00:00:00.500`. |

\* Required only when `Maxio:BaseUrl` is not set. When `BaseUrl` is absent the address is derived as
`https://{Subdomain}.chargify.com`. EU-hosted sites should set `Maxio:BaseUrl` to
`https://{subdomain}.ebilling.maxio.com`.

Nothing about a particular Maxio site or catalog is compiled in: the same build runs against any
site and any product family.

**Secrets never go in the repository.** Load them from your environment into .NET user-secrets:

```bash
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

Environment variables work too (`Maxio__ApiKey`, `Maxio__Subdomain`, ...), which is the usual choice
outside development.

If the section is missing the host still starts and every other endpoint keeps working; the three
subscription routes answer `503` naming the settings that need a value, and startup logs a warning
saying the same.

## How idempotency works

Maxio enforces uniqueness on the `reference` a caller supplies for a customer and for a subscription,
and exposes lookup-by-reference for both. This integration uses that as its idempotency key:

- customer reference — `eshoponweb:{userName}`
- subscription reference — `eshoponweb:{userName}:{planHandle}` (plus `:{idempotencyKey}` when given)

Subscribing therefore looks up before it creates, and if a create is rejected it re-reads by
reference to see whether a concurrent or retried request already did the work. A double-clicked
Subscribe button produces one customer and one subscription, and so do six simultaneous requests.

Because the key is a pure function of the authenticated user name, **no local userId-to-subscription
table exists** — which matters on this machine, where the in-memory database loses everything on
restart. The mapping lives in Maxio and outlives the process. The user *name* is used rather than the
ASP.NET Identity row id precisely because those ids are regenerated whenever the identity store is
rebuilt.

Subscribing again to a plan whose previous subscription has ended (`canceled`, `expired`,
`trial_ended`, `failed_to_create`) answers `409` rather than silently returning the dead
subscription. Pass a distinct `idempotencyKey` to enroll again deliberately.

## Payment collection

Both demo plans are configured with *payment method not required*, but Maxio still needs a way to
collect the first period's charge: creating a subscription with the default `automatic` collection
fails with *"No payment method was on file for the $299.00 balance"*. Subscriptions are therefore
created with `payment_collection_method` = `remittance` (Relationship Invoicing), which invoices the
customer instead of charging a card, so signup needs no card capture and no 3-DS. Sites on the legacy
Statements architecture should set `Maxio:PaymentCollectionMethod` to `invoice`; sites that do capture
cards should set it to `automatic`.

## Failure mapping

| Situation | Status |
| --- | --- |
| Plan handle not offered by the configured product family | `404` |
| Previous subscription in the same scope has ended | `409` |
| Maxio rejected the request | `422` |
| Maxio unreachable or answering unexpectedly | `502` |
| `Maxio` section missing or incomplete, or API key rejected | `503` |

Transient failures (`429`, `5xx`, connection faults) are retried with exponential backoff and full
jitter before any of that. Advanced Billing returns no rate-limit or `Retry-After` headers, so the
backoff is driven entirely from the client; a `Retry-After` header is honoured if one appears.

## Layout

| Path | Contents |
| --- | --- |
| `src/ApplicationCore/Subscriptions` | Domain model and `ISubscriptionBillingService`; no provider details. |
| `src/ApplicationCore/Exceptions/Billing*.cs` | Billing failure taxonomy. |
| `src/Infrastructure/Billing/Maxio` | Settings, typed HTTP client, retry handler, reference factory, service. |
| `src/PublicApi/SubscriptionEndpoints` | The three endpoints and their DTOs. |
| `tests/UnitTests/Billing/Maxio` | Provider behaviour over a stubbed transport. |

## Verifying it end to end

```bash
# 1. Run the API (the ASP.NET Core 8 runtime is present; only the 8.0 SDK is not).
DOTNET_ROLL_FORWARD=Major UseOnlyInMemoryDatabase=true \
  dotnet run --project src/PublicApi

# 2. Get a bearer token.
TOKEN=$(curl -sS -X POST https://localhost:26863/api/authenticate \
  -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)

# 3. Browse plans.
curl -sS https://localhost:26863/api/subscription-plans -H "Authorization: Bearer $TOKEN" | jq

# 4. Subscribe (201 the first time).
curl -sS -i -X POST https://localhost:26863/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}'

# 5. Subscribe again — 200 with "created": false, and no second subscription.
curl -sS -i -X POST https://localhost:26863/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"planHandle":"eshop-pro"}'

# 6. See it on the account.
curl -sS https://localhost:26863/api/my-subscriptions -H "Authorization: Bearer $TOKEN" | jq
```

Swagger UI at `https://localhost:26863/swagger` lists the endpoints under **SubscriptionEndpoints**;
use *Authorize* with `Bearer <token>` to call them from the browser.
