# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb via **PayPal**, exposed as JWT-authenticated HTTP
endpoints on **`src/PublicApi`**. It is additive — the existing catalog/basket/order flow is
untouched. A shopper places an order, authorizes (holds) the total against a card or a saved
card, an operator fulfils (captures), cancels (voids) or the order is refunded; shoppers can save
a card once and reuse it.

## What was added

| Endpoint | Who | What it does |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items → returns `orderId` (state: awaiting payment) |
| `POST /api/orders/{orderId}/pay` | shopper (owner) | **Authorize** the total (a hold) using inline card **or** a saved card |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Mark fulfilled and **capture** — reports captured amount, PayPal fee, net |
| `POST /api/orders/{orderId}/cancel` | **admin** | Cancel before fulfilment — **void** the hold, no money moves |
| `POST /api/orders/{orderId}/refunds` | shopper (owner) | Refund a capture in full/part → returns `refundId` (idempotency-keyed) |
| `GET /api/my-orders` | shopper | The caller's orders with payment state |
| `GET /api/reconciliation?from=&to=` | **admin** | PayPal's transactions lined up against eShop orders over an ISO-8601 range |
| `POST /api/payment-methods` | shopper | Save a card (vaulted in PayPal) → returns `paymentMethodId` |
| `GET /api/payment-methods` | shopper | The caller's saved cards (brand + last four + expiry, never full details) |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper (owner) | Remove a saved card; afterwards it is gone and unusable |

Design notes:
- The app calls PayPal's **Orders v2 / Payments v2 / Vault v3 / Transaction-Search** REST APIs
  directly (`src/Infrastructure/Payments/PayPalPaymentGateway.cs`), grounded in the PayPal plugin's
  guidance. Card payments are **direct/headless** — no browser step. If PayPal ever returns a
  browser-approval challenge, the call fails clearly rather than building an approval round-trip.
- Authorize/capture are split: `pay` holds, `fulfil` captures. A stale authorization is renewed
  (reauthorize) before capture; one that can no longer be renewed returns an operator-actionable
  error.
- Idempotency: a double `pay` never authorizes twice; a repeated `refund` under the same
  idempotency key never refunds twice; the total refunded can never exceed what was captured.
- Ownership: a shopper only ever sees/acts on their own orders and saved cards (others → 404).
- No card number is stored in this app's database or written to its logs.

## Configuration (secrets stay out of the repo)

Settings bind from the `PayPal:` section — no values are hard-coded, so the same build runs
against any PayPal account. Load the sandbox credentials from the environment variables into
.NET user-secrets for `src/PublicApi` (names only shown here; the values come from your env):

```bash
cd <repo root>
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"     --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      --project src/PublicApi/PublicApi.csproj
# Optional: PayPal:BaseUrl — if set, it is used verbatim for every PayPal call (incl. the token
# request) instead of deriving the base URL from PayPal:Environment.
```

## Run it (this machine)

Only the .NET 10 SDK is installed and the ASP.NET 8.0 runtime is absent, and there's no LocalDB,
so run in-memory with roll-forward. `global.json` already allows `latestMajor`.

```bash
cd <repo root>
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development          # loads user-secrets
export UseOnlyInMemoryDatabase=true                # no SQL Server LocalDB needed
export ASPNETCORE_URLS="https://localhost:10123;http://localhost:10124"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

Swagger: <https://localhost:10123/swagger>. (In-memory data lives only for one run — pay, fulfil
and refund orders you created in the same run.)

## Verify end-to-end (curl)

`curl -k` accepts the dev cert. Get a shopper token and an operator (admin) token first:

```bash
B=https://localhost:10123
SHOP=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
ADMIN=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}'    | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
CARD='{"number":"4111111111111111","expiryMonth":12,"expiryYear":2027,"securityCode":"123","name":"Demo User","billingAddress":{"addressLine1":"123 Main St","city":"San Jose","state":"CA","postalCode":"95131","countryCode":"US"}}'
```

1. **Save a card** (Flow 2) → note `paymentMethodId`:
   ```bash
   curl -sk -X POST $B/api/payment-methods -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
     -d "{\"card\":$CARD,\"label\":\"my visa\"}"
   curl -sk $B/api/payment-methods -H "Authorization: Bearer $SHOP"        # lists it (VISA •1111)
   ```
2. **Place an order** → note `orderId`:
   ```bash
   curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
     -d '{"items":[{"catalogItemId":5,"quantity":2},{"catalogItemId":4,"quantity":1}]}'
   ```
3. **Authorize (pay) with the card** — places a hold equal to the total:
   ```bash
   curl -sk -X POST $B/api/orders/<orderId>/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
     -d "{\"card\":$CARD}"     # status -> Authorized, authorizationId returned, nothing captured
   ```
4. **See your orders**: `curl -sk $B/api/my-orders -H "Authorization: Bearer $SHOP"`
5. **Fulfil (capture)** — operator; reports fee & net:
   ```bash
   curl -sk -X POST $B/api/orders/<orderId>/fulfil -H "Authorization: Bearer $ADMIN"
   # status -> Fulfilled, capturedAmount, payPalFee, netAmount
   ```
6. **Refund** — full or partial, with an idempotency key (repeat with the same key never double-refunds):
   ```bash
   curl -sk -X POST $B/api/orders/<orderId>/refunds -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
     -d '{"amount":5.00,"idempotencyKey":"refund-1"}'   # -> refundId, status PartiallyRefunded
   ```
7. **Reuse the saved card on a second order** (Flow 2 → Flow 1):
   ```bash
   O2=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
     -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")
   curl -sk -X POST $B/api/orders/$O2/pay -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
     -d '{"savedPaymentMethodId":<paymentMethodId>}'
   curl -sk -X POST $B/api/orders/$O2/fulfil -H "Authorization: Bearer $ADMIN"
   ```
8. **Cancel before fulfil** (on a fresh authorized order) — releases the hold:
   ```bash
   curl -sk -X POST $B/api/orders/<orderId>/cancel -H "Authorization: Bearer $ADMIN"   # -> Cancelled, hold VOIDED
   ```
9. **Reconciliation** (operator) — use a range that already has data (recent ranges legitimately
   come back empty because PayPal reporting lags a few hours):
   ```bash
   curl -sk "$B/api/reconciliation?from=2026-07-22T00:00:00Z&to=2026-08-16T00:00:00Z" -H "Authorization: Bearer $ADMIN"
   # payPalTransactionCount, matched[], payPalOnly[] (PayPal knows, eShop doesn't), eShopOnly[] (the reverse)
   ```

Sandbox test card: Visa `4111 1111 1111 1111`, any future expiry, any CVC, any name/address.
```
