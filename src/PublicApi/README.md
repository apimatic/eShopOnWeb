# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

eShopOnWeb's own commerce flow (catalog → basket → order) is one-time purchase. Alongside it, this API
exposes **recurring-subscription billing**, with [Maxio Advanced Billing](https://www.maxio.com/) as the
system of record. It is an additive, parallel capability: nothing in the cart or checkout flow changes, and a
deployment with no Maxio configuration still serves every other endpoint.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | The plans a shopper can subscribe to, with price, billing interval and currency. |
| `POST /api/subscriptions` | Subscribe the signed-in shopper to a plan. |
| `GET /api/my-subscriptions` | The signed-in shopper's subscriptions, with state and next billing date. |

All three require a JWT bearer token from `POST /api/authenticate`; the shopper is identified from the token,
never from the request body. Add `?includeInactive=true` to `my-subscriptions` to include cancelled and
expired subscriptions.

The port below is this project's `launchSettings.json` HTTPS port; adjust if you run it elsewhere.

```bash
TOKEN=$(curl -sk -X POST https://localhost:26443/api/authenticate \
  -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)

curl -sk https://localhost:26443/api/subscription-plans -H "Authorization: Bearer $TOKEN"

curl -sk -X POST https://localhost:26443/api/subscriptions \
  -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"planHandle":"eshop-pro"}'
```

Subscribing answers **201** the first time and **200** with `"alreadySubscribed": true` thereafter.

### Configuration

Bound from the `Maxio` section. **The API key is a secret — keep it in user-secrets or the environment, never
in `appsettings*.json`.**

| Key | Required | Meaning |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Maxio API key, sent as the basic-auth user name. |
| `Maxio:Subdomain` | yes, unless `BaseUrl` is set | Maxio site subdomain. |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family whose products are published as plans. |
| `Maxio:BaseUrl` | no | Verbatim base-address override, used instead of the address derived from the subdomain. |
| `Maxio:Environment` | no | `US` (default) or `EU`. |
| `Maxio:HttpTimeoutSeconds` | no | Bounds one HTTP attempt. Default 15. |
| `Maxio:AttemptTimeoutSeconds` | no | Bounds one SDK attempt inside its retry pipeline. Default 10. |
| `Maxio:CallBudgetSeconds` | no | Bounds a whole operation, retries and backoff included. Default 30. |
| `Maxio:CatalogCacheSeconds` | no | How long the resolved product family and site facts are cached. Default 300. |
| `Maxio:LogRequests` | no | Logs every outbound Maxio request and status at Debug level. Default false. |

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

Without a usable `Maxio` section the host still starts, logs a warning naming the missing keys, and the three
endpoints above answer **503**.

### How it is put together

- `ApplicationCore/Interfaces/ISubscriptionBillingService.cs` — the capability, expressed in eShopOnWeb's own
  terms. `ApplicationCore/Billing` holds its plain DTOs; no SDK type reaches this layer.
- `Infrastructure/Billing` — the Maxio implementation, the SDK client registration, and the integration
  boundary that converts every provider, transport and deserialization failure into a single
  `BillingException`.
- `PublicApi/SubscriptionEndpoints` — the three endpoints, following this project's `IEndpoint` convention.

Three properties are worth knowing about, because they are what make the flow safe to expose to a browser:

**Only handles cross the boundary.** Maxio reassigns numeric ids whenever a catalog is re-seeded, so no
numeric id appears in configuration or in code. The product family is resolved from its handle, cached
briefly, and re-resolved automatically the moment Maxio stops recognising it.

**Subscribing is idempotent.** The billing customer and the subscription are keyed on references derived
deterministically from the shopper's identity, and the subscribe flow serializes per shopper, checks Maxio
before writing, and reconciles afterwards. A double-click — or eight concurrent requests — creates one
customer and one subscription.

**A write is never re-sent.** The SDK retries a dropped connection on every verb, `POST` included, and retries
cannot be disabled; a reset thrown after the bytes arrived is indistinguishable from one thrown before. A
message handler therefore allows exactly one send per write and refuses the rest, and the caller settles what
happened by re-reading Maxio. If it cannot, the API answers **502** — an unknown outcome, never something that
looks safe to retry.

Because this API captures no payment method and runs no 3-D Secure flow, subscriptions ask to be billed rather
than charged (the collection method valid for the site's billing architecture). Plans that require a card are
rejected up front with a clear message rather than failing deep inside Maxio.
