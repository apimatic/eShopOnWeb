# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscriptions

PublicApi exposes an additive subscription flow alongside the existing catalog, basket, and order flow:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "..." }`
- `GET /api/my-subscriptions`

All three endpoints require a bearer token from `POST /api/authenticate`. The shopper is resolved from the token; no user ID is accepted from request input.

Configuration is bound from the `Maxio` section. For local development, load the environment-provided values into the PublicApi user-secret store:

```powershell
dotnet user-secrets set "Maxio:ApiKey" "$env:MAXIO_API_KEY" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" "$env:MAXIO_SITE_SUBDOMAIN" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "UseOnlyInMemoryDatabase" "true" --project src/PublicApi/PublicApi.csproj
```

`Maxio:BaseUrl` is optional. When supplied through configuration (for example, `Maxio__BaseUrl`), it is used as the API base URL; otherwise PublicApi uses the OpenAPI server template `https://{subdomain}.chargify.com`.

Maxio is queried for all displayed catalog and subscription state. The local `SubscriptionBillingRecords` table is limited to a concurrency-safe idempotency reservation and remote-ID mapping. Subscription creation uses Maxio's spec-defined `remittance` collection method because this API does not capture payment credentials.
