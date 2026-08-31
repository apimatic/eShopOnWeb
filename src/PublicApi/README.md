# API Endpoints

This folder demonstrates how to configure API endpoints as individual classes. You can compare it to the traditional controller-based approach found in /Web/Controllers/Api.

## PayPal payments

The payment API uses the existing catalog and order aggregate. `POST /api/orders` snapshots current
catalog prices into an order; `/pay` authorizes its exact total; `/fulfil` captures it; `/cancel`
voids an uncaptured authorization; and `/refunds` refunds a completed capture. Saved cards are PayPal
Vault tokens. Only their PayPal token ID, brand, last digits, and expiry are stored locally.

Configuration is bound from the `PayPal` section. The host maps the supplied environment variable
names to these keys without writing their values to configuration files:

| Configuration key | Environment variable |
| --- | --- |
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` |
| `PayPal:Currency` | `PAYPAL_CURRENCY` |
| `PayPal:BaseUrl` | `PAYPAL_BASE_URL` (optional) |

All payment routes require a PublicApi JWT. Fulfilment, cancellation, and reconciliation additionally
require the `Administrators` role. The reconciliation route accepts ISO-8601 `from` and `to` values,
splits ranges into PayPal-supported windows, and reads every result page.
