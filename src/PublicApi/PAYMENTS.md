# PayPal payments & saved cards (PublicApi)

Adds real card payments (via **PayPal**, direct card processing) and saved cards to eShopOnWeb,
as JWT-authenticated HTTP endpoints on `src/PublicApi`. It is **additive**: the existing
`Order`/`OrderItem` aggregate is reused unchanged; a new `Payment` aggregate (1:1 with an order)
carries the money + fulfilment state and the PayPal ids/statuses, and a `SavedPaymentMethod`
aggregate holds vaulted-card metadata (never card details).

All PayPal interaction goes through one class, `Payments/PayPalGateway.cs` (built on the PayPal
Server SDK, vendored under `src/PayPalServerSdk`). Design and contract notes are in
`pay-pal-server-sdk-plan.md` at the repo root.

## Endpoints

| Method & route | Who | Purpose | Top-level id returned |
| --- | --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog items (`{ items:[{catalogItemId,quantity}], shipToAddress? }`). Prices come from the catalog. | `orderId` |
| `POST /api/orders/{orderId}/pay` | shopper | Authorize (hold) the order total with a one-off `card` **or** a `savedPaymentMethodId`. | — |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Mark fulfilled and capture the funds (renews a stale hold automatically). | — |
| `POST /api/orders/{orderId}/cancel` | **admin** | Cancel before fulfilment; release the held funds. | — |
| `POST /api/orders/{orderId}/refunds` | shopper (owner) | Refund a fulfilled order in full or in part (`{ amount?, idempotencyKey }`). | `refundId` |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. | — |
| `GET /api/reconciliation?from=&to=` | **admin** | PayPal's transaction record lined up against eShop orders (ISO-8601 range). | — |
| `POST /api/payment-methods` | shopper | Save a card (`{ number, expiry, securityCode, cardholderName?, billingAddress? }`). | `paymentMethodId` |
| `GET /api/payment-methods` | shopper | The caller's saved cards (safe descriptor only). | — |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper (owner) | Remove a saved card. | — |

Shopper endpoints act only on the caller's own data (a shopper never sees, uses, or deletes
another's order or card). `fulfil`, `cancel` and `reconciliation` require the `Administrators` role.

## Configuration (no secrets in the repo)

Settings bind from the `PayPal:` section — `PayPal:ClientId`, `PayPal:ClientSecret`,
`PayPal:Environment`, `PayPal:Currency`, and optional `PayPal:BaseUrl` (used verbatim for every
call, including the OAuth token request, when set). At startup the host **fails fast** if any of
ClientId/ClientSecret/Currency is missing/blank or Environment is unsupported.

The `PAYPAL_*` environment variables are mapped onto these keys at startup, and the credentials are
also loaded into **.NET user-secrets** (stored outside the repo). Load them once:

```bash
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"     --project src/PublicApi
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET" --project src/PublicApi
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   --project src/PublicApi
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      --project src/PublicApi
```

## Run (this machine)

The SDK pins net8.0 but only the .NET 10 SDK is installed, and LocalDB is absent, so run with
roll-forward and the in-memory database. In-memory data survives only within a single run, so pay,
fulfil and refund the orders you create in that same run.

```bash
export DOTNET_ROLL_FORWARD=Major
export UseOnlyInMemoryDatabase=true
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="https://localhost:37383;http://localhost:37384"
dotnet run --project src/PublicApi --no-launch-profile
```

Ensure the HTTPS dev cert is trusted (`dotnet dev-certs https --check`), or pass `-k`/
`-SkipCertificateCheck` to your client.

## Verify it works (no browser needed)

Get a bearer token from the API's own authenticate endpoint (seeded users:
`demouser@microsoft.com` = shopper, `admin@microsoft.com` = operator; password `Pass@word1`).
Use PayPal's sandbox test card **Visa `4111 1111 1111 1111`**, any future expiry (`YYYY-MM`),
any CVC, any name/billing address. Example with PowerShell:

```powershell
$B='https://localhost:37383'
$demo  = Invoke-RestMethod -SkipCertificateCheck -Method Post "$B/api/authenticate" -ContentType application/json -Body (@{username='demouser@microsoft.com';password='Pass@word1'}|ConvertTo-Json)
$admin = Invoke-RestMethod -SkipCertificateCheck -Method Post "$B/api/authenticate" -ContentType application/json -Body (@{username='admin@microsoft.com';password='Pass@word1'}|ConvertTo-Json)
$hDemo=@{Authorization="Bearer $($demo.token)"}; $hAdmin=@{Authorization="Bearer $($admin.token)"}
$card=@{number='4111111111111111';expiry='2029-12';securityCode='123';cardholderName='Demo Shopper';billingAddress=@{countryCode='US';addressLine1='1 Market St';adminArea1='CA';adminArea2='San Francisco';postalCode='94105'}}

# Flow 2: save a card
$pm = Invoke-RestMethod -SkipCertificateCheck -Method Post "$B/api/payment-methods" -Headers $hDemo -ContentType application/json -Body ($card|ConvertTo-Json)

# Flow 1: place -> pay (one-off card) -> fulfil (capture) -> refund
$o  = Invoke-RestMethod -SkipCertificateCheck -Method Post "$B/api/orders" -Headers $hDemo -ContentType application/json -Body (@{items=@(@{catalogItemId=5;quantity=2})}|ConvertTo-Json)
Invoke-RestMethod -SkipCertificateCheck -Method Post "$B/api/orders/$($o.orderId)/pay" -Headers $hDemo -ContentType application/json -Body (@{card=$card}|ConvertTo-Json)
Invoke-RestMethod -SkipCertificateCheck -Method Post "$B/api/orders/$($o.orderId)/fulfil" -Headers $hAdmin   # shows captured gross/fee/net
Invoke-RestMethod -SkipCertificateCheck -Method Post "$B/api/orders/$($o.orderId)/refunds" -Headers $hDemo -ContentType application/json -Body (@{amount=5.00;idempotencyKey=[guid]::NewGuid().ToString()}|ConvertTo-Json)

# Flow 2 reuse: a second order paid with the SAVED card
$o2 = Invoke-RestMethod -SkipCertificateCheck -Method Post "$B/api/orders" -Headers $hDemo -ContentType application/json -Body (@{items=@(@{catalogItemId=5;quantity=1})}|ConvertTo-Json)
Invoke-RestMethod -SkipCertificateCheck -Method Post "$B/api/orders/$($o2.orderId)/pay" -Headers $hDemo -ContentType application/json -Body (@{savedPaymentMethodId=$pm.paymentMethodId}|ConvertTo-Json)
Invoke-RestMethod -SkipCertificateCheck -Method Post "$B/api/orders/$($o2.orderId)/fulfil" -Headers $hAdmin

Invoke-RestMethod -SkipCertificateCheck "$B/api/my-orders" -Headers $hDemo                                   # states
Invoke-RestMethod -SkipCertificateCheck "$B/api/reconciliation?from=2026-09-01T00:00:00Z&to=2026-09-26T00:00:00Z" -Headers $hAdmin
```

Notes:
- **Reconciliation may show your just-created orders under `onlyInEShop`** because PayPal's
  transaction reporting lags live activity — that is expected sandbox behaviour, not a gap. The
  report still covers the whole range (all pages) and lines up both sides.
- If PayPal ever answers a card payment with a browser approval challenge (3-D Secure /
  `PAYER_ACTION_REQUIRED`), the pay endpoint returns **422** with a clear message rather than
  building an approval round-trip. The standard sandbox test card authorizes without a challenge.
