# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run alongside the one-time basket/order flow. Maxio is the system of record for
plans, customers and subscriptions; the app keeps only claim rows (`MaxioCustomerClaims`,
`MaxioSubscriptionClaims` in `CatalogContext`) that stop a user being enrolled twice.

| Route | Auth | Purpose |
| --- | --- | --- |
| `GET /api/subscription-plans` | none | Non-archived products of the configured product family |
| `POST /api/subscriptions` `{ "planHandle": "eshop-pro" }` | JWT | Ensure a Maxio customer (reference = user name) and subscribe. `201` created, `200` already subscribed, `202` pending confirmation, `400` unknown plan, `409` in progress / other plan held, `422` refused by Maxio |
| `GET /api/my-subscriptions` | JWT | The caller's subscriptions as Maxio reports them |

Configuration (section `Maxio`). Locally, put the values in user-secrets; never commit them:

```
dotnet user-secrets set "Maxio:ApiKey" "<api key>" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "<site subdomain>" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "<product family handle>" --project src/PublicApi
```

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Falls back to env `MAXIO_API_KEY` |
| `Maxio:Subdomain` | yes, unless `Maxio:BaseUrl` is set | Falls back to env `MAXIO_SITE_SUBDOMAIN` |
| `Maxio:ProductFamilyHandle` | yes | Falls back to env `MAXIO_DEFAULT_PRODUCT_FAMILY` |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address instead of deriving one from the subdomain |
| `Maxio:Environment` | no | `US` (default) or `EU`; falls back to env `MAXIO_ENVIRONMENT` |
| `Maxio:PaymentCollectionMethod` | no | Default `remittance` (invoice-billed, no card needed); `automatic` requires a payment method on file |

The API refuses to start when a required key is missing. The Maxio SDK is referenced as a project from the
locally installed `maxio-advanced-billing` Claude Code plugin
(`src/Infrastructure/Infrastructure.csproj`); on another machine, re-point that `ProjectReference` at
your copy of the plugin. Building it needs the .NET 10 SDK (C# 14).
