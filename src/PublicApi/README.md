# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription Billing (Maxio Advanced Billing)

Recurring-subscription billing is exposed as an additive capability on this
API. Maxio Advanced Billing is the billing system of record; eShopOnWeb keeps
a local mirror of each user's subscriptions (`UserSubscriptions` table).

### Endpoints (JWT-authenticated — get a bearer token from `POST /api/authenticate`)

| Method | Route                    | Description |
|--------|--------------------------|-------------|
| GET    | `/api/subscription-plans` | Lists the plans available in the configured Maxio product family. |
| POST   | `/api/subscriptions`      | Subscribes the authenticated user to a plan. Body: `{ "planHandle": "eshop-pro" }`. Idempotent: repeated calls for the same user + plan never create duplicate Maxio customers or subscriptions. |
| GET    | `/api/my-subscriptions`   | Lists the authenticated user's subscriptions, with state/price/next-billing refreshed from Maxio. |

Unknown plan handles return `404`; unauthenticated requests return `401`.

### Configuration

Settings are bound from the `Maxio:` configuration section. **Never commit
their values** — load them into .NET user-secrets or the environment:

| Key | Source | Notes |
|-----|--------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | Site API key (sent as the Basic-auth username). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Site subdomain. |
| `Maxio:Environment` | `MAXIO_ENVIRONMENT` | `US` (default) or `EU`; selects the host suffix. |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Handle of the product family holding the plans. |
| `Maxio:BaseUrl` | — | Optional override. When set, it is used verbatim as the API base address. |

Load the secrets for local development:

```powershell
dotnet user-secrets set "Maxio:ApiKey" "$env:MAXIO_API_KEY"   --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$env:MAXIO_SITE_SUBDOMAIN"   --project src/PublicApi
dotnet user-secrets set "Maxio:Environment" "$env:MAXIO_ENVIRONMENT"   --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$env:MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

The integration talks to the Advanced Billing full-JSON API (hosted at
`https://{subdomain}.chargify.com` for the US environment) with HTTP Basic
auth (API key as username, `x` as password), using `payment_collection_method:
"invoice"` so subscriptions can be created without card capture — matching the
seeded sandbox catalog, which does not require a payment method.

### Layout

- `SubscriptionEndpoints/` — the three endpoints above.
- `ApplicationCore/Interfaces/ISubscriptionBillingProvider.cs` — provider abstraction.
- `Infrastructure/Maxio/MaxioClient.cs` — Maxio Advanced Billing implementation.
- `ApplicationCore/Services/SubscriptionService.cs` — orchestration + idempotency.
