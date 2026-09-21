# PayPal payments & saved cards — eShopOnWeb PublicApi

Additive card-payment capability on `src/PublicApi`, using the **PayPal Server SDK (.NET)** for every
PayPal interaction. Shoppers place and pay orders (authorize → capture at fulfilment → void/refund);
they can save a card and reuse it. Operators fulfil, cancel, and reconcile.

## What was added

- **Domain** (`src/ApplicationCore`): `Order` gains `PaymentStatus` + a `Payment` child (PayPal ids &
  statuses for the hold, capture, and refunds) and guarded transitions (`Authorize`, `Fulfil`, `Cancel`,
  `AddRefund`); new `SavedPaymentMethod` aggregate; `IPaymentGateway` + `IPaymentService`.
- **Infrastructure** (`src/Infrastructure/Payments`): `PayPalPaymentGateway` (SDK calls + error
  translation), `PayPalOptions` (fail-fast config), `AddPayPalIntegration` DI. EF config + a migration
  (`Data/Migrations/*_AddPaymentsAndSavedPaymentMethods`) for the SQL path.
- **Vendored SDK**: `src/PayPalServerSdk` (built from `context-plugins/paypal-csharp-sdk@main`; not on
  NuGet, so vendored to keep the solution buildable).
- **Endpoints** (`src/PublicApi/PaymentEndpoints`): the ten routes below.

## Endpoints

| Method | Route | Who | Purpose |
| --- | --- | --- | --- |
| POST | `/api/orders` | shopper | place an order (awaiting payment); returns `orderId` |
| POST | `/api/orders/{orderId}/pay` | shopper | authorize (hold) with a card **or** `savedPaymentMethodId` |
| POST | `/api/orders/{orderId}/fulfil` | **admin** | capture the hold (money taken); shows captured/fee/net |
| POST | `/api/orders/{orderId}/cancel` | **admin** | void the hold before fulfilment |
| POST | `/api/orders/{orderId}/refunds` | shopper | full/partial refund; body `{amount?, idempotencyKey}`; returns `refundId` |
| GET | `/api/my-orders` | shopper | caller's orders + payment state |
| GET | `/api/reconciliation?from=&to=` | **admin** | PayPal transactions vs eShop orders (ISO-8601 range) |
| POST | `/api/payment-methods` | shopper | save (vault) a card; returns `paymentMethodId` + safe description |
| GET | `/api/payment-methods` | shopper | caller's saved cards |
| DELETE | `/api/payment-methods/{paymentMethodId}` | shopper | remove a saved card |

## Configuration & secrets

Bound from the `PayPal:` section (no values in the repo): `PayPal:ClientId`, `PayPal:ClientSecret`,
`PayPal:Environment`, `PayPal:Currency`, and optional `PayPal:BaseUrl` (verbatim override for all calls
incl. the token request). Load them into **user-secrets** for the PublicApi project from the env vars:

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   # sandbox
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      # USD
```

The host **refuses to start** if any of ClientId/ClientSecret/Environment/Currency is missing or blank.

## Run it (this machine)

SDK 8 is pinned but only .NET 10 is installed, and there's no LocalDB — so roll forward and use the
in-memory store. Bind to your assigned port block (36320–36339; example uses 36323):

```bash
cd <repo root>
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development          # loads user-secrets
export ASPNETCORE_URLS="https://localhost:36323"
export UseOnlyInMemoryDatabase=true
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

Swagger: `https://localhost:36323/swagger`. Ensure the dev cert is trusted
(`dotnet dev-certs https --check --trust`) or use `curl -k`.

> **In-memory caveat:** Web and PublicApi keep separate in-memory stores and data is lost on restart, so
> place/pay/fulfil/refund the orders you create **within one run**, driving everything through PublicApi.

## Verify end to end (sandbox test card, no browser)

Test card: Visa `4111 1111 1111 1111`, any future expiry (`YYYY-MM`), any CVC, any name.

```bash
B=https://localhost:36323
tok(){ grep -o '"token":"[^"]*"'|sed 's/"token":"//;s/"$//'; }
oid(){ grep -o '"orderId":[0-9]*'|head -1|grep -o '[0-9]*'; }
SHOPPER=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}'|tok)
ADMIN=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" -d '{"username":"admin@microsoft.com","password":"Pass@word1"}'|tok)
CARD='{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","cardholderName":"Test Buyer"}}'

# Flow 1 — pay, fulfil, refund
OID=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" \
      -d '{"items":[{"catalogItemId":5,"quantity":2},{"catalogItemId":4,"quantity":1}]}' | oid)     # $29.00
curl -sk -X POST $B/api/orders/$OID/pay    -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" -d "$CARD"   # -> Authorized
curl -sk -X POST $B/api/orders/$OID/fulfil -H "Authorization: Bearer $ADMIN"                                                    # -> Paid + capturedAmount/payPalFee/netAmount
curl -sk -X POST $B/api/orders/$OID/refunds -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" \
      -d "{\"amount\":10.00,\"idempotencyKey\":\"rf-$(date +%s%N)\"}"                                                            # -> refundId, PartiallyRefunded
curl -sk $B/api/my-orders -H "Authorization: Bearer $SHOPPER"

# Flow 2 — save a card and reuse it
PMID=$(curl -sk -X POST $B/api/payment-methods -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" -d "$CARD" | grep -o '"paymentMethodId":[0-9]*'|grep -o '[0-9]*')
curl -sk $B/api/payment-methods -H "Authorization: Bearer $SHOPPER"
OID2=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":3,"quantity":1}]}' | oid)
curl -sk -X POST $B/api/orders/$OID2/pay -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" -d "{\"savedPaymentMethodId\":$PMID}"   # -> Authorized with the saved card
curl -sk -X DELETE $B/api/payment-methods/$PMID -H "Authorization: Bearer $SHOPPER" -w " (HTTP %{http_code})\n"                  # -> 204, then no longer usable

# Operator: cancel before fulfilment, and reconcile
OID3=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" -d '{"items":[{"catalogItemId":4,"quantity":1}]}' | oid)
curl -sk -X POST $B/api/orders/$OID3/pay    -H "Authorization: Bearer $SHOPPER" -H "Content-Type: application/json" -d "$CARD" >/dev/null
curl -sk -X POST $B/api/orders/$OID3/cancel -H "Authorization: Bearer $ADMIN"                                                    # -> Cancelled (VOIDED)
curl -sk "$B/api/reconciliation?from=2026-09-21T00:00:00Z&to=2026-09-22T00:00:00Z" -H "Authorization: Bearer $ADMIN"
```

Seeded users: shopper `demouser@microsoft.com`, admin `admin@microsoft.com`, password `Pass@word1`.

## Notes / expected sandbox behaviour

- **Reconciliation lag:** PayPal's transaction reporting can lag live activity by up to a few hours, so a
  range covering payments you just made may show them as `EShopOnly` (or the range may be empty of
  matches). That is expected — the report is correct over a range that has settled data, and it covers
  the whole range (paged, in ≤31-day windows).
- **Idempotency keys:** gateway-issued request ids are salted with a per-process nonce so the in-memory
  reset (order ids restart at 1) never replays a prior run's cached PayPal response. Refund idempotency
  keys are **caller-supplied** and used verbatim — reuse a **fresh unique key** per new refund when
  testing, since PayPal dedupes them globally for ~6 hours.
- **No browser step:** if PayPal ever answers a card with a 3-D Secure / payer-action challenge, the API
  returns `422` with a clear message rather than building an approval round-trip.
- Full card details are never stored or logged; only the vault token id + brand + last four + expiry.
