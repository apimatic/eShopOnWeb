# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb via **PayPal**, exposed as JWT-authenticated HTTP
endpoints on the **PublicApi** project. It is additive — the existing catalog/basket/order flow
is untouched; a new `OrderPayment` aggregate carries the payment/fulfilment state alongside the
reused `Order`/`OrderItem` model, and a `SavedPaymentMethod` aggregate holds vaulted cards.

## Endpoints

| Method & route | Role | Purpose |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog items (awaiting payment). Returns `orderId`. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** (hold) the order total with a one-off card or a saved card. |
| `POST /api/orders/{orderId}/fulfil` | operator | Mark fulfilled → **capture** the money (returns captured amount, PayPal fee, net). |
| `POST /api/orders/{orderId}/cancel` | operator | Cancel before fulfilment → **void** the hold (no money moves). |
| `POST /api/orders/{orderId}/refunds` | shopper | **Refund** a captured order, full or partial. Returns `refundId`. |
| `GET  /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET  /api/reconciliation?from=&to=` | operator | PayPal transactions for a range, lined up against eShop orders. |
| `POST /api/payment-methods` | shopper | Save a card (vaulted at PayPal). Returns `paymentMethodId`. |
| `GET  /api/payment-methods` | shopper | The caller's saved cards (safe descriptor only). |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card. |

Operator endpoints require the existing `Administrators` role. Every shopper endpoint acts only
on the caller's own data (identity taken from the JWT). Full card details are never stored in the
application database and never logged.

## PayPal APIs used (from `api-specs/`)

- **Checkout Orders v2** — create order with `intent=AUTHORIZE` + `payment_source.card`
  (raw card or `vault_id`) to place the hold.
- **Payments v2** — capture / void / reauthorize the authorization; refund the capture.
- **Vault v3** — save and delete cards (payment tokens).
- **Transaction Search v1** — reconciliation (paged, and chunked into ≤31-day windows).

Auth is OAuth2 client-credentials against the spec's token URL `/v1/oauth2/token`.

## Configuration (`PayPal:` section)

Bound from configuration; values are supplied via user-secrets / environment and never committed:

| Key | Source env var |
| --- | --- |
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` (`sandbox`/`live`) |
| `PayPal:Currency` | `PAYPAL_CURRENCY` |
| `PayPal:BaseUrl` | *(optional)* explicit API base; when set it is used verbatim for every call incl. the token request |

Load them once into user-secrets for the PublicApi project:

```bash
dotnet user-secrets --project src/PublicApi set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets --project src/PublicApi set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets --project src/PublicApi set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets --project src/PublicApi set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Running (this machine)

```bash
export DOTNET_ROLL_FORWARD=Major          # .NET 10 SDK rolls forward from the 8.0 pin
export ASPNETCORE_ENVIRONMENT=Development  # loads user-secrets
export UseOnlyInMemoryDatabase=true        # no LocalDB here
export ASPNETCORE_URLS="https://localhost:31383;http://localhost:31384"
dotnet run --project src/PublicApi/PublicApi.csproj
```

Get a bearer token from `POST /api/authenticate` (`demouser@microsoft.com` shopper /
`admin@microsoft.com` operator, password `Pass@word1`), then drive the endpoints above.

## Idempotency

- Pay / fulfil / cancel / refund are serialized per order and short-circuit on persisted state,
  so a double-click never authorizes or captures twice.
- Refunds carry a caller-supplied idempotency key: repeating it returns the same refund; two
  distinct keys make two distinct partial refunds. A partly-refunded order can never be refunded
  beyond what was captured.

> Note: with the in-memory database, data (orders, payments, saved cards) survives only within a
> single run — pay, fulfil and refund the orders you create in that same run.
