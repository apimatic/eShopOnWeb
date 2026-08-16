# PayPal payments & saved cards (PublicApi)

An **additive** capability on top of the existing catalog/basket/order flow: a shopper places an
order, pays for it by card (or a saved card), and an operator fulfils, cancels or refunds it. PayPal
is the payment processor; all money movement goes through the Orders v2, Payments v2, Vault v3 and
Transaction Search v1 REST APIs via `Infrastructure/PayPal/PayPalClient.cs`.

## Endpoints (all under `/api/`, JWT-authenticated)

| Method & route | Who | What |
| --- | --- | --- |
| `POST /api/orders` | shopper | Place an order from catalog items (reuses `Order`/`OrderItem`). Returns `orderId`. Starts *AwaitingPayment*. |
| `POST /api/orders/{orderId}/pay` | shopper (owner) | **Authorize** (hold) the order total by one-off `card` **or** a `savedCardId`. Does not capture. Idempotent. |
| `POST /api/orders/{orderId}/fulfil` | **admin** | Mark fulfilled and **capture** the funds. Renews a stale hold first; reports if it can no longer be renewed. |
| `POST /api/orders/{orderId}/cancel` | **admin** | Cancel before fulfilment; **void** the hold so no money moved. |
| `POST /api/orders/{orderId}/refunds` | owner or admin | Refund a captured order, full or partial. Body: `{ amount?, idempotencyKey }`. Returns `refundId`. |
| `GET /api/my-orders` | shopper | The caller's orders with payment state. |
| `GET /api/reconciliation?from=&to=` | **admin** | PayPal's transactions for the range lined up against eShop orders (whole range, paged). |
| `POST /api/payment-methods` | shopper | Vault a card. Returns `paymentMethodId` + safe description (brand + last four). |
| `GET /api/payment-methods` | shopper | The caller's saved cards. |
| `DELETE /api/payment-methods/{paymentMethodId}` | shopper (owner) | Remove a saved card (deletes the PayPal vault token). |

Shopper endpoints act only on the caller's own data (identity comes from the JWT name claim).
`fulfil`, `cancel` and `reconciliation` require the `Administrators` role.

## Guarantees

- **Idempotent in effect** — a double-click never authorizes or captures twice (per-order lock +
  order-state guard + deterministic `PayPal-Request-Id`). Refunds are keyed by the caller-supplied
  `idempotencyKey`; a replay returns the original refund, two distinct partial refunds both apply.
- **Never over-refunds** — a partly-refunded order can never be refunded beyond the captured amount.
- **Captured amount equals the order total to the cent**; after fulfilment the payment shows PayPal's
  captured amount, fee and net proceeds.
- **No card data stored or logged** — full card numbers pass straight to PayPal; only the vault token
  and a safe description (brand + last four) are persisted.

## Configuration (`PayPal:` section — no values in the repo)

Bind from these keys (supplied via environment variables, loaded into user-secrets):

| Key | Env var |
| --- | --- |
| `PayPal:ClientId` | `PAYPAL_CLIENT_ID` |
| `PayPal:ClientSecret` | `PAYPAL_CLIENT_SECRET` |
| `PayPal:Environment` | `PAYPAL_ENVIRONMENT` (`sandbox`/`live`) |
| `PayPal:Currency` | `PAYPAL_CURRENCY` |
| `PayPal:BaseUrl` | *(optional override; used verbatim for every call, including the token request)* |

Load them into user-secrets for this project:

```bash
dotnet user-secrets set "PayPal:ClientId"     "$PAYPAL_CLIENT_ID"     --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:ClientSecret" "$PAYPAL_CLIENT_SECRET" --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Environment"  "$PAYPAL_ENVIRONMENT"   --project src/PublicApi/PublicApi.csproj
dotnet user-secrets set "PayPal:Currency"     "$PAYPAL_CURRENCY"       --project src/PublicApi/PublicApi.csproj
```

## Running locally (this machine)

```bash
# SDK is pinned to 8.0.x but only .NET 10 is installed → roll forward.
DOTNET_ROLL_FORWARD=Major dotnet build src/PublicApi/PublicApi.csproj

# In-memory DB (no LocalDB here); http-only avoids the dev-cert dance for curl.
cd src/PublicApi
ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://localhost:9724 UseOnlyInMemoryDatabase=true \
  dotnet bin/Debug/net8.0/PublicApi.dll
```

Get a bearer token from `POST /api/authenticate` (`demouser@microsoft.com` / `admin@microsoft.com`,
password `Pass@word1`) and drive the endpoints above. Verify with PayPal's sandbox test card
`4111 1111 1111 1111`, any future expiry, any CVC.

> In-memory stores are per-process and reset on restart — place, pay, fulfil and refund within one run.
