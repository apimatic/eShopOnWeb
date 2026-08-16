# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb as an **additive** capability on the
**`src/PublicApi`** project (JWT auth). It does not change the existing catalog/basket/order
flow. PayPal is the processor; a shopper can also save a card and reuse it.

## What was added

| Capability | Endpoint | Who |
|---|---|---|
| Place an order (from catalog items) | `POST /api/orders` → `orderId` | shopper |
| Authorize/hold the order total (card or saved card) | `POST /api/orders/{orderId}/pay` | shopper |
| Fulfil → capture the money (fee/net recorded) | `POST /api/orders/{orderId}/fulfil` | **admin** |
| Cancel before fulfilment → release the hold | `POST /api/orders/{orderId}/cancel` | **admin** |
| Refund after fulfilment (full/partial, idempotent) | `POST /api/orders/{orderId}/refunds` → `refundId` | shopper (own order) |
| List my orders with payment state | `GET /api/my-orders` | shopper |
| Reconcile PayPal vs eShop for a date range | `GET /api/reconciliation?from=&to=` | **admin** |
| Save a card | `POST /api/payment-methods` → `paymentMethodId` | shopper |
| List my saved cards | `GET /api/payment-methods` | shopper |
| Delete a saved card | `DELETE /api/payment-methods/{paymentMethodId}` | shopper |

- **Authorize now, capture at fulfilment.** `pay` places a PayPal *authorization* (a hold) equal
  to the order total to the cent. `fulfil` *captures* it and records the captured amount, PayPal's
  fee and the net proceeds. `cancel` *voids* the hold (no money moves). `refunds` returns money
  after capture, never exceeding what was captured.
- **Stale authorizations** are reauthorized before capture rather than failing fulfilment; one that
  can no longer be renewed returns a clear, operator-actionable message.
- **Idempotent in effect.** A double-click on `pay`/`fulfil` never authorizes or captures twice.
  Refunds take a caller-supplied idempotency key (body `idempotencyKey` or `Idempotency-Key`
  header): repeating it returns the same `refundId`; two *different* keys make two partial refunds.
- **Ownership.** A shopper only ever sees/acts on their own orders and cards. Full card details are
  never stored in the app database and never logged; only a PayPal vault token plus a safe
  descriptor (brand / last four / expiry) are kept.

## Architecture

- `ApplicationCore/Entities/PaymentAggregate/OrderPayment` + `PaymentRefund` — the PayPal state the
  app owns a copy of (order/authorization/capture/refund ids and statuses, fee/net, refunds).
- `ApplicationCore/Entities/BuyerAggregate/SavedCard` — a vaulted card (token + safe descriptor).
- `ApplicationCore/Interfaces/PayPal/IPayPalClient` — the only seam that talks to PayPal;
  implemented by `Infrastructure/Services/PayPalClient` over the PayPal REST API (Orders v2,
  Payments v2, Vault v3, Transaction Search v1), following the PayPal plugin's best-practices
  reference: OAuth client-credentials with a cached token, a `PayPal-Request-Id` on every POST,
  429/5xx retry with backoff, and `debug_id` captured from errors.
- Services: `OrderPaymentService`, `SavedCardService`, `ReconciliationService`.
- Endpoints under `src/PublicApi/PaymentEndpoints/` following the project's `IEndpoint` convention.

## Configuration (secrets stay out of the repo)

Settings are bound from the **`PayPal:`** section — nothing is hard-coded:

| Key | From env var | Notes |
|---|---|---|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | REST app client id |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | REST app secret |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | `sandbox` or `live` |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | e.g. `USD` |
| `PayPal:BaseUrl` | *(optional)* | when set, used verbatim for **every** call incl. the token request; otherwise derived from `Environment` |

Load them into **.NET user-secrets** for the PublicApi project (values never touch the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
# Optional: dotnet user-secrets set "PayPal:BaseUrl" "https://api-m.sandbox.paypal.com"
```

## Run it (this machine)

Only the .NET 10 SDK is present and there is no SQL LocalDB, so roll forward and use the
in-memory store. In-memory data lives only for the run — pay/fulfil/refund the orders you create
in the same run.

```bash
cd src/PublicApi
DOTNET_ROLL_FORWARD=Major \
UseOnlyInMemoryDatabase=true \
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://localhost:9903;http://localhost:9904" \
dotnet run --no-launch-profile
```

Swagger: <https://localhost:9903/swagger>. If the dev cert isn't trusted, run
`dotnet dev-certs https --trust` (curl examples below use `-k`).

## Verify it yourself (no browser needed)

Demo users (seeded): `demouser@microsoft.com` (shopper) and `admin@microsoft.com` (operator),
password `Pass@word1`. Sandbox test card: Visa `4111 1111 1111 1111`, any future expiry, any CVC.

```bash
B=https://localhost:9903
tok(){ curl -sk -X POST "$B/api/authenticate" -H 'Content-Type: application/json' \
  -d "{\"username\":\"$1\",\"password\":\"Pass@word1\"}" | python -c "import sys,json;print(json.load(sys.stdin)['token'])"; }
U=$(tok demouser@microsoft.com)     # shopper
A=$(tok admin@microsoft.com)        # operator
CARD='{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"Demo User",
  "billingAddress":{"line1":"123 Main St","city":"San Jose","state":"CA","postalCode":"95131","countryCode":"US"}}'

# 1) Place an order (catalog item ids from GET /api/catalog-items). Returns orderId.
OID=$(curl -sk -X POST "$B/api/orders" -H "Authorization: Bearer $U" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":5,"quantity":2},{"catalogItemId":4,"quantity":1}]}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")

# 2) Pay = authorize/hold the total (does NOT take the money yet)
curl -sk -X POST "$B/api/orders/$OID/pay" -H "Authorization: Bearer $U" -H 'Content-Type: application/json' \
  -d "{\"card\":$CARD}"                         # -> status Authorized, authorizationId, hold = order total

# 3) Fulfil = capture (operator). Response shows capturedAmount, payPalFee, netAmount
curl -sk -X POST "$B/api/orders/$OID/fulfil" -H "Authorization: Bearer $A"

# 4) Refund part of it (shopper, own order). Returns refundId. Repeat same key -> same refundId
curl -sk -X POST "$B/api/orders/$OID/refunds" -H "Authorization: Bearer $U" -H 'Content-Type: application/json' \
  -d '{"amount":10.00,"idempotencyKey":"demo-key-1"}'

# 5) Save a card -> paymentMethodId; then reuse it to pay a second order
PMID=$(curl -sk -X POST "$B/api/payment-methods" -H "Authorization: Bearer $U" -H 'Content-Type: application/json' \
  -d "{\"card\":$CARD}" | python -c "import sys,json;print(json.load(sys.stdin)['paymentMethodId'])")
OID2=$(curl -sk -X POST "$B/api/orders" -H "Authorization: Bearer $U" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -sk -X POST "$B/api/orders/$OID2/pay" -H "Authorization: Bearer $U" -H 'Content-Type: application/json' \
  -d "{\"savedPaymentMethodId\":$PMID}"         # -> Authorized using the saved card

# 6) Cancel the second order before fulfilment (operator) -> hold voided, no money moved
curl -sk -X POST "$B/api/orders/$OID2/cancel" -H "Authorization: Bearer $A"

# 7) My orders with payment state; delete the saved card
curl -sk "$B/api/my-orders" -H "Authorization: Bearer $U"
curl -sk -X DELETE "$B/api/payment-methods/$PMID" -H "Authorization: Bearer $U"   # -> 204; card no longer usable

# 8) Reconciliation for a date range (operator). ISO-8601 date-times.
FROM=$(python -c "import datetime;print((datetime.datetime.now(datetime.UTC)-datetime.timedelta(days=20)).strftime('%Y-%m-%dT%H:%M:%SZ'))")
TO=$(python   -c "import datetime;print(datetime.datetime.now(datetime.UTC).strftime('%Y-%m-%dT%H:%M:%SZ'))")
curl -sk "$B/api/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $A"
```

### Notes on expected sandbox behaviour

- **Reconciliation lag.** PayPal's transaction reporting trails live activity, so a range that only
  covers payments you just created can legitimately show them as *eShop-only* (or come back empty on
  the PayPal side). The report is correct over a range that already has data; it fetches the **whole**
  range (chunked into ≤31-day windows, all pages), and lines PayPal transactions up against eShop
  captures by capture id and by the `ESHOP-…` reference stamped on each order.
- **Stale-authorization renewal** (fulfil) is implemented (reauthorize → capture; a hold that can no
  longer be renewed returns HTTP 409 with an operator-actionable message). A single sandbox run won't
  naturally age an authorization past its honor period, so this path isn't exercised by the steps above.
- **3-D Secure challenge.** The sandbox test card processes headlessly. If PayPal ever answered a card
  with a browser-approval challenge, `pay` returns HTTP 422 telling the operator to stop — this
  integration deliberately does not build a browser approval round-trip.
