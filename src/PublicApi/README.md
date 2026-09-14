# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

`SubscriptionEndpoints/` adds recurring-subscription billing as an **additive, parallel** capability
to the existing one-time commerce flow. Maxio Advanced Billing is the system of record; eShopOnWeb
persists no subscription state of its own. All three endpoints require a valid JWT (obtain one from
`POST /api/authenticate`); the subscriber identity is taken from the token, never from the request body.

| Method & route | Purpose |
|----------------|---------|
| `GET /api/subscription-plans` | Lists the plans a shopper can subscribe to (products in the configured Maxio product family). |
| `POST /api/subscriptions` | Subscribes the caller to a plan (`{ "planHandle": "eshop-pro" }`). Ensures a backing Maxio customer exists and is idempotent per user — a double-click never creates a second customer or a duplicate subscription. |
| `GET /api/my-subscriptions` | Lists the subscriptions the caller currently holds. |

### Configuration

Settings bind from the `Maxio` configuration section (see `Infrastructure/Maxio/MaxioSettings.cs`).
**Secrets must never be committed** — load them into .NET user-secrets from the environment:

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey"              "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain"           "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
# Optional: dotnet user-secrets set "Maxio:BaseUrl" "<explicit API base>"  (otherwise derived as https://{Subdomain}.chargify.com)
```

If Maxio is not configured, the storefront still runs; only the subscription endpoints return a
clear error. The implementation lives in `Infrastructure/Maxio/` (typed HttpClient + orchestration
service) behind the `IMaxioBillingService` abstraction in `ApplicationCore`.

