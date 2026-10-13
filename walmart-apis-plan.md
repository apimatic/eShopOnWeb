# Walmart Marketplace stock integration — plan

Add an **additive** capability to eShopOnWeb: keep Walmart Marketplace told how much of the
catalog the shop has to sell. SKU for catalog item `N` is `ESHOP-{N}`. All HTTP surface lives on
`src/PublicApi` (JWT). Every Walmart interaction goes through the **wm-v4** SDK (`WalmartApIs`,
`client.Inventory`).

## 1. Scope & sequence

| Step | What | Walmart op(s) |
| --- | --- | --- |
| A | Add `AvailableStock` to `CatalogItem` (domain) + EF mapping; mutators `SetStock`/`RemoveStock` | — |
| B | `WalmartSettings` (ClientId/ClientSecret/BaseUrl) bound from `Walmart:` with fail-fast; `BaseUrl` rewriter helper | — |
| C | Custom `IOAuth2TokenStrategy<OAuth2ClientCredentials>` for `WalletAuth` so the token host honors `Walmart:BaseUrl` | — (token endpoint) |
| D | `WalmartApIsClient` DI registration (named HttpClient, singleton, server BaseUrl override, retry/timeout, explicit LoggerFactory) | — |
| E | `IWalmartInventoryService` + impl: SetStock / GetStock / Sync / ListAll — wraps SDK, translates errors to `WalmartIntegrationException`, bounds each handler with a deadline | `UpdateInventoryForSku`, `GetInventoryForSku`, `BulkUpdateInventory`, `GetInventorySummaries` |
| F | PublicApi endpoints (see §surface) | via E |
| G | Tests: domain unit tests; service tests (SDK stub `HttpMessageHandler`); PublicApi integration tests (admin/role gating, in-memory DB) | — |

### HTTP surface (all `IEndpoint<...>`, auto-registered via `AddEndpoints()`/`MapEndpoints()`)

| Method & route | Role | Body | Top-level response fields |
| --- | --- | --- | --- |
| `PUT /api/catalog-items/{catalogItemId}/stock` | admin | `{quantity}` | `catalogItemId`, `quantity` (eShop level) |
| `POST /api/orders` | any signed-in shopper | `{items:[{catalogItemId,quantity}]}` | `orderId` |
| `PUT /api/walmart/stock/{sku}` | admin | `{quantity}` | `sku`, `quantity` |
| `GET /api/walmart/stock/{sku}` | admin | — | `sku`, `quantity` |
| `POST /api/walmart/stock/sync` | admin | — | `results:[{sku,status}]` (`updated`\|`failed`) |
| `GET /api/walmart/stock` | admin | — | `items:[{sku,walmartQuantity,eshopQuantity}]` (+ `truncated`) |

Admin attribute (copied verbatim from `CreateCatalogItemEndpoint`):
`[Authorize(Roles = BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS, AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]`.
`POST /api/orders` uses `[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]` (no role).

### Flow semantics
- **PUT /api/walmart/stock/{sku}** → `UpdateInventoryForSku(Sku, Body{Quantity})`. Sets Walmart's record; does **not** touch eShop level.
- **GET /api/walmart/stock/{sku}** → `GetInventoryForSku(Sku)` → read quantity from `InventorySummary.InventoryDetails` (see quantity-read decision below). Live read from Walmart, not a local copy.
- **POST /api/walmart/stock/sync** → read **every** `CatalogItem` (unset level = 0), chunk into batches of ≤500, `BulkUpdateInventory(Body{Items:[{SellerSku,Quantity}]})` per batch. One `results` entry per SKU sent: `updated` if the SKU is in `BulkUpdateInventoryResponse.Succeeded`, else `failed` (in `Errors`, or unreported).
- **GET /api/walmart/stock** → walk `GetInventorySummaries` pages (hand-driven on `nextToken`, page-capped); **union** Walmart SKUs with all eShop catalog SKUs (`ESHOP-{id}`); `walmartQuantity`/`eshopQuantity` null on the side lacking that SKU.
- **PUT /api/catalog-items/{id}/stock** → `CatalogItem.SetStock(quantity)`; **no** Walmart call. **POST /api/orders** → build `Order`/`OrderItem` from the app's existing aggregate, decrement each item's eShop stock (`RemoveStock`), refuse the whole order if any line exceeds stock; **no** Walmart call. Walmart learns new levels at the next sync.

## 2. CONTRACT SHEET

⚠ Signatures are generated code, verbatim: each operation that takes input takes **one request record** as its first parameter, built with an object initializer whose property names are the record's own (never flat args).
⚠ Every SDK type is written fully-qualified with the namespace its source path implies (taken from that type's own path), never from a neighbour's.

Accessor: `client.Inventory` · all four ops: server group **`Default1`** (`…/seller/v1`), auth **`options.WalletAuth`**, error **Case A (typed)**.

| Op | Signature (req record · members) | Body model · fields | Response envelope · fields read | Error accessors | Verb/route | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `UpdateInventoryForSku` | `UpdateInventoryForSkuRequest` · `Sku: string (req)`, `Body: UpdateInventoryRequest (req)` | `UpdateInventoryRequest` · `Quantity (quantity): int (req, ≥0)`, `ShipNode?`, `FulfillmentLag?` | `UpdateInventoryResponse` · `SellerSku?`, `Quantity?` | `TryGetErrorList(out ErrorList)` [400,403,404,429,500] · `TryGetRawError(out RawError)` | PUT `/inventory/v4/items/{sku}` | map Inventory.md; Requests/Inventory/UpdateInventoryForSkuRequest.cs; Models/UpdateInventoryRequest.cs; Models/UpdateInventoryResponse.cs |
| `GetInventoryForSku` | `GetInventoryForSkuRequest` · `Sku: string (req)`, `ShipNode?` | — | `InventorySummary` · `SellerSku`, `InventoryDetails?` → `OnHandQuantity? / FulfillableQuantity?` | `TryGetErrorList` [400,403,404,429,500] · `TryGetRawError` | GET `/inventory/v4/items/{sku}` | Models/InventorySummary.cs; Models/InventoryDetails.cs |
| `BulkUpdateInventory` | `BulkUpdateInventoryOperationRequest` · `Body: BulkUpdateInventoryRequest (req)` | `BulkUpdateInventoryRequest` · `Items (items): IReadOnlyList<BulkInventoryItem> (req, 1..500)`; item `BulkInventoryItem` · `SellerSku (sellerSku): string (req)`, `Quantity (quantity): int (req,≥0)`, `ShipNode?`, `FulfillmentLag?` | `BulkUpdateInventoryResponse` · `Succeeded?: IReadOnlyList<UpdateInventoryResponse>` (`.SellerSku`), `Errors?: IReadOnlyList<BulkItemError>` (`.SellerSku`,`.Message`) | `TryGetErrorList` [400,403,429,500] · `TryGetRawError` | POST `/inventory/v4/items/bulk` | Models/BulkUpdateInventoryRequest.cs; Models/BulkInventoryItem.cs; Models/BulkUpdateInventoryResponse.cs; Models/BulkItemError.cs |
| `GetInventorySummaries` | `GetInventorySummariesRequest` · `Skus?`, `ShipNode?`, `PageSize: int = 100 (1..500)`, `NextToken?` | — | `GetInventorySummariesResponse` · `InventorySummaries?: IReadOnlyList<InventorySummary>`, `Pagination?: Pagination4` (`.NextToken?`, `.TotalCount?`) | `TryGetErrorList` [400,403,429,500] · `TryGetRawError` | GET `/inventory/v4/items` | Models/GetInventorySummariesResponse.cs; Models/Pagination4.cs |

`ErrorList` · `Errors: IReadOnlyList<Error3> (req)`; `Error3` · `Code (code): string (req)`, `Message (message): string (req)`, `Details?`. Source: Models/ErrorList.cs, Models/Error3.cs.

**Client / auth / server construction**
- Client: `new WalmartApIsClient(HttpClient, WalmartApIsClientOptions)`. Source: WalmartApIsClient.cs.
- Auth: Inventory ops use `options.WalletAuth = new OAuth2ClientCredentials { ClientId, ClientSecret }` (namespace `WalmartApIs.Core.Authentication.OAuth2.ClientCredentials`). Default token URL `https://marketplace.walmart.com/seller/v1/auth/token` (HTTP Basic `base64(clientId:clientSecret)`, form `grant_type=client_credentials`). Caching done by the SDK's `OAuth2Scheme` around whatever strategy is set.
- Token-host override: `options.WalletAuthTokenStrategy = <custom IOAuth2TokenStrategy<OAuth2ClientCredentials>>` (namespace `WalmartApIs.Core.Authentication.OAuth2`; token model `OAuthToken`, members `AccessToken`/`TokenType`/`ExpiresIn?`/`Scope?`). YOUR CALL to supply it (documented extension point), required to satisfy the `Walmart:BaseUrl`-covers-token mandate.
- Server: `options.Environment = ServerEnvironment.Production` (default). Override point `options.Server.Default.Production.BaseUrl` (default `…/seller`) and `options.Server.Default1.Production.BaseUrl` (default `…/seller/v1`). Source: ServerOptions.cs, Servers/Default1Options.cs, Servers/DefaultOptions.cs.

### Quantity-read decision — `YOUR CALL — not in the map`
`UpdateInventoryRequest.Quantity` is the "Declared on-hand quantity"; `InventorySummary` returns a nested `InventoryDetails` with several quantity fields and no single echoed scalar. Read Walmart's recorded quantity as `InventoryDetails?.OnHandQuantity ?? InventoryDetails?.FulfillableQuantity` — prefer on-hand (the field our set value maps to), fall back to fulfillable, `null` if neither present. One helper, used by both GET `/api/walmart/stock/{sku}` and GET `/api/walmart/stock`.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| none — no value a caller supplies to one Walmart op must be drawn from the legal set defined by another Walmart op. `sku` on PUT/GET `/api/walmart/stock/{sku}` is free-form (any `ESHOP-{id}`); sync derives SKUs from the eShop catalog, not from a Walmart list; the Flow-3 report is read-only. | n/a | n/a |

The `{catalogItemId}` a caller passes to PUT `/api/catalog-items/{id}/stock` and to `POST /api/orders` must be an existing `CatalogItem` — enforced by `IRepository<CatalogItem>.GetByIdAsync`/spec returning null → 404/400. This is an eShop-store invariant, not a Walmart cross-op one.

## 3. Trap notes
- E/D error boundary — a drifted **2xx** body or an error body that doesn't match `{Operation}Error` surfaces as `ResponseDeserializationException` (an `ApiException`, **not** `ApiException<TError>`); a typed-only catch ladder lets it escape. `MUST load dotnet-error-handling`.
- E — `GetInventorySummaries` is **not** SDK-paginated; a hand-driven `nextToken` loop can spin forever on a provider that never clears `nextToken`. `MUST load dotnet-configuration-resilience`.
- D/E — `options.Retry.Timeout` is per-attempt, and `PUT`/GET are retryable so a hung call costs a multiple; only a `CancellationToken` deadline bounds the whole call. `MUST load dotnet-configuration-resilience`.
- D — a hand-built client leaves logging to the `WALMARTAPISCLIENT_LOG` env var unless `LoggerFactory` is set explicitly; `LogRequestBody` would log bodies unredacted. `MUST load dotnet-configuration-resilience`.
- C — token strategy semantics (Basic-auth token request, caching wrapper, `expires_in` absence) must match the SDK's. `MUST load dotnet-authentication`.

## 4. REQUIRED READING (load before implementation)
- `wm-v4:dotnet-client-initialization` — step D (client construction, HttpClient lifetime, DI).
- `wm-v4:dotnet-authentication` — step C (WalletAuth, custom token strategy, fail-fast).
- `wm-v4:dotnet-calling-endpoints` — step E (request records, reading responses).
- `wm-v4:dotnet-models` — step E (plain records/enums; no unions in scope — minimal).
- `wm-v4:dotnet-error-handling` — step E (Case A ladder, `ResponseDeserializationException`, connection faults). Always required.
- `wm-v4:dotnet-configuration-resilience` — steps D/E (retry, per-attempt vs total timeout, hand-driven pagination bound, logging redaction).
- `wm-v4:dotnet-testing` — step G (HttpMessageHandler seam).

Hazard row (verbatim): a body that does not match its declared type — a drifted/malformed **2xx** (missing `required` member) or a **non-2xx** body not matching the operation's `{Operation}Error` — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** `ApiException<TError>`; a ladder handling only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

These skills are loaded before implementation; this sheet deliberately does not carry their contents.

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `WalmartSettings` bound from `Walmart:` with `[Required]` on `ClientId` and `ClientSecret`; `AddOptions<WalmartSettings>().Bind(...).ValidateDataAnnotations().ValidateOnStart()` in `Program.cs`. Both parts checked (blank ≠ missing). Host refuses to start if either is missing/blank. `BaseUrl` optional. |
| 2 | Secret sourcing & rotation | Secrets come from `Walmart:ClientId`/`Walmart:ClientSecret` loaded into **.NET user-secrets** from env (`WALMART_CLIENT_ID`/`WALMART_CLIENT_SECRET`). `WalmartApIsClient` is a singleton built once at registration, capturing the options — a rotated secret takes effect on process restart only (acceptable; no hot-rotation required). |
| 3 | Total timeout budget | Per-attempt `options.Retry.Timeout = 30s`; named `HttpClient.Timeout = 30s` backstop. Whole-call budget enforced by a `CancellationTokenSource` linked to `HttpContext.RequestAborted` with `CancelAfter` (per-op 30s; sync/list use a larger budget, e.g. 120s, since they fan out). Implemented in `WalmartInventoryService` via one `Bounded(...)` helper. |
| 4 | Write-retry ownership | `UpdateInventoryForSku` = PUT (SDK **does** resend by default); `BulkUpdateInventory` = POST (SDK never resends). Both are **absolute-value sets** (idempotent), so a PUT resend is harmless — left in the default retry set. GET ops retryable, safe. |
| 5 | Idempotency & ambiguous writes | No operation declares a caller-supplied idempotency key (the injected `Idempotency-Key` header is `Guid.NewGuid()`, not a key). Not needed: both writes set absolute quantities, so re-sending or re-syncing converges. Reconciliation path = Flow 3 (`GET /api/walmart/stock`) + idempotent re-sync. |
| 6 | Observability | SDK logger wired to the host `ILoggerFactory` explicitly; request line (`Information`) + failures (`Warning`/`Error`) logged. `LogRequestBody` stays **off**. Service logs operation + SKU + outcome; provider `Error3.Code`/`Message` from `ErrorList` carried into `WalmartIntegrationException` and logged. Client secret never logged. |
| 7 | Sensitive data | Request models carry only SKU + integer quantities — no PII/cards/credentials. Still, `LogRequestBody` stays off and `LoggerFactory` is assigned explicitly so `WALMARTAPISCLIENT_LOG` cannot switch body logging on from outside the code. |
| 8 | Environment selection | `ServerEnvironment.Production` always. No sandbox/test account exists; test traffic is kept off the live system by the `Walmart:BaseUrl` override, which rewrites scheme/host/port of **every** call (API groups `Default`+`Default1` **and** the token endpoint via the custom strategy), preserving each contract path. With `BaseUrl` unset the integration targets production. |
| 9 | Duplicate prevention under concurrency | Walmart writes are idempotent absolute sets — a concurrent double-set/double-sync converges, no claim needed (see DUPLICATE CLAIMS). `POST /api/orders` is an **eShop-store** write (not a Walmart call); duplicate-submit protection is out of task scope and not added — noted as a known limitation. |
| 10 | Partial results | `GET /api/walmart/stock` walks pages with a hard page cap; when the cap is hit the response carries a top-level `truncated: true` so the caller (not a log) learns the list was cut short. |
| 11 | Unknown outcomes | On a connection/timeout fault during a set/sync the write may have landed; because every write is an idempotent absolute set, the outcome is settled by design — the next `GET /api/walmart/stock(/sku)` reads Walmart's actual record and a re-sync re-converges. The boundary surfaces the fault as a 502 (not a false success); it does not report a definite failure for a write whose outcome is unknown. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| none — both Walmart writes (`UpdateInventoryForSku`, `BulkUpdateInventory`) set an **absolute** quantity, so a second identical call produces the same end state; there is nothing to deduplicate. `POST /api/orders` writes the eShop store, not Walmart, and is out of this table's scope. | n/a | n/a | n/a | n/a |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `GetInventorySummaries` (Flow 3) | page cap `MaxSummariesPages=1000` + `PageSize=500`; loop also stops when `Pagination.NextToken` is null/empty; bounded by the handler deadline token | top-level `truncated` bool on the `GET /api/walmart/stock` response is set `true` when the cap is reached | cap/`truncated` set in `WalmartInventoryService.ListAllAsync`; surfaced via `WalmartStockReportResponse.Truncated` in `GetWalmartStockReportEndpoint.HandleAsync` |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `UpdateInventoryForSku` | `GetInventoryForSku` | the same `sku` | fault caught (not reported as success) in `WalmartInventoryService.CallAsync` (`SdkConnectionException`/`SdkTimeoutException` arms → `WalmartIntegrationException`, null status); re-read via `WalmartInventoryService.GetStockAsync` (GET `/api/walmart/stock/{sku}`) settles the record; idempotent re-set converges | `WalmartInventoryServiceTests.SetStock_TranslatesConnectionFaultToIntegrationException_WithNoStatus` |
| `BulkUpdateInventory` | `GetInventorySummaries` (Flow 3 report) | the SKUs sent | per-batch catch in `WalmartInventoryService.SyncAsync` marks the batch `failed` (not `updated`); re-read via `WalmartInventoryService.ListAllAsync` (GET `/api/walmart/stock`) settles the records; idempotent re-sync converges | same connection-fault path as above (shared `CallAsync`) |

Both writes are idempotent absolute sets, so an "unknown" outcome is benign: the catch surfaces the fault as provider-unavailable (502) / per-SKU `failed` rather than a definite success, and the Flow-3 report + idempotent re-sync (both wired above) settle the actual state — a possibly-applied write is never retried as if it never happened.

## 6. Assumptions & Blockers
- **Assumption (minor):** `POST /api/orders` needs a ship-to `Address`; the request carries only item ids+quantities, so a fixed placeholder address (as the Web checkout uses) is supplied. Order creation's point is the stock decrement + `orderId`.
- **Assumption (minor):** buyer identity for the order = the JWT name claim (`HttpContext.User.Identity.Name`).
- **Assumption (minor):** "eShop's level" for an item never set = 0 (default `AvailableStock`).
- No blockers. The SDK exposes every capability needed: single set/get (`UpdateInventoryForSku`/`GetInventoryForSku`), whole-catalog push (`BulkUpdateInventory`), full listing (`GetInventorySummaries`), and a token-strategy extension point to route token acquisition through `Walmart:BaseUrl`.
