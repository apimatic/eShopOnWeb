# Walmart Marketplace stock integration — plan & contract sheet

Additive capability on the eShopOnWeb reference app: offer the catalog's stock on Walmart
Marketplace as a second sales channel. All capabilities are HTTP endpoints on `src/PublicApi`
(JWT). SKU for catalog item `N` is `ESHOP-{N}`.

## 1. Scope & sequence

| # | Step | Walmart operation(s) |
| --- | --- | --- |
| 1 | `WalmartSettings` bound from `Walmart:` section (`ClientId`, `ClientSecret`, `BaseUrl`); fail-fast validation at startup | — |
| 2 | Infrastructure `AddWalmartIntegration(config, services)` — named `HttpClient` + singleton `WalmartApIsClient` with `WalletAuth` creds; `Walmart:BaseUrl` rewrites authority of `Server.Default` + `Server.Default1` | — |
| 3 | eShop-side stock: new aggregate `CatalogStockEntry` (CatalogItemId, Quantity), DbSet on `CatalogContext`, EF config; absence ⇒ level 0 | — |
| 4 | `ICatalogStockService` (ApplicationCore) — set/get one level, enumerate all catalog items with levels, place order (reuse `Order`/`OrderItem`, decrement, refuse over-order) | — |
| 5 | `IWalmartMarketplaceService` (ApplicationCore iface, Infrastructure impl) wrapping the SDK: SetStock, GetStock, SyncCatalog, ListReconciliation | `UpdateInventoryForSku`, `GetInventoryForSku`, `GetInventorySummaries` |
| 6 | 6 PublicApi endpoints (MinimalApi.Endpoint style, mirroring `CreateCatalogItemEndpoint`) | — |

Flow→endpoint: Flow1 = `PUT`/`GET /api/walmart/stock/{sku}`; Flow2 = `POST /api/walmart/stock/sync` (loops `UpdateInventoryForSku` per catalog item, one PUT per SKU, so each SKU gets its own `updated`/`failed` status — my design choice over the async `BulkUpdateInventory` feed, which returns a feedId not per-SKU outcome); Flow3 = `GET /api/walmart/stock`. Plus `PUT /api/catalog-items/{id}/stock` and `POST /api/orders`.

Auth: all except `POST /api/orders` restricted to `Constants.Roles.ADMINISTRATORS`; `POST /api/orders` any signed-in user. Pattern from `CreateCatalogItemEndpoint.cs`.

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Each operation that takes input takes ONE request record as its first parameter, built with an object initializer whose property names are the record's own — never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies (taken from the path the map gives for THAT type), never from where a neighbouring type sits.

Controller accessor: `client.Inventory` (`WalmartApIs.Api.Inventory`). All three ops: **Server group `Default1`**, **Auth `options.WalletAuth`** (OAuth2 client credentials), **Case A typed error**.

| Op | Signature (request record · members) | Body model fields | Response envelope · fields read | Error accessors | Source |
| --- | --- | --- | --- | --- | --- |
| `UpdateInventoryForSku` | `UpdateInventoryForSku(UpdateInventoryForSkuRequest request, RequestOptions? = null, CancellationToken = default)` · `Sku: string, required`; `Body: UpdateInventoryRequest, required` | `UpdateInventoryRequest`: `Quantity (quantity): int, required`; `ShipNode (shipNode): string?`; `FulfillmentLag (fulfillmentLag): int?` | `UpdateInventoryResponse` · `SellerSku (sellerSku): string?`, `Quantity (quantity): int?` | `TryGetErrorList(out ErrorList)` [400,403,404,429,500] · `TryGetRawError(out RawError)` [fallback] | map/operations/Inventory.md; Requests/Inventory/UpdateInventoryForSkuRequest.cs; Models/UpdateInventoryRequest.cs; Models/UpdateInventoryResponse.cs |
| `GetInventoryForSku` | `GetInventoryForSku(GetInventoryForSkuRequest request, RequestOptions? = null, CancellationToken = default)` · `Sku: string, required`; `ShipNode: string?` | — (GET) | `InventorySummary` · `SellerSku (sellerSku): string, required`; `InventoryDetails (inventoryDetails): InventoryDetails?` → `OnHandQuantity (onHandQuantity): int?`, `FulfillableQuantity (fulfillableQuantity): int?` | `TryGetErrorList` [400,403,404,429,500] · `TryGetRawError` [fallback] | map/operations/Inventory.md; Requests/Inventory/GetInventoryForSkuRequest.cs; Models/InventorySummary.cs; Models/InventoryDetails.cs |
| `GetInventorySummaries` | `GetInventorySummaries(GetInventorySummariesRequest request, RequestOptions? = null, CancellationToken = default)` · `Skus: string?`; `ShipNode: string?`; `PageSize: int = 100` (`[Minimum(1)][Maximum(500)]`); `NextToken: string?` | — (GET) | `GetInventorySummariesResponse` · `InventorySummaries: IReadOnlyList<InventorySummary>?`; `Pagination: Pagination4?` → `NextToken (nextToken): string?`, `TotalCount (totalCount): int?` | `TryGetErrorList` [400,403,429,500] · `TryGetRawError` [fallback] | map/operations/Inventory.md; Requests/Inventory/GetInventorySummariesRequest.cs; Models/GetInventorySummariesResponse.cs; Models/Pagination4.cs |

Error payload `ErrorList` (`Models/ErrorList.cs`): `Errors (errors): IReadOnlyList<Error3>, required`. `Error3` (`Models/Error3.cs`): `Code (code): string, required`; `Message (message): string, required`; `Details (details): string?`. Read only `Code`/`Message` for a caller-safe message; never echo secrets.

**Pagination posture:** `GetInventorySummaries` has NO Pagination bullet on its map row ⇒ it is a plain call returning a single `GetInventorySummariesResponse`, NOT a `Pageable`. Paging is hand-driven via `NextToken` → see PAGED READS.

**Quantity read-back decision (`YOUR CALL — not in the map`):** `GetInventoryForSku`/summaries expose a breakdown, not a single "the quantity". We SET `UpdateInventoryRequest.Quantity` ("Declared on-hand quantity"), so the faithful read-back is `InventoryDetails.OnHandQuantity`; fall back to `FulfillableQuantity`, else `0`, to stay robust against a Walmart-compatible service that populates only one field.

**Client construction / auth / server:** `new WalmartApIsClient(httpClient, options)`. `options.WalletAuth = new WalmartApIs.Core.Authentication.OAuth2.ClientCredentials.OAuth2ClientCredentials { ClientId, ClientSecret }` (Inventory's declared scheme). Token endpoint is `server.Default1("/auth/token")` i.e. `{Default1.BaseUrl}/auth/token` — **derived from the Default1 base URL**, so overriding `options.Server.Default1.{Env}.BaseUrl` rewrites both operation calls AND token acquisition (source: `AuthSchemes.cs`, `Server.cs`, `Servers/Default1Options.cs`). Default env = `Production`. `ServerEnvironment` in `WalmartApIs.Servers`.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| Sync pushes a level for every eShop catalog item; the SKU set is defined by the eShop catalog (`ESHOP-{id}`), not by any Walmart list — Walmart accepts any seller SKU on `UpdateInventoryForSku`, so there is no legal-set to validate against | `UpdateInventoryForSku` ← eShop `IReadRepository<CatalogItem>.ListAsync` | SKUs built from the catalog in `SyncCatalogAsync` before each PUT |
| Reconcile lines Walmart SKUs up against eShop catalog ids by parsing `ESHOP-{id}`; a Walmart SKU not matching ⇒ `eshopQuantity = null`; an eShop item with no Walmart summary ⇒ `walmartQuantity = null` | `GetInventorySummaries` + eShop catalog | union built in `ListReconciliationAsync` |

(No caller-supplied value is constrained by a Walmart *list/search* operation: the sku path param on PUT/GET is accepted by Walmart as-is, and the caller-supplied catalog item ids on `POST /api/orders` are validated against the **eShop** catalog, not Walmart.)

## 3. Trap notes

- Client/DI lifetime + singleton stale-DNS: register one long-lived `WalmartApIsClient` over a named `HttpClient` with `PooledConnectionLifetime`; a per-request client re-fetches the OAuth token every call. **MUST load wm-v4:dotnet-client-initialization.**
- Credentials applied silently: an unset `WalletAuth` sends no credential and fails one round-trip later as 401; validate at startup. **MUST load wm-v4:dotnet-authentication.**
- Timeout is per-attempt not total, and `PUT` IS retried by default (its per-attempt timeout is itself a retry trigger): bound the whole call with a `CancellationToken` deadline. **MUST load wm-v4:dotnet-configuration-resilience.**
- Hand-driven `NextToken` page loop has no provider-independent stop: needs a page cap and must surface truncation in the result, not a log. **MUST load wm-v4:dotnet-configuration-resilience.**
- Enum `Condition` on `InventorySummary` is an open string-enum, not a C# enum (we don't read it, but don't `ToString()` any enum onto the wire). **MUST load wm-v4:dotnet-models.**
- Error boundary: Case A typed ladder (`TryGetErrorList` then `TryGetRawError` last), plus `ResponseDeserializationException`/`SdkConnectionException`/`SdkTimeoutException`/`AuthSchemeException` arms; a drifted body escapes a typed-only ladder. **MUST load wm-v4:dotnet-error-handling.**
- Test seam is the `HttpClient` constructor arg + fake handler; match the repo's xUnit stack. **MUST load wm-v4:dotnet-testing.**

## 4. REQUIRED READING (load before implementation; contents deliberately not copied here)

- `wm-v4:dotnet-client-initialization` — client construction & DI registration (step 2).
- `wm-v4:dotnet-authentication` — WalletAuth credentials & startup fail-fast (steps 1–2).
- `wm-v4:dotnet-calling-endpoints` — building the three request records (step 5).
- `wm-v4:dotnet-models` — request/response model & open-enum handling (step 5).
- `wm-v4:dotnet-configuration-resilience` — timeouts, PUT retry, base-URL override, hand-driven paging (steps 2, 5).
- `wm-v4:dotnet-error-handling` — the integration error boundary (step 5). Always required.
- `wm-v4:dotnet-testing` — tests for the integration layer (step 6/tests).

⚠ Hazard (verbatim): a body that does not match its declared type — a drifted/malformed **2xx** (a missing `required` member) or a **non-2xx** body that does not match the operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** `ApiException<TError>`; a ladder handling only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `WalmartSettings` bound from `Walmart:` section; `AddOptions<WalmartSettings>().Bind(...).Validate(...).ValidateOnStart()` refuses to boot when `ClientId` **or** `ClientSecret` is missing/blank (both parts checked). `BaseUrl` optional. Message names the key, never the value. |
| 2 | Secret sourcing & rotation | Values come from .NET user-secrets (populated by me from `WALMART_CLIENT_ID`/`WALMART_CLIENT_SECRET` env vars) via the `Walmart` config section; never written to any repo file. DI builds the `OAuth2ClientCredentials` once at registration and captures it in the singleton client ⇒ a rotated secret needs a process restart (acceptable; documented). |
| 3 | Total timeout budget | Caller-facing budget enforced by a linked `CancellationTokenSource` (`RequestAborted` + `CancelAfter(budget)`) in the Walmart service; `options.Retry.Timeout` set explicitly (per-attempt) so a hung PUT cannot cost the 100s×4 default. Sync bounds the whole handler, not each PUT. |
| 4 | Write-retry ownership | Only write in scope is `UpdateInventoryForSku` (**PUT**, in the default `HttpMethodsToRetry`) ⇒ SDK may resend it. Safe: it is an **absolute set** of the quantity, so a resend writes the identical value. GETs retried, idempotent. |
| 5 | Idempotency & ambiguous writes | `UpdateInventoryForSku` declares no caller-supplied idempotency key (request record = `Sku` + `Body`; the injected `Idempotency-Key` header is `Guid.NewGuid()`, not a key). Not needed: absolute-set PUT is idempotent by value. Reconciliation path = `GetInventoryForSku`. |
| 6 | Observability | SDK built-in logger via DI `ILoggerFactory` at `Information` (request/response line, retries at `Warning`, failure at `Error`). `LogRequestBody` left **off**. On SDK errors we log status + `Error3.Code`/`Message` (the provider's correlation signal); never the secret or token. |
| 7 | Sensitive data | Request bodies carry only `quantity`/`shipNode`/`fulfillmentLag` — no PII/secret. `LogRequestBody` stays off and `LoggerFactory` is assigned explicitly (via DI) so `WALMARTAPISCLIENT_LOG` cannot switch body logging on from outside. |
| 8 | Environment selection | SDK envs: Production / Environment2 (sandbox) / Environment3 (mock). We keep `Production` and rely on `Walmart:BaseUrl` to point every call (incl. token) at the Walmart-compatible service supplied at run time; with no override, calls go to the contract's production hosts. No live Walmart is reachable while building — tests use the `HttpClient` stub seam, never a live call. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS — the only provider write is an absolute-set PUT, harmless on a duplicate. `POST /api/orders` is a **local DB** write (never calls Walmart); its stock decrement runs in one `SaveChanges` on the per-host in-memory store. |
| 10 | Partial results | See PAGED READS — `ListReconciliationAsync` caps the `NextToken` loop and throws `WalmartIntegrationException` if the cap is hit rather than silently returning a partial reconcile (the report must cover all). |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES — a connection failure on `SetStockAsync` re-reads via `GetInventoryForSku` to settle; in sync, a failed SKU is reported `failed` and the next sync re-sets it (idempotent). |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| `UpdateInventoryForSku` (set stock on Walmart) | N/A — absolute-set PUT is idempotent by value (option 1 in config-resilience: a duplicate writes the identical quantity, so "at most one write" is not required) | N/A | N/A | `WalmartMarketplaceService.SetStockAsync` / `SyncCatalogAsync` |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `GetInventorySummaries` (reconcile) | `MaxPages` const (provider-independent) + no-progress guard (NextToken unchanged/empty) | throws `WalmartIntegrationException` (502) at the cap — never a silent partial, since the report must cover every SKU | `WalmartMarketplaceService.ListAllStockAsync` (`do/while` over `NextToken`; the `MaxPages` and no-progress guards throw) |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `UpdateInventoryForSku` | `GetInventoryForSku` | the `sku` just written | `WalmartMarketplaceService.SetStockAsync` catch `SdkConnectionException` → `SettleUnknownSetAsync` (re-reads via `GetInventoryForSku`; if Walmart records the intended quantity the set is reported applied, else `503` unknown) | `WalmartMarketplaceServiceTests.SetStock_connection_failure_then_reread_confirms_is_settled_as_applied` (stub throws `HttpRequestException` on the PUT, returns the quantity on the re-read GET) |

## 6. Assumptions & Blockers

- **Assumption:** `POST /api/orders` carries catalog item ids + quantities only (no shipping address); I build the required `Order.ShipToAddress` with placeholder values since the existing `Order` model requires an `Address`. BuyerId = caller identity (JWT `ClaimTypes.Name`).
- **Assumption:** order creation reuses the `Order`/`OrderItem` public constructors + `IRepository<Order>` directly (the existing `IOrderService.CreateOrderAsync(basketId, …)` requires a basket, which `POST /api/orders` does not supply); `CatalogItemOrdered` snapshot built from the catalog lookup. This reuses the existing order aggregate, not a parallel model.
- **Assumption:** eShop stock lives in a new `CatalogStockEntry` row in `CatalogContext`; with the in-memory provider it survives only within a run (per the task's caveat). An item with no row = level 0.
- No Walmart-contract gap: Inventory exposes set (PUT), get (GET) and list (GET) — all three flows are covered by map operations. No Blockers.
