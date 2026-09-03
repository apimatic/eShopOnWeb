# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

The JWT API additionally exposes the complete order-payment lifecycle:

- `POST /api/orders` and `GET /api/my-orders`
- `POST /api/orders/{orderId}/pay`
- `POST /api/orders/{orderId}/fulfil` (administrator)
- `POST /api/orders/{orderId}/cancel` (administrator)
- `POST /api/orders/{orderId}/refunds`
- `POST|GET /api/payment-methods` and `DELETE /api/payment-methods/{paymentMethodId}`
- `GET /api/reconciliation?from=...&to=...` (administrator)

Configure `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, and
`PayPal:Currency`. `PayPal:BaseUrl` is an optional absolute HTTPS override and applies to
both OAuth and API calls. The process also maps `PAYPAL_CLIENT_ID`,
`PAYPAL_CLIENT_SECRET`, `PAYPAL_ENVIRONMENT`, `PAYPAL_CURRENCY`, and optional
`PAYPAL_BASE_URL` into those section keys at startup.

The application persists only PayPal token identifiers and safe card metadata (brand,
last four digits, and expiry). Card numbers and security codes are neither persisted nor
included in SDK request logging. Payment mutations use stable PayPal request IDs; refund
requests additionally require a caller-supplied `idempotencyKey`.
