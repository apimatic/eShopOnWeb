# Subscriptions (Maxio Advanced Billing)

Recurring billing, parallel to the Catalog → Basket → Order flow. Maxio is the system of record; endpoints live on `src/PublicApi` (JWT).

| Endpoint | Purpose |
| --- | --- |
| `GET /api/subscription-plans` | Plans of the product family named by `Maxio:ProductFamilyHandle` (`truncated: true` if the page cap cut the list) |
| `POST /api/subscriptions` `{ "planHandle": "eshop-pro" }` | Subscribe the token's user. Idempotent per user + plan: 201 on create, 200 + `alreadySubscribed: true` on repeat, 409 if another request for the same account is in flight, 503 if the outcome at Maxio is still pending (retry; nothing is duplicated) |
| `GET /api/my-subscriptions` | The caller's subscriptions (plan, price, state, next billing date) |

## Configuration (`Maxio:` section)

| Key | Notes |
| --- | --- |
| `Maxio:ApiKey` | secret — user-secrets / secret store, never a file in the repo |
| `Maxio:Subdomain` | site subdomain |
| `Maxio:ProductFamilyHandle` | family whose products are the plans |
| `Maxio:BaseUrl` | optional; used verbatim as the API base address instead of one derived from the subdomain |

The host refuses to start when a required key is blank.

```
cd src/PublicApi
dotnet user-secrets set "Maxio:ApiKey" "$MAXIO_API_KEY"
dotnet user-secrets set "Maxio:Subdomain" "$MAXIO_SITE_SUBDOMAIN"
dotnet user-secrets set "Maxio:ProductFamilyHandle" "$MAXIO_DEFAULT_PRODUCT_FAMILY"
```

The Maxio .NET SDK ships as source inside the maxio-advanced-billing plugin; `Infrastructure.csproj` references it through the `MaxioSdkProject` property (machine-local path, override with `/p:MaxioSdkProject=...`).
Design notes and the SDK contract sheet: `maxio-advanced-billing-plan.md`.
