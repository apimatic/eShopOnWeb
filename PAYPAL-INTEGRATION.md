# PayPal payments & saved cards (PublicApi)

An **additive** capability on the existing eShopOnWeb reference app: it lets a shopper pay for an
order with a card through **PayPal** (sandbox, direct card processing), lets an operator fulfil /
cancel / refund, and lets a shopper **save a card** for reuse. It does not replace the existing
catalog / basket / order flow — it adds the money movement and the operator flows that follow a real
payment (hold at checkout, take at fulfilment, give back on a return).

Everything is exposed as JWT-authenticated HTTP endpoints on **`src/PublicApi`**, routed under `/api/`.

---

## What was added

### Flow 1 — pay for an order
| Endpoint | Who | What it does |
| --- | --- | --- |
| `POST /api/orders` | shopper | Places an order from catalog item ids + quantities (reuses the existing `Order`/`OrderItem` model). Starts **awaiting payment**. Returns `orderId`. |
| `POST /api/orders/{orderId}/pay` | shopper (own order) | **Authorizes** (holds) the order total with PayPal, using raw card details **or** a saved card. Does not capture. Idempotent in effect. |
| `POST /api/orders/{orderId}/fulfil` | **operator** | Marks fulfilled and **captures** the money. Records PayPal's captured amount, fee and net proceeds. Renews a stale hold before capturing. |
| `POST /api/orders/{orderId}/cancel` | **operator** | Cancels before fulfilment — **voids** the hold, so no money moves. |
| `POST /api/orders/{orderId}/refunds` | shopper (own order) | **Refunds** the capture, full or partial, keyed by a caller-supplied `idempotencyKey`. Returns `refundId`. Never refundable beyond what was captured. |
| `GET /api/my-orders` | shopper | The caller's orders with their payment state. |
| `GET /api/reconciliation?from=&to=` | **operator** | Lists PayPal's own transactions for a date range and lines them up against eShop orders (whole range, chunked + paged). |

### Flow 2 — saved cards
| Endpoint | Who | What it does |
| --- | --- | --- |
| `POST /api/payment-methods` | shopper | Saves (vaults) a card. Returns `paymentMethodId` and a safe descriptor (brand, last-4, expiry) — never full card details. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Removes a saved card (from PayPal's vault and the app). Afterwards it is neither listed nor usable to pay. |

Operator actions (fulfil, cancel, reconciliation) require the existing **Administrators** role. Every
other endpoint is shopper-scoped and acts only on the caller's own data. One shopper can never see,
use or delete another's orders or saved cards.

---

## Design notes

- **Single gateway.** All PayPal calls go through `IPayPalPaymentGateway` (`PayPalClient` in
  Infrastructure): OAuth client-credentials (token cached process-wide), Orders v2, Payments v2,
  Vault v3 and Transaction Search v1. Built strictly from the PayPal docs.
- **Authorize in one call.** `/pay` creates a PayPal order with `intent=AUTHORIZE` and the card
  (raw or `vault_id`) as the payment source, yielding an authorization hold equal to the order total
  to the cent. If PayPal answers with a browser challenge (e.g. 3-D Secure), the request **stops** with
  a `409` — there is no approval round-trip.
- **State PayPal owns** is persisted on a `Payment` child of the `Order` aggregate: the PayPal order
  id, authorization id + status + expiry, capture id + status + captured/fee/net, and each refund.
- **Idempotency.** Pay is guarded by a per-order lock plus a status re-check, so a double-click never
  authorizes twice. Capture uses a request id derived from the (unique) authorization id. Refunds use
  the caller's key both as an app-level dedupe (a repeat returns the original refund) and, namespaced
  with the capture id, as PayPal's `PayPal-Request-Id`.
- **Stale holds.** At fulfilment, an expired authorization is re-authorized (PayPal mints a fresh
  hold) before capture; one that can no longer be renewed returns a `409` with an operator-actionable
  message rather than failing silently.
- **Security.** Full card details are only ever forwarded to PayPal — never stored in the app database
  and never logged. Credentials live in .NET user-secrets, never in the repository.

---

## Configuration

Settings bind from the **`PayPal:`** section (values come from environment / user-secrets, never the
repo):

| Key | From env var | Notes |
| --- | --- | --- |
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | sandbox business app client id |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | secret |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | `sandbox` (default) or `live`/`production` |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | e.g. `USD` |
| `PayPal:BaseUrl` | — (optional) | when set, used **verbatim** as the API base for every call (incl. the token request), overriding the environment-derived URL |

Load them into user-secrets (bash), reading from the environment — the values never touch a file in
the repo:

```bash
proj=src/PublicApi/PublicApi.csproj
dotnet user-secrets --project "$proj" set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets --project "$proj" set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets --project "$proj" set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets --project "$proj" set "PayPal:Currency"     "$PAYPAL_CURRENCY"
# PayPal:BaseUrl only if you want to override the environment-derived base URL.
```

---

## Run it (this machine)

Only the .NET 10 SDK is installed (the app targets .NET 8), and there is no LocalDB, so roll forward
and use the in-memory store:

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development     # loads user-secrets
export UseOnlyInMemoryDatabase=true
export ASPNETCORE_URLS="https://localhost:30223;http://localhost:30224"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> The in-memory store is per-host and is lost on restart. Place, pay, fulfil and refund the orders you
> create **within a single run**. Swagger UI: <https://localhost:30223/swagger>.

Seeded users: shopper `demouser@microsoft.com` and operator `admin@microsoft.com`, password
`Pass@word1`.

---

## Verify it yourself (curl)

```bash
API=https://localhost:30223
tok() { curl -sk -X POST $API/api/authenticate -H "Content-Type: application/json" \
  -d "{\"username\":\"$1\",\"password\":\"Pass@word1\"}" | python -c "import sys,json;print(json.load(sys.stdin)['token'])"; }
SHOP=$(tok demouser@microsoft.com); ADMIN=$(tok admin@microsoft.com)

# 1) Place an order (2x item 5 @ 8.50 + 1x item 4 @ 12.00 = 29.00)
OID=$(curl -sk -X POST $API/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":5,"quantity":2},{"catalogItemId":4,"quantity":1}]}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")

# 2) Pay = authorize (sandbox Visa 4111 1111 1111 1111, any future expiry / CVC / name / address)
curl -sk -X POST $API/api/orders/$OID/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"Demo Shopper",
       "billingAddress":{"addressLine1":"1 Market St","adminArea2":"San Jose","adminArea1":"CA","postalCode":"95131","countryCode":"US"}}}'

# 3) Fulfil = capture (operator). Response shows capturedAmount, payPalFee, netAmount.
curl -sk -X POST $API/api/orders/$OID/fulfil -H "Authorization: Bearer $ADMIN"

# 4) Refund (shopper, own order). Partial then repeat the same key (idempotent — no second refund).
curl -sk -X POST $API/api/orders/$OID/refunds -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"amount":10.00,"idempotencyKey":"refund-A"}'
curl -sk -X POST $API/api/orders/$OID/refunds -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"amount":10.00,"idempotencyKey":"refund-A"}'   # returns the same refundId

# 5) See payment state
curl -sk $API/api/my-orders -H "Authorization: Bearer $SHOP"

# --- Saved cards (Flow 2) ---
# Save a card, then pay a NEW order with it, then fulfil, then delete the card.
PMID=$(curl -sk -X POST $API/api/payment-methods -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-05","securityCode":"123","name":"Demo Shopper",
       "billingAddress":{"countryCode":"US"}},"alias":"my visa"}' \
  | python -c "import sys,json;print(json.load(sys.stdin)['paymentMethodId'])")
curl -sk $API/api/payment-methods -H "Authorization: Bearer $SHOP"
OID2=$(curl -sk -X POST $API/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
curl -sk -X POST $API/api/orders/$OID2/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d "{\"paymentMethodId\":$PMID}"
curl -sk -X POST $API/api/orders/$OID2/fulfil -H "Authorization: Bearer $ADMIN"
curl -sk -X DELETE $API/api/payment-methods/$PMID -H "Authorization: Bearer $SHOP"   # 204; card no longer listed or usable

# --- Reconciliation (operator). PayPal reporting lags, so a range covering payments you JUST made
# may come back empty — that is expected. Use a wider past range to see matched rows. ---
curl -sk "$API/api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-09-09T23:59:59Z" -H "Authorization: Bearer $ADMIN"
```

### Cancel instead of fulfil
To exercise the release-hold path, pay an order and then `POST /api/orders/{id}/cancel` (operator)
before fulfilling — the payment becomes `Voided` and no money moves.

---

## Sandbox notes
- Nothing is pre-seeded on the PayPal side; orders, payments and vaulted cards are created dynamically.
- PayPal's Transaction Search lags live activity, so reconciling a range that only covers just-created
  payments may legitimately return empty — that is a sandbox timing effect, not a missing capability.
- If a card payment ever returns a browser challenge (3-D Secure), the API returns `409` and stops
  rather than building an approval round-trip.
