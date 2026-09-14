# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

PublicApi exposes the JWT-authenticated subscription flow at:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "<handle>" }`
- `GET /api/my-subscriptions`

Maxio credentials are bound from the `Maxio` configuration section. For local development,
load the supplied environment variables into PublicApi user-secrets without copying their
values into repository files:

```powershell
dotnet user-secrets set "Maxio:ApiKey" "$env:MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$env:MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

`Maxio:BaseUrl` is optional. When omitted, PublicApi derives
`https://{Maxio:Subdomain}.chargify.com`; when supplied, it is used as the API base address.
For this repository's in-memory development mode:

```powershell
$env:UseOnlyInMemoryDatabase = "true"
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet run --project src/PublicApi --launch-profile PublicApi
```

Authenticate at `POST /api/authenticate` and send the returned token as
`Authorization: Bearer <token>`. Maxio is authoritative for plan, price, state, and billing
dates. The local `SubscriptionEnrollments` table stores only correlation IDs and enforces a
unique user/plan mapping; in-memory mappings disappear when the process stops.
