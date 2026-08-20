# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscriptions

The subscription API is an additive flow alongside the existing basket and checkout:

- `GET /api/subscription-plans`
- `POST /api/subscriptions`
- `GET /api/my-subscriptions`

All three routes require a PublicApi JWT. Maxio is the system of record; the local
`UserSubscriptions` table stores deterministic idempotency mappings only. Product and product
family handles are used throughout, so catalog reseeds do not invalidate the integration.

Configuration binds from the `Maxio` section. For local development, copy the supplied environment
variables to the existing PublicApi user-secrets store (the commands do not write them to the repo):

```powershell
dotnet user-secrets set "Maxio:ApiKey" "$env:MAXIO_API_KEY" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" "$env:MAXIO_SITE_SUBDOMAIN" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi/PublicApi.csproj
```

`Maxio:BaseUrl` is optional. When supplied, it replaces the spec-derived
`https://{subdomain}.chargify.com` base address. For deployed environments, provide the equivalent
hierarchical environment variables (`Maxio__ApiKey`, `Maxio__Subdomain`,
`Maxio__ProductFamilyHandle`, and optionally `Maxio__BaseUrl`).

Run locally with the in-memory provider:

```powershell
$env:DOTNET_ROLL_FORWARD="Major"
$env:UseOnlyInMemoryDatabase="true"
dotnet run --project src/PublicApi/PublicApi.csproj --launch-profile PublicApi
```

Authenticate at `POST /api/authenticate`, then send the returned token as
`Authorization: Bearer <token>`. A minimal enrollment body is:

```json
{ "productHandle": "eshop-pro" }
```

`firstName` and `lastName` are optional customer display fields. Email and user identity always
come from the authenticated local account, never from the enrollment request.
