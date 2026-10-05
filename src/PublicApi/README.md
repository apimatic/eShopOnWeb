# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio Advanced Billing)

`SubscriptionEndpoints/` adds recurring subscriptions alongside the one-time catalog/basket/order flow.
Maxio is the billing system of record. All three endpoints require a JWT from `POST api/authenticate`.

| Endpoint | Purpose |
| --- | --- |
| `GET api/subscription-plans` | Plans offered: the products of the configured Maxio product family (`isTruncated` flags a capped listing) |
| `POST api/subscriptions` `{ "planHandle": "...", "firstName"?: "...", "lastName"?: "..." }` | Subscribe the caller: 201 when created, 200 + `alreadySubscribed` when they already hold the plan, 409 while a request for the same plan is in flight |
| `GET api/my-subscriptions` | The caller's subscriptions as Maxio records them, plus any not yet confirmed (`pending`) |

Configuration (section `Maxio`; the host refuses to start when a required value is missing):

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Maxio API key. Keep it in user-secrets / environment (`Maxio__ApiKey`), never in a file in this repo |
| `Maxio:Subdomain` | unless `Maxio:BaseUrl` is set | Maxio site subdomain |
| `Maxio:ProductFamilyHandle` | yes | Product family whose products are offered as plans |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address instead of `https://{subdomain}.chargify.com` |

Development setup from the sandbox environment variables:

```
dotnet user-secrets --project src/PublicApi set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets --project src/PublicApi set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets --project src/PublicApi set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

Every request spends at most 25 seconds waiting on Maxio in total; past that the caller gets
`504 "Maxio did not respond…"`. The Maxio SDK is referenced from the `maxio` plugin's install location
(`MaxioSdkProject` property in `src/Infrastructure/Infrastructure.csproj`; override it with
`-p:MaxioSdkProject=<path>` when the plugin lives elsewhere). Design notes and the contract sheet:
`maxio-advanced-billing-plan.md` at the repository root.
