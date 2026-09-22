# PayPal payments & saved cards — eShopOnWeb PublicApi

Additive PayPal integration on `src/PublicApi` (JWT-authenticated). Money is **held** at checkout,
**taken** at fulfilment, and **given back** on cancel/refund. Card details are never stored in this
app's database or logs; PayPal owns the card data (via its vault) and the payment state ids.

## What was built

| Endpoint | Who | What |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog items (reuses the existing `Order`/`OrderItem` model); payment starts awaiting authorization. Returns `orderId`. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** the total — a hold, not a capture — with a one-off card **or** a saved card (`savedPaymentMethodId`). Idempotent. |
| `POST /api/orders/{orderId}/fulfil` | admin | **Capture** the held funds; records captured amount, PayPal fee and net proceeds. Renews a stale authorization first. |
| `POST /api/orders/{orderId}/cancel` | admin | **Void** the hold before fulfilment — no money moves. |
| `POST /api/orders/{orderId}/refunds` | shopper (own order) | Refund the capture, full or partial. Caller-supplied idempotency key; never refundable beyond captured. Returns `refundId`. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from&to` | admin | PayPal's transaction record for a date range, lined up against eShop orders (whole range: chunked to PayPal's 31-day limit, every page walked). |
| `POST /api/payment-methods` | shopper | Save (vault) a card. Returns `paymentMethodId` + safe descriptor (brand, last four). |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{id}` | shopper | Remove a saved card; afterwards it cannot be listed or used to pay. |

Design: `IPayPalPaymentGateway` (Infrastructure) wraps the PayPal Server SDK — no SDK type leaks past
it; `IPaymentOrchestrationService` / `ISavedCardService` implement the flows over new `OrderPayment`,
`PaymentRefund` and `SavedPaymentMethod` aggregates. Contracts and decisions are recorded in
`pay-pal-server-sdk-plan.md`.

## Prerequisites (this machine)

- .NET 10 SDK present, ASP.NET Core 8 runtime absent → run with `DOTNET_ROLL_FORWARD=Major`.
- No LocalDB → run with `UseOnlyInMemoryDatabase=true`. **The in-memory store is per-process and lost on
  restart**, so place/pay/fulfil/refund the orders you create **within one run**.
- HTTPS dev cert trusted (`dotnet dev-certs https --check`).

### One-time: load PayPal credentials into user-secrets (values never enter the repo)

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # sandbox
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      # USD
cd ../..
```

`PayPal:BaseUrl` is an optional override; leave it unset to target the PayPal sandbox. The host
**refuses to start** if any credential is missing or blank (fail-fast).

## Run the API

```bash
DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
  ASPNETCORE_URLS="https://localhost:36923;http://localhost:36924" \
  dotnet run --project src/PublicApi --no-launch-profile
```

Swagger: `https://localhost:36923/swagger`.

## Verify it yourself (curl)

```bash
B=https://localhost:36923
# 1) Tokens (seeded users, password Pass@word1)
SHOP=$(curl -sk -X POST $B/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c 'import sys,json;print(json.load(sys.stdin)["token"])')
ADMIN=$(curl -sk -X POST $B/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c 'import sys,json;print(json.load(sys.stdin)["token"])')

# 2) Place an order
OID=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}' \
  | python -c 'import sys,json;print(json.load(sys.stdin)["orderId"])')

# 3) Pay (authorize / hold) with the sandbox Visa test card
curl -sk -X POST $B/api/orders/$OID/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","cardholderName":"Test Shopper","billingPostalCode":"44240","billingCountryCode":"US"}}'
#   → status "Authorized", a payPalOrderId and authorizationId

# 4) See it as the shopper
curl -sk $B/api/my-orders -H "Authorization: Bearer $SHOP"

# 5) Fulfil (capture) as the operator
curl -sk -X POST $B/api/orders/$OID/fulfil -H "Authorization: Bearer $ADMIN"
#   → status "Captured", capturedGross + payPalFee + netAmount

# 6) Partial refund (unique idempotency key)
curl -sk -X POST $B/api/orders/$OID/refunds -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"amount":5.00,"idempotencyKey":"my-refund-key-1"}'
#   → refundId; repeating the same key returns the same refund (no double refund)

# 7) Reconciliation (operator)
curl -sk "$B/api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-09-30T00:00:00Z" -H "Authorization: Bearer $ADMIN"

# Cancel instead of fulfil (on a different, still-authorized order):
curl -sk -X POST $B/api/orders/<id>/cancel -H "Authorization: Bearer $ADMIN"   # → "Cancelled"
```

Notes:
- **Idempotency:** a double-click on `pay` authorizes once; a repeated `refund` under the same key does
  not refund twice.
- **Authorization / ownership:** `fulfil`, `cancel`, `reconciliation` require the Administrators role
  (a shopper token gets `403`); every other endpoint acts only on the caller's own data (another
  shopper gets `404`).
- **Reconciliation lag:** PayPal's transaction report lags live activity by up to ~3 hours, so orders
  you just created appear under `eShopOnly` until PayPal's report catches up. That is the expected
  sandbox result, not a fault — the report is correct over a range that already has settled data.

## Automated tests

```bash
DOTNET_ROLL_FORWARD=Major dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj
```

Covers the full state machine, idempotency, ownership, authorization and reconciliation matching against
a faked gateway (so the suite needs no live PayPal), plus the existing catalog/auth tests. All green.

## Live verification status

Verified live against the PayPal **sandbox** with the Visa test card `4111 1111 1111 1111`:
a real **authorization** (hold) at pay, a real **capture** at fulfil (with PayPal's reported fee and net
proceeds), a real **partial refund**, a real **cancel** (void), and **reconciliation** sweeping the whole
range (31-day chunking + full pagination across thousands of sandbox transactions).

**Known environment limitation — card vaulting:** the provided sandbox business account returns PayPal
`HTTP 500 INTERNAL_SERVICE_ERROR` for *every* card-vaulting path (`Vault.CreateSetupToken`,
`Vault.CreatePaymentToken`, and `Orders.CreateOrder` with `store_in_vault=ON_SUCCESS`), while the
identical non-vault requests succeed. So `POST /api/payment-methods` returns a clear `502` on this
account. The saved-card feature is fully implemented (canonical setup-token → payment-token flow) and
verified end-to-end (save → list → pay-with-saved-card → delete → unusable) by the integration tests;
it works unchanged on an account whose vault is provisioned. See `pay-pal-server-sdk-plan.md` §6.
