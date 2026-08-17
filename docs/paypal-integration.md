# PayPal payments & saved cards (PublicApi)

An **additive** capability on top of eShopOnWeb's existing catalog/basket/order flow: it holds
money at checkout (authorize), takes it at fulfilment (capture), releases it on cancel (void),
and returns it on a refund — with PayPal as the processor and shopper-saved (vaulted) cards.

Nothing in the original flow was replaced. `POST /api/orders` creates a real `Order` with real
`OrderItem`s from the existing aggregate; a new `Payment` aggregate carries the money-movement
and fulfilment state that `Order` never held.

## Endpoints (all under `/api/`, JWT-authenticated)

| Method & route | Who | Purpose |
|---|---|---|
| `POST /api/orders` | shopper | Place an order from catalog items. Returns `orderId`. Starts *AwaitingPayment*. |
| `POST /api/orders/{orderId}/pay` | shopper (own) | **Authorize** — hold the order total. Body carries `card` **or** `savedPaymentMethodId`. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | **Capture** — take the money. Reports captured amount, PayPal fee, net proceeds. Renews a stale hold. |
| `POST /api/orders/{orderId}/cancel` | **admin** | **Void** before fulfilment — release the hold. |
| `POST /api/orders/{orderId}/refunds` | shopper (own) or admin | Refund a captured order, full or partial. Returns `refundId`. Idempotent on `idempotencyKey`. |
| `GET /api/my-orders` | shopper | The caller's own orders with payment state. |
| `GET /api/reconciliation?from={iso}&to={iso}` | **admin** | PayPal's transactions vs eShop orders over the whole range. |
| `POST /api/payment-methods` | shopper | Save (vault) a card. Returns `paymentMethodId` + safe descriptor. |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{id}` | shopper (own) | Remove a saved card (also deletes it from PayPal's Vault). |

Operator actions (`fulfil`, `cancel`, `reconciliation`) require the `Administrators` role.
Every other endpoint is shopper-scoped and acts only on the caller's own data — one shopper
never sees, uses, or deletes another's order or card. `refunds` is allowed for the order's
owner or an administrator.

## How PayPal is called

`IPayPalClient` (`ApplicationCore/Interfaces`) → `PayPalClient` (`Infrastructure/PayPal`) is the
single place that speaks PayPal REST. It uses:

- **Orders v2** — `POST /v2/checkout/orders` (intent `AUTHORIZE`) with `payment_source.card`
  (raw card) or `payment_source.card.vault_id` (saved card).
- **Payments v2** — `.../authorizations/{id}/capture`, `/reauthorize`, `/void`,
  `.../captures/{id}/refund`.
- **Vault v3** — `setup-tokens` → `payment-tokens` to save a card; `DELETE payment-tokens/{id}`.
- **Reporting v1** — `GET /v1/reporting/transactions`, chunked to PayPal's 31-day window and
  fully paginated, for reconciliation.

OAuth tokens are cached (refreshed a minute before expiry). Every state-changing call carries a
`PayPal-Request-Id`; 429/5xx are retried with backoff; `debug_id` is logged on failures. Full
card details are held only transiently and are never persisted to the database or written to
logs. Money is formatted to the cent; the captured hold always equals the order total.

If PayPal ever answers a card payment with a browser approval challenge (`PAYER_ACTION_REQUIRED`),
the client raises `PayPalChallengeRequiredException` (HTTP 422) instead of building an approval
round-trip.

## Configuration (`PayPal:` section)

Bound from `PayPalSettings`. **Values live only in .NET user-secrets / environment — never in the
repo.** Keys: `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:Environment`, `PayPal:Currency`,
and optional `PayPal:BaseUrl`. When `PayPal:BaseUrl` is set it is used verbatim for *every* call
(including the OAuth token request); otherwise the base address is derived from `PayPal:Environment`
(`sandbox` → `https://api-m.sandbox.paypal.com`, `live`/`production` → `https://api-m.paypal.com`).

Load the sandbox credentials once (from the provided env vars) into user-secrets:

```bash
cd src/PublicApi
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET"
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"
```

## Running & verifying (in-memory, no browser)

This machine has only the .NET 10 SDK and no SQL LocalDB, so run rolled-forward with the
in-memory store (`global.json` is set to `rollForward: latestMajor`):

```bash
export DOTNET_ROLL_FORWARD=Major
dotnet run --project src/PublicApi --no-launch-profile \
  --urls "https://localhost:10743;http://localhost:10744" \
  -e ASPNETCORE_ENVIRONMENT=Development -e UseOnlyInMemoryDatabase=true
```

> In-memory data lives only for one run — place, pay, fulfil and refund within the same run.

Then drive the API with a bearer token from `POST /api/authenticate`
(`demouser@microsoft.com` / `Pass@word1` for a shopper, `admin@microsoft.com` / `Pass@word1`
for an operator). The end-to-end script `scripts/verify-paypal.sh` exercises every flow with
PayPal's sandbox test card `4111 1111 1111 1111`:

```bash
bash scripts/verify-paypal.sh
```

It authorizes and captures a real hold, refunds it (with idempotency and over-refund checks),
saves a card and reuses it to pay a second order, cancels a third, checks the operator/scoping
boundaries, and prints a reconciliation summary.

> **Sandbox note:** the shared sandbox card occasionally returns a soft `PAYER_CANNOT_PAY`
> decline; simply calling `pay` again completes it (the script retries). Reconciliation over a
> range covering just-created payments can legitimately come back empty because PayPal's
> transaction reporting lags — those payments then show under `inEShopNotInPayPal`, which is the
> report working correctly, not a gap.
