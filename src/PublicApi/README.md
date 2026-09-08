# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

An additive, parallel capability that lets an authenticated shopper browse plans and subscribe. It is backed by **Maxio Advanced Billing** as the system of record; eShopOnWeb's existing one-time cart/checkout flow is untouched.

Endpoints (all JWT-authenticated; the shopper's identity comes from the bearer token):

| Endpoint | Description |
|---|---|
| `GET  /api/subscription-plans` | Lists the plans in the configured Maxio product family |
| `POST /api/subscriptions` | Subscribes the shopper to a plan (idempotent: re-subscribing to a plan they already hold returns the existing subscription) |
| `GET  /api/my-subscriptions` | Lists the shopper's current subscriptions |

The Maxio customer for an eShopOnWeb user is keyed by the user's e-mail address (stored as the Maxio customer `reference`), so idempotency holds without any local mapping table.

### Configuration

The integration is configured from the `Maxio` configuration section (no values are hard-coded, so the same build can target any Maxio site/catalog). Set it via .NET user-secrets:

```
dotnet user-secrets set "Maxio:ApiKey" "<value>" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "<value>" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "<value>" --project src/PublicApi
```

The canonical sources are the environment variables `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, and `MAXIO_DEFAULT_PRODUCT_FAMILY`; load them into user-secrets yourself and never commit their values. An optional `Maxio:BaseUrl` override is honoured verbatim; when absent, the API base URL is derived as `https://{subdomain}.chargify.com`.

Implementation lives in `src/Infrastructure/Maxio` (`IMaxioSubscriptionService` / `MaxioSubscriptionService` + options/models); endpoint classes live under `SubscriptionEndpoints`. Exercise the flow by calling the three endpoints above with a bearer token obtained from `POST /api/authenticate`.


