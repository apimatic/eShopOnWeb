# Subscription billing API

The subscription endpoints use Maxio Advanced Billing as the billing system of record.
The Maxio API key, site subdomain, and product-family handle are bound from the `Maxio`
configuration section. `Maxio:BaseUrl` is optional; when empty, the US/EU server template
from `maxio-spec/openapi.yaml` is selected using `MAXIO_ENVIRONMENT`.

All endpoints require a PublicApi JWT. The caller identity is taken from the token; request
bodies cannot select another eShop user.

- `GET /api/subscription-plans`
- `POST /api/subscriptions` with `{ "planHandle": "eshop-pro" }`
- `GET /api/my-subscriptions`

Maxio customer references and subscription references are deterministic, and the local
identity database stores the resulting Maxio IDs. The in-memory provider is suitable for
local runs only; use the SQL Server provider and apply the included identity migration for
durable mappings across restarts.
