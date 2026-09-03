# PayPal payments & saved cards — eShopOnWeb PublicApi

Additive card-payment capability on `src/PublicApi`, using the **paypal-platforms-team** .NET SDK for every
PayPal interaction. Sandbox only. It reuses the existing `Order`/`OrderItem` model and adds a `Payment`
(money-movement state) and `SavedPaymentMethod` (vaulted cards) alongside it.

## Endpoints (all under `/api/`, JWT-authenticated; caller identity from the token)

| Method & route | Who | What |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog ids+quantities. Starts awaiting payment. Returns `orderId`. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** (hold) the order total, funded by a one-off card **or** a saved card (`savedPaymentMethodId`). |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Mark fulfilled and **capture** the money; records PayPal's captured amount, fee and net. Renews a stale authorization if needed. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Cancel before fulfilment; releases the held funds (void). |
| `POST /api/orders/{orderId}/refunds` | shopper | Refund the captured payment, full or partial, under a caller `idempotencyKey`. Returns `refundId`. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from=&to=` | **admin** | PayPal's transactions for a date range lined up against eShop orders. |
| `POST /api/payment-methods` | shopper | Save a card. Returns `paymentMethodId` + a safe description (brand, last 4, expiry). |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{id}` | shopper | Remove a saved card; afterwards it can't be listed or used to pay. |

## One-time setup

1. **Credentials → user-secrets** (values never live in the repo). With `PAYPAL_CLIENT_ID`,
   `PAYPAL_CLIENT_SECRET`, `PAYPAL_ENVIRONMENT`, `PAYPAL_CURRENCY` set in your environment:

   ```bash
   cd src/PublicApi
   dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
   dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
   dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
   dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
   ```

   The host **refuses to start** if any is missing or blank (`ValidateOnStart`). `PayPal:BaseUrl` is an
   optional override used verbatim for every call (incl. the token request).

2. **Trust the HTTPS dev cert** (once): `dotnet dev-certs https --check --trust`.

## Run (this machine: .NET 10 SDK only, no LocalDB)

```bash
cd <repo root>
export DOTNET_ROLL_FORWARD=Major        # global.json pins 8.0.x; roll forward to the installed .NET 10 SDK
export ASPNETCORE_ENVIRONMENT=Development
export UseOnlyInMemoryDatabase=true      # no SQL Server LocalDB here
export ASPNETCORE_URLS="https://localhost:21623;http://localhost:21624"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

> In-memory data lives only for one run — place, pay, fulfil and refund the orders you create **in the same
> run**. Web and PublicApi each hold their own store, so drive everything through PublicApi (that is why
> `POST /api/orders` exists).

## Verify the flows (curl; `-k` skips dev-cert validation)

```bash
B=https://localhost:21623
UT=$(curl -sk -X POST "$B/api/authenticate" -H "Content-Type: application/json" \
      -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)   # shopper
AT=$(curl -sk -X POST "$B/api/authenticate" -H "Content-Type: application/json" \
      -d '{"username":"admin@microsoft.com","password":"Pass@word1"}'    | jq -r .token)   # operator/admin
```

**Flow 1 — pay for an order** (sandbox test card Visa `4111 1111 1111 1111`, any future expiry/CVC):

```bash
# 1) place an order (catalog items 5 x2 + 4 x1 = $29.00) -> orderId
OID=$(curl -sk -X POST "$B/api/orders" -H "Authorization: Bearer $UT" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":5,"quantity":2},{"catalogItemId":4,"quantity":1}],
       "shipToAddress":{"street":"1 Main","city":"Redmond","state":"WA","country":"US","zipCode":"98052"}}' | jq -r .orderId)

# 2) authorize (hold) with a one-off card
curl -sk -X POST "$B/api/orders/$OID/pay" -H "Authorization: Bearer $UT" -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2027-12","securityCode":"123","cardholderName":"Demo Shopper","billingCountryCode":"US","billingPostalCode":"98052"}}' | jq
# -> paymentStatus "Authorized", authorizationId set, no money taken

# 3) fulfil (capture) — admin. Shows PayPal's capturedGross / payPalFee / netAmount
curl -sk -X POST "$B/api/orders/$OID/fulfil" -H "Authorization: Bearer $AT" | jq

# 4) refund part of it (idempotent by key); repeat the same key -> same refundId, no double refund
curl -sk -X POST "$B/api/orders/$OID/refunds" -H "Authorization: Bearer $UT" -H "Content-Type: application/json" \
  -d '{"amount":5.00,"idempotencyKey":"ref-1"}' | jq   # top-level refundId

# cancel-before-fulfil (on a fresh order) releases the hold — admin
# curl -sk -X POST "$B/api/orders/<other>/cancel" -H "Authorization: Bearer $AT" | jq   # -> Canceled / VOIDED
```

**Flow 2 — saved cards**:

```bash
# save a card -> paymentMethodId
PMID=$(curl -sk -X POST "$B/api/payment-methods" -H "Authorization: Bearer $UT" -H "Content-Type: application/json" \
  -d '{"card":{"number":"4111111111111111","expiry":"2028-06","securityCode":"123","cardholderName":"Demo Shopper","billingCountryCode":"US","billingPostalCode":"98052"}}' | jq -r .paymentMethodId)

curl -sk "$B/api/payment-methods" -H "Authorization: Bearer $UT" | jq   # brand + last 4 + expiry, never the PAN

# place a second order and pay it with the SAVED card (no card details re-entered)
OID2=$(curl -sk -X POST "$B/api/orders" -H "Authorization: Bearer $UT" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":4,"quantity":1}],"shipToAddress":{"street":"1 Main","city":"Redmond","state":"WA","country":"US","zipCode":"98052"}}' | jq -r .orderId)
curl -sk -X POST "$B/api/orders/$OID2/pay" -H "Authorization: Bearer $UT" -H "Content-Type: application/json" \
  -d "{\"savedPaymentMethodId\":$PMID}" | jq
curl -sk -X POST "$B/api/orders/$OID2/fulfil" -H "Authorization: Bearer $AT" | jq

curl -sk -X DELETE "$B/api/payment-methods/$PMID" -H "Authorization: Bearer $UT" -o /dev/null -w "%{http_code}\n"  # 204
```

**Reports & checks**:

```bash
curl -sk "$B/api/my-orders" -H "Authorization: Bearer $UT" | jq
curl -sk "$B/api/reconciliation?from=2026-09-01T00:00:00Z&to=2026-09-30T23:59:59Z" -H "Authorization: Bearer $AT" | jq
```

## Notes

- **Idempotency**: a double-click on pay never authorizes twice; a repeated refund key never refunds twice;
  a partial refund can never exceed the captured amount.
- **Reconciliation lag**: PayPal's transaction reporting trails live activity, so a range covering payments
  you just created may come back without them (they surface as `eShopOnly`). That is expected sandbox
  behaviour, not a gap — the report is correct over a range that already has data.
- **Browser challenge (3-D Secure)**: if PayPal answers a card with a challenge that needs browser approval,
  `pay` returns HTTP 409 `challenge_required` and stops — no approval round-trip is built (as specified).
- **Security**: card PAN/CVV are handed straight to PayPal, never stored in the app's database and never
  logged (`LogRequestBody` is off and the logger factory is set explicitly). Credentials come only from
  configuration/user-secrets.

## Tests

- `dotnet test tests/UnitTests` — deterministic orchestration tests (fake gateway): totals, idempotent
  authorize/refund, capture fee/net, refund cap, cancel/void, cross-shopper isolation.
- `dotnet test tests/PublicApiIntegrationTests` — existing suite still green.
