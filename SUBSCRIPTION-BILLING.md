# Subscription billing (Maxio Advanced Billing)

eShopOnWeb's one-time flow (Catalog → Basket → Order) is unchanged. This adds a **parallel**
capability: a logged-in shopper can browse recurring plans, subscribe to one, and see the
enrolment on their account. **Maxio Advanced Billing is the system of record** — eShopOnWeb stores
no customer or subscription rows of its own.

## Endpoints

All three live on `src/PublicApi` and require a JWT bearer token. The shopper is taken from the
token; no request field names a user, so a caller can only act on their own subscriptions.

| Method | Route | Purpose |
|---|---|---|
| `GET` | `/api/subscription-plans` | Plans on offer, cheapest first. |
| `POST` | `/api/subscriptions` | Subscribe. `201` on first enrolment, `200` on a repeat. |
| `GET` | `/api/my-subscriptions` | The caller's enrolments, newest first. |

`POST /api/subscriptions` body — only `planHandle` is required:

```json
{
  "planHandle": "pro-plan",
  "firstName": "Ada",
  "lastName": "Lovelace",
  "organization": "Analytical Engines Ltd",
  "paymentCollectionMethod": "remittance"
}
```

`firstName`, `lastName` and `organization` are only used the first time a billing customer record
is created; when omitted they are derived from the caller's token. Plan handles are whatever the
configured product family contains — `pro-plan` above is only an example, take the real ones from
`GET /api/subscription-plans`.

### Status codes

| Status | When |
|---|---|
| `200` | Read succeeded, or the caller was already subscribed (`"created": false`). |
| `201` | The subscription was created (`"created": true`). |
| `400` | `planHandle` missing, or Maxio rejected the request. |
| `401` | No/invalid bearer token. |
| `404` | `planHandle` is not offered by the configured product family. |
| `502` / `504` | Maxio answered with something unusable / did not answer in time. |
| `503` | Maxio is not configured on this host, or rejected the API key. The message names the settings at fault. |

## Configuration

Bound from the `Maxio` configuration section. **No value is hard-coded** — the same build runs
against a different Maxio site and a different catalogue by changing configuration alone.

| Key | Required | Default | Notes |
|---|---|---|---|
| `Maxio:ApiKey` | yes | — | Site API key. Sent as the HTTP Basic username with the literal password `x`. |
| `Maxio:Subdomain` | yes¹ | — | Site subdomain; the base address is derived from it. |
| `Maxio:ProductFamilyHandle` | yes | — | Product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | — | When set, used **verbatim** as the API base address instead of deriving one. |
| `Maxio:Environment` | no | `US` | `US` → `https://{subdomain}.chargify.com`, `EU` → `https://{subdomain}.ebilling.maxio.com`. |
| `Maxio:PaymentCollectionMethod` | no | `remittance` | `automatic`, `remittance`, `invoice` or `prepaid`. |
| `Maxio:ReferencePrefix` | no | `eshoponweb` | Prefix for the references this app owns on the Maxio site. |
| `Maxio:TimeoutSeconds` | no | `30` | Per-request budget, retries included. |
| `Maxio:MaxRetryAttempts` | no | `3` | Retries after the first attempt. |
| `Maxio:CatalogCacheSeconds` | no | `60` | Plan/site cache lifetime. `0` disables caching. |

¹ Not required when `Maxio:BaseUrl` is set.

### Secrets

Credentials never enter the repository. In development they live in .NET user-secrets for
`src/PublicApi`; elsewhere use environment variables (`Maxio__ApiKey`, …) or a secret store.

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets set "Maxio:Environment"         "$MAXIO_ENVIRONMENT"
```

Settings are validated on first use rather than at startup, so a host without Maxio credentials
still boots and serves the rest of the API; only the subscription endpoints answer `503`.

## How it works

```
PublicApi/SubscriptionEndpoints   HTTP surface; resolves the subscriber from the bearer token
        │
ApplicationCore                   ISubscriptionBillingService + provider-agnostic models
        │
Infrastructure/Billing/Maxio      MaxioSubscriptionBillingService → MaxioApiClient → Maxio REST
```

### Idempotency — a double-click never enrols twice

Three layers, each covering what the one above cannot:

1. **Per-shopper in-process lock** (`KeyedAsyncLock`) — concurrent requests on one host cannot
   interleave the "already subscribed?" check with the create that follows it.
2. **Live-subscription check** — the shopper's existing Maxio subscriptions are read first; a live
   one for the same plan is returned as-is with `"created": false`.
3. **Deterministic references** — Maxio enforces `reference` as unique per site, so a write that
   slips past 1 and 2 (a second host, a retried request) is rejected and resolved by looking the
   winning record up.

References are readable, so an operator can find a shopper in the Maxio UI by email:

```
customer      eshoponweb:demouser@microsoft.com
subscription  eshoponweb:demouser@microsoft.com:pro-plan
```

A shopper whose subscription has ended can enrol again; the new subscription takes the next
generation (`…:pro-plan:1`).

Because the shopper is keyed on their email rather than on a database row, enrolments survive an
eShopOnWeb restart — which matters when running on the in-memory provider, where the identity
store is rebuilt from scratch on every start.

### Why `remittance` by default

Both demo plans are configured "payment method not required", but a Maxio site still tries to
collect at signup under the default `automatic` collection method and fails with
*"No payment method was on file for the $299.00 balance"*. `remittance` invoices the subscription
instead, so a shopper can subscribe with no card capture and no 3-DS. Override per site with
`Maxio:PaymentCollectionMethod`, or per request with `paymentCollectionMethod`.

### Resilience

`MaxioRetryHandler` retries with exponential backoff and full jitter, honouring `Retry-After`.
A `429` was never processed, so any method may repeat it; a `5xx` or connection failure leaves a
write's outcome unknown, so only `GET` is retried. Maxio's `X-Request-Id` is logged with every
call. The API key is never logged.

## Maxio API surface used

Every path was taken from Maxio's own generated .NET SDK
([maxio-com/ab-dotnet-sdk](https://github.com/maxio-com/ab-dotnet-sdk)) and confirmed against a
live Advanced Billing sandbox before being coded against.

| Purpose | Call |
|---|---|
| Site currency | `GET /site.json` |
| Plan catalogue | `GET /product_families/handle:{handle}/products.json` |
| Find customer | `GET /customers/lookup.json?reference=…` |
| Create customer | `POST /customers.json` |
| Customer's subscriptions | `GET /customers/{id}/subscriptions.json` |
| Create subscription | `POST /subscriptions.json` |
| Find subscription | `GET /subscriptions/lookup.json?reference=…` |

Product families and products are addressed by **handle**, never by numeric id: Maxio reassigns
ids when a catalogue is re-seeded, handles are stable.

## Tests

* `tests/UnitTests/Infrastructure/Billing/Maxio` — enrolment rules, idempotency (including a
  concurrency test and a lost-race test), reference derivation, options/base-address resolution,
  error parsing and the retry policy, all against an in-memory Maxio. No credentials needed.
* `tests/UnitTests/PublicApi/SubscriberIdentityTests.cs` — identity and name derivation from claims.
* `tests/PublicApiIntegrationTests/SubscriptionEndpoints` — routes and authentication. The
  plan-listing test calls the configured Maxio site and reports inconclusive when no credentials
  are present. Nothing in the automated suite writes to the billing provider.

## Verifying it end to end

```bash
# 1. Load credentials into user-secrets (see "Secrets" above), then run the API.
DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true   dotnet run --project src/PublicApi --launch-profile PublicApi

# 2. Get a bearer token (the storefront cookie does not work against PublicApi).
API=https://localhost:26943
TOKEN=$(curl -sk -X POST "$API/api/authenticate" -H 'Content-Type: application/json'   -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)

# 3. Browse the plans and note a handle.
curl -sk -H "Authorization: Bearer $TOKEN" "$API/api/subscription-plans" | jq

# 4. Subscribe -> 201, "created": true.
curl -sk -i -X POST "$API/api/subscriptions" -H "Authorization: Bearer $TOKEN"   -H 'Content-Type: application/json' -d '{"planHandle":"<handle-from-step-3>"}'

# 5. Subscribe again -> 200, "created": false, same subscription id.
# 6. See it on the account.
curl -sk -H "Authorization: Bearer $TOKEN" "$API/api/my-subscriptions" | jq
```

Ports come from `src/PublicApi/Properties/launchSettings.json`. `UseOnlyInMemoryDatabase=true`
avoids the LocalDB dependency; subscriptions still survive a restart, because Maxio holds them.
