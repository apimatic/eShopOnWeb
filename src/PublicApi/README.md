# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

Subscription billing is a parallel flow backed by Maxio Advanced Billing. All three routes require a PublicApi JWT:

- `GET /api/subscription-plans` lists active products in the configured Maxio product family.
- `POST /api/subscriptions` accepts `{ "productHandle": "eshop-pro" }` and idempotently enrolls the authenticated user.
- `GET /api/my-subscriptions` reads the authenticated user's current subscriptions from Maxio.

The integration resolves catalog entities by stable handles. It creates deterministic customer and subscription references, looks them up before creating anything, and keeps a uniquely constrained local mapping for reconciliation. Current price, state, and next billing time are always returned from Maxio. New subscriptions use Maxio's `remittance` collection method so products configured without card capture can enroll without transmitting payment credentials.

Configuration is bound from the `Maxio` section:

- `Maxio:ApiKey` (required)
- `Maxio:Subdomain` (required unless `Maxio:BaseUrl` is set)
- `Maxio:ProductFamilyHandle` (required)
- `Maxio:BaseUrl` (optional absolute HTTPS override; when omitted the client uses `https://{subdomain}.chargify.com`)

For local development, load the supplied environment variables into PublicApi user-secrets without putting their values in the repository:

```powershell
dotnet user-secrets set --project src/PublicApi "Maxio:ApiKey" "$env:MAXIO_API_KEY"
dotnet user-secrets set --project src/PublicApi "Maxio:Subdomain" "$env:MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set --project src/PublicApi "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets set --project src/PublicApi "UseOnlyInMemoryDatabase" "true"
```

The HTTP contract follows the official Maxio documentation for [authentication](https://developers.maxio.com/http/getting-started/authentication), [products](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/products/list-products), [customer reference lookup](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/customers/read-customer-by-reference), [customer creation](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/customers/create-customer), [subscription reference lookup](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/subscriptions/find-subscription), [subscription creation](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/subscriptions/create-subscription), and [customer subscriptions](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/customers/list-customer-subscriptions).
