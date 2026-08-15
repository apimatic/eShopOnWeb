# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Subscription billing (Maxio Advanced Billing)

An additive, parallel capability to the one-time commerce flow: recurring subscriptions, with Maxio
Advanced Billing as the system of record. All three endpoints are JWT-authenticated; the caller's
identity comes from the token (the user name claim is the stable Maxio customer `reference`).

| Method & route | Purpose |
|---|---|
| `GET /api/subscription-plans` | List the plans (products) in the configured Maxio product family. |
| `POST /api/subscriptions` | Subscribe the caller to a plan (`{"planHandle":"eshop-pro"}`). Ensures a single Maxio customer per shopper and does not duplicate an active subscription (idempotent under double-click / retry). Returns `201` for a new subscription, `200` when one already existed. |
| `GET /api/my-subscriptions` | List the caller's subscriptions (plan, price, state, period, next billing). |

Code lives in `SubscriptionEndpoints/` (HTTP endpoints) and `Maxio/` (`IMaxioBillingService`, the
single SDK boundary; `ICurrentShopperService`; settings and DI wiring in
`MaxioServiceCollectionExtensions`).

### Configuration (`Maxio:` section — never commit secret values)

Bound from configuration; load real values via user-secrets or environment, not source:

- `Maxio:ApiKey` — Maxio API key (HTTP Basic username).
- `Maxio:Subdomain` — site subdomain; the API base URL is derived from it.
- `Maxio:ProductFamilyHandle` — product family whose products are offered as plans.
- `Maxio:BaseUrl` — optional explicit base-URL override; used verbatim when set.

```
dotnet user-secrets set "Maxio:ApiKey"              "<key>"    --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain"           "<site>"   --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "<family>" --project src/PublicApi
```

Plans with no payment method required are enrolled on a **remittance (invoice)** collection basis, so
no card capture / 3-DS is needed.

