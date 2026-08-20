# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio recurring subscriptions

The subscription capability is parallel to the existing basket and order flow. Maxio Advanced Billing is the source of truth for catalog prices and subscription state; the catalog database stores only the local user-to-Maxio customer/subscription identifiers and an idempotency reservation.

The hand-written Maxio client is a narrow projection of `maxio-spec/openapi.yaml`. It uses only these operations from that contract:

- `GET /site.json`
- `GET /products.json`
- `GET /customers/lookup.json`
- `POST /customers.json`
- `GET /customers/{customer_id}/subscriptions.json`
- `GET /subscriptions/lookup.json`
- `POST /subscriptions.json`

All three eShop endpoints require a PublicApi JWT:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "eshop-pro" }`
- `GET /api/my-subscriptions`

### Configuration

PublicApi binds the `Maxio` section. Do not put an API key in an appsettings file. For local development, import the supplied environment variables into the project's user-secret store:

```powershell
dotnet user-secrets set "Maxio:ApiKey" "$env:MAXIO_API_KEY" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" "$env:MAXIO_SITE_SUBDOMAIN" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi/PublicApi.csproj
```

The supported keys are `Maxio:ApiKey`, `Maxio:Subdomain`, `Maxio:ProductFamilyHandle`, and optional `Maxio:BaseUrl`. When `Maxio:BaseUrl` is absent, the client derives `https://{subdomain}.chargify.com` from the OpenAPI server template. When present, the override is used as the API base URL and may include a path prefix.

### Run and verify

The repository's `global.json` allows the installed .NET SDK to roll to a later major. This machine still needs runtime roll-forward and the in-memory providers:

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
dotnet dev-certs https --check --trust
dotnet run --project src/PublicApi/PublicApi.csproj --launch-profile PublicApi
```

In another PowerShell session, authenticate and call the hero flow (replace the URL only if the launch profile uses another assigned port):

```powershell
$api = "https://localhost:12803"
$auth = Invoke-RestMethod -Method Post -Uri "$api/api/authenticate" -ContentType "application/json" -Body '{"username":"demouser@microsoft.com","password":"Pass@word1"}'
$headers = @{ Authorization = "Bearer $($auth.token)" }

Invoke-RestMethod -Uri "$api/api/subscription-plans" -Headers $headers
Invoke-RestMethod -Method Post -Uri "$api/api/subscriptions" -Headers $headers -ContentType "application/json" -Body '{"productHandle":"eshop-pro"}'
Invoke-RestMethod -Uri "$api/api/my-subscriptions" -Headers $headers
```

Repeat the subscribe request: it returns the same Maxio subscription rather than creating another one. With `UseOnlyInMemoryDatabase=true`, local mappings last only for that process; deterministic Maxio references still allow the service to reconcile an already-created customer and subscription during the run. SQL Server deployments apply the `AddSubscriptionBillingMappings` catalog migration for persistent mappings.
