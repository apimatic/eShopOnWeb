# PayPal payments & saved cards (PublicApi)

An **additive** capability on top of eShopOnWeb: shoppers pay for orders by card through PayPal,
save cards for reuse, and operators fulfil / cancel / refund and reconcile. The existing
catalog / basket / order flow is untouched. All endpoints live on **`src/PublicApi`** (JWT auth)
under `/api/`.

## What was built

**Flow 1 — pay for an order**

| Method & route | Role | What it does |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items (`{ items:[{catalogItemId,quantity}], shipToAddress? }`). Priced from the catalog. Starts **AwaitingPayment**. Returns top-level **`orderId`**. |
| `POST /api/orders/{orderId}/pay` | shopper (own order) | **Authorize** (hold) the order total. Body carries either `card{…}` (one-off) **or** `savedPaymentMethodId`. |
| `POST /api/orders/{orderId}/fulfil` | admin | **Capture** the hold (takes the money); records captured amount, PayPal fee, net proceeds. Renews a stale authorization first; if it can no longer be renewed, returns an operator-actionable error. |
| `POST /api/orders/{orderId}/cancel` | admin | **Void** the hold before fulfilment — held funds released, no money moved. |
| `POST /api/orders/{orderId}/refunds` | shopper (own order) | Refund the capture, full or partial. Body `{ amount?, idempotencyKey }`. Returns top-level **`refundId`**. Never refundable beyond what was captured. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from={iso}&to={iso}` | admin | PayPal's transaction report for the range (all pages) lined up against eShop orders. |

**Flow 2 — saved cards**

| Method & route | Role | What it does |
|---|---|---|
| `POST /api/payment-methods` | shopper | Vault a card at PayPal. Body `{ card{…} }`. Returns top-level **`paymentMethodId`** + safe display (brand, last4, expiry). |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card (deletes the PayPal vault token too). Afterwards it can no longer be listed or used to pay. |

### PayPal APIs used (all verified live against the sandbox)
- OAuth2 client-credentials token — `POST /v1/oauth2/token`
- Create order `intent=AUTHORIZE` with a raw card **or** a vaulted card (`payment_source.card.vault_id`), single call — `POST /v2/checkout/orders` → authorization `CREATED`
- Capture — `POST /v2/payments/authorizations/{id}/capture` → `seller_receivable_breakdown` (gross / paypal_fee / net)
- Reauthorize (stale hold) — `POST /v2/payments/authorizations/{id}/reauthorize`
- Void — `POST /v2/payments/authorizations/{id}/void`
- Refund — `POST /v2/payments/captures/{id}/refund` (idempotent via `PayPal-Request-Id`)
- Vault / delete card — `POST` / `DELETE /v3/vault/payment-tokens`
- Transaction reporting — `GET /v1/reporting/transactions` (paginated over the whole range, 31-day windows)

Idempotency: create-order / capture / refund carry a run-scoped, deterministic `PayPal-Request-Id`,
and payment state is also guarded in the database, so a double-click never authorizes or captures
twice. Refunds additionally dedupe on the caller's `idempotencyKey`.

## Configuration (no secrets in the repo)

Settings bind from the `PayPal:` section — `PayPal:ClientId`, `PayPal:ClientSecret`,
`PayPal:Environment`, `PayPal:Currency`, and optional `PayPal:BaseUrl` (used verbatim for **every**
call when set, otherwise the base url is derived from `Environment`). Load the sandbox credentials
into user-secrets (values come from the environment variables; they are never written to any file):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # sandbox
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      # USD
```

## Run it (this machine)

```bash
export DOTNET_ROLL_FORWARD=Major          # .NET 10 SDK/runtime present; global.json pins 8.0.x
export UseOnlyInMemoryDatabase=true       # no LocalDB here; data lives only for one run
export ASPNETCORE_ENVIRONMENT=Development # loads user-secrets
dotnet run --project src/PublicApi
```

The API listens on `https://localhost:30763` (Swagger at `/swagger`). Because the store is
in-memory and per-host, place, pay, fulfil and refund the orders you create **in the same run**.

## Verify it yourself (end to end, no browser)

```bash
B=https://localhost:30763
tok(){ curl -s -k -X POST $B/api/authenticate -H 'Content-Type: application/json' \
  -d "{\"username\":\"$1\",\"password\":\"Pass@word1\"}" | python -c "import sys,json;print(json.load(sys.stdin)['token'])"; }
SHOP=$(tok demouser@microsoft.com)   # shopper
ADMIN=$(tok admin@microsoft.com)     # administrator

# 1) place an order (prices come from the catalog)
OID=$(curl -s -k -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":1,"quantity":2},{"catalogItemId":2,"quantity":1}]}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")

# 2) authorize with the sandbox test card (hold, not taken)
curl -s -k -X POST $B/api/orders/$OID/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"card":{"number":"4111111111111111","expiry":"2027-12","securityCode":"123","cardholderName":"Demo User",
       "billingAddress":{"addressLine1":"1 Market St","city":"San Jose","state":"CA","postalCode":"95131","countryCode":"US"}}}'

# 3) fulfil = capture (money taken; shows gross/fee/net)
curl -s -k -X POST $B/api/orders/$OID/fulfil -H "Authorization: Bearer $ADMIN"

# 4) partial refund (idempotencyKey makes retries safe)
curl -s -k -X POST $B/api/orders/$OID/refunds -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"amount":10.00,"idempotencyKey":"my-key-1"}'

# 5) save a card, reuse it to pay a second order
PMID=$(curl -s -k -X POST $B/api/payment-methods -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"card":{"number":"4111111111111111","expiry":"2028-09","securityCode":"123"}}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['paymentMethodId'])")
O2=$(curl -s -k -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -s -k -X POST $B/api/orders/$O2/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d "{\"savedPaymentMethodId\":$PMID}"
curl -s -k -X POST $B/api/orders/$O2/fulfil -H "Authorization: Bearer $ADMIN"

# 6) cancel-before-fulfil (void) on a third order
O3=$(curl -s -k -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":4,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -s -k -X POST $B/api/orders/$O3/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d "{\"savedPaymentMethodId\":$PMID}"
curl -s -k -X POST $B/api/orders/$O3/cancel -H "Authorization: Bearer $ADMIN"

# 7) my orders, and reconciliation over a range (admin)
curl -s -k $B/api/my-orders -H "Authorization: Bearer $SHOP"
curl -s -k -G $B/api/reconciliation --data-urlencode "from=2026-09-01T00:00:00Z" --data-urlencode "to=2026-09-30T00:00:00Z" \
  -H "Authorization: Bearer $ADMIN"
```

**Notes**
- PayPal's transaction reporting lags live activity by up to a few hours, so a reconciliation range
  covering payments you just created can legitimately come back empty on the PayPal side — those
  orders then show under `eShopOnly` (eShop knows, PayPal's report doesn't yet). Over an older range
  that already has data, the report matches transactions to orders and lists discrepancies both ways.
- Card details are never stored (only PayPal's vault token + brand/last4/expiry) and never logged.
- For a real SQL Server deployment, the schema is covered by the `AddPaymentsAndSavedCards` EF
  migration; the in-memory provider used here builds the schema from the model automatically.
