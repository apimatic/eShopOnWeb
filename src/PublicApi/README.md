# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

The payment API uses a two-step PayPal flow: checkout authorizes the exact catalog-priced
order total, fulfilment captures it, cancellation voids the authorization, and refunds act on
the capture. Card numbers and security codes are forwarded to PayPal only and are never
persisted by eShopOnWeb. Saved cards retain only PayPal's vault token and safe display data.

Configuration is bound from the `PayPal` section:

- `PayPal:ClientId`
- `PayPal:ClientSecret`
- `PayPal:Environment` (`sandbox` or `live`)
- `PayPal:Currency`
- `PayPal:BaseUrl` (optional; overrides the base URL for OAuth and every API request)

For local development, load the first four values from `PAYPAL_CLIENT_ID`,
`PAYPAL_CLIENT_SECRET`, `PAYPAL_ENVIRONMENT`, and `PAYPAL_CURRENCY` into user-secrets.
The PublicApi also maps those environment-variable names directly at startup. Use
`UseOnlyInMemoryDatabase=true` on machines without SQL Server LocalDB.

All routes require a JWT from `POST /api/authenticate`. Shopper routes use the token's user
name and never accept a buyer ID from a request. The operator routes `fulfil`, `cancel`, and
`reconciliation` additionally require the existing administrator role.

Payment routes:

- `POST /api/orders`
- `POST /api/orders/{orderId}/pay`
- `POST /api/orders/{orderId}/fulfil`
- `POST /api/orders/{orderId}/cancel`
- `POST /api/orders/{orderId}/refunds`
- `GET /api/my-orders`
- `POST`, `GET /api/payment-methods`
- `DELETE /api/payment-methods/{paymentMethodId}`
- `GET /api/reconciliation?from={iso-8601}&to={iso-8601}`

Refund requests require a caller-generated `idempotencyKey` of at most 108 characters. Reuse
that key only when retrying the same refund; use a new key for a separate partial refund.
