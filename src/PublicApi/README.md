# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio Advanced Billing)

Recurring plans are billed through Maxio Advanced Billing (system of record). Endpoints (JWT required; the buyer is the token's user name):

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Plans in the configured product family (`isTruncated` flags a partial catalog) |
| `POST /api/subscriptions` `{ "planHandle": "...", "firstName"?: "...", "lastName"?: "..." }` | Subscribe. `201` created, `200` already subscribed (same subscription), `202` outcome being confirmed, `400` unknown plan, `409` in progress / subscribed to another plan, `422` rejected by Maxio, `502/503` provider problem |
| `GET /api/my-subscriptions` | The caller's subscriptions, read live from Maxio |

Settings (`Maxio` section — keep the API key in user-secrets or the environment, never in appsettings):

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Basic-auth user name |
| `Maxio:Subdomain` | unless `BaseUrl` is set | Site subdomain |
| `Maxio:ProductFamilyHandle` | yes | Family whose products are offered as plans |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address |
| `Maxio:Environment` | no | `US` (default) or `EU` |
| `Maxio:PaymentCollectionMethod` | no | `remittance` (default, no card needed), `automatic`, `prepaid`, `invoice` |
| `Maxio:AttemptTimeoutSeconds` / `Maxio:CallBudgetSeconds` | no | Per-attempt (10) and whole-call (20) bounds |

The host refuses to start when a required key is missing. Local setup:

```
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "<your key>"
dotnet user-secrets set "Maxio:Subdomain" "<site>"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "<family handle>"
```

The `Infrastructure` project references the Maxio SDK from the `maxio-advanced-billing` plugin's install path (`..\..\..\marketplace\plugins\maxio-advanced-billing\sdk\dotnet\...`); re-point that `ProjectReference` on machines where the plugin lives elsewhere.
