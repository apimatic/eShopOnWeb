# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

Subscription billing is an additive API capability; catalog basket checkout remains unchanged. Maxio Advanced Billing is queried for current plan and subscription data on every account read.

The integration consumes these operations from `maxio-spec/openapi.yaml`:

- `listProductsForProductFamily` using the configured family handle
- `readCustomerByReference`
- `createSubscription`, including `customer_attributes` for a new customer
- `findSubscription` for retry reconciliation
- `listCustomerSubscriptions` for the account view

The API key uses the spec's HTTP Basic scheme (`ApiKey:x`). Products, customers, and subscriptions are addressed by stable handles/references; re-seeded numeric catalog IDs are never configured.

### Configuration

PublicApi binds the `Maxio` section with these keys:

- `Maxio:ApiKey`
- `Maxio:Subdomain`
- `Maxio:ProductFamilyHandle`
- `Maxio:BaseUrl` (optional; when present it is used as the API base address)

At startup, `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, and `MAXIO_DEFAULT_PRODUCT_FAMILY` are mapped to the first three keys. For local development they can be copied into .NET user-secrets without writing values to the repository:

```powershell
dotnet user-secrets set "Maxio:ApiKey" "$env:MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$env:MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

Use `UseOnlyInMemoryDatabase=true` where SQL Server is unavailable. SQL Server deployments apply the `AddBillingSubscriptions` migration during the existing catalog seed startup path.

### Endpoints

All subscription endpoints require the bearer JWT returned by `POST /api/authenticate`.

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "eshop-pro" }`
- `GET /api/my-subscriptions`

Enrollment uses Maxio's `remittance` collection method so the configured no-card plans can activate without attempting automatic payment collection. A unique local user/plan claim plus deterministic Maxio references makes retries safe; an already-created enrollment returns `200` and `alreadyExisted: true` with the existing Maxio subscription.
