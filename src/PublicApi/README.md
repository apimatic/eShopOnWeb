# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

An additive, parallel capability alongside the one-time catalog/basket/order flow: shoppers can subscribe
to a recurring plan. **Maxio Advanced Billing is the system of record** — no plan, customer or subscription
is stored in the eShopOnWeb database, so the capability works unchanged on the in-memory provider and
survives a restart.

| Endpoint | Purpose |
|---|---|
| `GET /api/subscription-plans` | The plans on offer (the products of the configured Maxio product family). |
| `POST /api/subscriptions` | Subscribe the caller to a plan. Body: `{ "planHandle": "..." }`; omit it to use the configured default plan. |
| `GET /api/my-subscriptions` | The caller's own subscriptions. |

All three are JWT-authenticated and take the shopper's identity from the token — never from the request
body. Get a token from `POST /api/authenticate` first; the storefront's cookie will not work here.

### How the shopper is identified

The Maxio customer `reference` is derived from the login name
(`MaxioCustomerReference`) rather than stored, which is what lets the lookup be re-derived on every request.
It is a digest, so the e-mail address is not duplicated into a third-party identifier — the address itself
is still on the Maxio customer record.

### Idempotency

Subscribing twice is safe. Four layers cooperate:

1. a per-shopper gate serializes concurrent attempts within the process;
2. a read-before-write finds an existing customer (Maxio enforces one customer per reference) and an
   existing live subscription to the same plan;
3. a `DelegatingHandler` refuses a write the SDK's retry pipeline tries to re-send after a transport
   failure — the pipeline retries on any verb, so the duplicate has to be stopped before it reaches the
   network;
4. a failed or unconfirmed write is reconciled by re-reading Maxio rather than assumed to have had no
   effect.

A repeat call answers `200 OK` with `alreadySubscribed: true`; a first call answers `201 Created`.

### Configuration

Bound from the `Maxio` section. Only the first three are required.

| Key | Meaning |
|---|---|
| `Maxio:ApiKey` | Site API key. **Secret** — supply via user-secrets or the host's secret store, never a file in the repo. |
| `Maxio:Subdomain` | Maxio site subdomain; the base address is derived from it. |
| `Maxio:ProductFamilyHandle` | Product family whose products are the plans on offer. A handle, never a numeric id. |
| `Maxio:BaseUrl` | Optional. Used verbatim as the API base address instead of deriving one from the subdomain. |
| `Maxio:DefaultPlanHandle` | Optional. Plan used when a subscribe request names none. |
| `Maxio:Environment` | Optional. `US` (default) or `EU`. |
| `Maxio:PaymentCollectionMethod` | Optional. `remittance` (default), `invoice`, `automatic` or `prepaid`. |
| `Maxio:AttemptTimeoutSeconds`, `Maxio:HttpClientTimeoutSeconds`, `Maxio:CallBudgetSeconds`, `Maxio:MaxRetries`, `Maxio:PageSize` | Optional tuning; the defaults are sensible for an interactive request path. |

`PaymentCollectionMethod` is load-bearing. A product marked "credit card not required" does **not** stop
Maxio from trying to settle the first period's balance — the subscription's collection method does. The
default, `remittance`, invoices the shopper instead, which is what lets them subscribe without a card being
captured. Set it to `automatic` on a deployment that does capture one.

To load the credentials in development:

```powershell
dotnet user-secrets set "Maxio:ApiKey"              "$env:MAXIO_API_KEY"              --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "$env:MAXIO_SITE_SUBDOMAIN"       --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

### Where the code lives

| Layer | Contents |
|---|---|
| `ApplicationCore/Billing`, `ApplicationCore/Interfaces/ISubscriptionBillingService.cs` | The capability's own model and abstraction — no dependency on Maxio. |
| `ApplicationCore/Exceptions/BillingProviderException.cs` | The single failure type the integration raises, carrying a caller-safe message. |
| `Infrastructure/Billing/Maxio` | The Maxio implementation, its configuration, the write-once guard and DI registration. |
| `PublicApi/SubscriptionEndpoints` | The three endpoints and their DTOs. |
