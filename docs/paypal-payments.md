# PayPal payments & saved cards (PublicApi)

This is an **additive** capability on top of eShopOnWeb: it lets a logged-in shopper place an order,
pay for it by card (PayPal holds the money at checkout and takes it at fulfilment), save a card for
reuse, and lets an operator fulfil / cancel / refund and reconcile against PayPal's own records. The
existing catalog/basket/order flow is untouched.

Every PayPal interaction is built directly against the OpenAPI specs in `api-specs/paypal/` — no
pre-built PayPal SDK is used. The client is hand-written in `src/Infrastructure/PayPal/`.

## What was added

| Endpoint | Role | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities. Returns `orderId`. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** (hold) the order total with a raw card or a saved card. |
| `POST /api/orders/{orderId}/fulfil` | operator | **Capture** the held funds; records captured amount, PayPal fee, net. |
| `POST /api/orders/{orderId}/cancel` | operator | **Void** the hold before fulfilment; no money moves. |
| `POST /api/orders/{orderId}/refunds` | shopper | **Refund** a captured payment, full or partial. Returns `refundId`. |
| `GET  /api/my-orders` | shopper | The caller's orders with their payment state. |
| `GET  /api/reconciliation?from=&to=` | operator | PayPal's transactions for a date range lined up against eShop orders. |
| `POST /api/payment-methods` | shopper | Save (vault) a card. Returns `paymentMethodId` + a safe descriptor. |
| `GET  /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card. |

"operator" = the `Administrators` role (the project's existing privileged role). Every other endpoint
is scoped to the caller's own data via the JWT name claim: one shopper never sees or acts on another's
orders or cards. Full card numbers/CVVs are never stored in the app database nor written to logs — only
the PayPal vault token and a safe descriptor (brand, last four, expiry) are kept.

PayPal specs used: `checkout_orders_v2` (authorize), `payments_payment_v2` (capture / reauthorize /
void / refund), `vault_payment_tokens_v3` (saved cards), `transaction_search_v1` (reconciliation).

## Configuration

Settings bind from the `PayPal:` section — no values are hard-coded, so the same build runs against a
different PayPal account:

| Key | From env var | Notes |
|---|---|---|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | REST client id (secret) |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | REST secret |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | `sandbox` or `live` — selects the API base URL |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | ISO-4217, e.g. `USD` |
| `PayPal:BaseUrl` | (optional) | If set, used **verbatim** as the base for every call (incl. the token) |

Secrets never live in the repo — load them into .NET user-secrets from the environment:

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Running (this machine)

Only the .NET 10 SDK is installed and there is no LocalDB, so roll the SDK forward and use the in-memory
database. The in-memory store is per-host and is wiped on restart, so create, pay, fulfil and refund
within one PublicApi run.

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development
export UseOnlyInMemoryDatabase=true
export ASPNETCORE_URLS="https://localhost:9603;http://localhost:9604"   # your assigned port block
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

Swagger UI: <https://localhost:9603/swagger>. If the dev cert isn't trusted, use `curl -k` (below) or
`dotnet dev-certs https --trust`.

## Step-by-step verification (curl)

All commands assume `B=https://localhost:9603`. A bearer token comes from the PublicApi authenticate
endpoint (the storefront cookie does not work here).

```bash
B=https://localhost:9603
SHOP=$(curl -sk -X POST $B/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
ADMIN=$(curl -sk -X POST $B/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | python -c "import sys,json;print(json.load(sys.stdin)['token'])")
```

### Flow 1 — pay, fulfil, refund

```bash
# 1. Place an order (2x item 5 + 1x item 3). Note the returned orderId.
curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":5,"quantity":2},{"catalogItemId":3,"quantity":1}]}'

# 2. Authorize (hold) with the PayPal sandbox test Visa. Returns status PaymentAuthorized + authorizationId.
curl -sk -X POST $B/api/orders/1/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"John Shopper","countryCode":"US"}}'

# 3. Fulfil (capture) — operator. Payment now shows capturedGross, payPalFee and netAmount.
curl -sk -X POST $B/api/orders/1/fulfil -H "Authorization: Bearer $ADMIN"

# 4. Refund part of it (idempotency key prevents a repeat from refunding twice). Returns refundId.
curl -sk -X POST $B/api/orders/1/refunds -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"amount":10.00,"idempotencyKey":"refund-A"}'

# 5. See the payment state.
curl -sk $B/api/my-orders -H "Authorization: Bearer $SHOP"
```

Cancel instead of fulfil (before fulfilment): `POST /api/orders/{id}/cancel` (operator) voids the hold.

### Flow 2 — save a card and reuse it

```bash
# Save a card -> paymentMethodId (+ brand/last4/expiry only).
curl -sk -X POST $B/api/payment-methods -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","name":"John Shopper","countryCode":"US"},"alias":"my visa"}'

# List saved cards.
curl -sk $B/api/payment-methods -H "Authorization: Bearer $SHOP"

# Place a second order and pay it with the saved card (no card details re-entered).
curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":4,"quantity":1}]}'
curl -sk -X POST $B/api/orders/2/pay -H "Authorization: Bearer $SHOP" -H 'Content-Type: application/json' \
  -d '{"savedCardId":1}'
curl -sk -X POST $B/api/orders/2/fulfil -H "Authorization: Bearer $ADMIN"

# Delete the card; afterwards it is not listed and cannot be used to pay.
curl -sk -X DELETE $B/api/payment-methods/1 -H "Authorization: Bearer $SHOP"
```

### Reconciliation

```bash
curl -sk "$B/api/reconciliation?from=2026-07-05T00:00:00Z&to=2026-08-16T23:59:59Z" -H "Authorization: Bearer $ADMIN"
```

The report covers the whole range (it splits ranges over PayPal's 31-day limit into windows and pages
through each). Each line is `Matched`, `MissingInEShop` (PayPal has it, eShop doesn't) or
`MissingInPayPal` (eShop captured it, PayPal's report doesn't show it yet). **PayPal's transaction
reporting lags live activity by up to a few hours**, so a range covering payments you just made may
legitimately show them as `MissingInPayPal` (or the range may be empty). That is expected sandbox
behaviour, not a missing capability — run the report over an older range that already has data to see
`Matched` lines.

## Notes on behaviour

- **Idempotency.** Paying an order twice never places a second hold; fulfilling twice never captures
  twice; a refund repeated under the same `idempotencyKey` never refunds twice (two *different* keys
  are two legitimate partial refunds). A partial refund can never exceed the captured amount.
- **Stale holds.** If a hold has aged past its honor period by fulfilment time, fulfilment renews it
  (reauthorize) and captures the renewed hold. If it can no longer be renewed, fulfilment fails with a
  message an operator can act on (it cannot be force-triggered in a single short sandbox run).
- **3-D Secure.** If PayPal answers a card payment with a browser-approval challenge, the pay endpoint
  returns 422 explaining a challenge is required — this integration deliberately does not build a
  browser approval round-trip. The sandbox test Visa authorizes without a challenge.

## Tests

- `tests/UnitTests` — payment domain rules and the orchestration service (idempotency, over-refund
  guard, stale-hold renewal, reconciliation matching).
- `tests/PublicApiIntegrationTests/PaymentEndpoints` — the HTTP endpoints end-to-end with in-memory
  PayPal fakes (routing, auth roles, shopper isolation, saved-card lifecycle).

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet test tests/UnitTests/UnitTests.csproj
dotnet test tests/PublicApiIntegrationTests/PublicApiIntegrationTests.csproj
```
