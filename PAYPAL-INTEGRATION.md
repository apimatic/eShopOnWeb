# PayPal payments & saved cards (PublicApi)

Additive capability on top of eShopOnWeb: collect money with **PayPal** as the processor, and
let shoppers **save a card** for reuse. The existing catalog/basket/order flow is untouched.

## What was added

All endpoints live on **`src/PublicApi`** (JWT auth; caller identity comes from the token) and
follow the project's `IEndpoint` convention. Money moves in the real PayPal stages:
**authorize (hold) → capture (at fulfilment) → refund**, plus **void** on cancel.

| Method & route | Who | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order (reuses the `Order`/`OrderItem` model). Amounts come from catalog prices. Returns top-level `orderId`. Starts *AwaitingPayment*. |
| `POST /api/orders/{orderId}/pay` | shopper (owner) | **Authorize** the total (hold, not taken). Body carries `card` **or** `savedPaymentMethodId`. Idempotent. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Capture the held funds. Renews a stale authorization; if it can't be renewed, reports it in operator terms. Records captured amount, PayPal fee, net proceeds. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Void the hold before fulfilment — funds released, no money moved. |
| `POST /api/orders/{orderId}/refunds` | shopper (owner) | Refund a capture, full or partial. Caller-supplied `idempotencyKey`; never refunds beyond what was captured. Returns top-level `refundId`. |
| `GET /api/my-orders` | shopper | The caller's own orders with payment state. |
| `GET /api/reconciliation?from=&to=` | **admin** | Lines PayPal's transaction records up against eShop orders over the whole range (ISO-8601 date-times). |
| `POST /api/payment-methods` | shopper | Save (vault) a card. Returns top-level `paymentMethodId` + safe descriptor (brand, last four, expiry) — never full card data. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper (owner) | Remove a saved card (deleted at PayPal too). |

Shopper endpoints act only on the caller's own data; `fulfil`/`cancel`/`reconciliation` are
restricted to the administrator role. Full card numbers / CVVs are never stored in the app DB
and never logged (they are sent straight to PayPal's Vault / Orders API).

### Design
- **PayPal REST** via a typed `HttpClient` (`Infrastructure/Payments/PayPalClient.cs`):
  Orders v2 (authorize), Payments v2 (capture / reauthorize / void / refund), Payment Method
  Tokens v3 (Vault), Transaction Search v1 (reconciliation). OAuth token cached
  (`PayPalTokenProvider`).
- New aggregates `OrderPayment` (+ `PaymentRefund`) and `SavedPaymentMethod`
  (`ApplicationCore/Entities/PaymentAggregate`), persisted via the existing repository/EF
  setup. EF migration `AddPaymentsAndVault` included for the SQL Server path.
- Idempotency: a per-order in-process lock plus deterministic `PayPal-Request-Id`s; refund
  keys are remembered so a repeat never double-refunds.
- Reconciliation walks the range in ≤31-day windows and pages each fully; correlates via
  `invoice_id`/`custom_id` set on every order.

## Configuration (no secrets in the repo)

Settings bind from the `PayPal:` section using exactly these keys: `PayPal:ClientId`,
`PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`, `PayPal:BaseUrl` (optional
verbatim override). Values come from **.NET user-secrets** or the `PAYPAL_*` environment
variables — never from a file in the repo.

Load user-secrets from the environment variables:

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # sandbox
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      # USD
```

(The app also maps the `PAYPAL_*` env vars onto `PayPal:*` at startup, so setting the env vars
alone is enough too.)

## Run it (this machine)

```bash
export DOTNET_ROLL_FORWARD=Major
export UseOnlyInMemoryDatabase=true            # no LocalDB here; data lives for one run
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="https://localhost:10703;http://localhost:10704"
dotnet run --project src/PublicApi --no-launch-profile
```

> In-memory store is per-host and reset on restart — place, pay, fulfil and refund orders
> within the same run. Swagger UI: `https://localhost:10703/swagger`.

## Verify end-to-end (curl)

```bash
API=https://localhost:10703
tok(){ curl -sk -X POST "$API/api/authenticate" -H "Content-Type: application/json" \
  -d "{\"username\":\"$1\",\"password\":\"Pass@word1\"}" | python -c 'import sys,json;print(json.load(sys.stdin)["token"])'; }
SHOP=$(tok demouser@microsoft.com)      # shopper
ADMIN=$(tok admin@microsoft.com)        # operator
CARD='{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","cardholderName":"Demo User"}'

# 1) place -> pay (authorize/hold) -> fulfil (capture) -> partial refund
OID=$(curl -sk -X POST "$API/api/orders" -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
      -d '{"items":[{"catalogItemId":5,"quantity":2}]}' | python -c 'import sys,json;print(json.load(sys.stdin)["orderId"])')
curl -sk -X POST "$API/api/orders/$OID/pay"    -H "Authorization: Bearer $SHOP"  -H "Content-Type: application/json" -d "{\"card\":$CARD}"
curl -sk -X POST "$API/api/orders/$OID/fulfil" -H "Authorization: Bearer $ADMIN"     # capture: shows capturedAmount, payPalFee, netAmount
curl -sk -X POST "$API/api/orders/$OID/refunds" -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
     -d '{"amount":5.00,"idempotencyKey":"demo-1"}'                                   # returns refundId; repeat same key => same refund

# 2) save a card, reuse it to pay a second order
PM=$(curl -sk -X POST "$API/api/payment-methods" -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
     -d "$CARD" | python -c 'import sys,json;print(json.load(sys.stdin)["paymentMethodId"])')
O2=$(curl -sk -X POST "$API/api/orders" -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
     -d '{"items":[{"catalogItemId":5,"quantity":1}]}' | python -c 'import sys,json;print(json.load(sys.stdin)["orderId"])')
curl -sk -X POST "$API/api/orders/$O2/pay"    -H "Authorization: Bearer $SHOP"  -H "Content-Type: application/json" -d "{\"savedPaymentMethodId\":$PM}"
curl -sk -X POST "$API/api/orders/$O2/fulfil" -H "Authorization: Bearer $ADMIN"

# 3) cancel (void) an authorized-but-unfulfilled order
O3=$(curl -sk -X POST "$API/api/orders" -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
     -d '{"items":[{"catalogItemId":5,"quantity":1}]}' | python -c 'import sys,json;print(json.load(sys.stdin)["orderId"])')
curl -sk -X POST "$API/api/orders/$O3/pay"    -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" -d "{\"card\":$CARD}"
curl -sk -X POST "$API/api/orders/$O3/cancel" -H "Authorization: Bearer $ADMIN"

# 4) views
curl -sk "$API/api/my-orders" -H "Authorization: Bearer $SHOP"
FROM=$(python -c "import datetime;print((datetime.datetime.now(datetime.UTC)-datetime.timedelta(days=20)).strftime('%Y-%m-%dT%H:%M:%SZ'))")
TO=$(python   -c "import datetime;print(datetime.datetime.now(datetime.UTC).strftime('%Y-%m-%dT%H:%M:%SZ'))")
curl -sk "$API/api/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $ADMIN"
```

> **Reconciliation note:** PayPal's transaction reporting lags live activity, so a range
> covering payments you just made can legitimately show them under `eShopOnly` (eShop knows,
> PayPal's report doesn't yet). That is expected sandbox behaviour, not a defect — the report
> is correct over ranges that already have data.
