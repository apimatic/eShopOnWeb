# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run alongside the one-time Catalog → Basket → Order flow. Maxio is the system of record.
All three endpoints require a JWT from `POST /api/authenticate`; the caller is identified only by the token.

| Route | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Active (non-archived) products of the configured Maxio product family. `truncated: true` means the list may be incomplete. |
| `POST /api/subscriptions` `{ "productHandle": "<plan handle>" }` | Subscribe. Idempotent per user and plan: `201 created`, `200 already_subscribed`, `202 pending` (unconfirmed; repeat to settle), `409` (an identical request is in flight), `400` (unknown plan), `422` (Maxio refused), `502/503/504` (Maxio unavailable). |
| `GET /api/my-subscriptions` | The caller's subscriptions: plan, price, state, next billing date. |

### Configuration (`Maxio:` section)

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Maxio API key (Basic-auth user name). **Secret — user-secrets / environment / secret store only.** |
| `Maxio:Subdomain` | unless `BaseUrl` is set | Site subdomain → `https://<subdomain>.chargify.com`. |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address instead of the subdomain-derived one. |
| `Maxio:PaymentCollectionMethod` | no | `remittance` (default, invoice-based, no card needed), `invoice` (legacy Statements sites), `automatic`, `prepaid`. |

The host refuses to start when a required key is missing. For local development, load the values into user-secrets
from your environment, for example (PowerShell):

```powershell
dotnet user-secrets set "Maxio:ApiKey" "$env:MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$env:MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

Elsewhere, use environment variables (`Maxio__ApiKey`, ...) or a secret store. Duplicate submissions are blocked by
the `BillingClaims` table in `CatalogContext` (migration `AddBillingClaims`). With `UseOnlyInMemoryDatabase=true` that
table is lost on restart. Duplicates are still prevented then, because every write first re-reads Maxio by its
deterministic reference.

