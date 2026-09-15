# PayPal payments & saved cards (PublicApi)

Additive capability on top of the existing catalog/basket/order flow: collect money with **PayPal**
(authorize at checkout, capture at fulfilment, refund on return) and let a shopper **save a card**
to reuse on a later order. Everything is drivable through the PublicApi HTTP surface alone.

## Configuration

Settings are bound from the `PayPal:` configuration section (never hard-coded):

| Key | Source env var | Notes |
|-----|----------------|-------|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | REST app client id (sandbox business account) |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | REST app secret |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | `sandbox` (default) or `live` |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | ISO-4217, e.g. `USD` |
| `PayPal:BaseUrl` | – | Optional. When set, used verbatim as the API base for **every** call (incl. token); otherwise derived from `Environment` |

Load the credentials into user-secrets (they must never be committed):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Endpoints

Flow 1 — pay for an order:
- `POST /api/orders` — place an order from catalog item ids + quantities (shopper). Returns `orderId`.
- `POST /api/orders/{orderId}/pay` — **authorize** (hold) the total with card details or a saved `paymentMethodId` (shopper).
- `POST /api/orders/{orderId}/fulfil` — capture the held funds (operator/admin). Records captured amount, PayPal fee, net proceeds.
- `POST /api/orders/{orderId}/cancel` — void the hold before fulfilment (operator/admin).
- `POST /api/orders/{orderId}/refunds` — refund a captured order, full or partial (shopper). Body: `{ "amount"?, "idempotencyKey" }`. Returns `refundId`.
- `GET /api/my-orders` — the caller's orders with payment state (shopper).
- `GET /api/reconciliation?from={iso}&to={iso}` — PayPal transactions vs eShop orders (operator/admin).

Flow 2 — saved cards (all shopper-scoped):
- `POST /api/payment-methods` — vault a card. Returns `paymentMethodId` + safe description (brand, last four, expiry).
- `GET /api/payment-methods` — the caller's saved cards.
- `DELETE /api/payment-methods/{paymentMethodId}` — remove a saved card (also deletes it from PayPal's vault).

Operator actions (`fulfil`, `cancel`, `reconciliation`) require the `Administrators` role; every other
endpoint acts only on the caller's own data.

## Behaviour notes

- **Authorize ≠ capture.** `/pay` holds the money; `/fulfil` takes it. `/cancel` voids the hold; `/refunds` returns captured money.
- **Idempotency.** A double `/pay` never authorizes twice; a repeated `/refunds` under the same `idempotencyKey` never refunds twice; two different keys are two legitimate partial refunds. A partly-refunded order never becomes refundable beyond what was captured.
- **Stale holds.** `/fulfil` checks the authorization and re-authorizes a stale hold before capturing; a hold that can no longer be renewed is reported in operator-actionable terms.
- **Card data.** Full card details flow straight to PayPal and are never stored in this app's database or written to logs. Only PayPal's vault token id and a safe description are persisted.
- **3-D Secure.** If PayPal answers a card with a browser-approval challenge (`PAYER_ACTION_REQUIRED`), the request fails with a clear message — no approval round-trip is built.
- **Reconciliation lag.** PayPal's Transaction Search can take up to ~3 hours to show a just-created transaction, so a reconciliation range covering very recent payments may legitimately show them as eShop-only until PayPal catches up.

## Verify end-to-end (sandbox, no browser)

Run with the environment gotchas honoured:

```bash
cd src/PublicApi
DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
  ASPNETCORE_URLS="https://localhost:9583;http://localhost:9584" \
  dotnet run --no-launch-profile
```

1. Get a shopper token: `POST /api/authenticate {"username":"demouser@microsoft.com","password":"Pass@word1"}`.
2. Get an admin token: same with `admin@microsoft.com`.
3. `POST /api/orders` with `{"items":[{"catalogItemId":1,"quantity":2}]}` → note `orderId`.
4. `POST /api/orders/{orderId}/pay` with the sandbox test card
   `{"card":{"number":"4111111111111111","expiry":"2027-02","securityCode":"123","name":"Test Shopper"}}`
   → order becomes `Authorized`, payment shows the authorization id.
5. `POST /api/orders/{orderId}/fulfil` (admin) → order `Fulfilled`; payment shows captured amount / fee / net.
6. `POST /api/orders/{orderId}/refunds` (shopper) `{"amount":5.00,"idempotencyKey":"r1"}` → returns `refundId`; repeat with the same key returns the same refund.
7. `POST /api/payment-methods` (shopper) with the same card → returns `paymentMethodId`; place a second order and `/pay` it with `{"paymentMethodId": <id>}`, then `/fulfil`.
8. `GET /api/reconciliation?from=...&to=...` (admin) lists PayPal transactions lined up against eShop orders.

The whole flow works with the direct sandbox card — no browser step required.
