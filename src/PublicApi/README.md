# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

Recurring-subscription billing is exposed as JWT-authenticated endpoints, backed by
Maxio Advanced Billing as the billing system of record:

| Endpoint | Description |
|----------|-------------|
| `GET /api/subscription-plans` | Lists subscribable plans (products in the configured Maxio product family). |
| `POST /api/subscriptions` | Subscribes the authenticated user to a plan (`{ "productHandle": "<handle>" }`). Idempotent: ensures the Maxio customer exists (keyed on the eShopOnWeb user id as the Maxio customer `reference`) and returns the existing live subscription instead of creating a duplicate when the user already holds the plan. |
| `GET /api/my-subscriptions` | Lists the authenticated user's subscriptions with plan, price, state and next billing date. |

Configuration is bound from the `Maxio:` section with these keys (supply secrets via
.NET user-secrets or environment variables — never in `appsettings*.json`):

- `Maxio:ApiKey` — Maxio Advanced Billing API key (used as the HTTP Basic username; the password is the literal `x`, per the Maxio docs).
- `Maxio:Subdomain` — site subdomain; the base address is derived as `https://{subdomain}.chargify.com`.
- `Maxio:ProductFamilyHandle` — handle of the product family whose products are offered as plans.
- `Maxio:BaseUrl` — optional override; when set, it is used verbatim as the API base address instead of the derived one.

Example (values from your environment):

```bash
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

Subscriptions are created with `payment_collection_method: remittance` (invoice-based
collection), so signup works without capturing a payment method when the plan does not
require one.

