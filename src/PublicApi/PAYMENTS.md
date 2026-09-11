# PayPal Payments & Saved Cards (PublicApi)

Adds real money movement to eShopOnWeb via **PayPal** as the payment processor, plus vaulted
(saved) cards. It is **additive** — the existing catalog/basket/order flow is untouched. Every
PayPal interaction is built strictly against the OpenAPI specs in `api-specs/paypal/`.

## Capability → PayPal API mapping

| Capability | PayPal API (spec) | Operation |
|---|---|---|
| Authorize (hold) | Checkout Orders v2 | `POST /v2/checkout/orders` (intent `AUTHORIZE`, `payment_source.card`) |
| Fulfil (capture) | Payments v2 | `POST /v2/payments/authorizations/{id}/capture` |
| Renew stale hold | Payments v2 | `POST /v2/payments/authorizations/{id}/reauthorize` |
| Cancel (release) | Payments v2 | `POST /v2/payments/authorizations/{id}/void` |
| Refund | Payments v2 | `POST /v2/payments/captures/{id}/refund` |
| Save / list / delete card | Vault Payment Tokens v3 | `POST`/`GET`/`DELETE /v3/vault/payment-tokens` |
| Reconciliation | Transaction Search v1 | `GET /v1/reporting/transactions` (chunked ≤31d, all pages) |
| Auth | (all specs) | `POST /v1/oauth2/token` (client credentials) |

## Configuration (`PayPal:` section — values never committed)

Bound from configuration into `PayPalSettings`:

| Key | Env var |
|---|---|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` (`sandbox`/`live`) |
| `PayPal:Currency` | `PAYPAL_CURRENCY` (e.g. `USD`) |
| `PayPal:BaseUrl` | optional explicit base URL; when set it is used verbatim for **every** call (incl. the token request), otherwise derived from `Environment` |

Load the secrets from the environment into .NET user-secrets (they are never written to any
repo file):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Running on this machine

```bash
export DOTNET_ROLL_FORWARD=Major          # only .NET 10 SDK present; global.json rolls forward
cd src/PublicApi
UseOnlyInMemoryDatabase=true ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="https://localhost:31483;http://localhost:31484" \
  dotnet run --no-launch-profile
```

The in-memory store resets each run and PublicApi keeps its own store, so create, pay, fulfil
and refund orders **within the same run**.

## Endpoints (all JWT-authenticated; identity comes from the token)

Shopper-scoped: `POST /api/orders`, `POST /api/orders/{id}/pay`,
`POST /api/orders/{id}/refunds`, `GET /api/my-orders`,
`POST|GET|DELETE /api/payment-methods`.
Operator (administrator role) only: `POST /api/orders/{id}/fulfil`,
`POST /api/orders/{id}/cancel`, `GET /api/reconciliation?from=&to=`.

Response id fields: `orderId` (place order), `paymentMethodId` (save card),
`refundId` (refund).

## Design notes

- **Idempotency** — authorize/capture/void carry a stable `PayPal-Request-Id`; repeating a
  `pay`/`fulfil`/`cancel` is a no-op against current state. Refunds use the caller-supplied
  idempotency key: a replay returns the same refund; distinct keys allow two partial refunds.
  A refund can never exceed the captured amount.
- **Stale holds** — before capture an expired (or nearly-expired) authorization is
  reauthorized; if PayPal reports it expired mid-capture it is renewed and retried once. A hold
  that can no longer be renewed returns a 409 telling the operator to collect payment again.
- **Card safety** — full card numbers/CVVs are never stored in the app database or logged; only
  the PayPal vault token id and a safe description (brand, last four, expiry) are kept.
- **3-D Secure** — if PayPal answers a card with a browser-approval challenge the API returns a
  clear `PAYER_ACTION_REQUIRED` error rather than attempting an approval round-trip.
