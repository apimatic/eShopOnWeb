# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing

`SubscriptionEndpoints/` adds recurring-subscription billing alongside the existing one-time
Catalog → Basket → Order flow. It does not change that flow. **Maxio Advanced Billing is the system of
record** — eShopOnWeb stores no subscription state of its own.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | The plans on offer, from the configured Maxio product family. |
| `POST /api/subscriptions` | Subscribe the caller to a plan, by handle. Idempotent per (caller, plan). |
| `GET /api/my-subscriptions` | The caller's subscriptions with plan, price, state and next billing date. |

All three require a JWT (`POST /api/authenticate`). The shopper is taken **only** from the validated token,
never from the request body, so one authenticated caller cannot act as another.

`POST /api/subscriptions` answers `201` when it enrolled the caller and `200` with
`"alreadySubscribed": true` when a live subscription for that plan already existed — the expected result of a
double click.

### Layering

- `ApplicationCore` — `ISubscriptionBillingService` and the domain records. No SDK dependency.
- `Infrastructure/Billing/Maxio` — the only code that knows the Maxio SDK exists. Every provider, transport
  and configuration failure is translated to `BillingException` here.
- `PublicApi/SubscriptionEndpoints` — the HTTP surface; `Middleware/ExceptionMiddleware` maps
  `BillingException` to a status code.

### Configuration

Bound from the `Maxio:` section. **Never commit these values.** In development, load them with user-secrets:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

Outside Development, supply the same keys through any configuration provider (for example the environment
variables `Maxio__ApiKey`, `Maxio__Subdomain`, `Maxio__ProductFamilyHandle`).

| Key | Required | Meaning |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Maxio API key. Sent as the Basic-auth user name. |
| `Maxio:Subdomain` | yes* | Maxio site subdomain, e.g. `cp-exp-2`. A sandbox is a *site*, not an environment. |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Used **verbatim** as the API base address instead of one derived from the subdomain. |
| `Maxio:Environment` | no | `US` (default) or `EU`. Any other value falls back to `US`. |
| `Maxio:PaymentCollectionMethod` | no | Forces `remittance` / `invoice` / `automatic` / `prepaid`. Derived from the site when unset. |
| `Maxio:AttemptTimeoutSeconds` | no | Bound on one HTTP attempt. Default 10. |
| `Maxio:CallBudgetSeconds` | no | Bound on a whole call, retries included. Default 30. |
| `Maxio:MaxRetries` | no | Retry attempts after the first. Floor of 1. Default 2. |
| `Maxio:CatalogCacheSeconds` | no | Plan/site cache lifetime. Default 60. |
| `Maxio:LogRequests` | no | Logs each Maxio request and status at Debug. Default off. |

\* Either `Maxio:Subdomain` or `Maxio:BaseUrl` must be present.

Billing configuration is validated **per request**, not at startup: an unconfigured deployment still boots,
the rest of the API is unaffected, and only the three subscription endpoints answer `503` naming the missing
key.

### Notes worth knowing

- **Plans are addressed by handle, never by numeric id.** Maxio reassigns product and family ids whenever the
  catalog is re-seeded.
- **Prices are minor units.** `priceInCents` is what Maxio reports; `price` is the derived major-unit value.
- **A signup balance is collected, not waived.** A product with `require_credit_card` false still assesses its
  first period at creation, so the subscription is created with a non-card payment collection method — Maxio
  issues an invoice for the amount. If the intent is "nothing due at signup", that is a catalog/pricing change
  in Maxio (a $0 or trial price point), not an application change.
- **Double-click safety is the application's job.** Maxio offers no idempotency key and no create-or-get
  subscription operation. `IBillingOperationLock` closes the check-then-act window; the in-process
  implementation covers a single instance, and a distributed implementation can be registered in its place
  without touching anything else.
