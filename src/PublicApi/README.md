# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run beside the one-time Catalog → Basket → Order flow, with Maxio as the billing
system of record. All three endpoints need a JWT from `POST /api/authenticate`; the caller's identity comes
from the token.

| Endpoint | What it does |
| --- | --- |
| `GET /api/subscription-plans` | The plans of the configured Maxio product family (`isComplete` is false if the list was cut short) |
| `POST /api/subscriptions` `{ "planHandle": "..." }` | Subscribes the caller. `201 created`, `200 already_subscribed`, `202 pending` (outcome still being confirmed — not a failure), `409` while another request for the same plan is in flight |
| `GET /api/my-subscriptions` | The caller's Maxio subscriptions (plan, price, state, next billing date) and any signups still pending |

Configuration (section `Maxio:`; never put the values in a tracked file — use user-secrets in development
and the host's secret store elsewhere):

| Key | Required | Meaning |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | The site's API key |
| `Maxio:Subdomain` | yes, unless `Maxio:BaseUrl` is set | The Maxio site |
| `Maxio:ProductFamilyHandle` | yes | The product family whose plans are offered |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address instead of the one derived from the subdomain |
| `Maxio:Environment` | no | `US` (default) or `EU` |

The `MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY` and `MAXIO_ENVIRONMENT`
environment variables are read as the lowest-priority fallback for these keys. The host refuses to start
when a required key is missing.

```bash
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

The Maxio .NET SDK is not published as a package: `src/Infrastructure` builds it from source through the
`MaxioSdkRoot` MSBuild property, which defaults to the maxio-advanced-billing plugin's `sdk/dotnet` folder
next to this repository. On another machine or CI runner, pass `-p:MaxioSdkRoot=<path to the SDK folder>`
(or set the `MaxioSdkRoot` environment variable).
