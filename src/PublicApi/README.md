# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## Payments (PayPal)

Orders can be paid for by card, held, taken at fulfilment, released on a cancellation and given back on a
return. Cards can be saved for later orders. Everything is exposed as ordinary endpoints here, so the whole
flow is drivable over HTTP with a JWT from `POST /api/authenticate`.

| Endpoint | Who | What it does |
| --- | --- | --- |
| `POST /api/orders` | shopper | Places an order from catalog items. Starts `AwaitingPayment`. Returns `orderId`. |
| `POST /api/orders/{orderId}/pay` | shopper | Authorizes the order total - the money is held, not taken. Body carries either `card` or a `paymentMethodId`. |
| `POST /api/orders/{orderId}/fulfil` | operator | Takes the held money. Reports PayPal's gross, fee and net. Renews a hold that has gone stale. |
| `POST /api/orders/{orderId}/cancel` | operator | Releases the hold before fulfilment, so no money moves. |
| `POST /api/orders/{orderId}/refunds` | shopper | Returns captured money, in full or in part. Requires an `idempotencyKey` (body or `Idempotency-Key` header). Returns `refundId`. |
| `GET /api/my-orders` | shopper | The caller's orders with their payment state. |
| `GET /api/payment-methods` | shopper | The caller's saved cards, described safely (brand, last four, expiry). |
| `POST /api/payment-methods` | shopper | Saves a card for later. Returns `paymentMethodId`. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Forgets a saved card, here and at PayPal. |
| `GET /api/reconciliation?from=&to=` | operator | PayPal's own record for the range, lined up against this application's payments. |

Configuration comes from the `PayPal:` section - never from values committed to the repository:

| Key | Environment variable | Notes |
| --- | --- | --- |
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | REST app credentials |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | REST app credentials |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | `sandbox` or `live` |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | ISO code every payment is moved in |
| `PayPal:BaseUrl` | `PAYPAL_BASE_URL` | Optional. When set it is used verbatim for every call, including the token request. |

Locally, put the credentials in user secrets:

```powershell
dotnet user-secrets --project src/PublicApi set "PayPal:ClientId" $env:PAYPAL_CLIENT_ID
dotnet user-secrets --project src/PublicApi set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET
dotnet user-secrets --project src/PublicApi set "PayPal:Environment" $env:PAYPAL_ENVIRONMENT
dotnet user-secrets --project src/PublicApi set "PayPal:Currency" $env:PAYPAL_CURRENCY
```

Card numbers are handed to PayPal and dropped: they are never stored in the database and never written to
logs. A saved card is kept as PayPal's payment-method token, so a later order can be paid without the
shopper entering anything again, and deleting it removes the token at PayPal too.

