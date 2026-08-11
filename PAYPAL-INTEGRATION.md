# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb as an **additive** capability on the existing
catalog/basket/order model: hold the money at checkout, take it at fulfilment, give it back on a
return, plus save-a-card for reuse. PayPal is the processor. It is exposed entirely on
`src/PublicApi` (JWT-authenticated), and every flow is drivable through that API alone.

## Endpoints

| Method & route | Role | Purpose |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities (reuses `Order`/`OrderItem`). Starts *AwaitingPayment*. Returns `orderId`. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** (hold) the order total with card details or a saved card (`paymentMethodId`). No capture. |
| `POST /api/orders/{orderId}/fulfil` | admin | **Capture** the held funds. Renews a stale hold first; reports one that can't be renewed. |
| `POST /api/orders/{orderId}/cancel` | admin | **Void** the hold before fulfilment (no money moved). |
| `POST /api/orders/{orderId}/refunds` | shopper | **Refund** the caller's captured order in full/part under an idempotency key. Returns `refundId`. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from=&to=` | admin | PayPal's transactions for a range lined up against eShop orders (whole range, all pages). |
| `POST /api/payment-methods` | shopper | Vault a card. Returns `paymentMethodId` + safe descriptor (brand/last4/expiry). |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card (also removed from the PayPal vault). |

Shopper endpoints act only on the caller's own data (owner-scoped by the JWT subject, which matches
`Order.BuyerId`). `fulfil`, `cancel` and `reconciliation` require the `Administrators` role.

## PayPal APIs used (contract = `api-specs/paypal`)

Built by hand against the OpenAPI specs — **no** third-party PayPal SDK.

- **Orders v2** (`checkout_orders_v2`) — `POST /v2/checkout/orders` (intent `AUTHORIZE`, `payment_source.card` with raw card **or** `vault_id`).
- **Payments v2** (`payments_payment_v2`) — capture / reauthorize / void an authorization; refund / get a capture.
- **Vault v3** (`vault_payment_tokens_v3`) — `POST`/`GET`/`DELETE /v3/vault/payment-tokens`.
- **Transaction Search v1** (`transaction_search_v1`) — `GET /v1/reporting/transactions` (paged + 31-day-window-chunked).
- OAuth2 client-credentials token at `/v1/oauth2/token` (from the specs' security scheme).

## Design highlights

- **State on the order.** `Order` now carries `PaymentStatus` (AwaitingPayment → Authorized → Paid →
  PartiallyRefunded/Refunded, or Cancelled) plus the PayPal ids/statuses for the hold, capture and
  refunds, so a later request can act on what PayPal owns.
- **Idempotent in effect.** A per-order lock + the persisted state mean a double-click never
  authorizes or captures twice; PayPal `PayPal-Request-Id` adds an HTTP-level guard. Refunds use the
  caller's idempotency key (replays return the same refund; distinct keys are distinct partial refunds,
  never exceeding the captured amount).
- **Stale holds.** Fulfil checks the authorization and reauthorizes a stale one before capturing; if it
  can no longer be renewed the operator gets an actionable 409 instead of a silent failure.
- **Card safety.** The PAN/CVV are never persisted or logged — only brand/last4/expiry + the PayPal
  vault id are stored. A 3-D Secure browser challenge is reported (409), not worked around.
- **Reconciliation** matches PayPal transactions to orders by a unique per-order invoice reference
  (stamped as `invoice_id`) and by capture id, and surfaces both `inPayPalOnly` and `inEShopOnly`.

## Configuration (never committed)

Bound from the `PayPal:` section — load into user-secrets from the env vars:

```
PayPal:ClientId      <- PAYPAL_CLIENT_ID
PayPal:ClientSecret  <- PAYPAL_CLIENT_SECRET
PayPal:Environment   <- PAYPAL_ENVIRONMENT   (sandbox | live)
PayPal:Currency      <- PAYPAL_CURRENCY
PayPal:BaseUrl       <- optional; when set, used verbatim for every call (incl. token)
```

## Run

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
cd ../..

DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://localhost:8943;http://localhost:8944" \
UseOnlyInMemoryDatabase=true \
dotnet run --project src/PublicApi --no-launch-profile
```

The in-memory store resets each run, so create/pay/fulfil/refund the orders within one run. Test card:
Visa `4111 1111 1111 1111`, any future expiry (`YYYY-MM`), any CVC, any billing address.
