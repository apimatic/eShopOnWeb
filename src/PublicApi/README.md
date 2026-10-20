# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run alongside the one-time Catalog → Basket → Order flow. Maxio Advanced Billing is the
billing system of record. All three endpoints require a JWT from `POST api/authenticate`; the caller's identity
comes from the token.

| Endpoint | Purpose |
| --- | --- |
| `GET api/subscription-plans` | Plans (products) of the configured Maxio product family, with price and interval. `isTruncated` is true if the list was capped. |
| `POST api/subscriptions` `{ "planHandle": "..." }` | Ensures a Maxio customer for the caller and subscribes them. `201` when created, `200` with `created: false` when the caller already holds that plan. Idempotent per (user, plan). A concurrent duplicate gets `409`. |
| `GET api/my-subscriptions` | The caller's subscriptions: plan, price, state and next billing date. |

Errors: `400` unknown or missing plan, `409` same request already in progress, `422` Maxio rejected the request
(reasons in `Errors`), `502` Maxio unavailable or misconfigured, `503` outcome not yet confirmed or Maxio busy.
On `503`, retrying is safe and never creates a duplicate.

### Configuration

Settings bind from the `Maxio:` section. The host refuses to start if a required key is missing.

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Secret: user-secrets in development, environment/secret store in deployments. |
| `Maxio:Subdomain` | unless `Maxio:BaseUrl` is set | The Maxio site; the base address is derived from it. |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address instead of the subdomain. |
| `Maxio:Environment` | no | `US` (default) or `EU` hosting region. |

`MAXIO_API_KEY`, `MAXIO_SITE_SUBDOMAIN`, `MAXIO_DEFAULT_PRODUCT_FAMILY` and `MAXIO_ENVIRONMENT` are mapped onto these
keys when set; `Maxio__ApiKey`-style variables also work. For local development:

```bash
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY" --project src/PublicApi
```

The Maxio .NET SDK is referenced as source from the `maxio-advanced-billing` Claude Code plugin
(see `MaxioSdkProject` in `src/Infrastructure/Infrastructure.csproj`; override it with
`/p:MaxioSdkProject=<path>` when the plugin is installed elsewhere). It needs the .NET 10 SDK to build.
