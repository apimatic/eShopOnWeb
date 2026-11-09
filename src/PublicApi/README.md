# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.


## Stax payments

Card and bank payments go through Stax, using the SDK in `sdk/stax`. Settings are read from the `Stax:` section:

| Key | Meaning |
| --- | --- |
| `Stax:ApiKey` | the shop's Stax API key (sandbox key while testing) |
| `Stax:PlatformApiKey` | only if the shop is a Stax platform; needed to onboard sellers (`POST /api/admin/sellers`) |
| `Stax:BaseUrl` | optional; used verbatim instead of the public base address |
| `Stax:SyncIntervalSeconds` | optional; how often pending bank payments/refunds are re-checked (default 300) |

Load the keys into user-secrets, never into a file in the repository:

```
dotnet user-secrets set "Stax:ApiKey" "$STAX_API_KEY" --project src/PublicApi
dotnet user-secrets set "Stax:PlatformApiKey" "$STAX_PLATFORM_API_KEY" --project src/PublicApi   # only if you have one
```

Endpoints (shopper = any authenticated caller, identity from the JWT; admin = `Administrators` role):
`/api/payment-methods` (POST/GET/DELETE `{id}`), `/api/orders` (POST), `/api/orders/{id}` (GET),
`/api/orders/{id}/checkout`, `/api/orders/{id}/pay`, `/api/account/close`,
`/api/admin/shoppers/{email}/payment-methods`, `/api/admin/orders/{id}` (GET), `/api/admin/orders/{id}/refunds`,
`/api/admin/invoice-schedules` (+ `/{id}/pause`, `/{id}/resume`), `/api/admin/sellers`.

Notes:
* A bank payment is `pending` until Stax reports it settled; a background worker (and the order endpoints) pick that up, so `paid` turns true without anyone checking by hand.
* Stax only allows refunding a bank payment some business days after it was made. Such a refund is accepted (`refundStatus: pending`) and the worker sends it as soon as Stax allows it.
* The SDK does not model a few things the integration needs, so it adds them around the calls: a `customer_id`/`email` filter on list calls, reading the body of invoice-schedule calls, an idempotency key on charges, and Stax's surcharge preview (`GET /surcharge/review`, called directly with the shop key).
* The tests in `tests/PublicApiIntegrationTests/StaxEndpoints` run against the Stax sandbox.
