# PayPal payments API

The implementation uses the repository's PayPal OpenAPI documents as its wire contract:

- `checkout_orders_v2` creates and authorizes orders.
- `payments_payment_v2` reads/renews/voids authorizations, captures, and refunds.
- `vault_payment_tokens_v3` creates and deletes saved-card tokens.
- `transaction_search_v1` supplies reconciliation data.

No PayPal SDK is used. Card numbers and security codes exist only in the inbound request and
the outbound PayPal request. The application persists only PayPal's vault token and masked
card metadata.

## Configuration

PublicApi binds `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`,
`PayPal:Currency`, and optional `PayPal:BaseUrl`. The standard deployment environment names
`PAYPAL_CLIENT_ID`, `PAYPAL_CLIENT_SECRET`, `PAYPAL_ENVIRONMENT`, and `PAYPAL_CURRENCY` are
mapped to those keys at startup. If `PayPal:BaseUrl` is present, OAuth and every API operation
use that base URL.

For local development, load the four environment values into user-secrets and run PublicApi
with `UseOnlyInMemoryDatabase=true`. Keep the same process running for the complete order
lifecycle because the in-memory database is per-process.

## Lifecycle and access

`POST /api/orders` creates an `AwaitingPayment` order. `POST /api/orders/{id}/pay` performs
authorization only. An administrator captures it with `/fulfil` or releases the hold with
`/cancel`. The owning shopper can refund a fulfilled order with a caller-provided
`idempotencyKey`. `/reconciliation` is administrator-only and exhausts all PayPal pages and
splits ranges longer than PayPal's 31-day request limit.

All routes require a PublicApi bearer token. Shopper routes resolve ownership exclusively
from the token's name claim; request bodies cannot select a buyer.
