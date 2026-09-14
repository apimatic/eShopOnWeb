# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

An additive, parallel capability to the existing catalog/basket/order flow: logged-in shoppers can
subscribe to recurring plans, with **Maxio Advanced Billing** as the system of record. It does not
change the one-time commerce flow.

### Endpoints (all JWT-authenticated; identity comes from the token, not the body)

| Method & route | Purpose |
|---|---|
| `GET /api/subscription-plans` | Lists the plans available for the configured product family. |
| `POST /api/subscriptions` | Subscribes the caller to a plan (`{ "planHandle": "<handle>" }`). Idempotent: ensures a single Maxio customer per user and reuses an existing live subscription to the same plan instead of creating a duplicate. |
| `GET /api/my-subscriptions` | Lists the caller's own subscriptions (plan, price, state, next billing date). |

### Design

- `ApplicationCore` defines the billing-system-agnostic abstraction `ISubscriptionBillingService`
  and its models (`SubscriptionPlan`, `CustomerSubscription`) — no Maxio dependency there.
- `Infrastructure/Billing` implements it with `MaxioBillingService` over the
  `AsadAli.AdvancedBilling.Sdk` client. The idempotency key is the Maxio customer `reference`, set to
  the eShopOnWeb username. All SDK failures (typed 422 errors, transport failures, malformed bodies)
  are translated to `BillingException` so no SDK type or raw provider detail leaks; a provider 4xx
  surfaces as that same client 4xx, everything else as 502.
- Shoppers subscribe **without a payment method** by using invoice/remittance collection
  (`Maxio:PaymentCollectionMethod`, default `remittance`).

### Configuration (`Maxio:` section)

Bind these keys — never hard-code the secret values. In development they are loaded via **user-secrets**;
in production supply them via environment variables / key vault.

| Key | Source env var | Notes |
|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Site API key (HTTP Basic username; password is the literal `x`). Secret. |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Site subdomain. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family whose plans are exposed. |
| `Maxio:BaseUrl` | — | Optional. When set, used verbatim instead of deriving the URL from the subdomain. |
| `Maxio:Environment` | `MAXIO_ENVIRONMENT` | `US` (default) or `EU`. |
| `Maxio:PaymentCollectionMethod` | — | `remittance` (default), `invoice`, `automatic`, or `prepaid`. |
| `Maxio:DebugWireLogging` | — | `true` logs raw Maxio request/response detail (diagnostics only; off by default). |

Load the secrets into user-secrets (values read from the environment, never written to the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets set "Maxio:Environment"         "$MAXIO_ENVIRONMENT"
```
