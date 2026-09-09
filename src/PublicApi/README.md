# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Subscription billing (Maxio Advanced Billing)

Recurring-subscription billing is exposed on this API with Maxio Advanced Billing as the
billing system of record. Endpoints (all JWT-authenticated — get a bearer token from
`POST /api/authenticate` first):

| Route | Purpose |
|---|---|
| `GET /api/subscription-plans` | Lists the plans available in the configured product family |
| `POST /api/subscriptions` | Subscribes the caller to a plan. Body: `{"planHandle": "<handle>"}`. Idempotent — a double-click never creates two customers or two live subscriptions |
| `GET /api/my-subscriptions` | Lists the caller's subscriptions (plan, price, state, next billing date) |

### Configuration

Values are read from the `Maxio` configuration section; supply them via user secrets
(`dotnet user-secrets set ... --project src/PublicApi`) or environment variables — never
commit them:

| Key | From | Required | Notes |
|---|---|---|---|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | yes | Site API key (Basic auth username) |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | yes | Derives `https://<subdomain>.chargify.com` |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | yes | Product family that holds the plans |
| `Maxio:BaseUrl` | — | no | When set, used verbatim as the API base address instead of deriving one from the subdomain |
| `Maxio:PaymentCollectionMethod` | — | no | Defaults to `remittance` so subscribe works without capturing a payment method |

### Running locally without SQL Server

```
ASPNETCORE_ENVIRONMENT=Development
UseOnlyInMemoryDatabase=true
DOTNET_ROLL_FORWARD=Major
```
