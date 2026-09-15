# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb as an **additive** capability on the `PublicApi`
project: a shopper places an order, authorizes it (hold), an operator fulfils it (capture),
and it can be cancelled (void) or refunded — all through PayPal, plus saved (vaulted) cards.
It does not change the existing catalog/basket/order flow.

## What was built

| Method & route | Who | What it does |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities (priced from the catalog). Returns `orderId`. Starts **AwaitingPayment**. |
| `POST /api/orders/{orderId}/pay` | shopper (own) | **Authorize** the total (hold, no capture) with one-off card details **or** a saved card (`savedPaymentMethodId`). |
| `POST /api/orders/{orderId}/fulfil` | admin | **Capture** the hold. Records captured amount, PayPal fee and net proceeds. Renews a stale authorization first when possible. |
| `POST /api/orders/{orderId}/cancel` | admin | **Void** the hold before fulfilment — no money moves. |
| `POST /api/orders/{orderId}/refunds` | shopper (own) | Refund the capture, full or partial. Body: optional `amount`, required `idempotencyKey`. Returns `refundId`. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from=&to=` | admin | PayPal's transactions for a date range lined up against eShop orders. Covers the whole range. |
| `POST /api/payment-methods` | shopper | Save (vault) a card. Returns `paymentMethodId` and a safe description. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper (own) | Remove a saved card (deletes it from PayPal's vault too). |

Design notes:
- The order/payment state lives on the existing `Order` aggregate (`Order.Status` + an owned
  `Payment` carrying PayPal's order/authorization/capture ids, statuses, fee/net, and refunds).
- Saved cards are the `PaymentMethod` aggregate, scoped to their owner. **The full card number is
  never stored by this app** and never written to logs — only PayPal's vault token id and the safe
  brand/last-four/expiry.
- PayPal is reached over HTTP (Orders v2, Payments v2, Vault v3, Transaction Search v1) via
  `IPayPalPaymentGateway` (`src/Infrastructure/Services/PayPal/PayPalPaymentGateway.cs`).
- **Idempotency:** authorize/capture use a stable `PayPal-Request-Id` per order plus a DB status
  guard, so a double-click never holds/captures twice. Refunds use the caller-supplied key (stored
  and replayed) — the same key never refunds twice, but two distinct partial refunds are allowed.
- A card that would need a **browser (3-D Secure) challenge** is refused with HTTP 422 rather than
  building an approval round-trip (this integration is browser-free).

## Configuration

Settings bind from the `PayPal` configuration section (keys, never hard-coded values):
`PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, and the optional
`PayPal:BaseUrl`. When `PayPal:BaseUrl` is set it is used verbatim for **every** PayPal call
(including the OAuth token request); otherwise the host is derived from `PayPal:Environment`
(`sandbox` → `https://api-m.sandbox.paypal.com`, `live` → `https://api-m.paypal.com`).

Load the sandbox credentials into user-secrets (values come from the environment; they never enter
the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # sandbox
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      # USD
```

## Run it (this machine)

```bash
export DOTNET_ROLL_FORWARD=Major
cd src/PublicApi
ASPNETCORE_ENVIRONMENT=Development \
UseOnlyInMemoryDatabase=true \
ASPNETCORE_URLS="https://localhost:8523;http://localhost:8524" \
dotnet run --no-launch-profile
```

Caveats on this machine: the in-memory database is **per host and reset on restart**, so pay,
fulfil and refund orders created in the *same run*. `curl -k` skips dev-cert validation
(or `dotnet dev-certs https --trust`).

## Verify it yourself (no browser)

All commands use `curl -k` against `https://localhost:8523`. Get two tokens first:

```bash
BASE=https://localhost:8523/api
SHOPPER=$(curl -sk -X POST $BASE/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
ADMIN=$(curl -sk -X POST $BASE/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
```

**1. Place an order** (note the returned `orderId`):

```bash
curl -sk -X POST $BASE/orders -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}'
```

**2. Pay (authorize) with the sandbox test card** — include a billing address for reliable sandbox
AVS. The response shows `status: Authorized` and the hold's `authorizationId`:

```bash
curl -sk -X POST $BASE/orders/1/pay -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"John Doe",
       "billingAddress":{"addressLine1":"123 Main St","adminArea2":"San Jose","adminArea1":"CA","postalCode":"95131","countryCode":"US"}}}'
```

**3. See payment state:** `curl -sk $BASE/my-orders -H "Authorization: Bearer $SHOPPER"`

**4. Fulfil (capture) as operator** — response shows `capturedAmount`, `payPalFee`, `netAmount`:

```bash
curl -sk -X POST $BASE/orders/1/fulfil -H "Authorization: Bearer $ADMIN"
```

**5. Refund** (partial, then repeat with the *same* key to prove idempotency):

```bash
curl -sk -X POST $BASE/orders/1/refunds -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" \
  -d '{"amount":10.00,"idempotencyKey":"refund-key-A"}'   # returns refundId
curl -sk -X POST $BASE/orders/1/refunds -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" \
  -d '{"amount":10.00,"idempotencyKey":"refund-key-A"}'   # same refundId, no second refund
```

An over-refund (more than the remaining captured amount) returns HTTP 409.

**6. Saved card — save, reuse, cancel, delete:**

```bash
# save
curl -sk -X POST $BASE/payment-methods -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" \
  -d '{"alias":"My Visa","card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"John Doe",
       "billingAddress":{"addressLine1":"123 Main St","adminArea2":"San Jose","adminArea1":"CA","postalCode":"95131","countryCode":"US"}}}'
# -> paymentMethodId (say 1)
curl -sk $BASE/payment-methods -H "Authorization: Bearer $SHOPPER"

# new order, pay with the saved card
curl -sk -X POST $BASE/orders -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":3,"quantity":1}]}'
curl -sk -X POST $BASE/orders/2/pay -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" -d '{"savedPaymentMethodId":1}'

# cancel that order before fulfilment (operator) -> hold voided, no money moved
curl -sk -X POST $BASE/orders/2/cancel -H "Authorization: Bearer $ADMIN"

# delete the saved card -> 204; afterwards it is gone and can no longer pay
curl -sk -X DELETE $BASE/payment-methods/1 -H "Authorization: Bearer $SHOPPER" -w "%{http_code}\n"
```

**7. Reconciliation** (operator; `from`/`to` are ISO-8601 date-times):

```bash
curl -sk -G $BASE/reconciliation -H "Authorization: Bearer $ADMIN" \
  --data-urlencode "from=2026-07-01T00:00:00Z" --data-urlencode "to=2026-07-31T23:59:59Z"
```

PayPal's transaction reporting lags live activity by up to a few hours, so a range covering payments
you just created may legitimately come back with those under `inEShopNotInPayPal` (or empty). That is
expected sandbox behaviour, not a gap — run the report over an older range that has data to see
matched/PayPal-only entries. Ranges longer than PayPal's 31-day window are chunked and fully paginated.
