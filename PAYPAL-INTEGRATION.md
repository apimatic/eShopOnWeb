# PayPal payments + saved cards for eShopOnWeb

This adds real money movement to eShopOnWeb as an **additive** capability on the JWT-authenticated
`src/PublicApi` project: a shopper places an order, pays it by card (hold), an operator fulfils it
(capture), cancels it (void) or refunds it, and a shopper can save a card once and reuse it. The
existing catalog/basket/order flow is untouched.

PayPal is spoken to **directly over HTTP**, built to the OpenAPI specs in `api-specs/paypal`
(no third-party SDK). Direct card payments use the sandbox test card `4111 1111 1111 1111`.

## Endpoints (all under `/api/`, JWT-authenticated)

| Method & route | Who | What |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog item ids + quantities → **`orderId`**. Starts *AwaitingPayment*. |
| `POST /api/orders/{orderId}/pay` | shopper (own) | Authorize (hold) the order total. Body carries `card` **or** `paymentMethodId` (a saved card). |
| `POST /api/orders/{orderId}/fulfil` | operator | Mark fulfilled and **capture** the money. Renews a stale hold first. |
| `POST /api/orders/{orderId}/cancel` | operator | Cancel before fulfilment → **void** the hold (no money moves). |
| `POST /api/orders/{orderId}/refunds` | shopper (own) | Refund the capture, full or partial → **`refundId`**. Idempotency key required. |
| `GET  /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET  /api/reconciliation?from={ISO}&to={ISO}` | operator | PayPal's transactions for a range, lined up against eShop orders. |
| `POST /api/payment-methods` | shopper | Save (vault) a card → **`paymentMethodId`** + safe description. |
| `GET  /api/payment-methods` | shopper | The caller's saved cards (safe descriptions only). |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper (own) | Remove a saved card. |

Operator = the existing **Administrators** role. Every shopper endpoint acts only on the caller's own
data (enforced by the token identity). Full card numbers are never stored in this app's database and
never logged.

## Which PayPal specs are used

- `checkout_orders_v2` — create order (`intent=AUTHORIZE`) with a direct card or a vaulted
  `card.vault_id`; the single-step card create authorizes inline (the hold).
- `payments_payment_v2` — capture / reauthorize / void an authorization, refund a capture
  (reads `seller_receivable_breakdown` for gross / `paypal_fee` / `net_amount`).
- `vault_payment_tokens_v3` — create and delete a payment token from a card.
- `transaction_search_v1` — reconciliation (`GET /v1/reporting/transactions`, chunked into PayPal's
  31-day windows and paged to cover the whole range).
- Access token: the specs' `Oauth2` security scheme points at `POST /v1/oauth2/token`
  (client-credentials). The token request/response envelope itself is the OAuth2 standard the scheme
  references (confirmed against PayPal docs) — the spec contributes the endpoint and the scheme.
  This is not a gap.

## Configuration & secrets

Settings bind from the `PayPal:` configuration section — **no values are hard-coded** in the repo:

| Key | From env var |
|---|---|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` (`sandbox`/`live`) |
| `PayPal:Currency` | `PAYPAL_CURRENCY` |
| `PayPal:BaseUrl` | *(optional)* `PAYPAL_BASEURL` — when set, used verbatim for every PayPal call incl. the token request |

Load the sandbox credentials into **user-secrets** (they never touch the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"     >/dev/null
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET" >/dev/null
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   >/dev/null
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      >/dev/null
```

(As a convenience, if those flat `PAYPAL_*` environment variables are present at startup they are also
mapped onto the `PayPal:` keys, so the app runs from the environment alone too.)

## Run it (this machine: .NET 10 SDK only, no LocalDB)

The launch profile already sets `DOTNET_ROLL_FORWARD=Major` and `UseOnlyInMemoryDatabase=true`, so:

```bash
dotnet run --project src/PublicApi --no-launch-profile \
  -- # or just: dotnet run --project src/PublicApi
```

Explicit form (what was used to verify):

```bash
DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="https://localhost:30163;http://localhost:30164" \
UseOnlyInMemoryDatabase=true \
dotnet run --project src/PublicApi --no-launch-profile
```

The API listens on `https://localhost:30163` (Swagger at `/swagger`). The in-memory store is
per-process and resets on restart, so **pay/fulfil/refund the orders you created in the same run**.

## Verify end to end (PowerShell, no browser)

```powershell
$B = 'https://localhost:30163'
function Login($u){ (Invoke-RestMethod -SkipCertificateCheck -Method Post "$B/api/authenticate" `
  -ContentType 'application/json' -Body (@{username=$u;password='Pass@word1'}|ConvertTo-Json)).token }
$sh = @{ Authorization = "Bearer $(Login 'demouser@microsoft.com')" }   # shopper
$ad = @{ Authorization = "Bearer $(Login 'admin@microsoft.com')" }      # operator
function REST($m,$u,$h,$b){ if($b){ Invoke-RestMethod -SkipCertificateCheck -Method $m $u -Headers $h `
  -ContentType 'application/json' -Body ($b|ConvertTo-Json -Depth 8) } else { Invoke-RestMethod -SkipCertificateCheck -Method $m $u -Headers $h } }
$card = @{ number='4111111111111111'; expiry='2027-01'; securityCode='123'; name='John Doe' }

# 1) place -> 2) pay (hold) -> 3) fulfil (capture)
$oid  = (REST POST "$B/api/orders" $sh @{items=@(@{catalogItemId=5;quantity=2},@{catalogItemId=4;quantity=1})}).orderId
$pay  = REST POST "$B/api/orders/$oid/pay" $sh @{card=$card}          # status: PaymentAuthorized, hold = 29.00
$ful  = REST POST "$B/api/orders/$oid/fulfil" $ad $null               # captured gross/fee/net reported by PayPal
$ful.payment | Format-List status,capturedGross,payPalFee,netAmount

# 4) refunds: partial, idempotent repeat (same refundId), a second distinct partial
REST POST "$B/api/orders/$oid/refunds" $sh @{amount=10.00; idempotencyKey='K1'}
REST POST "$B/api/orders/$oid/refunds" $sh @{amount=10.00; idempotencyKey='K1'}   # same refundId, no double refund
REST POST "$B/api/orders/$oid/refunds" $sh @{amount=5.00;  idempotencyKey='K2'}

# 5) saved card: save -> reuse on a second order -> fulfil -> delete
$pmid = (REST POST "$B/api/payment-methods" $sh @{card=$card; alias='my-visa'}).paymentMethodId
$o2   = (REST POST "$B/api/orders" $sh @{items=@(@{catalogItemId=5;quantity=1})}).orderId
REST POST "$B/api/orders/$o2/pay" $sh @{paymentMethodId=$pmid}        # paid with the saved card
REST POST "$B/api/orders/$o2/fulfil" $ad $null
REST DELETE "$B/api/payment-methods/$pmid" $sh $null                  # gone; can no longer pay with it

# 6) cancel-before-fulfil (void)
$o3 = (REST POST "$B/api/orders" $sh @{items=@(@{catalogItemId=3;quantity=1})}).orderId
REST POST "$B/api/orders/$o3/pay" $sh @{card=$card}
REST POST "$B/api/orders/$o3/cancel" $ad $null                        # payment.status -> Voided

# 7) my-orders, and 8) reconciliation (operator)
REST GET "$B/api/my-orders" $sh $null | Format-Table orderId,status
$from = (Get-Date).AddDays(-40).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$to   = (Get-Date).AddDays(1).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
REST GET "$B/api/reconciliation?from=$from&to=$to" $ad $null |
  Format-List payPalTransactionCount,@{n='matched';e={$_.matched.Count}},@{n='inPayPalOnly';e={$_.inPayPalOnly.Count}},@{n='inEShopOnly';e={$_.inEShopOnly.Count}}
```

**Expected:** the hold equals the order total to the cent; fulfil reports PayPal's captured amount,
fee and net proceeds; the repeated refund returns the *same* `refundId`; an over-refund is rejected;
the deleted card can no longer pay; cancel voids the hold. For reconciliation, transactions you just
created may not appear yet — PayPal's transaction reporting lags, so a recent range legitimately shows
your captures under `inEShopOnly` (eShop has them, PayPal's report doesn't yet). Over a settled range
with data they line up by invoice id under `matched`.

## Notes / decisions

- The **order aggregate is reused** (not duplicated): `Order` gained a coarse `Status` and a one-to-one
  `Payment` child that carries the PayPal ids/status for the hold, the capture (with fee/net) and the
  refunds — enough state that a later request can act on it.
- **Idempotency:** pay/capture use a `PayPal-Request-Id` stable per order instance, plus a DB guard
  (an already-authorized order returns its existing hold). Refunds dedupe on the caller-supplied key in
  our own data *before* calling PayPal, and scope the PayPal request-id to the capture.
- **Stale holds:** fulfil renews (reauthorizes) an expiring authorization before capturing; if PayPal
  will no longer renew it, the API returns `409` with an operator-actionable message.
- **Browser challenges:** if PayPal ever answers a card payment with `PAYER_ACTION_REQUIRED`, the API
  returns an error saying a browser approval is required rather than building an approval round-trip.
- **Database:** EF configurations for the new `Payment`/`Refund`/`PaymentMethod` tables are in place.
  This machine runs the in-memory provider (which ignores migrations); moving to SQL Server would need
  an EF migration generated from these configurations.
