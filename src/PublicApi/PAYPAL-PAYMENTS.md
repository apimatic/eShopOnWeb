# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb as an **additive** capability on the `PublicApi`
project. A shopper places an order, authorizes a card (a hold), an operator fulfils (capture),
cancels (void) or refunds it; a shopper can also save a card and reuse it. PayPal is the
processor; every PayPal call goes through `IPayPalGateway` (Orders v2, Payments v2, Vault v3,
Transaction Search v1).

## Endpoints

| Method & route | Who | What |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog items → `orderId`. Starts `AwaitingPayment`. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** (hold) the order total with a one-off card **or** a saved card. |
| `POST /api/orders/{orderId}/fulfil` | operator | Fulfil → **capture** the money. Reports captured amount, PayPal fee, net. |
| `POST /api/orders/{orderId}/cancel` | operator | Cancel before fulfilment → **void** the hold (no money moves). |
| `POST /api/orders/{orderId}/refunds` | shopper | **Refund** a captured payment, full or partial → `refundId`. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from={iso}&to={iso}` | operator | PayPal transactions vs eShop orders over a date range. |
| `POST /api/payment-methods` | shopper | Save (vault) a card → `paymentMethodId` + safe descriptor. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card. |

Operator = the administrator role the project already uses. Every other endpoint is
shopper-scoped and acts only on the caller's own data (JWT identity). Full card details are
never stored in this app's database and never logged; only the PayPal vault token id and a safe
descriptor (brand, last four, expiry) are kept.

## Configuration & secrets

Settings bind from the `PayPal:` configuration section — **no values are hard-coded**:

| Key | From env var |
| --- | --- |
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` (`sandbox` / `live`) |
| `PayPal:Currency` | `PAYPAL_CURRENCY` |
| `PayPal:BaseUrl` | *(optional)* if set, used verbatim for every PayPal call including the token request |

Load the credentials into .NET user-secrets (kept **outside** the repo). From `src/PublicApi`:

```bash
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Run (this machine's constraints)

Only the .NET 10 SDK is installed (global.json rolls forward), and there is no SQL LocalDB, so
run in-memory. In-memory data is per-process and per-host — create, pay, fulfil and refund
within the **same run**, all through PublicApi.

```bash
export DOTNET_ROLL_FORWARD=Major
export UseOnlyInMemoryDatabase=true
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="https://localhost:8543;http://localhost:8544"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

Ensure the HTTPS dev cert is trusted: `dotnet dev-certs https --check`.

## Verify it end-to-end (PowerShell, sandbox test Visa)

`-SkipCertificateCheck` accepts the dev cert. Seeded users: `demouser@microsoft.com` (shopper)
and `admin@microsoft.com` (operator), password `Pass@word1`.

```powershell
$base = 'https://localhost:8543'
function Auth($u){ (Invoke-RestMethod -SkipCertificateCheck -Method Post -Uri "$base/api/authenticate" `
  -ContentType 'application/json' -Body (@{username=$u;password='Pass@word1'}|ConvertTo-Json)).token }
$sh = @{ Authorization = "Bearer $(Auth 'demouser@microsoft.com')" }   # shopper
$ad = @{ Authorization = "Bearer $(Auth 'admin@microsoft.com')" }      # operator
function Api($m,$u,$h,$b){ Invoke-RestMethod -SkipCertificateCheck -Method $m -Uri "$base$u" `
  -Headers $h -ContentType 'application/json' -Body ($b|ConvertTo-Json -Depth 8) }
$card = @{ number='4111111111111111'; expiry='2030-01'; securityCode='123'; name='Demo Shopper';
  billingAddress=@{ addressLine1='1 Market St'; adminArea2='San Jose'; adminArea1='CA'; postalCode='95131'; countryCode='US' } }

# Flow 1 — pay an order
$o  = Api POST /api/orders $sh @{ items=@(@{catalogItemId=1;quantity=1},@{catalogItemId=2;quantity=2}) }
$o.orderId                                                              # -> orderId
Api POST "/api/orders/$($o.orderId)/pay"    $sh @{ card=$card }         # authorize (hold)
Api POST "/api/orders/$($o.orderId)/fulfil" $ad @{}                     # capture (fee + net reported)
Api POST "/api/orders/$($o.orderId)/refunds" $sh @{ amount=10; idempotencyKey='r-1' }  # -> refundId
Api GET  /api/my-orders $sh @{}                                         # payment state

# Flow 2 — save a card and reuse it
$pm = Api POST /api/payment-methods $sh @{ alias='My Visa'; card=$card }   # -> paymentMethodId (+ brand/last4)
$o2 = Api POST /api/orders $sh @{ items=@(@{catalogItemId=3;quantity=1}) }
Api POST "/api/orders/$($o2.orderId)/pay" $sh @{ savedPaymentMethodId=[int]$pm.paymentMethodId }  # pay with saved card
Api POST "/api/orders/$($o2.orderId)/cancel" $ad @{}                    # void the hold

# Operator report
Invoke-RestMethod -SkipCertificateCheck -Headers $ad `
  -Uri "$base/api/reconciliation?from=2026-08-01T00:00:00Z&to=2026-08-12T00:00:00Z"
```

Notes:
- **Idempotency**: paying twice returns the same authorization; repeating a refund under the
  same `idempotencyKey` returns the same `refundId` (no second refund). Distinct partial refunds
  use distinct keys and are guarded so total refunds never exceed the capture.
- **Reconciliation lag**: PayPal's transaction reporting lags live activity, so a range covering
  payments you just created may legitimately come back empty (your paid orders then appear as
  `EShopOnly`). This is expected sandbox behaviour, not a gap. The report chunks the range into
  ≤31-day windows and pages through all results, so it is correct over a range that has data.
- **Browserless**: if PayPal ever answers a card with a challenge that needs browser approval,
  the API returns `422` with a clear message rather than attempting an approval round-trip.

## SQL Server note

The new `Payment`, `Refund`, `Buyer`, and `PaymentMethod` tables are mapped via EF Core
configurations and work with the in-memory provider used here. A relational deployment would
add an EF migration for these tables (`dotnet ef migrations add AddPayments --project
src/Infrastructure --startup-project src/PublicApi`); this machine has no SQL Server to validate
one against, so none is committed.
```
