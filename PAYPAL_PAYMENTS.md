# PayPal Payments & Saved Cards (PublicApi)

This adds real money movement to eShopOnWeb as an **additive** capability on top of the existing
catalog/basket/order model. PayPal is the processor; every PayPal interaction goes through the
`paypal-sdk` (`AsadAli.Checkout.Sdk`) inside a single gateway class.

- **Flow 1 – Pay for an order:** place an order → authorize a hold → fulfil (capture) → cancel (void) or refund.
- **Flow 2 – Saved cards:** save a card once (PayPal vault) and reuse it to pay later orders.

All capabilities are HTTP endpoints on **`src/PublicApi`** (JWT-authenticated), routed under `/api/`.

## Architecture (where things live)

| Concern | Location |
|---|---|
| Order lifecycle state (`OrderStatus`, transitions) | `src/ApplicationCore/Entities/OrderAggregate/Order.cs`, `OrderStatus.cs` |
| Payment aggregate (hold/capture/refund state, ids, invariants) | `src/ApplicationCore/Entities/PaymentAggregate/Payment.cs`, `PaymentRefund.cs` |
| Saved card | `src/ApplicationCore/Entities/PaymentAggregate/SavedPaymentMethod.cs` |
| Gateway abstraction (SDK-free contract) | `src/ApplicationCore/Interfaces/IPaymentGateway.cs` |
| Orchestration services | `src/ApplicationCore/Services/PaymentService.cs`, `SavedCardService.cs`, `ReconciliationService.cs`, `PaymentReadService.cs` |
| **PayPal SDK integration (the only place the SDK is used)** | `src/Infrastructure/Services/PayPal/PayPalPaymentGateway.cs` |
| SDK client + gateway registration | `src/Infrastructure/Services/PayPal/PayPalRegistration.cs` |
| HTTP endpoints | `src/PublicApi/PaymentEndpoints/*` |

## Endpoints

| Method & route | Who | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities → returns `orderId` |
| `POST /api/orders/{orderId}/pay` | shopper | Authorize (hold) the total with a one-off card **or** a saved card |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Fulfil → capture; shows captured amount, PayPal fee, net proceeds |
| `POST /api/orders/{orderId}/cancel` | **admin** | Cancel before fulfilment → release the hold (void) |
| `POST /api/orders/{orderId}/refunds` | shopper | Refund (full/partial); body carries an `idempotencyKey` → returns `refundId` |
| `GET /api/my-orders` | shopper | The caller's orders with payment state |
| `GET /api/reconciliation?from=&to=` | **admin** | PayPal's transactions for a range vs eShop orders (all pages) |
| `POST /api/payment-methods` | shopper | Save a card (vault) → returns `paymentMethodId` + safe description |
| `GET /api/payment-methods` | shopper | The caller's saved cards |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card |

Idempotency: pay/fulfil never double-charge (local state guard + PayPal `PayPal-Request-Id`);
refunds dedupe on the caller-supplied `idempotencyKey`; two distinct keys are two legitimate partial refunds.
Ownership: a shopper only ever sees/acts on their own orders and saved cards. Full card numbers are never
stored in this app's database and never logged.

---

## Configuration

Settings are bound from the `PayPal:` section — **never committed**; load the values into .NET
user-secrets for `src/PublicApi` (the values come from environment variables):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # sandbox
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      # USD
# Optional: dotnet user-secrets set "PayPal:BaseUrl" "https://api-m.sandbox.paypal.com"
```

`PayPal:BaseUrl` is an optional override used verbatim for **every** call (including the OAuth token
request) when set; otherwise the SDK's sandbox host is used.

---

## Run it (this machine)

The SDK is pinned to .NET 8; only the .NET 10 SDK + ASP.NET Core 8 runtime are present, so roll forward,
and use the in-memory database (no LocalDB here). PublicApi keeps its own in-memory store, so drive the whole
flow through PublicApi (that is why `POST /api/orders` exists) and pay/fulfil/refund within one run.

```bash
export DOTNET_ROLL_FORWARD=Major
cd src/PublicApi
UseOnlyInMemoryDatabase=true ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://localhost:9203;http://localhost:9204" \
dotnet run
```

(Trust the dev cert once with `dotnet dev-certs https --trust`, or use `curl -k` as below.)

### 1. Get bearer tokens

```bash
API=https://localhost:9203/api
DTOK=$(curl -sk -X POST "$API/authenticate" -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | grep -o '"token":"[^"]*"' | sed 's/"token":"//;s/"//')
ATOK=$(curl -sk -X POST "$API/authenticate" -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | grep -o '"token":"[^"]*"' | sed 's/"token":"//;s/"//')
```

### 2. Flow 2 — save a card, then Flow 1 — pay a first order with it

```bash
# Save the card (PayPal vault). Returns paymentMethodId + safe description (brand + last4).
curl -sk -X POST "$API/payment-methods" -H "Authorization: Bearer $DTOK" -H "Content-Type: application/json" \
  -d '{"number":"4111111111111111","expiryMonth":"12","expiryYear":"2030","securityCode":"123","cardholderName":"Demo Shopper"}'

# Place order A, then AUTHORIZE (hold) with the saved card id 1.
curl -sk -X POST "$API/orders" -H "Authorization: Bearer $DTOK" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":1,"quantity":2}]}'
curl -sk -X POST "$API/orders/1/pay" -H "Authorization: Bearer $DTOK" -H "Content-Type: application/json" \
  -d '{"savedPaymentMethodId":1}'

# FULFIL (capture) — admin. Response shows capturedAmount, payPalFee, netAmount.
curl -sk -X POST "$API/orders/1/fulfil" -H "Authorization: Bearer $ATOK"

# REFUND $5 (shopper) — idempotencyKey required. Repeat with the same key: no double refund.
curl -sk -X POST "$API/orders/1/refunds" -H "Authorization: Bearer $DTOK" -H "Content-Type: application/json" \
  -d '{"amount":5.00,"idempotencyKey":"demo-refund-1"}'
```

### 3. Reuse the saved card on a second order, then cancel it before fulfilment

```bash
curl -sk -X POST "$API/orders" -H "Authorization: Bearer $DTOK" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":2,"quantity":1}]}'
curl -sk -X POST "$API/orders/2/pay" -H "Authorization: Bearer $DTOK" -H "Content-Type: application/json" \
  -d '{"savedPaymentMethodId":1}'
# CANCEL (admin) before fulfilment → the hold is voided, no money moved.
curl -sk -X POST "$API/orders/2/cancel" -H "Authorization: Bearer $ATOK"
```

### 4. One-off card payment (no saved card)

```bash
curl -sk -X POST "$API/orders" -H "Authorization: Bearer $DTOK" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}'
curl -sk -X POST "$API/orders/3/pay" -H "Authorization: Bearer $DTOK" -H "Content-Type: application/json" -d '{
  "card":{"number":"4111111111111111","expiryMonth":"12","expiryYear":"2030","securityCode":"123",
          "cardholderName":"Demo Shopper",
          "billingAddress":{"line1":"123 Main St","city":"San Jose","state":"CA","postalCode":"95131","countryCode":"US"}}}'
```

### 5. Read state, reconcile, and remove the card

```bash
curl -sk "$API/my-orders" -H "Authorization: Bearer $DTOK"
curl -sk "$API/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-31T00:00:00Z" -H "Authorization: Bearer $ATOK"
curl -sk "$API/payment-methods" -H "Authorization: Bearer $DTOK"
curl -sk -X DELETE "$API/payment-methods/1" -H "Authorization: Bearer $DTOK"   # 204; afterwards it can no longer pay
```

---

## Sandbox notes / gotchas

- **Transaction reporting lags.** `GET /api/reconciliation` over a range that includes payments you just
  made may show them only under `onlyInEShop` (PayPal has not reported them yet) — that is expected, not a
  bug. Over a range that already has settled data the report matches by transaction id. It pages through the
  **whole** range, not just the first 100.
- **Sandbox risk declines (`TRANSACTION_REFUSED`).** The sandbox business account's risk engine can decline
  card **order authorizations** — sometimes intermittently under high velocity (many authorizations in a
  short window during repeated testing). The card and credentials are valid (vaulting the same card
  succeeds), the request shape is correct per the SDK contract, and the identical code path authorizes when
  the processor accepts it. If a `pay` returns `TRANSACTION_REFUSED`, wait a little and retry, or re-run with
  a fresh order — this is an account/runtime decision from PayPal, not a code fault. (A genuine 3-D Secure
  *challenge* — `PAYER_ACTION_REQUIRED` — is surfaced as an error and intentionally **not** worked around
  with a browser round-trip.)
- **In-memory store per host.** Data does not survive a restart and is not shared with the Web storefront.
  Create, pay, fulfil and refund within a single PublicApi run.

## Tests

- Domain invariants: `tests/UnitTests/ApplicationCore/Entities/PaymentTests/PaymentAggregateTests.cs`
- Orchestration (authorize/fulfil/cancel/refund, idempotency, reauth-on-stale, ownership) with a fake
  gateway: `tests/UnitTests/ApplicationCore/Services/PaymentServiceTests/*`

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet test tests/UnitTests/UnitTests.csproj
```
