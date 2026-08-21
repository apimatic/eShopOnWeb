# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

Subscription billing is additive to the existing basket and order flow. Maxio Advanced Billing is the source of truth for plans, customers, and subscriptions. The implementation consumes only operations and fields defined by `maxio-spec/openapi.yaml`:

- `GET /site.json`
- `GET /product_families/{product_family_id}/products.json`
- `GET /customers/lookup.json` and `POST /customers.json`
- `GET /subscriptions/lookup.json` and `POST /subscriptions.json`
- `GET /customers/{customer_id}/subscriptions.json`

The PublicApi routes are JWT protected:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "eshop-pro" }`
- `GET /api/my-subscriptions`

Customer and subscription references are deterministic from the Identity user ID and product handle. Together with a per-reference concurrency gate and lookup-before-create/recovery, repeated requests return the existing Maxio subscription. Products are resolved by handles at request time; numeric Maxio IDs are never configured.

### Configuration

The application binds exactly these settings from `Maxio`: `ApiKey`, `Subdomain`, `ProductFamilyHandle`, and optional `BaseUrl`. When `BaseUrl` is non-empty it is used as the base address; otherwise the OpenAPI US server template derives `https://{Subdomain}.chargify.com`.

Keep credentials outside the repository. For local development, load the provided environment variables into PublicApi user-secrets:

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi/PublicApi.csproj
```

For a custom endpoint or the OpenAPI EU environment, also set `Maxio:BaseUrl`. Empty or missing required settings fail startup validation.

### Run and verify

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
dotnet run --project src/PublicApi/PublicApi.csproj --launch-profile PublicApi
```

Authenticate at `POST /api/authenticate` with the seeded demo user, use the returned `token` as `Authorization: Bearer <token>`, then call the three subscription routes in order. A new subscription returns `201`; a retry returns `200` with `alreadyExisted: true`. Both responses carry the same Maxio ID, state, recurring price, currency, and next billing timestamp.

Run the automated coverage with:

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj
```
