# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio Advanced Billing)

Recurring plans run alongside the one-time Catalog → Basket → Order flow, with Maxio Advanced Billing as the system of record. All endpoints require a JWT from `POST /api/authenticate`; the shopper's identity comes from the token.

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Plans in the configured product family (`isComplete` is false if the listing was capped) |
| `POST /api/subscriptions` `{ "planHandle": "..." }` | Subscribes the caller. Idempotent per shopper: `201 created`, `200 already_subscribed`, `202 in_progress` / `unconfirmed`, `409` when a different plan is already held, `400`/`422` when rejected, `502`/`503`/`504` when Maxio is unavailable |
| `GET /api/my-subscriptions` | The caller's subscriptions as held by Maxio (plan, price, state, next billing date) |

Configuration (section `Maxio`). The host refuses to start if a required value is missing. Never commit values: use user-secrets in development (`dotnet user-secrets set "Maxio:ApiKey" ...` from this folder) and environment variables (`Maxio__ApiKey`, ...) or a secret store elsewhere.

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | API key (Basic-auth user name) |
| `Maxio:Subdomain` | yes, unless `BaseUrl` is set | Site subdomain |
| `Maxio:ProductFamilyHandle` | yes | Family whose products are offered as plans |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address instead of one derived from the subdomain |
| `Maxio:Environment` | no | `US` (default) or `EU` |
| `Maxio:PaymentCollectionMethod` | no | `remittance` (default; invoiced, no card on file), `automatic`, `prepaid`, `invoice` |
| `Maxio:AttemptTimeoutSeconds` / `RequestBudgetSeconds` | no | Per-attempt timeout (10) and per-operation budget (30) |

The Maxio .NET SDK is referenced as source from the `maxio-advanced-billing` plugin (`MaxioSdkProject` MSBuild property in `PublicApi.csproj`). Override it with `-p:MaxioSdkProject=<path to MaxioAdvancedBilling.csproj>` when the plugin is installed somewhere else.

The `SubscriptionEnrollments` table (migration `AddSubscriptionEnrollments`) holds the per-shopper claim that prevents duplicate enrollments. With `UseOnlyInMemoryDatabase=true` it resets on restart. Maxio is still checked by reference before any create, so a restart never produces a duplicate.
