# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscriptions (Maxio Advanced Billing)

Recurring subscriptions run in parallel to the one-time Catalog → Basket → Order flow. Maxio is the
billing system of record. All three endpoints require a JWT from `POST api/authenticate`; the caller is
always the token's user.

| Endpoint | What it does |
| --- | --- |
| `GET api/subscription-plans` | Non-archived plans of the product family `Maxio:ProductFamilyHandle` (`isTruncated` is true if Maxio holds more than could be read). |
| `POST api/subscriptions` `{"planHandle":"eshop-pro"}` | Ensures a Maxio customer for the user (reference `eshop-<username>`) and subscribes it. `201` = created, `200` = already subscribed to that plan (same subscription returned), `409` = in progress / subscribed to another plan, `400` = unknown plan, `422` = Maxio rejected it, `504` = Maxio did not respond. |
| `GET api/my-subscriptions` | The user's subscriptions, read from Maxio. |

Design notes:

- **No duplicates.** A per-user row in `SubscriptionEnrollments` (primary key = user name) is claimed *before*
  anything is written to Maxio, so a double-click reaches Maxio once. Customer and subscription carry
  deterministic references, so a lost response or a wiped database is reconciled by looking them up instead
  of creating them again.
- **Bounded waits.** One deadline per request (`Maxio:RequestBudgetSeconds`, default 25 s) plus a short
  settle window (`Maxio:SettleBudgetSeconds`, 4 s) for an unanswered create; startup rejects settings whose
  sum exceeds 30 s. POSTs are never retried by the SDK.
- **Fail fast.** The host refuses to start when `Maxio:ApiKey`, `Maxio:Subdomain` (unless `Maxio:BaseUrl`
  is set) or `Maxio:ProductFamilyHandle` is missing.

Configuration (`Maxio:` section; secrets via user-secrets or environment, never in appsettings):

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
dotnet user-secrets set "Maxio:Environment" "$MAXIO_ENVIRONMENT"     # optional: US (default) or EU
# optional: dotnet user-secrets set "Maxio:BaseUrl" "https://proxy.example.com"   (used verbatim)
```

The Maxio .NET SDK is consumed as source from the `maxio` Claude Code plugin (it is not on NuGet); see the
`MaxioSdkDir` property in `src/Infrastructure/Infrastructure.csproj` to point the build at another copy.
