# PayPal payments & saved cards (PublicApi)

This adds real money movement to eShopOnWeb as an **additive** capability on the `src/PublicApi`
project. PayPal is the payment processor, integrated through the **paypal-sdk** plugin
(`AsadAli.Checkout.Sdk`, root namespace `PayPalServerSdk`). It does not replace the existing
catalog/basket/order flow.

The money lifecycle mirrors real commerce: **authorize** a hold at pay time, **capture** the money
at fulfilment, **void** on cancel (before fulfilment), **refund** after capture (full or partial).

## Endpoints

All endpoints are JWT-authenticated on the PublicApi host; the caller's identity comes from the
token's name claim. Get a bearer token from `POST /api/authenticate` first.

### Flow 1 — pay for an order

| Method & route | Who | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items (`{ items: [{catalogItemId, quantity}], shipToAddress? }`). Prices come from the catalog. Starts *AwaitingPayment*. Returns `orderId`. |
| `POST /api/orders/{orderId}/pay` | shopper | **Authorize** (hold) the order total. Body carries either `card` (one-off) **or** `savedPaymentMethodId` (a saved card). Does not capture. Idempotent. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Mark fulfilled and **capture** the money. Renews a stale hold first. Response shows PayPal's captured amount, fee, and net proceeds. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Cancel before fulfilment: **void** the hold, releasing the funds. |
| `POST /api/orders/{orderId}/refunds` | shopper | **Refund** a captured order, full or partial. Body: `{ amount?, idempotencyKey }`. Returns `refundId`. Never refunds beyond what was captured. |
| `GET /api/my-orders` | shopper | The caller's own orders with payment state. |
| `GET /api/reconciliation?from={iso}&to={iso}` | **admin** | PayPal's own transactions for a date range, lined up against eShop orders. Covers the whole range (paginated, chunked into ≤30-day windows). |

### Flow 2 — saved cards

| Method & route | Who | Purpose |
|---|---|---|
| `POST /api/payment-methods` | shopper | Vault a card. Returns `paymentMethodId` and a safe descriptor (brand, last four, expiry) — never full card details. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper | Remove a saved card (deletes the PayPal vault token). Afterwards it cannot be listed or used to pay. |

Shopper-scoped endpoints act only on the caller's own data; a shopper can never see, use, or delete
another shopper's order or card (returns 404, indistinguishable from missing). Full card numbers are
never stored in this app's database and never written to logs — they pass through only to PayPal.

## Design notes

- **Idempotency.** Authorize/capture use deterministic `PayPal-Request-Id` keys plus an order-status
  guard, so a double-click never authorizes or captures twice. Refunds use the caller's idempotency
  key: a repeat under the same key returns the same `refundId` without re-refunding; distinct keys
  produce distinct partial refunds, always capped at the captured amount.
- **Stale holds.** At fulfilment, an authorization at/near its expiry is re-authorized before capture
  (and reactively if the capture itself reports the hold expired). A hold that can no longer be
  renewed surfaces as an operator-actionable 422.
- **State ownership.** Each order's payment carries the PayPal ids and current status of the hold, the
  capture, and every refund, so a later request can act on it.
- **Error mapping.** Provider rejections → 422, provider unreachable / unreadable → 502, invalid state
  transitions → 409, not-found/ownership → 404. Messages are curated and caller-safe.

## Configuration

Settings bind from the `PayPal:` configuration section (never hard-coded):

| Key | Source env var | Notes |
|---|---|---|
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | Sandbox business REST client id |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | Load into .NET user-secrets; never commit |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | e.g. `sandbox` |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | e.g. `USD` |
| `PayPal:BaseUrl` | — | Optional. When set, used verbatim for **every** PayPal call (including the token request). |

Load credentials into user-secrets (values stay out of the repo):

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Running & verifying (this machine)

Only the .NET 10 SDK is installed and there is no LocalDB, so:

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development     # loads user-secrets
export UseOnlyInMemoryDatabase=true           # no LocalDB; data lives for one run only
export ASPNETCORE_URLS="https://localhost:13543;http://localhost:13544"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
```

Because the in-memory store is per-run and per-host, drive the whole flow through PublicApi in a
single run. Verify with PayPal's sandbox test card: Visa `4111 1111 1111 1111`, any future expiry,
any CVC. Transaction reporting lags, so a reconciliation range covering just-created payments can
legitimately come back empty — that is expected, not a gap.
