# PayPal payments & saved cards — verification guide

An additive capability on `src/PublicApi`: the shopper pays for an order by card (PayPal holds the
money), an operator fulfils it (the money is taken), and cancels or refunds it afterwards. A shopper
can save a card once and reuse it. The existing catalog/basket/order flow is untouched.

Everything below runs against the PayPal **sandbox** and needs no browser step.

---

## 1. One-time setup

**Credentials — never committed.** Load them from the environment into .NET user-secrets:

```bash
cd <repo root>
for k in ClientId:PAYPAL_CLIENT_ID ClientSecret:PAYPAL_CLIENT_SECRET \
         Environment:PAYPAL_ENVIRONMENT Currency:PAYPAL_CURRENCY; do
  eval "val=\$${k##*:}"
  dotnet user-secrets set "PayPal:${k%%:*}" "$val" --project src/PublicApi/PublicApi.csproj
done
```

Settings bind from the `PayPal:` section: `PayPal:ClientId`, `PayPal:ClientSecret`,
`PayPal:Environment`, `PayPal:Currency`, and the optional `PayPal:BaseUrl`. The host **refuses to
start** if any of the first four is missing or blank.

**Environment gotchas on this machine:**

- `DOTNET_ROLL_FORWARD=Major` (`global.json` now says `"rollForward": "latestMajor"`).
- `UseOnlyInMemoryDatabase=true` — there is no LocalDB here. The store is per-process and lost on
  restart, so **pay, fulfil and refund the orders you create in the same run**.
- The dev HTTPS cert must be trusted: `dotnet dev-certs https --check`.

## 2. Build, test, run

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet build eShopOnWeb.sln
dotnet test  eShopOnWeb.sln            # 129 tests

ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true \
ASPNETCORE_URLS="https://localhost:19383;http://localhost:19384" \
dotnet run --project src/PublicApi/PublicApi.csproj
```

Swagger: <https://localhost:19383/swagger>. Stop this instance before starting another.

## 3. Get tokens

PublicApi is JWT-authenticated; the caller's identity comes from the token. `curl -k` skips the dev
cert check.

```bash
API=https://localhost:19383
SHOPPER=$(curl -sk -X POST $API/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | jq -r .token)
OPERATOR=$(curl -sk -X POST $API/api/authenticate -H 'Content-Type: application/json' \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | jq -r .token)
```

`demouser` is the shopper; `admin` holds the `Administrators` role and is the operator.

Sandbox test card: **4111 1111 1111 1111**, any future expiry, any CVC, any name and address.

---

## 4. Flow 1 — pay for an order

**Place an order** (catalog ids and quantities; prices come from the catalog, never the request):

```bash
ORDER=$(curl -sk -X POST $API/api/orders -H "Authorization: Bearer $SHOPPER" \
  -H 'Content-Type: application/json' -d '{
    "items":[{"catalogItemId":5,"quantity":2}],
    "shipToAddress":{"street":"1 Market St","city":"San Francisco","state":"CA","country":"USA","zipCode":"94105"}
  }' | jq -r .orderId)
```

→ `201`, top-level `orderId`, order status `AwaitingPayment`.

**Authorize** — puts a hold on the money; nothing is taken:

```bash
curl -sk -X POST $API/api/orders/$ORDER/pay -H "Authorization: Bearer $SHOPPER" \
  -H 'Content-Type: application/json' -d '{
    "card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123",
            "cardholderName":"Demo Shopper",
            "billingAddress":{"countryCode":"US","line1":"1 Market St","city":"San Francisco",
                              "state":"CA","postalCode":"94105"}}
  }' | jq .payment
```

→ `paymentStatus: "Authorized"`, a real `authorizationId`, `amount` equal to the order total to the
cent, and `authorizationExpiresAt`.
**Run it a second time** — you get the *same* `authorizationId` back. A double-click never
authorizes twice.

**Fulfil** — operator only, and *this* is when the money is taken:

```bash
curl -sk -X POST $API/api/orders/$ORDER/fulfil -H "Authorization: Bearer $OPERATOR" | jq .payment
```

→ `captureStatus: "COMPLETED"` plus what PayPal reported: `capturedAmount`, `payPalFee`, `netAmount`.
Repeating it returns the same capture. With the shopper's token it is `403`.

**Refund** — full or partial, with the caller's own idempotency key:

```bash
curl -sk -X POST $API/api/orders/$ORDER/refunds -H "Authorization: Bearer $SHOPPER" \
  -H 'Content-Type: application/json' -d '{"amount":4.00,"idempotencyKey":"k1"}' | jq .
```

→ `201` with a top-level `refundId`. Then check the three rules:

| do this | expect |
| --- | --- |
| repeat the request with `"idempotencyKey":"k1"` | the **same** `refundId` — no second refund |
| send `{"amount":2.00,"idempotencyKey":"k2"}` | a **new** `refundId` — distinct partial refunds are legitimate |
| send `{"amount":99.00,"idempotencyKey":"k3"}` | `400` — never refundable beyond what was captured |

Omit `amount` to refund the whole remaining balance.

**Cancel instead of fulfilling** — operator only, releases the hold so no money ever moved. Place and
pay for a fresh order, then:

```bash
curl -sk -X POST $API/api/orders/$OTHER/cancel -H "Authorization: Bearer $OPERATOR" | jq .payment
```

→ `paymentStatus: "Voided"`, order `Cancelled`. Cancelling a *fulfilled* order is `409` and tells you
to refund instead.

**The caller's orders and their payment state:**

```bash
curl -sk $API/api/my-orders -H "Authorization: Bearer $SHOPPER" | jq '.orders[]'
```

## 5. Flow 2 — saved cards

```bash
PM=$(curl -sk -X POST $API/api/payment-methods -H "Authorization: Bearer $SHOPPER" \
  -H 'Content-Type: application/json' \
  -d '{"card":{"number":"4111111111111111","expiry":"2030-01","securityCode":"123","cardholderName":"Demo Shopper"}}' \
  | jq -r .paymentMethodId)

curl -sk $API/api/payment-methods -H "Authorization: Bearer $SHOPPER" | jq .paymentMethods
```

→ `201` with a top-level `paymentMethodId`; the description is brand + last four + expiry only —
never full card details. The card itself lives in PayPal's vault; this app stores no card number.

**Pay a second order with it:**

```bash
ORDER2=$(curl -sk -X POST $API/api/orders -H "Authorization: Bearer $SHOPPER" \
  -H 'Content-Type: application/json' -d '{"items":[{"catalogItemId":4,"quantity":1}]}' | jq -r .orderId)

curl -sk -X POST $API/api/orders/$ORDER2/pay -H "Authorization: Bearer $SHOPPER" \
  -H 'Content-Type: application/json' -d "{\"paymentMethodId\":$PM}" | jq .payment
```

**Delete it, and confirm it is really gone:**

```bash
curl -sk -X DELETE $API/api/payment-methods/$PM -H "Authorization: Bearer $SHOPPER" -o /dev/null -w '%{http_code}\n'
curl -sk $API/api/payment-methods -H "Authorization: Bearer $SHOPPER" | jq .paymentMethods   # []
```

Paying with `paymentMethodId: $PM` afterwards is `404`.

## 6. Reconciliation (operator only)

```bash
FROM=$(date -u -d '45 days ago' +%Y-%m-%dT%H:%M:%SZ); TO=$(date -u +%Y-%m-%dT%H:%M:%SZ)
curl -sk "$API/api/reconciliation?from=$FROM&to=$TO" -H "Authorization: Bearer $OPERATOR" \
  | jq '.report | {providerTransactionCount, matchedCount, onlyAtPayPalCount, onlyInEShopCount, providerLastRefreshedAt}'
```

Lists PayPal's own record of transactions for the range and lines it up against eShop's payments:
`matched`, `onlyAtPayPal` (PayPal knows, eShop does not) and `onlyInEShop` (the reverse). PayPal caps
a search window at 31 days and pages results, so the report walks the whole range window by window
and pages each to the end — a 45-day range really does cover 45 days.

> **Expected sandbox result:** PayPal's transaction reporting lags live activity by up to three
> hours, so payments you *just* created come back under `onlyInEShop` with a note saying so, and
> `matchedCount` is `0`. That is reporting lag, not a discrepancy. The matching itself is covered by
> unit tests in `tests/UnitTests/ApplicationCore/Services/PaymentServiceTests/BuildReconciliationReport.cs`.

## 7. Access control worth spot-checking

| request | expect |
| --- | --- |
| `/fulfil`, `/cancel`, `/reconciliation` with `$SHOPPER` | `403` — operator actions |
| another shopper's order on `/pay`, `/refunds` | `404` — not "forbidden", so existence cannot be probed |
| another shopper's card on `DELETE /api/payment-methods/{id}` | `404` |
| `GET /api/my-orders`, `GET /api/payment-methods` | only the caller's own |

## 8. Two things worth seeing for yourself

**Missing credentials stop the app, not the first payment:**

```bash
PayPal__ClientSecret=" " dotnet run --project src/PublicApi/PublicApi.csproj
# → OptionsValidationException: PayPal:ClientSecret is not configured. …
```

The message names the key and never echoes a value.

**`PayPal:BaseUrl` is used verbatim for every call, the OAuth token request included:**

```bash
PayPal__BaseUrl="https://localhost:19399" dotnet run --project src/PublicApi/PublicApi.csproj
# then POST /pay, and watch the log:
#   HTTP POST https://localhost:19399/v1/oauth2/token
#   HTTP POST https://localhost:19399/v2/checkout/orders
```

Leave it unset to use the environment's own base URL.

---

## Where the code lives

| | |
| --- | --- |
| Endpoints | `src/PublicApi/OrderEndpoints/`, `PaymentMethodEndpoints/`, `ReconciliationEndpoints/` |
| Domain (payment state machine, refund rules) | `src/ApplicationCore/Entities/PaymentAggregate/` |
| Orchestration | `src/ApplicationCore/Services/PaymentService.cs`, `PaymentMethodService.cs`, `ReconciliationService.cs` |
| Processor-agnostic contract | `src/ApplicationCore/Interfaces/IPaymentGateway.cs`, `src/ApplicationCore/Payments/` |
| PayPal implementation, settings, DI | `src/Infrastructure/PayPal/` |
| Vendored PayPal SDK (not on a package feed) | `third-party/paypal-csharp-sdk/` |
| Contract sheet & design record | `pay-pal-server-sdk-plan.md` |

`IPaymentGateway` is the seam: `ApplicationCore` contains no PayPal type, so the domain and its tests
never depend on the processor's wire model.
