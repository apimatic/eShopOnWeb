# PayPal payments & saved cards (PublicApi)

An **additive** capability on `src/PublicApi` that lets a shopper pay for an order by card via
**PayPal** and save a card for reuse. It does not change the existing catalog/basket/order flow.

## Money model

An order gains a payment/fulfilment lifecycle (`Order.Status`) and an owned `OrderPayment` that
carries the PayPal-owned state — the hold (authorization), the capture, and the refunds:

```
AwaitingPayment ──/pay──▶ PaymentAuthorized ──/fulfil──▶ Fulfilled ──/refunds──▶ Partially/Refunded
                                   │
                                   └──/cancel──▶ Cancelled (hold released, no money moved)
```

- **Authorize (`/pay`)** holds the order total to the cent; it does not take the money.
- **Fulfil (`/fulfil`)** captures the hold — that is when the money is taken. The payment then
  records what PayPal reported: captured amount, PayPal fee, and net proceeds. A hold that has
  gone stale is renewed (reauthorize) before capture; one that can no longer be renewed is
  reported to the operator.
- **Cancel (`/cancel`)** voids the hold before fulfilment — no money ever moved.
- **Refund (`/refunds`)** returns a captured payment, fully or partially, under a caller-supplied
  idempotency key. A partly-refunded order never becomes refundable beyond what was captured.

Saved cards are vaulted in **PayPal Vault** (`/v3/vault/payment-tokens`). This app stores only the
vault token id and a safe description (brand, last four, expiry) — never full card details.

## Endpoints

| Method & route | Who | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items → `orderId` |
| `POST /api/orders/{id}/pay` | shopper | Authorize (hold) with a one-off card or `savedPaymentMethodId` |
| `POST /api/orders/{id}/fulfil` | **admin** | Capture at fulfilment |
| `POST /api/orders/{id}/cancel` | **admin** | Void the hold before fulfilment |
| `POST /api/orders/{id}/refunds` | shopper | Refund (full/partial) → `refundId` (needs `idempotencyKey`) |
| `GET  /api/my-orders` | shopper | The caller's orders with payment state |
| `GET  /api/reconciliation?from=&to=` | **admin** | PayPal transactions vs eShop orders over a range |
| `POST /api/payment-methods` | shopper | Save a card → `paymentMethodId` |
| `GET  /api/payment-methods` | shopper | The caller's saved cards |
| `DELETE /api/payment-methods/{id}` | shopper | Remove a saved card |

Shopper endpoints act only on the caller's own data (identity from the JWT). Fulfil, cancel and
reconciliation require the `Administrators` role.

## Configuration

Bound from the `PayPal:` section — no values are hard-coded; load them into .NET user-secrets:

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # sandbox | live
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      # e.g. USD
# Optional: dotnet user-secrets set "PayPal:BaseUrl" "https://api-m.sandbox.paypal.com"
```

`PayPal:BaseUrl` is an optional override used verbatim for **every** PayPal call (including the
token request) when set; otherwise the base URL is derived from `PayPal:Environment`.

## Run (this machine)

```bash
export DOTNET_ROLL_FORWARD=Major
cd src/PublicApi
UseOnlyInMemoryDatabase=true ASPNETCORE_URLS="https://localhost:8783;http://localhost:8784" \
  dotnet run --no-launch-profile
```

The in-memory store is per-process and resets on restart, so pay/fulfil/refund the orders you
create within the same run. See `docs/paypal-verify.md` for a full end-to-end walkthrough.
