# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb as an **additive** capability on top of the existing
catalog / basket / order model. PayPal is the payment processor. Everything is driven through
JWT-authenticated HTTP endpoints on the **PublicApi** project — no storefront UI.

The integration is built **directly against the PayPal OpenAPI specifications** in `api-specs/`
(no third-party PayPal SDK):

| Capability                     | PayPal API (spec)                    | Endpoint(s) used                                                  |
| ------------------------------ | ------------------------------------ | ----------------------------------------------------------------- |
| Authorize a hold               | Checkout Orders v2                   | `POST /v2/checkout/orders` (intent `AUTHORIZE`, card / vault_id)  |
| Capture at fulfilment          | Payments v2                          | `POST /v2/payments/authorizations/{id}/capture`                   |
| Renew a stale hold             | Payments v2                          | `POST /v2/payments/authorizations/{id}/reauthorize`               |
| Release a hold (cancel)        | Payments v2                          | `POST /v2/payments/authorizations/{id}/void`                      |
| Refund a capture               | Payments v2                          | `POST /v2/payments/captures/{id}/refund`                          |
| Save / delete a card           | Vault v3                             | `POST` / `DELETE /v3/vault/payment-tokens`                        |
| Reconciliation                 | Transaction Search v1                | `GET /v1/reporting/transactions` (all pages, 31-day windows)      |
| Auth                           | OAuth2 client-credentials (per spec) | `POST /v1/oauth2/token`                                           |

## Endpoints

Shopper-scoped (any authenticated user, acts only on the caller's own data):

- `POST /api/orders` — place an order from catalog item ids + quantities → returns `orderId`.
- `POST /api/orders/{orderId}/pay` — **authorize** the total (hold). Body carries `card` **or** `savedPaymentMethodId`.
- `POST /api/orders/{orderId}/refunds` — refund a captured payment, full or partial → returns `refundId`. Requires an idempotency key (`idempotencyKey` in the body or an `Idempotency-Key` header).
- `GET  /api/my-orders` — the caller's orders with their payment state.
- `POST /api/payment-methods` — save a card → returns `paymentMethodId` + a safe description.
- `GET  /api/payment-methods` — the caller's saved cards.
- `DELETE /api/payment-methods/{paymentMethodId}` — remove a saved card.

Operator-only (**Administrators** role):

- `POST /api/orders/{orderId}/fulfil` — capture the held funds (money is taken here). A stale hold is reauthorized first; one that cannot be renewed returns an operator-actionable message.
- `POST /api/orders/{orderId}/cancel` — void the hold before fulfilment (no money moves).
- `GET  /api/reconciliation?from={ISO-8601}&to={ISO-8601}` — PayPal's transaction record lined up against eShop orders across the whole range.

## Configuration (`PayPal:` section)

Bound from configuration; **no values are committed to the repository** — load them into .NET
user-secrets (or environment) for the PublicApi project:

| Key                  | Source env var         | Notes                                                            |
| -------------------- | ---------------------- | ---------------------------------------------------------------- |
| `PayPal:ClientId`    | `PAYPAL_CLIENT_ID`     | REST app client id of the sandbox **business** account.          |
| `PayPal:ClientSecret`| `PAYPAL_CLIENT_SECRET` | REST app secret.                                                 |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT`   | `sandbox` (default) or `live`.                                   |
| `PayPal:Currency`    | `PAYPAL_CURRENCY`      | ISO-4217, e.g. `USD`.                                            |
| `PayPal:BaseUrl`     | (optional)             | When set, used verbatim for **every** call, including the token. |

```powershell
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$env:PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$env:PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$env:PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$env:PAYPAL_CURRENCY"
```

## Design notes

- **Money it holds.** A `Payment` aggregate (1:1 with `Order`) carries the state PayPal owns —
  the PayPal order id, authorization id + status + expiry, capture id + status + captured/fee/net
  amounts, and the list of refunds — so any later request can act on it.
- **Idempotency.** PayPal `PayPal-Request-Id` keys are built from globally-unique ids (a per-payment
  GUID seed for the authorization; PayPal's own authorization/capture ids for capture/void/refund).
  Order ids alone are **not** safe: the in-memory provider restarts ids at 1 and PayPal remembers
  request-ids and invoice-ids forever. Refunds also carry a caller idempotency key — repeating it
  returns the same refund; distinct keys make legitimate separate partial refunds. Refunds can never
  exceed the captured amount.
- **Card data.** Full card details are passed straight to PayPal and are never stored in the app
  database nor written to logs. Saved cards keep only the PayPal vault token plus brand + last four
  digits + expiry.
- **Ownership.** Orders and saved cards are scoped by the buyer (the JWT `name` claim). One shopper
  can never see, use, or delete another's; a foreign or missing resource is reported as `404`.
