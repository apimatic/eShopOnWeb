# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

Subscription billing is an additive JWT-authenticated API alongside the existing basket and
checkout flow:

- `GET /api/subscription-plans` lists active products in the configured Maxio product family.
- `POST /api/subscriptions` accepts `{ "productHandle": "<handle>" }` and idempotently enrolls
  the authenticated user.
- `GET /api/my-subscriptions` reads the authenticated user's current subscriptions from Maxio.

PublicApi requires the following configuration. Development credentials belong in .NET
user-secrets; do not add them to an appsettings or launch-profile file.

```powershell
dotnet user-secrets set "Maxio:ApiKey" "$env:MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$env:MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
dotnet user-secrets set "UseOnlyInMemoryDatabase" "true" --project src/PublicApi
```

`Maxio:BaseUrl` is optional. When supplied through the deployment's configuration/secret
provider, it replaces the subdomain-derived API address verbatim and must be an absolute HTTPS
URL. Omitting it uses the configured subdomain. The first three Maxio settings are validated at
startup.

On machines without the pinned .NET 8 SDK/runtime, set `DOTNET_ROLL_FORWARD=Major`. The local
launch profile uses the repository-assigned ports and Development mode, which loads user-secrets:

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet run --project src/PublicApi
```

Obtain a bearer token from `POST /api/authenticate`; a storefront cookie is not valid for these
routes. For a relational database, apply the `AddSubscriptionBilling` EF migration before
deploying. The in-memory provider intentionally loses the local idempotency mapping and seeded
Identity user ID whenever the process restarts.
