# Payments & Saved Cards (PayPal)

An **additive** capability on the `PublicApi` project that lets a shopper pay for an order by card
(processed through **PayPal**) and save a card for reuse. It does not change the existing
catalog/basket/order flow — it adds the money movement and the operator flows around it.

PayPal integration:
- **Orders v2** — create an order with `intent=AUTHORIZE` and a direct card (or a vaulted card) → a hold.
- **Payments v2** — capture (fulfil), reauthorize (renew a stale hold), void (cancel), refund.
- **Payment Method Tokens v3 (vault)** — save / delete a card token; pay with `card.vault_id`.
- **Transaction Search v1** — reconciliation report over a date range.

Full card details are **never** stored in this app's database and never written to logs. Only PayPal's
vault token id and a safe descriptor (brand, last four, expiry) are kept for saved cards.

## Endpoints

All routes are JWT-authenticated and shopper-scoped unless marked **operator** (Administrators role).

| Method & route | Who | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items → returns `orderId` |
| `POST /api/orders/{orderId}/pay` | shopper | Authorize (hold) the total; body carries a card **or** `savedPaymentMethodId` |
| `POST /api/orders/{orderId}/fulfil` | **operator** | Capture the money; records captured amount, PayPal fee, net |
| `POST /api/orders/{orderId}/cancel` | **operator** | Void the hold before fulfilment (funds released) |
| `POST /api/orders/{orderId}/refunds` | shopper | Refund a capture (full/partial) → returns `refundId`; body carries `idempotencyKey` |
| `GET /api/my-orders` | shopper | The caller's orders with payment state |
| `GET /api/reconciliation?from=&to=` | **operator** | PayPal's transactions vs eShop orders for an ISO-8601 range |
| `POST /api/payment-methods` | shopper | Save a card → returns `paymentMethodId` + safe descriptor |
| `GET /api/payment-methods` | shopper | The caller's saved cards |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card (also deletes the PayPal vault token) |

Behavioural guarantees:
- **Idempotent in effect** — a double-click never authorizes or captures twice (guarded by payment state +
  a stable `PayPal-Request-Id`). Refunds use the caller's `idempotencyKey`: a repeat returns the same refund;
  distinct keys are distinct partial refunds. Total refunds can never exceed the captured amount.
- **Stale holds are renewed** — at fulfilment an expired authorization is re-authorized before capture; if it
  can no longer be renewed, the response says so in operator-actionable terms.
- **3-D Secure** — if PayPal answers a card with a browser-approval challenge (`PAYER_ACTION_REQUIRED`), the
  request is rejected (HTTP 422); no approval round-trip is built.
- **Ownership** — one shopper can never see, use, or delete another's orders or saved cards (returns 404).

## Configuration (`PayPal` section)

Bound from the `PayPal:` configuration section — **no values are hard-coded**. Load them into user-secrets
(the values come from the `PAYPAL_*` environment variables; never commit them):

```
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"     --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET" --project src/PublicApi
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   --project src/PublicApi
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      --project src/PublicApi
```

- `PayPal:Environment` — `sandbox` (default) or `live`/`production`; selects the API host.
- `PayPal:BaseUrl` — optional override used **verbatim** for every call (including the token request) when set.
- `PayPal:Currency` — settlement currency; the authorized amount always equals the order total to the cent.

## Running locally

```
export DOTNET_ROLL_FORWARD=Major            # global.json rolls forward to the .NET 10 SDK
export UseOnlyInMemoryDatabase=true         # no LocalDB on this box; data lives for one run only
cd src/PublicApi && dotnet run
```

Get a bearer token from `POST /api/authenticate` (`admin@microsoft.com` / `demouser@microsoft.com`,
password `Pass@word1`). Verify with PayPal's sandbox test card: Visa `4111 1111 1111 1111`, any future
expiry (`YYYY-MM`), any CVC.

### Windows dev caveat (TLS)

On **Windows**, .NET's `SslStream` (SChannel) TLS ClientHello is fingerprinted and refused by PayPal's
sandbox card-processing fraud layer (`TRANSACTION_REFUSED`), while OpenSSL clients (curl/python) — and .NET
on **Linux** (the Docker deploy target, which uses OpenSSL) — are accepted. The integration code is correct
and works directly on Linux. To drive the flow on a Windows dev box, re-originate the egress through an
OpenSSL client by pointing `PayPal:BaseUrl` at a local forwarding proxy (see the verification steps used
during development). This affects only local Windows testing, not production.
