# PayPal payments & saved cards (PublicApi)

An **additive** capability on top of the existing catalog/basket/order flow: collect money for an
order via **PayPal** (authorize at checkout, capture at fulfilment, refund on return) and let a
shopper **save a card** to reuse on later orders. All PayPal interaction goes through the
`paypal-docs` MCP-documented REST API; the app never stores full card details and never logs them.

## Endpoints (all JWT-authenticated, under `/api/`)

| Method & route | Role | Purpose |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog items (`items:[{catalogItemId,quantity}]`). Starts awaiting payment. Returns `orderId`. |
| `POST /api/orders/{orderId}/pay` | shopper (own order) | Authorize the order total — a hold, not a capture. Body: `{card:{…}}` **or** `{savedPaymentMethodId}`. Idempotent. |
| `POST /api/orders/{orderId}/fulfil` | operator | Mark fulfilled and capture the funds. Reports captured amount, PayPal fee, net. Renews a stale hold; reports an actionable error if it can no longer be renewed. |
| `POST /api/orders/{orderId}/cancel` | operator | Cancel before fulfilment; releases the held funds (void). |
| `POST /api/orders/{orderId}/refunds` | operator | Refund the capture, full or partial. Body: `{amount?, idempotencyKey}` (or `Idempotency-Key` header). Returns `refundId`. Never refunds beyond what was captured. |
| `GET /api/my-orders` | shopper | The caller's own orders with payment state. |
| `GET /api/reconciliation?from=&to=` | operator | Lines PayPal's transactions (whole range, chunked + fully paginated) up against eShop orders. `from`/`to` are ISO-8601. |
| `POST /api/payment-methods` | shopper | Save a card (vaulted at PayPal). Returns `paymentMethodId` + safe metadata (brand, last4, expiry). |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper (own card) | Remove a saved card; deletes the PayPal vault token so it can no longer pay. |

Every shopper endpoint acts only on the caller's own data; one shopper can never see, use, or
delete another's order or saved card. Operator endpoints require the `Administrators` role.

## Configuration (`PayPal:` section — never hard-coded, never committed)

| Key | From env var | Notes |
| --- | --- | --- |
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` | REST client id of the sandbox **business** account. |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` | Kept in .NET user-secrets, never in the repo. |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` | `sandbox` or `live`; picks the base URL when `BaseUrl` is unset. |
| `PayPal:Currency` | `PAYPAL_CURRENCY` | Currency for all amounts, e.g. `USD`. |
| `PayPal:BaseUrl` | — | Optional. When set, used verbatim for **every** call (including the token request). |

Load the secrets once (values come from the environment; they never enter the repo):

```bash
proj=src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"     --project $proj
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET" --project $proj
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   --project $proj
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"      --project $proj
```

## Run & verify

```bash
export DOTNET_ROLL_FORWARD=Major ASPNETCORE_ENVIRONMENT=Development UseOnlyInMemoryDatabase=true
export ASPNETCORE_URLS="https://localhost:8883;http://localhost:8884"
dotnet run --project src/PublicApi/PublicApi.csproj --no-launch-profile
# In another shell — drives every flow end to end against the PayPal sandbox:
BASE=https://localhost:8883 bash tests/PaymentFlows.verify.sh
```

The verify script uses the seeded users `demouser@microsoft.com` (shopper) and
`admin@microsoft.com` (operator), password `Pass@word1`, and the sandbox test card
`4111 1111 1111 1111` (any future expiry / CVC). With the in-memory database each run is isolated,
so place, pay, fulfil and refund the orders you create within the same run.
