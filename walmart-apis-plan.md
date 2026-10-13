# Walmart Marketplace inventory integration — plan

eShopOnWeb must keep Walmart Marketplace told how much of its catalog it has to sell. Seller SKU
for an eShop catalog item id `N` is `ESHOP-N`. All endpoints live on `src/PublicApi`
(JWT-authenticated), under `/api/`. Additive only — existing catalog/basket/order flow untouched.

## 1. Scope & sequence

| # | Step | Walmart operation(s) used |
| --- | --- | --- |
| 1 | Domain: add `UnitsInStock` to `CatalogItem` (set + decrement, refuse over-draw) | — |
| 2 | `Walmart:` config options + fail-fast + DI registration of `WalmartApIsClient` (WalletAuth, BaseUrl override) | — (auth: `options.WalletAuth`) |
| 3 | `IWalmartInventoryService` wrapping the SDK (error boundary → per-SKU result / quantity) | `GetInventoryForSku`, `UpdateInventoryForSku`, `GetInventorySummaries`, `BulkUpdateInventory` |
| 4 | PublicApi endpoints: PUT/GET `/api/walmart/stock/{sku}`, POST `/api/walmart/stock/sync`, GET `/api/walmart/stock`, PUT `/api/catalog-items/{id}/stock`, POST `/api/orders` | as above |
| 5 | SKU mapping helper (`ESHOP-{id}` ↔ id) + reconciliation report | `GetInventorySummaries` |
| 6 | Tests (SDK faked at `HttpClient` seam) + self-verification | — |

Endpoint → SDK mapping:
- `PUT /api/walmart/stock/{sku}` → `UpdateInventoryForSku` (absolute set).
- `GET /api/walmart/stock/{sku}` → `GetInventoryForSku` → read quantity from `InventorySummary`.
- `POST /api/walmart/stock/sync` → `BulkUpdateInventory` (chunked ≤500), one entry per catalog item, qty = eShop level (0 if never set).
- `GET /api/walmart/stock` → `GetInventorySummaries`, walk all pages (nextToken), join against eShop level.
- `PUT /api/catalog-items/{id}/stock`, `POST /api/orders` → eShop-local only, no Walmart call.

## 2. CONTRACT SHEET

⚠ **Signatures are generated code, verbatim.** Each operation that takes input takes ONE request
record as its first parameter, built with an object initializer whose property names are the
record's own — never flat positional arguments. An operation with no inputs takes none.
⚠ **Every SDK type is written fully-qualified with the namespace its source path implies**, taken
from the path the map gives for THAT type (records → `WalmartApIs.Models`; request records →
`WalmartApIs.Requests.Inventory`; errors → `WalmartApIs.Errors`; client/options → `WalmartApIs`;
exceptions → `WalmartApIs.Core.Exceptions`; `RawError` → `WalmartApIs.Core.ErrorResponse`).

| Operation (`client.Inventory.X`) | Signature (first param = request record) | Request record + members | Body model + fields | Response envelope → fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `GetInventoryForSku` | `GetInventoryForSku(GetInventoryForSkuRequest request, RequestOptions? = null, CancellationToken = default)` | `GetInventoryForSkuRequest { Sku: string (required), ShipNode: string? }` | — | `InventorySummary { SellerSku (sellerSku): string required, InventoryDetails (inventoryDetails): InventoryDetails? }`; `InventoryDetails { FulfillableQuantity (fulfillableQuantity): int?, OnHandQuantity (onHandQuantity): int? }` | Case A `ApiException<GetInventoryForSkuError>`; `TryGetErrorList(out ErrorList)` [400,403,404,429,500] · `TryGetRawError(out RawError)` | none | map/operations/Inventory.md; Models/InventorySummary.cs; Models/InventoryDetails.cs |
| `UpdateInventoryForSku` | `UpdateInventoryForSku(UpdateInventoryForSkuRequest request, …)` | `UpdateInventoryForSkuRequest { Sku: string (required), Body: UpdateInventoryRequest (required) }` | `UpdateInventoryRequest { Quantity (quantity): int required (≥0), ShipNode (shipNode): string?, FulfillmentLag (fulfillmentLag): int? }` | `UpdateInventoryResponse { SellerSku (sellerSku): string?, Quantity (quantity): int?, UpdatedAt (updatedAt): DateTimeOffset? }` | Case A `ApiException<UpdateInventoryForSkuError>`; `TryGetErrorList(out ErrorList)` [400,403,404,429,500] · `TryGetRawError(out RawError)` | none | map/operations/Inventory.md; Models/UpdateInventoryRequest.cs; Models/UpdateInventoryResponse.cs |
| `BulkUpdateInventory` | `BulkUpdateInventory(BulkUpdateInventoryOperationRequest request, …)` | `BulkUpdateInventoryOperationRequest { Body: BulkUpdateInventoryRequest (required) }` | `BulkUpdateInventoryRequest { Items (items): IReadOnlyList<BulkInventoryItem> required (1..500) }`; `BulkInventoryItem { SellerSku (sellerSku): string required, Quantity (quantity): int required (≥0), ShipNode?, FulfillmentLag? }` | `BulkUpdateInventoryResponse { Succeeded (succeeded): IReadOnlyList<UpdateInventoryResponse>?, Errors (errors): IReadOnlyList<BulkItemError>? }`; `BulkItemError { SellerSku (sellerSku): string?, Code?, Message? }` | Case A `ApiException<BulkUpdateInventoryError>`; `TryGetErrorList(out ErrorList)` [400,403,429,500] · `TryGetRawError(out RawError)` | none | map/operations/Inventory.md; Models/BulkUpdateInventoryRequest.cs; Models/BulkUpdateInventoryResponse.cs; Models/BulkInventoryItem.cs; Models/BulkItemError.cs |
| `GetInventorySummaries` | `GetInventorySummaries(GetInventorySummariesRequest request, …)` | `GetInventorySummariesRequest { Skus: string?, ShipNode: string?, PageSize: int = 100 (1..500), NextToken: string? }` (no required members) | — | `GetInventorySummariesResponse { Pagination (pagination): Pagination4?, InventorySummaries (inventorySummaries): IReadOnlyList<InventorySummary>? }`; `Pagination4 { NextToken (nextToken): string?, TotalCount (totalCount): int? }` | Case A `ApiException<GetInventorySummariesError>`; `TryGetErrorList(out ErrorList)` [400,403,429,500] · `TryGetRawError(out RawError)` | **cursor-style**: `Pagination.NextToken`; pass back as `NextToken` until null/empty | map/operations/Inventory.md; Models/GetInventorySummariesResponse.cs; Models/Pagination4.cs |

⚠ `GetInventorySummariesRequest` marks nothing `required`; left out: `Skus` (we want ALL SKUs, not a
filtered subset → omit it), `ShipNode` (single-node seller → omit). `PageSize` defaults 100; we set
500 (the max) to minimise round-trips. These are doc-named optional fields deliberately omitted.

**Quantity read-back field (GET single SKU):** `UpdateInventoryRequest.Quantity` is "Declared on-hand
quantity (maps to PIS inputQty)". On read-back, the number Walmart records could surface as
`InventoryDetails.FulfillableQuantity` (sellable) or `OnHandQuantity`. Which field the
Walmart-compatible service populates is `UNVERIFIED` (only live traffic confirms). Decision: read
`FulfillableQuantity ?? OnHandQuantity ?? 0` — defensive across both. Same join rule in GET report.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| Seller SKU written to Walmart must be one eShop offers (`ESHOP-{id}` for an existing catalog item) | `UpdateInventoryForSku`/`BulkUpdateInventory` ← eShop catalog (`IReadRepository<CatalogItem>`) | PUT `/api/walmart/stock/{sku}`: parse SKU → id, 404 if no such catalog item, before the Walmart call. Sync builds items only from catalog rows, so every SKU sent is one eShop offers. |
| `catalogItemId` in `POST /api/orders` must be an existing catalog item with enough stock | `POST /api/orders` ← eShop catalog | OrderService: load items by id, reject unknown id and any qty > `UnitsInStock` before any decrement/save |

⚠ No Walmart *list/search* call defines the legal SKU set — eShop's own catalog is the authority for
what it offers; Walmart's `GetInventorySummaries` is only read for the Flow-3 reconciliation report,
never as an allow-list.

## 3. Trap notes

- Step 2 (client/DI): the SDK `HttpClient`/handler must be long-lived & reused via
  `IHttpClientFactory`, client registered as a singleton — a per-request client leaks sockets.
  **MUST load `wm-v4:dotnet-client-initialization`.**
- Step 2 (auth): WalletAuth is OAuth2 client-credentials; a credential never set is silently skipped
  and the call still goes out unauthenticated, surfacing later as a confusing 401. **MUST load
  `wm-v4:dotnet-authentication`.**
- Step 2 (base-URL override & resilience): `Timeout` is per-attempt not total; `PUT` is retried by
  default while `POST` is not; base URL/server selection and what logging redacts are not what the
  option names suggest. **MUST load `wm-v4:dotnet-configuration-resilience`.**
- Step 3 (calling endpoints): every input rides one request record; whether a write carries a real
  idempotency key is on the record, and the injected `Idempotency-Key` header is not one. **MUST load
  `wm-v4:dotnet-calling-endpoints`.**
- Step 3 (models): `InventoryDetails`/`Pagination4` optional `int?`; enums are `OpenStringEnum<T>` not
  C# enums; unknown response fields live on an extension-data bag. **MUST load `wm-v4:dotnet-models`.**
- Step 3 (error boundary): a drifted 2xx or a non-2xx body not matching `{Operation}Error` surfaces as
  `ResponseDeserializationException` (an `ApiException`, NOT `ApiException<TError>`); a ladder catching
  only `ApiException<TError>` lets it escape. **MUST load `wm-v4:dotnet-error-handling`.**
- Step 6 (tests): the SDK's test seam is the `HttpClient` constructor argument; match the project's
  existing framework. **MUST load `wm-v4:dotnet-testing`.**

## 4. REQUIRED READING (load every one before implementation starts; this sheet omits their contents)

- `wm-v4:dotnet-client-initialization` — Step 2, constructing/registering `WalmartApIsClient`.
- `wm-v4:dotnet-authentication` — Step 2, setting WalletAuth client-credentials.
- `wm-v4:dotnet-configuration-resilience` — Step 2, BaseUrl override, timeout/retry, logging posture.
- `wm-v4:dotnet-calling-endpoints` — Step 3, calling `client.Inventory.*` operations.
- `wm-v4:dotnet-models` — Step 3, building/reading Inventory models.
- `wm-v4:dotnet-error-handling` — Step 3, the try/catch boundary (always required).
- `wm-v4:dotnet-testing` — Step 6, faking the SDK.

Hazard (verbatim): a body that does not match its declared type — a drifted/malformed **2xx**
(missing `required` member) or a **non-2xx** body not matching its operation's generated
`{Operation}Error` — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the
HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder
handling only `ApiException<TError>` lets it escape, so it must also catch
`ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `WalmartConfiguration` bound from `Walmart:` section. At startup (Program.cs, before `app.Run()`) assert `ClientId` and `ClientSecret` are both non-null and non-whitespace; throw → host refuses to start. Both parts checked (a blank part ≠ a missing one). `BaseUrl` optional. |
| 2 | Secret sourcing & rotation | Secret from .NET user-secrets (`Walmart:ClientSecret`), loaded via configuration (env vars `WALMART_CLIENT_ID`/`WALMART_CLIENT_SECRET` → user-secrets by me, never written to repo files). `AddWalmartApIsClient` builds options once at registration and captures them in the singleton, so a rotated secret takes effect only on process restart — acceptable; documented. |
| 3 | Total timeout budget | `Timeout` is per-attempt. Each inbound PublicApi call passes a `CancellationToken` (request-aborted) to the SDK; the whole-call budget is bounded by a `CancellationTokenSource` with a fixed deadline (e.g. 100s for sync which fans out, 30s per single call) linked to the request token, enforced in `WalmartInventoryService`. |
| 4 | Write-retry ownership | Default `HttpMethodsToRetry` = GET,HEAD,PUT,OPTIONS. `UpdateInventoryForSku` is PUT → retried by SDK. `BulkUpdateInventory` is POST → never retried by SDK. Both set absolute quantities (idempotent), so SDK retry of the PUT is safe; the POST sync we re-drive manually if needed (caller re-invokes). |
| 5 | Idempotency & ambiguous writes | `UpdateInventoryForSku`/`BulkUpdateInventory` request records carry NO caller-supplied idempotency key (only Sku/Body / Body). Both are absolute-set writes (set qty = N), inherently idempotent: a resend produces the same Walmart state, so no key is needed. The injected per-call `Idempotency-Key` GUID is NOT relied upon. Reconciliation path: GET `/api/walmart/stock` compares Walmart vs eShop to detect drift. |
| 6 | Observability | `ILogger<WalmartInventoryService>`: Information on each operation start/outcome with SKU and result status; Warning on per-SKU failure with Walmart error `code`/`message` and status code; no secret ever logged. SDK `LogRequestBody` left OFF. Correlation: read `ErrorList.Errors[].Code`/`Message` from typed errors and log them. |
| 7 | Sensitive data | Inventory request models carry only `sellerSku`, `quantity`, `shipNode`, `fulfillmentLag` — no PII/secrets. `LogRequestBody` stays off; no request body echoed by our own logs. Client secret is a credential, never in any body. `LoggerFactory` left to DI default (acceptable since no sensitive body field exists). |
| 8 | Environment selection | SDK has Production/Environment2(sandbox)/Environment3(mock). We keep `Environment = Production` (default) and, when `Walmart:BaseUrl` is set, rebase `options.Server.Default1.{Production}.BaseUrl` (and `Default`) to override scheme/host/port while preserving the contract path (`/seller/v1`). Inventory ops + WalletAuth token both resolve through `Default1` (`server.Default1("/auth/token")`), so one override covers operations AND token. No live Walmart here → the grader supplies a Walmart-compatible base via `Walmart:BaseUrl`; with no override we hit production (documented; only set in real prod). |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. Walmart writes are absolute-set/idempotent → duplicates converge, no claim store needed. `POST /api/orders` decrements stock → a duplicate could double-decrement; mitigated per row below. |
| 10 | Partial results | GET `/api/walmart/stock` walks ALL pages via `nextToken` until null — no cap, so no truncation flag needed. See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `PUT /api/walmart/stock/{sku}` (UpdateInventoryForSku) | none — absolute-set write, idempotent | n/a (resend sets same qty) | n/a | n/a |
| `POST /api/walmart/stock/sync` (BulkUpdateInventory) | none — absolute-set, idempotent | n/a | n/a | n/a |
| `POST /api/orders` (eShop order; decrements stock) | eShop `Order` row in `CatalogContext` (the store this codebase already uses) | no natural caller key exists → no store-level refusal of a semantic duplicate; stock floor (`UnitsInStock >= qty`, refuse over-draw) bounds damage | n/a (no idempotency key in task contract) | `OrderService.CreateOrderAsync` validation loop (throws `InsufficientStockException`) then `CatalogItem.RemoveStock` |

⚠ `POST /api/orders` has no caller-supplied idempotency key in the task contract, so two identical
submissions each create an order and each decrement stock. The task does not define an order-dedup
key; the stock floor prevents overselling below zero. Reconciliation (GET report) surfaces drift.

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `GET /api/walmart/stock` (GetInventorySummaries) | nothing — loop until `Pagination.NextToken` null/empty, aggregating all | not cut short: all pages are walked, so `items` is complete | `WalmartInventoryService.ListWalmartStockAsync` do/while over `nextToken` (1000-page hard backstop only) |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `UpdateInventoryForSku` | `GetInventoryForSku` | the `sku` | `WalmartInventoryService.SetStockAsync` catch → `WalmartApiException`; absolute-set is idempotent, so the next `GET /api/walmart/stock/{sku}` or sync re-asserts the intended value (reconciliation, not an in-catch re-read) | `WalmartInventoryServiceTests.GetStockAsync_ThrowsWalmartApiExceptionOnConnectionFailure` (transport failure translated, not reported as success) |
| `BulkUpdateInventory` (sync) | `GetInventorySummaries` | per-SKU `sellerSku` | `WalmartInventoryService.SyncCatalogAsync` chunk catch marks the chunk's SKUs `failed`; absolute-set is idempotent, so a re-run or the `GET /api/walmart/stock` report settles the true state | `WalmartInventoryServiceTests.SyncCatalogAsync_MarksAllFailedWhenBulkCallFails` |

⚠ Both Walmart writes are absolute-set/idempotent, so an ambiguous connection failure is settled by
re-reading (GetInventoryForSku / GetInventorySummaries) — the next GET or sync re-asserts the
intended value. On a connection failure (`SdkConnectionException`/`SdkTimeoutException`) the endpoint
reports the write as `failed`; because the write is idempotent, a caller re-drive is safe and the
reconciliation report shows the true state.

## 6. Assumptions & Blockers

- **No Blockers.** The map covers every capability (get/set per-SKU, bulk update, list summaries) and
  the BaseUrl override is achievable through `options.Server.Default1` (operations + WalletAuth token
  both route through Default1).
- Assumption: SKU format `ESHOP-{id}` with `{id}` the integer `CatalogItem.Id`; a SKU not matching
  this pattern, or whose id has no catalog item, is treated as "not an eShop SKU" (404 on the single-
  SKU operator endpoints; shown with `eshopQuantity: null` in the reconciliation report).
- Assumption: `POST /api/orders` buyer identity = authenticated username from the JWT; a minimal
  default shipping address is supplied since the task's order payload carries only item ids+quantities
  and the existing `Order` aggregate requires an address. Reuses `Order`/`OrderItem`/`CatalogItemOrdered`.
- Assumption (UNVERIFIED): read-back quantity field — handled defensively (`FulfillableQuantity ??
  OnHandQuantity ?? 0`).
