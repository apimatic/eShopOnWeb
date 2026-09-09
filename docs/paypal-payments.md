# PayPal payments & saved cards (PublicApi)

Additive capability on `src/PublicApi`: collect money for orders with **PayPal** as the processor,
and let a shopper save a card for reuse. It does not change the existing catalog/basket/order flow.

## What was added

- **Domain** (`ApplicationCore`): `Order` gained a `Status` (`AwaitingPayment → Authorized → Fulfilled`/`Cancelled`)
  and a `Payment` (PayPal order/authorization/capture ids + statuses, captured amount, PayPal fee, net
  proceeds, and a `Refund` collection). `Buyer`/`PaymentMethod` now model saved cards (PayPal vault id +
  safe descriptor only). No card number or CVV is ever stored or logged.
- **Gateway** (`Infrastructure/PayPal`): `PayPalClient : IPayPalPaymentGateway` over the PayPal REST API
  (OAuth client-credentials with token caching; Orders v2 authorize, Payments v2 capture/reauthorize/void/refund,
  Payment Method Tokens v3 vaulting, Transaction Search reporting).
- **Services** (`ApplicationCore/Services`): `OrderPaymentService` (place/pay/fulfil/cancel/refund/my-orders/
  reconcile) and `SavedCardService` (save/list/delete).
- **Endpoints** (`PublicApi/PaymentEndpoints`): the routes below.

## Endpoints

| Method & route | Role | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order (awaiting payment) from catalog item ids + quantities. Returns `orderId`. |
| `POST /api/orders/{orderId}/pay` | shopper (own) | Authorize (hold) the total with `card` **or** `savedCardId`. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Fulfil + capture (re-authorizes a stale hold first). |
| `POST /api/orders/{orderId}/cancel` | **admin** | Cancel before fulfilment; voids the hold. |
| `POST /api/orders/{orderId}/refunds` | shopper (own) | Refund (full/partial). Body `{ amount?, idempotencyKey }`. Returns `refundId`. |
| `GET /api/my-orders` | shopper | Caller's orders with payment state. |
| `GET /api/reconciliation?from=&to=` | **admin** | PayPal transactions vs eShop orders over an ISO-8601 range. |
| `POST /api/payment-methods` | shopper | Save a card. Returns `paymentMethodId` + safe descriptor. |
| `GET /api/payment-methods` | shopper | Caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper (own) | Remove a saved card. |

Idempotency: `pay` and `fulfil` are idempotent in effect (double-click never double-charges). `refunds`
dedupe on the caller-supplied `idempotencyKey` (per capture); distinct keys = distinct partial refunds;
never refundable beyond the captured amount.

## Configuration

Settings bind from the `PayPal:` section — `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`
(`sandbox`/`live`), `PayPal:Currency`, and optional `PayPal:BaseUrl` (used verbatim for every call,
including the token request, when set). No values are hard-coded. Load credentials from the environment
into user-secrets (values never go in the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

(The app also reads the flat `PAYPAL_*` environment variables onto the `PayPal:` section as a fallback.)

## Run (this machine)

The SDK is .NET 10 only and the ASP.NET Core 8 runtime is absent, so roll forward; there is no LocalDB,
so use the in-memory database. Bind to a free port in your assigned block.

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development
export UseOnlyInMemoryDatabase=true
export ASPNETCORE_URLS="https://localhost:30123;http://localhost:30124"   # a free port in your block
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> In-memory state is per-process and resets on restart, and each host has its own store. Create, pay,
> fulfil and refund within one run, through PublicApi alone.

## Verify by hand

```bash
B=https://localhost:30123
SHOP=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)
ADMIN=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq -r .token)
CARD='{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"John Doe",
  "billingAddress":{"addressLine1":"123 Main St","city":"San Jose","state":"CA","postalCode":"95131","countryCode":"US"}}'

# 1. place -> 2. authorize -> 3. fulfil(capture) -> 4. partial refund
OID=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}' | jq -r .orderId)
curl -sk -X POST $B/api/orders/$OID/pay    -H "Authorization: Bearer $SHOP"  -H "Content-Type: application/json" -d "{\"card\":$CARD}"
curl -sk -X POST $B/api/orders/$OID/fulfil -H "Authorization: Bearer $ADMIN"
curl -sk -X POST $B/api/orders/$OID/refunds -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"amount":10.00,"idempotencyKey":"refund-1"}'
curl -sk $B/api/my-orders -H "Authorization: Bearer $SHOP"

# saved card: save -> reuse to pay a second order
PMID=$(curl -sk -X POST $B/api/payment-methods -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d "{\"card\":$CARD,\"alias\":\"my visa\"}" | jq -r .paymentMethodId)
OID2=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | jq -r .orderId)
curl -sk -X POST $B/api/orders/$OID2/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d "{\"savedCardId\":$PMID}"

# operator reconciliation (ISO-8601 date-times)
curl -sk "$B/api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-09-09T23:59:59Z" -H "Authorization: Bearer $ADMIN"
```

Use the sandbox test card Visa `4111 1111 1111 1111`, any future expiry, any CVC. PayPal's transaction
reporting lags live activity by up to ~3 hours, so a reconciliation range covering payments you just made
may legitimately show them as "eShop-only" until PayPal's report catches up — that is expected, not a gap.
