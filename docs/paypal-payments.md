# PayPal payments & saved cards (PublicApi)

An **additive** capability on `src/PublicApi`: a shopper places an order, pays it by card (PayPal
holds the money), and an operator fulfils (captures), cancels (releases the hold) or refunds it.
Shoppers can also save a card once and reuse it. It does not replace the existing catalog/basket/order
flow and reuses the app's `Order` / `OrderItem` model.

All PayPal interaction is a first-party REST integration built against the endpoints documented by
the **paypal** plugin: OAuth (`/v1/oauth2/token`), Orders v2 (`/v2/checkout/orders`, `intent=AUTHORIZE`,
`payment_source.card`), Payments v2 (`/v2/payments/authorizations/{id}/capture|void|reauthorize`,
`/v2/payments/captures/{id}/refund`), Vault v3 (`/v3/vault/payment-tokens`), and Transaction Search
(`/v1/reporting/transactions`).

## Endpoints

| Method & route | Role | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items → `orderId`. Starts *AwaitingPayment*. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** (hold) the total with a card or a saved card. |
| `POST /api/orders/{orderId}/fulfil` | admin | **Capture** the money; response shows captured amount, PayPal fee, net. |
| `POST /api/orders/{orderId}/cancel` | admin | Void the hold before fulfilment (no money moves). |
| `POST /api/orders/{orderId}/refunds` | shopper | Refund a capture, full or partial → `refundId`. Idempotency key required. |
| `GET  /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET  /api/reconciliation?from=&to=` | admin | PayPal's transactions for a range vs eShop orders. |
| `POST /api/payment-methods` | shopper | Save a card (vault) → `paymentMethodId`. Safe descriptor only. |
| `GET  /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card. |

Shopper endpoints act only on the caller's own data (identity comes from the JWT). Fulfil, cancel and
reconciliation require the `Administrators` role. Full card details are never stored or logged.

## Configuration

Settings bind from the `PayPal:` section — no values are hard-coded:

| Key | Env var it mirrors |
|---|---|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` (`sandbox` / `production`) |
| `PayPal:Currency` | `PAYPAL_CURRENCY` |
| `PayPal:BaseUrl` | *(optional)* explicit API base; used verbatim for **every** call incl. the token request |

Load the secrets into user-secrets for `src/PublicApi` (values stay out of the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Running (this machine)

`global.json` rolls forward to the installed SDK; run with the in-memory database:

```bash
cd src/PublicApi
DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development \
  ASPNETCORE_URLS="https://localhost:9983;http://localhost:9984" \
  dotnet run --no-launch-profile
```

`UseOnlyInMemoryDatabase=true` is set in `appsettings.Development.json` (no LocalDB here). The
in-memory store is per-process and resets on restart, so pay/fulfil/refund the orders created in the
same run. A SQL migration (`AddPaymentsAndSavedCards`) is included for a real database.

## End-to-end verification

See the "Verify it yourself" steps in the task hand-off, or drive the table above through Swagger at
`https://localhost:9983/swagger`. Authenticate first via `POST /api/authenticate`
(`demouser@microsoft.com` / `admin@microsoft.com`, password `Pass@word1`) and send the returned token
as `Authorization: Bearer <token>`. Use PayPal's sandbox test card `4111 1111 1111 1111`, any future
expiry, any CVC.
