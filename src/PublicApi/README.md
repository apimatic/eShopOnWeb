# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run alongside the one-time Catalog → Basket → Order flow, with Maxio Advanced Billing
as the billing system of record. All three endpoints require a JWT from `POST api/authenticate`; the caller's
identity comes only from the token.

| Endpoint | Purpose |
| --- | --- |
| `GET api/subscription-plans` | Plans (products) of the configured Maxio product family. `isTruncated` is true if the list was cut short. |
| `POST api/subscriptions` `{ "planHandle": "..." }` | Ensures a Maxio customer for the user, then subscribes them. `201` created, `200` already subscribed (same subscription returned), `202` pending (an earlier request for the same subscription is still in flight or being settled — repeat the call later). |
| `GET api/my-subscriptions` | The caller's subscriptions: plan, price, currency, state and next billing date. |

Errors: `400` plan not offered / missing `planHandle`, `422` Maxio refused the subscription (reason in `Message`),
`503` Maxio unreachable, timed out, throttled or rejected our credentials, `502` Maxio failed.

### Configuration

Bound from the `Maxio:` section and validated at startup (the host does not start if a value is missing):

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Secret. Never put it in `appsettings*.json`. |
| `Maxio:Subdomain` | unless `Maxio:BaseUrl` is set | The Maxio site subdomain. |
| `Maxio:ProductFamilyHandle` | yes | The product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address instead of the subdomain-derived one. |
| `Maxio:Environment` | no | `US` (default) or `EU`. |

Optional tuning: `Maxio:AttemptTimeoutSeconds` (10), `Maxio:CallBudgetSeconds` (30), `Maxio:ReconcileBudgetSeconds` (10),
`Maxio:ClaimLeaseSeconds` (90), `Maxio:MaxPlanPages` (5).

For local development, load the values into user-secrets from your environment, for example (bash):

```bash
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
dotnet user-secrets set "Maxio:Environment" "$MAXIO_ENVIRONMENT" --project src/PublicApi
```

### Duplicate prevention

Each Maxio write (create customer, create subscription) is first claimed in the `BillingClaims` table, keyed by a
deterministic reference derived from the user (and plan). The database refuses a second claim, so a double-click
or retry never reaches Maxio twice; the same reference is sent to Maxio, which is how a write whose outcome was lost
(timeout, dropped connection) is found and settled instead of being repeated. One subscription per user per plan.

The SDK is referenced as a project inside the installed `maxio-advanced-billing` plugin
(`src/Infrastructure/Infrastructure.csproj`); re-point that reference if the plugin lives elsewhere on your machine
or in CI.
