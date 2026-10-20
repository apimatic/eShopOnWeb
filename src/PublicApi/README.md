# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run alongside the one-time basket/order flow, with Maxio Advanced Billing as the
billing system of record. All three endpoints require a bearer token from `POST api/authenticate`; the
shopper is always the user named in the token.

| Endpoint | Purpose |
| --- | --- |
| `GET api/subscription-plans` | Plans on offer: the non-archived products of the configured product family. `complete: false` means the provider list was cut short. |
| `POST api/subscriptions` `{ "planHandle": "..." }` | Subscribe. `201` creates; repeating it for the same plan returns the existing subscription with `200`. `409` = the same request is already in progress; `503` = sent but not yet confirmed (check `my-subscriptions`). |
| `GET api/my-subscriptions` | The caller's subscriptions in the configured family: plan, price, state, next billing date. `pendingPlanHandles` lists unconfirmed subscribes. |

### Configuration

Settings bind from the `Maxio` section. The host refuses to start when a required one is missing.

| Key | Required | Meaning |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Advanced Billing API key (secret). |
| `Maxio:Subdomain` | unless `Maxio:BaseUrl` is set | Site subdomain; the API address is `https://{subdomain}.chargify.com`. |
| `Maxio:ProductFamilyHandle` | yes | Handle of the product family whose products are sold as plans. |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address instead of the subdomain (e.g. an EU-hosted site). |

Never put these values in a file in the repository. For local development, load them into user-secrets
from the environment:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

Elsewhere, use environment variables (`Maxio__ApiKey`, ...) or a secret store. The client is built once at
startup, so a rotated key takes effect after a restart.

### Behaviour worth knowing

- **No duplicates.** Each subscribe takes a claim in `CatalogContext` keyed by a deterministic reference
  (a hash of the user name and plan) before calling Maxio, so a double-click reaches Maxio once. The same
  reference is sent to Maxio and read back first, so a lost claim store (for example the in-memory database
  after a restart) still finds the existing customer and subscription instead of creating new ones.
- **Unknown outcomes are settled, not reported as failures.** If a create's connection drops or its answer
  is unreadable, the service re-reads Maxio by reference. An outcome it cannot settle is recorded and
  settled on the shopper's next request.
- **Collection method.** Subscriptions are created with remittance (invoice) collection, because this flow
  captures no card.
- **SQL Server** needs the `AddBillingClaims` migration (`src/Infrastructure/Data/Migrations`).
- **SDK reference.** `PublicApi.csproj` references the Maxio Advanced Billing .NET SDK source shipped with
  the maxio-advanced-billing Claude Code plugin, by a path relative to this clone. A machine (or CI runner)
  without the plugin at that path cannot build PublicApi until the reference is re-pointed.
