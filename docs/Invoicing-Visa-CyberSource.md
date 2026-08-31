# Customer invoicing with Visa (CyberSource)

This adds an **additive** invoicing capability to eShopOnWeb: a shopper's order can be billed
through **Visa**, using its **CyberSource Invoicing v2** platform, as the invoicing provider. It
does not change the existing catalog/basket/order flow.

Every Visa interaction goes through the official CyberSource .NET SDK
(`CyberSource.Rest.Client`), the `InvoicesApi` (Invoicing v2). All endpoints are exposed on the
**PublicApi** project (JWT-authenticated), under `/api/`.

## How the lifecycle maps to CyberSource

| Capability (this app)                         | CyberSource Invoicing v2 call                    | Result state |
|-----------------------------------------------|--------------------------------------------------|--------------|
| Raise a bill (`POST /api/orders/{id}/invoice`)| `POST /invoicing/v2/invoices` (SendImmediately=false) | `DRAFT` (not yet put to the shopper) |
| Read a bill (`GET /api/invoices/{id}`)        | `GET /invoicing/v2/invoices/{id}`                | — |
| Correct a bill (`PATCH /api/invoices/{id}`)   | `PUT /invoicing/v2/invoices/{id}`                | stays `DRAFT` |
| Issue / put to shopper (`POST .../issue`)     | `POST /invoicing/v2/invoices/{id}/publication`   | `CREATED` (payment link available) |
| Withdraw (`POST .../withdraw`)                | `POST /invoicing/v2/invoices/{id}/cancelation`   | `CANCELED` |
| Reconciliation (`GET /api/invoices/reconciliation`) | `GET /invoicing/v2/invoices` (+ per-bill history for dates) | — |

Notes:
- **Issue = publish.** Publishing a `DRAFT` moves it to `CREATED` and generates the payment link
  *without* emailing anyone (`DeliveryMode` is left unset / `SendImmediately=false`), which suits
  the test environment where no real customer is contacted.
- **The amount always comes from the order.** Neither create nor correct accept an amount; the
  order's items and prices are used. Correcting only changes the due date / customer details, and
  only while the bill is still a `DRAFT` (once issued or withdrawn, `PATCH` returns `409`).
- **Ownership.** Bills and orders are shopper-scoped: `GET`, `PATCH`, `POST /orders`,
  `POST /orders/{id}/invoice` and `GET /my-invoices` act only on the caller's own data. `issue`,
  `withdraw` and `reconciliation` are operator actions restricted to the `Administrators` role.
- **Reconciliation** pages the whole provider account over the range and flags each bill as eShop's
  (`Matched`) or not (`ProviderOnly`); a bill eShop recorded but the provider did not return shows
  as `EShopOnly`. The shared sandbox already holds bills from other activity — these appear as
  `ProviderOnly` with `belongsToEShop=false`. The provider list omits per-bill created-dates in the
  sandbox, so the creation time is sourced from each bill's own history.

## Configuration & secrets

- `Visa:BaseUrl` is bound from configuration (`appsettings.json`, default
  `https://apitest.cybersource.com`). **Every** provider call is routed through it — the SDK's
  `runEnvironment` (which it also uses to sign requests) is derived verbatim from this base
  address's host, so pointing `Visa:BaseUrl` at a different address moves all traffic there.
- Credentials come from environment variables `VISA_MERCHANT_ID`, `VISA_KEY_ID`,
  `VISA_SECRET_KEY` and are loaded into **.NET user-secrets** (`Visa:MerchantId`, `Visa:KeyId`,
  `Visa:SecretKey`). No credential value is ever written into the repository. Authentication uses
  CyberSource JWT with a shared secret (HS256). The secret is never logged (the SDK masks it and
  the app never logs it) and is never returned by any endpoint.

---

## Verify it yourself

Prerequisites: `VISA_MERCHANT_ID`, `VISA_KEY_ID`, `VISA_SECRET_KEY` set in the environment; the
.NET 10 SDK installed; trusted HTTPS dev cert (`dotnet dev-certs https --check`).

### 1. Load the Visa credentials into user-secrets (values stay out of the repo)

```bash
cd src/PublicApi
dotnet user-secrets set "Visa:MerchantId" "$VISA_MERCHANT_ID"
dotnet user-secrets set "Visa:KeyId"      "$VISA_KEY_ID"
dotnet user-secrets set "Visa:SecretKey"  "$VISA_SECRET_KEY"
cd ../..
```

### 2. Run the PublicApi (in-memory DB; .NET 8 app on the .NET 10 runtime)

```bash
export DOTNET_ROLL_FORWARD=Major
export ASPNETCORE_ENVIRONMENT=Development           # loads user-secrets + UseOnlyInMemoryDatabase
export ASPNETCORE_URLS="https://localhost:19803;http://localhost:19804"
dotnet run --project src/PublicApi
```

Swagger: <https://localhost:19803/swagger>. Because the store is in-memory, orders and bills only
survive within a single run — raise, correct, issue and withdraw within the same run.

### 3. Get bearer tokens (shopper and operator)

```bash
B=https://localhost:19803
SHOP=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"demouser@microsoft.com","password":"Pass@word1"}' | \
  python -c "import sys,json;sys.stdout.write(json.load(sys.stdin)['token'])")
ADMIN=$(curl -sk -X POST $B/api/authenticate -H "Content-Type: application/json" \
  -d '{"username":"admin@microsoft.com","password":"Pass@word1"}' | \
  python -c "import sys,json;sys.stdout.write(json.load(sys.stdin)['token'])")
```

### 4. Flow 1 — a bill for an order

```bash
# Place an order from catalog items -> returns { "orderId": N }
OID=$(curl -sk -X POST $B/api/orders -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"items":[{"catalogItemId":2,"quantity":3}]}' | python -c "import sys,json;print(json.load(sys.stdin)['orderId'])")

# Raise a bill for that order (due date + optional customer) -> returns { "invoiceId": "..." }
IID=$(curl -sk -X POST $B/api/orders/$OID/invoice -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"dueDate":"2026-10-31","customer":{"name":"Demo Shopper","email":"demo@example.test"}}' | \
  python -c "import sys,json;print(json.load(sys.stdin)['invoiceId'])")

curl -sk $B/api/invoices/$IID -H "Authorization: Bearer $SHOP"          # state=Draft, paymentLink=null, amount from order
curl -sk -X PATCH $B/api/invoices/$IID -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"dueDate":"2026-12-01","customer":{"name":"Corrected Name"}}'    # 200, still Draft
```

### 5. Flow 2 — put the bill to the shopper, and take it back

```bash
curl -sk -X POST $B/api/invoices/$IID/issue -H "Authorization: Bearer $SHOP"    # 403 (operator only)
curl -sk -X POST $B/api/invoices/$IID/issue -H "Authorization: Bearer $ADMIN"   # 200 -> state=Issued, paymentLink present
curl -sk $B/api/invoices/$IID -H "Authorization: Bearer $SHOP"                  # paymentLink now handed out
curl -sk -X PATCH $B/api/invoices/$IID -H "Authorization: Bearer $SHOP" -H "Content-Type: application/json" \
  -d '{"dueDate":"2027-01-01"}'                                                 # 409 (already issued)

# Withdraw a different bill
curl -sk -X POST $B/api/invoices/$IID/withdraw -H "Authorization: Bearer $ADMIN"  # -> state=Withdrawn; a re-withdraw returns 409
curl -sk $B/api/my-invoices -H "Authorization: Bearer $SHOP"                      # the caller's bills, each with invoiceId + state
```

### 6. Flow 3 — the operator's reconciliation report

```bash
curl -sk "$B/api/invoices/reconciliation?from=2026-08-31T00:00:00Z&to=2026-09-01T00:00:00Z" \
  -H "Authorization: Bearer $ADMIN"
```

Each entry carries an `invoiceId`, a `match` (`Matched` / `ProviderOnly` / `EShopOnly`) and
`belongsToEShop`, so the bills eShop raised are visible alongside — and distinct from — the bills
the shared provider account holds from other activity.
