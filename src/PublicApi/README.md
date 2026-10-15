# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio Advanced Billing)

Recurring subscriptions run alongside the one-time Catalog → Basket → Order flow. Maxio is the billing system of record.

| Endpoint | Description |
| --- | --- |
| `GET api/subscription-plans` | Plans offered: the products of the configured Maxio product family. |
| `POST api/subscriptions` `{"planHandle": "...", "firstName"?: "...", "lastName"?: "..."}` | Subscribes the caller. `201` created, `200` already subscribed to that plan (repeat requests are idempotent), `202` sent but not yet confirmed, `409` another plan / request in progress, `400` unknown plan, `422` refused by Maxio, `502`/`503` billing provider misconfigured/unavailable. |
| `GET api/my-subscriptions` | The caller's subscriptions in the family, read from Maxio (plan, price, state, next billing date). |

All three require a JWT from `POST api/authenticate`; the caller is identified by the token.

Configuration (section `Maxio`; supply values via user-secrets, environment variables such as `Maxio__ApiKey`, or a secret store — never in appsettings):

| Key | Required | Notes |
| --- | --- | --- |
| `Maxio:ApiKey` | yes | Maxio API key. |
| `Maxio:Subdomain` | unless `BaseUrl` is set | Maxio site; the API host is derived from it. |
| `Maxio:ProductFamilyHandle` | yes | Family whose products are offered as plans. |
| `Maxio:BaseUrl` | no | Used verbatim as the API base address when set. |
| `Maxio:Environment` | no | `US` (default) or `EU`. |
| `Maxio:PaymentCollectionMethod` | no | `remittance` (default — Maxio invoices; no card is captured here), `automatic`, `prepaid` or `invoice`. |

The host refuses to start when a required key is missing. Local setup:

```bash
dotnet user-secrets set "Maxio:ApiKey" "<key>" --project src/PublicApi
dotnet user-secrets set "Maxio:Subdomain" "<site>" --project src/PublicApi
dotnet user-secrets set "Maxio:ProductFamilyHandle" "<family-handle>" --project src/PublicApi
```

The Maxio .NET SDK is referenced as source from the `maxio-advanced-billing` plugin install (see `src/Infrastructure/Infrastructure.csproj`; override the path with `-p:MaxioAdvancedBillingSdkProject=...`).
