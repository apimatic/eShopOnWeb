# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio)

Recurring subscriptions run alongside the one-time Catalog → Basket → Order flow. Maxio Advanced Billing is the system of record.

| Route | Purpose |
| --- | --- |
| `GET api/subscription-plans` | Plans = non-archived products of the configured Maxio product family |
| `POST api/subscriptions` `{ "planHandle": "..." }` | Ensures a Maxio customer for the caller, then subscribes them. Idempotent per user: a repeat returns the existing subscription (200), a concurrent duplicate gets 409 |
| `GET api/my-subscriptions` | The caller's subscriptions, read from Maxio |

All three require a JWT from `POST api/authenticate`.

Configuration comes from the `Maxio` section. The host refuses to start if a required value is missing:

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Secret. Use user-secrets in development, env/secret store (`Maxio__ApiKey`) in deployment |
| `Maxio:Subdomain` | yes | Maxio site; the base URL is derived from it |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans |
| `Maxio:BaseUrl` | no | When set, used verbatim as the API base address |

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "<key>"
dotnet user-secrets set "Maxio:Subdomain" "<site>"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "<family-handle>"
```

Code lives in `Billing/` (SDK gateway, orchestration, options) and `SubscriptionEndpoints/`. Duplicate protection uses per-user claim rows in `CatalogContext` (`BillingCustomerClaims`, `BillingSubscriptionClaims`). With `UseOnlyInMemoryDatabase=true` those rows are lost on restart. After a restart the service re-finds the customer by reference and adopts a live Maxio subscription instead of creating a second one.
