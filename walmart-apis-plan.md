# walmart-apis-plan.md — eShopOnWeb × Walmart Marketplace inventory integration

## 1. Scope & sequence

Additive capability: tell Walmart how much of the eShop catalog the shop offers, and compare the two sides.
SKU for catalog item `N` is `ESHOP-{N}`.

| # | Step | Walmart operation(s) |
| --- | --- | --- |
| 1 | Domain: add stock level to `CatalogItem` (`AvailableStock`, default 0; `SetStockLevel`, `RemoveStock`). EF config + in-memory. | — |
| 2 | Walmart options (`Walmart:ClientId/ClientSecret/BaseUrl`), fail-fast validation, user-secrets sourcing. | — |
| 3 | SDK client registration over a named `HttpClient` + a `WalmartBaseUrlHandler` (`DelegatingHandler`) that rewrites scheme/host/port of **every** outgoing request (API calls **and** the OAuth token fetch) when `Walmart:BaseUrl` is set, preserving everything after the host. | — |
| 4 | `IWalmartInventoryService` wrapping the SDK: set one SKU, get one SKU, sync all, list-and-reconcile. Error boundary → `WalmartIntegrationException`. | `UpdateInventoryForSku`, `GetInventoryForSku`, `GetInventorySummaries` |
| 5 | PublicApi endpoints (MinimalApi.Endpoint pattern): `PUT/GET /api/walmart/stock/{sku}`, `POST /api/walmart/stock/sync`, `GET /api/walmart/stock` (admin); `PUT /api/catalog-items/{id}/stock` (admin); `POST /api/orders` (any signed-in shopper). | via step 4 |
| 6 | Tests: unit tests faking the SDK `HttpClient` seam (SKU mapping, BaseUrl rewrite incl. token, pagination, sync per-SKU status, reconcile, unknown-outcome re-read); integration tests through `WebApplicationFactory` with a stubbed Walmart handler + in-memory DB. | — |

Walmart server group for all Inventory ops: `Default1` (`…/seller/v1`). Auth: `options.WalletAuth` (OAuth2 client
credentials; token from `…/seller/v1/auth/token`). Default environment `Production`; the BaseUrl handler is the
sole base-URL control (it must also redirect the absolute token URL, which `options.Server` cannot reach).

## 2. CONTRACT SHEET

⚠ Signatures are generated code, verbatim: every operation that takes input takes ONE request record as its
first parameter, built with an object initializer whose property names are the record's own (never flat
arguments). ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from
the path the map gives for THAT type.

| Operation | Ctrl | Signature / request record (member: type, required?) | Body model fields (wire) | Response envelope → fields read | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `UpdateInventoryForSku` | `client.Inventory` | `UpdateInventoryForSku(UpdateInventoryForSkuRequest {Sku: string, req; Body: UpdateInventoryRequest, req})` | `UpdateInventoryRequest`: `Quantity (quantity): int, req` · `ShipNode (shipNode): string?` · `FulfillmentLag (fulfillmentLag): int?` | `UpdateInventoryResponse`: `SellerSku (sellerSku): string?` · `Quantity (quantity): int?` · `UpdatedAt (updatedAt): DateTimeOffset?` | Case A `ApiException<UpdateInventoryForSkuError>`: `TryGetErrorList(out ErrorList)` [400,403,404,429,500] · `TryGetRawError(out RawError)` | none | map/operations/Inventory.md; Requests/Inventory/UpdateInventoryForSkuRequest.cs; Models/UpdateInventoryRequest.cs; Models/UpdateInventoryResponse.cs |
| `GetInventoryForSku` | `client.Inventory` | `GetInventoryForSku(GetInventoryForSkuRequest {Sku: string, req; ShipNode: string?})` | — (GET) | `InventorySummary`: `SellerSku (sellerSku): string, req` · `InventoryDetails (inventoryDetails): InventoryDetails?` → `InventoryDetails.OnHandQuantity (onHandQuantity): int?` | Case A `ApiException<GetInventoryForSkuError>`: `TryGetErrorList(out ErrorList)` [400,403,404,429,500] · `TryGetRawError(out RawError)` | none | Inventory.md; Requests/Inventory/GetInventoryForSkuRequest.cs; Models/InventorySummary.cs; Models/InventoryDetails.cs |
| `GetInventorySummaries` | `client.Inventory` | `GetInventorySummaries(GetInventorySummariesRequest {Skus: string?; ShipNode: string?; PageSize: int = 100; NextToken: string?})` | — (GET) | `GetInventorySummariesResponse`: `InventorySummaries (inventorySummaries): IReadOnlyList<InventorySummary>?` · `Pagination (pagination): Pagination4?` → `Pagination4.NextToken (nextToken): string?` | Case A `ApiException<GetInventorySummariesError>`: `TryGetErrorList(out ErrorList)` [400,403,429,500] · `TryGetRawError(out RawError)` | manual: feed `NextToken` from response; no SDK `Pageable` | Inventory.md; Requests/Inventory/GetInventorySummariesRequest.cs; Models/GetInventorySummariesResponse.cs; Models/Pagination4.cs |

`ErrorList` (typed error payload) read for message text only; identity carried = `ex.StatusCode` + the error-list body string. Source: Models/ErrorList.cs (read at implementation).

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The seller SKU written to Walmart must correspond to a value this app defines — it is always `ESHOP-{catalogItemId}`, minted by this app from its own catalog, never a free value a caller invents. | `UpdateInventoryForSku` ← eShop catalog (`IReadRepository<CatalogItem>`) | sync mints SKUs from catalog ids; single-SKU `PUT` parses `ESHOP-{id}` and the store of record for the number set is Walmart itself (absolute set), so no cross-set membership check is required beyond SKU-format validation. |

Note: Walmart is the system of record for a SKU's quantity (set is absolute), so the only "legal set" a write
depends on is the SKU name space this app owns. There is no list/search call whose result constrains a
quantity value.

## 3. Trap notes

- Step 3 — the per-attempt `Timeout` is not a call budget and `PUT` **is** retried by default; a hung call costs a multiple of the knob. **MUST load wm-v4:dotnet-configuration-resilience.**
- Step 4 — `GetInventorySummaries` is hand-driven paging (no `Pageable`); "no next token" is a provider-supplied stop, not a bound, and a truncated result that looks complete is a wrong answer. **MUST load wm-v4:dotnet-configuration-resilience.**
- Step 4 — reading `InventorySummary`/`InventoryDetails`: optional members are `null`, enums are `OpenStringEnum`, response may omit the quantity. **MUST load wm-v4:dotnet-models.**
- Step 4 — a drifted 2xx or a non-2xx body not matching `{Operation}Error` surfaces as `ResponseDeserializationException` (an `ApiException`, not `ApiException<TError>`); a typed-only catch ladder lets it escape. Token-endpoint credential failure surfaces as `AuthSchemeException`, not `ApiException`. **MUST load wm-v4:dotnet-error-handling.**
- Step 4 — a transport failure on the `PUT` write leaves the outcome unknown (bytes may have landed). **MUST load wm-v4:dotnet-configuration-resilience.**
- Step 6 — the SDK test seam is the `HttpClient` the client is constructed over; assert the request the SDK actually built. **MUST load wm-v4:dotnet-testing.**

## 4. REQUIRED READING (load before implementation starts; this sheet deliberately omits their contents)

- `wm-v4:dotnet-client-initialization` — step 3 (client/DI, HttpClient lifetime). *loaded*
- `wm-v4:dotnet-authentication` — step 2/3 (WalletAuth client-credentials, fail-fast). *loaded*
- `wm-v4:dotnet-calling-endpoints` — step 4 (request records, reading responses). *loaded*
- `wm-v4:dotnet-models` — step 4 (InventorySummary/InventoryDetails/enums/nullability). *loaded*
- `wm-v4:dotnet-error-handling` — step 4 error boundary (always required). *loaded*
- `wm-v4:dotnet-configuration-resilience` — step 3/4 (retry/timeout/base-URL/manual paging/unknown outcome). *loaded*
- `wm-v4:dotnet-testing` — step 6. *loaded*

Hazard (verbatim): a body that does not match its declared type — a drifted **2xx** (missing `required`
member) or a **non-2xx** body not matching the operation's `{Operation}Error` shape — surfaces as
`ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but
is **not** `ApiException<TError>`; the catch ladder must also catch `ResponseDeserializationException` (or
`ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `WalmartSettings` bound from `Walmart:` with `[Required]` on `ClientId`/`ClientSecret`; `.ValidateDataAnnotations().ValidateOnStart()`. Both parts checked (a blank part ≠ missing). `BaseUrl` optional. Host refuses to boot on missing/blank. |
| 2 | Secret sourcing & rotation | Values come from env `WALMART_CLIENT_ID`/`WALMART_CLIENT_SECRET`, loaded by the operator into .NET user-secrets as `Walmart:ClientId`/`Walmart:ClientSecret` (never written to any repo file). Options built once at registration → captured in the singleton SDK client; a rotated secret takes effect on process restart. No hot-rotation required. |
| 3 | Total timeout budget | `options.Retry.Timeout = 30s` (per attempt). A per-request `CancellationToken` deadline links `HttpContext.RequestAborted` with a 60s ceiling in the service (`Bounded` helper), bounding the whole call incl. sync's loop of writes. |
| 4 | Write-retry ownership | Only write in scope is `UpdateInventoryForSku` = `PUT`, which **is** in the default `HttpMethodsToRetry`. Walmart inventory set is an **absolute** quantity set (idempotent) so a resend is harmless; retries left at default. `GET`s retry safely. |
| 5 | Idempotency & ambiguous writes | `UpdateInventoryForSku` declares no caller idempotency key on its request record (only `Sku`/`Body`); the injected `Idempotency-Key` header is `Guid.NewGuid()` and is not a key. Not needed: the write is an absolute set, so re-applying the same value is a no-op. Reconciliation path = `GET /api/walmart/stock`. |
| 6 | Observability | SDK logger wired from host `ILoggerFactory` (set explicitly). Request line `Info`, failures `Warning`/`Error`. `LogRequestBody` left OFF. On a Walmart error the `ErrorList` body text + `ex.StatusCode` are logged; client secret never logged. |
| 7 | Sensitive data | Request models in scope (`UpdateInventoryRequest`: quantity/shipNode/fulfillmentLag) carry no PII/secret. `LogRequestBody` stays off and `LoggerFactory` is assigned explicitly so `WALMARTAPISCLIENT_LOG` cannot switch body logging on from outside. |
| 8 | Environment selection | One deployment target. `options.Environment = Production` always; the `Walmart:BaseUrl` handler redirects every call (incl. token) to the override host when set. No live Walmart reachable here, so tests stub the `HttpClient` handler — no traffic to the real host. The grader points `Walmart:BaseUrl` at a compatible service. |
| 9 | Duplicate prevention under concurrency | `none` for Walmart writes: absolute-set `PUT` is idempotent, so two concurrent syncs/sets converge to the same value. `POST /api/orders` intentionally creates a new order per call (matches eShop's existing non-idempotent order model; task declares no idempotency key); not a duplicate-claim row. |
| 10 | Partial results | `GET /api/walmart/stock` pages `GetInventorySummaries` with a page cap (`MaxPages`); when the cap is hit the response carries `partial = true` so the caller sees the set was cut short (not only a log line). |
| 11 | Unknown outcomes | On `SdkConnectionException`/`SdkTimeoutException` from the single-SKU `PUT`, the service re-reads via `GetInventoryForSku` and settles the outcome (success if Walmart now holds the intended quantity) instead of reporting a bare failure. In sync, a per-SKU failure is marked `failed` and the catalog-wide `GET /api/walmart/stock` report is the sweep. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `UpdateInventoryForSku` (set/sync) | n/a — absolute quantity set, naturally idempotent | n/a (second call re-applies same value, no effect) | n/a | n/a |
| `POST /api/orders` | n/a — each order is deliberately a new aggregate (eShop model); no idempotency key in task scope | n/a | n/a | n/a |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `GetInventorySummaries` (reconcile) | `MaxPages` (50) cap + per-request `Bounded` deadline | `partial: true` on the `GET /api/walmart/stock` response body | `WalmartInventoryService.GetStockComparisonAsync` paging loop sets `partial`; surfaced as `WalmartStockReportResponse.Partial` |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `UpdateInventoryForSku` (single-SKU PUT) | `GetInventoryForSku` | the same `Sku` | `WalmartInventoryService.SetStockAsync` `catch (SdkConnectionException)` / `catch (SdkTimeoutException)` → `SettleUnknownAsync` (re-reads and settles) | `WalmartStockEndpointsTests.SetStock_TransportFailureButWriteLanded_SettlesAsSuccess` and `…AndWriteDidNotLand_SurfacesAsBadGateway` |

## 6. Assumptions & Blockers

- Assumption (minor): "the SKU's quantity Walmart records" maps to `InventoryDetails.OnHandQuantity` — the
  set operation's `Quantity` is documented "Declared on-hand quantity", so on-hand is the symmetric read.
  `null` on-hand is reported as `0` for the single-SKU GET and compared as the Walmart side in the report.
- Assumption (minor): sync pushes one `UpdateInventoryForSku` per catalog item (clean per-SKU `updated`/`failed`
  status) rather than `BulkUpdateInventory`; "how this application talks to Walmart is your call."
- No blockers. The wm-v4 plugin covers every Walmart capability needed (set one, get one, list all).

## 7. Source labels — every contract row above cites a map page or map-named source file. Application-shape
decisions (SKU scheme, sync-by-loop, OnHandQuantity mapping, partial flag, order non-idempotency) are
`YOUR CALL — not in the map` and decided here against the task.
