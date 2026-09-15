# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

PublicApi exposes an additive card-payment flow:

| Method | Route | Authorization | Purpose |
| --- | --- | --- | --- |
| `POST` | `/api/orders` | Shopper | Create an order from current catalog prices |
| `POST` | `/api/orders/{orderId}/pay` | Owning shopper | Authorize a one-off or saved card |
| `POST` | `/api/orders/{orderId}/fulfil` | Administrators | Renew a stale authorization when possible and capture it |
| `POST` | `/api/orders/{orderId}/cancel` | Administrators | Void an authorization and cancel an unfulfilled order |
| `POST` | `/api/orders/{orderId}/refunds` | Owning shopper | Full or partial capture refund |
| `GET` | `/api/my-orders` | Shopper | List the caller's orders and payment states |
| `POST/GET` | `/api/payment-methods` | Shopper | Save/list the caller's PayPal-vaulted cards |
| `DELETE` | `/api/payment-methods/{paymentMethodId}` | Owning shopper | Delete a vaulted card |
| `GET` | `/api/reconciliation?from=...&to=...` | Administrators | Compare all PayPal and local transactions in a range |

The PayPal client is implemented directly against the checked-in OpenAPI contracts:

- `api-specs/paypal/checkout_orders_v2/checkout_orders_v2.json`
- `api-specs/paypal/payments_payment_v2/payments_payment_v2.json`
- `api-specs/paypal/vault_payment_tokens_v3/vault_payment_tokens_v3.json`
- `api-specs/paypal/transaction_search_v1/transaction_search_v1.json`

No PayPal SDK is used. Card numbers and security codes are sent only to PayPal and are not persisted or logged. Saved-card rows contain the PayPal vault ID, brand, last four digits, and expiry.

### Configuration

The integration binds the `PayPal` section using these exact keys:

- `PayPal:ClientId`
- `PayPal:ClientSecret`
- `PayPal:Environment`
- `PayPal:Currency`
- `PayPal:BaseUrl` (optional)

The first four are also mapped at runtime from `PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`, `PAYPAL_ENVIRONMENT`, and `PAYPAL_CURRENCY`. To place them in PublicApi user-secrets without copying values into the repository:

```powershell
dotnet user-secrets set "PayPal:ClientId" $env:PAYPAL_CLIENT_ID --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" $env:PAYPAL_CLIENT_SECRET --project src/PublicApi
dotnet user-secrets set "PayPal:Environment" $env:PAYPAL_ENVIRONMENT --project src/PublicApi
dotnet user-secrets set "PayPal:Currency" $env:PAYPAL_CURRENCY --project src/PublicApi
```

When `PayPal:BaseUrl` is present, it is the base address for every request, including `/v1/oauth2/token`. Otherwise `PayPal:Environment` selects PayPal's sandbox or live API host.

For this repository's in-memory development mode:

```powershell
$env:DOTNET_ROLL_FORWARD = "Major"
$env:UseOnlyInMemoryDatabase = "true"
dotnet run --project src/PublicApi --launch-profile PublicApi
```

Authenticate at `POST /api/authenticate`, then use the returned token as `Authorization: Bearer <token>`. Swagger at `/swagger` documents the request and response bodies. Keep order creation, authorization, capture/void, and refund in the same host run when using the in-memory provider.

Refund requests require a caller-selected `idempotencyKey` (maximum 108 characters). Reusing it returns the original refund; a different key creates a distinct partial refund when captured funds remain. Reconciliation automatically splits ranges into the 31-day maximum accepted by Transaction Search and reads every page at 500 rows per page.
