# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

The subscription capability is additive to the existing basket and checkout endpoints. It exposes three JWT-protected routes:

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "productHandle": "<handle from the plans response>" }`
- `GET /api/my-subscriptions`

Maxio Advanced Billing is authoritative for catalog, price, state, and renewal date. The local `SubscriptionEnrollments` table stores only the stable eShop user-to-Maxio mapping and enforces one enrollment per user and product handle. Customer and subscription references are deterministic hashes, allowing a repeated or ambiguously timed-out signup request to be reconciled without creating another record.

Configuration binds from the `Maxio` section. `BaseUrl` is optional; when omitted, the client uses `https://{Subdomain}.chargify.com`.

```powershell
dotnet user-secrets set "Maxio:ApiKey" "$env:MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$env:MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

The process also maps those three environment variables directly to their `Maxio:` keys. To route the unchanged client contract to a test double, proxy, or alternate Maxio API address, configure `Maxio:BaseUrl`; it takes precedence over the derived address.

The integration uses Maxio's documented Basic Authentication (`API key:X`) and these official Advanced Billing operations: [list family products](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/product-families/list-products-for-product-family), [customer lookup](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/customers/read-customer-by-reference), [customer creation](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/customers/create-customer), [subscription lookup](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/subscriptions/find-subscription), [subscription creation](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/subscriptions/create-subscription), and [customer subscriptions](https://developers.maxio.com/http/advanced-billing-api/api-endpoints/customers/list-customer-subscriptions).
