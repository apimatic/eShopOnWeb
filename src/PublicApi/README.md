# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

The additive subscription API is exposed at:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "<handle>" }`
- `GET /api/my-subscriptions`

All three routes require a PublicApi bearer token. Maxio Advanced Billing is the billing source of truth;
the local `SubscriptionEnrollments` table is an idempotent provisioning reservation and recovery map.

Configure the PublicApi project with .NET user-secrets (or an equivalent production configuration provider):

```powershell
dotnet user-secrets set "Maxio:ApiKey" $env:MAXIO_API_KEY --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" $env:MAXIO_SITE_SUBDOMAIN --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" $env:MAXIO_DEFAULT_PRODUCT_FAMILY --project src/PublicApi
```

`Maxio:BaseUrl` is optional. When absent, the client uses
`https://{Maxio:Subdomain}.chargify.com`; when present, the configured absolute HTTPS URL is used as the API
base. Numeric Maxio catalog IDs are deliberately not configured because re-seeding changes them.

The HTTP contracts are based on Maxio's official Advanced Billing documentation for
[authentication](https://developers.maxio.com/http/getting-started/authentication),
[family products](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/product-families/list-products-for-product-family),
[customer lookup](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/customers/read-customer-by-reference),
[customer creation](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/customers/create-customer),
[subscription creation](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/subscriptions/create-subscription), and
[customer subscriptions](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/customers/list-customer-subscriptions).
