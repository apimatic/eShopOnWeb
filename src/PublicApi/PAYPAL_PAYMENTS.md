# PayPal payments & saved cards (PublicApi)

An **additive** capability on top of the existing eShopOnWeb catalog/basket/order flow: a
logged-in shopper can pay for an order by card (funds are **held** at checkout and **taken** at
fulfilment), save a card for reuse, and an operator can fulfil, cancel, refund and reconcile.
PayPal is the payment processor; every PayPal interaction goes through
`IPayPalGateway` (implemented in `Infrastructure/PayPal/PayPalGateway.cs`).

## Endpoints (all under `/api`, JWT-authenticated)

| Method & route | Who | What |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities. Starts `AwaitingPayment`. Returns **`orderId`**. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** the order total (hold, not capture) with one-off card details *or* `savedPaymentMethodId`. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Mark fulfilled and **capture**. Renews a stale hold first; reports one that can't be renewed. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Cancel before fulfilment — **void** the hold (no money moves). |
| `POST /api/orders/{orderId}/refunds` | shopper | **Refund** the capture, full or partial. Body: `{ amount?, idempotencyKey }`. Returns **`refundId`**. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `POST /api/payment-methods` | shopper | Save (vault) a card. Returns **`paymentMethodId`** + a safe description. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card (deletes the PayPal vault token too). |
| `GET /api/reconciliation?from={iso}&to={iso}` | **admin** | PayPal's transaction report for the range, lined up against eShop orders. |

Every shopper endpoint acts only on the caller's own data (identity comes from the token).
Fulfil, cancel and reconciliation require the `Administrators` role.

## Configuration

Settings bind from the `PayPal:` section — **no values are hard-coded**:

| Key | From env var |
| --- | --- |
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` (`sandbox`/`live`) |
| `PayPal:Currency` | `PAYPAL_CURRENCY` |
| `PayPal:BaseUrl` | *(optional)* if set, used verbatim for **every** call including the token request |

Load the credentials into .NET user-secrets (never into any repo file):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Run (this machine: .NET 10 SDK, no ASP.NET Core 8 runtime, no LocalDB)

`global.json` rolls forward to the installed SDK; run the app with `DOTNET_ROLL_FORWARD=Major`
and the in-memory database. PublicApi binds to its assigned ports (9143/9144).

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet build src/PublicApi/PublicApi.csproj
cd src/PublicApi/bin/Debug/net8.0        # run from the output dir so appsettings.json is found
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://localhost:9143;http://localhost:9144" \
UseOnlyInMemoryDatabase=true \
  dotnet PublicApi.dll
```

> In-memory stores are per-host and reset on restart. Create, pay, fulfil and refund the orders
> you make **within a single run**.

## Verify end to end (curl, no browser)

```bash
BASE=https://localhost:9143            # -k below trusts the dev cert

# tokens (seeded users, password Pass@word1)
USER=$(curl -sk -X POST $BASE/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)
ADMIN=$(curl -sk -X POST $BASE/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq -r .token)

# Flow 1 — pay for an order ------------------------------------------------------------
OID=$(curl -sk -X POST $BASE/api/orders -H "Authorization: Bearer $USER" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":5,"quantity":2},{"catalogItemId":4,"quantity":1}]}' | jq .orderId)

# authorize with the sandbox Visa (hold == order total to the cent)
curl -sk -X POST $BASE/api/orders/$OID/pay -H "Authorization: Bearer $USER" -H 'Content-Type: application/json' \
  -d '{"card":{"number":"4111111111111111","expiryMonth":12,"expiryYear":2030,"securityCode":"123",
       "cardholderName":"Demo User","billingAddressLine1":"1 Market St","billingCity":"San Jose",
       "billingState":"CA","billingPostalCode":"95131","billingCountryCode":"US"}}' | jq .payment

# fulfil (operator) -> capture; payment then shows captured amount, PayPal fee and net proceeds
curl -sk -X POST $BASE/api/orders/$OID/fulfil -H "Authorization: Bearer $ADMIN" | jq .payment

# partial refund; repeating the same idempotencyKey never refunds twice
curl -sk -X POST $BASE/api/orders/$OID/refunds -H "Authorization: Bearer $USER" -H 'Content-Type: application/json' \
  -d '{"amount":10.00,"idempotencyKey":"refund-1"}' | jq '{refundId, remaining:.payment.refundableRemaining}'

curl -sk $BASE/api/my-orders -H "Authorization: Bearer $USER" | jq '.[].status'

# Flow 2 — saved cards -----------------------------------------------------------------
PM=$(curl -sk -X POST $BASE/api/payment-methods -H "Authorization: Bearer $USER" -H 'Content-Type: application/json' \
  -d '{"card":{"number":"4111111111111111","expiryMonth":11,"expiryYear":2031,"securityCode":"123",
       "billingCountryCode":"US"},"alias":"My Visa"}' | jq .paymentMethodId)

O2=$(curl -sk -X POST $BASE/api/orders -H "Authorization: Bearer $USER" -H 'Content-Type: application/json' \
  -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | jq .orderId)
curl -sk -X POST $BASE/api/orders/$O2/pay -H "Authorization: Bearer $USER" -H 'Content-Type: application/json' \
  -d "{\"savedPaymentMethodId\":$PM}" | jq .status          # -> PaymentAuthorized (paid with the saved card)
curl -sk -X POST $BASE/api/orders/$O2/fulfil -H "Authorization: Bearer $ADMIN" | jq .payment.captureId

curl -sk -X DELETE $BASE/api/payment-methods/$PM -H "Authorization: Bearer $USER" -o /dev/null -w '%{http_code}\n'  # 204
curl -sk $BASE/api/payment-methods -H "Authorization: Bearer $USER"                 # []  (gone, and no longer usable)

# Reconciliation (operator) — covers the whole range (chunked <=31d, all pages) ---------
FROM=2026-06-01T00:00:00Z; TO=2026-09-01T00:00:00Z
curl -sk "$BASE/api/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $ADMIN" \
  | jq '{payPalTransactionCount, eShopRecordCount, matched:(.matched|length),
         missingInEShop:(.missingInEShop|length), missingInPayPal:(.missingInPayPal|length)}'
```

**Note on reconciliation in sandbox:** PayPal's transaction reporting lags live activity by up to
a few hours, so payments you just created will show under `missingInPayPal` until they surface in
PayPal's report — an expected sandbox result, not a missing capability. The report is correct over
any range that already has data (transactions PayPal knows about that eShop doesn't appear under
`missingInEShop`).

## Notes

- **Idempotency:** pay/fulfil are serialized per order and no-op if already done; refunds use the
  caller's idempotency key (returned unchanged on repeat). Authorize/capture/refund also send
  PayPal a `PayPal-Request-Id`.
- **Card data** is forwarded to PayPal only — never stored in eShop's database and never logged.
  Saved cards live in PayPal's vault; eShop keeps only a token id plus a safe description.
- **Browser challenges:** if PayPal answers a card payment with a challenge that needs browser
  approval (e.g. 3-D Secure), the pay call returns `501` and reports it rather than building an
  approval round-trip.
