# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

PublicApi supports direct-card authorization, capture on fulfilment, void on cancellation,
partial/full refunds, PayPal transaction reconciliation, and vaulted cards. Configure these
keys in the `PayPal` section; the four `PAYPAL_*` environment variables are mapped to them at
startup, and .NET user-secrets can be used for local development:

- `PayPal:ClientId` (`PAYPAL_CLIENT_ID`)
- `PayPal:ClientSecret` (`PAYPAL_CLIENT_SECRET`)
- `PayPal:Environment` (`PAYPAL_ENVIRONMENT`)
- `PayPal:Currency` (`PAYPAL_CURRENCY`)
- `PayPal:BaseUrl` (optional override used for every call, including OAuth)

No credential or full card number is persisted. Because the API accepts primary account
numbers directly, deploy it only behind TLS and operate it within the applicable PCI DSS
scope.

Payment routes are under `/api`: `orders`, `orders/{id}/pay`,
`orders/{id}/fulfil`, `orders/{id}/cancel`, `orders/{id}/refunds`, `my-orders`,
`payment-methods`, and `reconciliation`. All require a JWT from `/api/authenticate`.
Fulfilment, cancellation, and reconciliation additionally require the `Administrators` role.
