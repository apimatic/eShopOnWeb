# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Maxio subscription billing

An additive, parallel capability that adds recurring-subscription billing on top of the existing
one-time commerce flow, with **Maxio Advanced Billing** as the system of record. All Maxio
interactions are built to the OpenAPI contract in [`/maxio-spec`](../../maxio-spec).

Endpoints (all JWT-authenticated; the caller's identity comes from the token):

| Method & route | Purpose |
|----------------|---------|
| `GET /api/subscription-plans` | List the plans (products in the configured Maxio product family). |
| `POST /api/subscriptions` | Subscribe the caller to a plan (`{"planHandle":"eshop-pro"}`; `planHandle` optional). |
| `GET /api/my-subscriptions` | List the caller's subscriptions. |

The subscribe flow is **idempotent**: it maps each user to exactly one Maxio customer (keyed by the
user's identity as the customer `reference`) and returns an existing live subscription rather than
creating a duplicate, so a double-click never creates two customers or subscriptions. Because Maxio
holds the mapping, it survives app restarts even when running on the in-memory database. Plans
require no stored payment method, so subscriptions are created on invoice billing (`remittance`).

### Configuration (`Maxio` section — supply via user-secrets, never commit values)

| Key | Source env var | Notes |
|-----|----------------|-------|
| `Maxio:ApiKey` | `MAXIO_API_KEY` | HTTP Basic username (password is the literal `x`). |
| `Maxio:Subdomain` | `MAXIO_SITE_SUBDOMAIN` | Used to derive the base URL (`https://{subdomain}.chargify.com`). |
| `Maxio:ProductFamilyHandle` | `MAXIO_DEFAULT_PRODUCT_FAMILY` | Product family whose products are the subscribable plans. |
| `Maxio:BaseUrl` | — | Optional. When set, used verbatim instead of deriving from the subdomain. |

Load the secrets (values come from the environment; they must never be written into the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

