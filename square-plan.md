# Square integration plan — eShopOnWeb PublicApi

SDK: Square .NET SDK shipped in the `square` plugin at `sdk/dotnet/` (plugin-relative). Map: `sdk/dotnet/sdk-map.md`.
Every SDK operation in scope is **Case B** (`ApiException<Square.Core.ErrorResponse.RawError>`), throw-only, server group `Default`, no `Pageable` (sdk-map.md defaults table).

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 1 | Settings binding `Square:*` + fail-fast validation; SDK project reference; named `HttpClient`; client holder | — |
| 2 | Token source: custom `Oauth2TokenStrategy` reading the persisted OAuth connection, falling back to `Square:AccessToken`; refresh via `refresh_token` grant | `OAuth.ObtainToken` |
| 3 | Flow 1: `GET /api/square/connect` (state claim + sign-in URL), `GET /api/square/callback` (consume state, exchange code, persist), `GET /api/square/connection` | `OAuth.ObtainToken`, `Merchants.RetrieveMerchant` |
| 4 | Merchant context: merchant id + selling location + currency (cached per merchant) | `Merchants.RetrieveMerchant`, `Locations.ListLocations` |
| 5 | Flow 2a: `POST /api/square/catalog/sync` | `Catalog.BatchRetrieveCatalogObjects`, `Catalog.SearchCatalogObjects`, `Catalog.BatchUpsertCatalogObjects` |
| 6 | Flow 2b: `PUT /api/catalog-items/{id}/photo` | `Catalog.CreateCatalogImage` (+ `BatchRetrieveCatalogObjects` for URL fallback) |
| 7 | Gift-message field definition (ensure once per merchant) | `OrderCustomAttributes.RetrieveOrderCustomAttributeDefinition`, `OrderCustomAttributes.CreateOrderCustomAttributeDefinition` |
| 8 | Flow 3: `POST /api/orders`, `GET /api/my-orders/{id}` | `Orders.CreateOrder`, `Orders.RetrieveOrder`, `OrderCustomAttributes.UpsertOrderCustomAttribute`, `OrderCustomAttributes.RetrieveOrderCustomAttribute`, `Catalog.BatchRetrieveCatalogObjects` |
| 9 | EF entities + SQL migration, error mapping, offline tests, live sandbox verification | — |

Sign-in URL: the SDK builds the authorize URL only inside its internal `OAuth2AuthorizationCodeStrategy` (a blocking `PromptForAuthorizationCode` callback, unusable across two HTTP requests). The app builds the same URL itself from SDK facts: base URL = `Square.ServerOptions.Default.{Production|Sandbox}.BaseUrl` (source `Servers/DefaultOptions.cs`), path `/oauth2/authorize` (`AuthSchemes.cs`), query names `client_id`, `response_type=code`, `redirect_uri`, `scope`, `state` (`Core/Authentication/OAuth2/AuthorizationCode/OAuth2AuthorizationCodeStrategy.cs` `BuildAuthorizationQueryParams`). Scope value: task-given, space separated.

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim — each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own, never flat arguments. Always pass `cancellationToken:` by name.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type: `Models/X.cs` → `Square.Models`, `Models/Enums/X.cs` → `Square.Models.Enums`, `Requests/<Ctl>/X.cs` → `Square.Requests.<Ctl>`, `Core/Models/BinaryContent.cs` → `Square.Core.Models`, `ApiException<T>` → `Square.Core.Exceptions`, `RawError` → `Square.Core.ErrorResponse`.

| Controller · method | Request record (members) | Body model (C# (wire): type, req?) | Response + fields read | Err | Verb · SDK retries? | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `OAuth.ObtainToken(ObtainTokenOperationRequest, …)` — no auth scheme | `Body: ObtainTokenRequest, required` | `ClientId (client_id): string, req` · `ClientSecret (client_secret): string?` · `Code (code): string?` · `RedirectUri (redirect_uri): string?` · `GrantType (grant_type): string, req` (`authorization_code` / `refresh_token`) · `RefreshToken (refresh_token): string?` · left out: `migration_token`, `scopes`, `short_lived`, `code_verifier`, `use_jwt` | `ObtainTokenResponse`: `AccessToken (access_token)`, `TokenType`, `ExpiresAt (expires_at): string?`, `MerchantId (merchant_id)`, `RefreshToken (refresh_token)`, `RefreshTokenExpiresAt`, `Errors` | B | POST · no | `map/operations/OAuth.md`; `Models/ObtainTokenRequest.cs`; `Models/ObtainTokenResponse.cs`; remarks `Api/OAuth.cs` (code flow: code+client_id+client_secret; refresh in code flow returns same refresh token) |
| `Merchants.RetrieveMerchant(RetrieveMerchantRequest, …)` — `Oauth2` | `MerchantId: string, required` (`"me"` = merchant of this token) | — | `RetrieveMerchantResponse.Merchant: Merchant?` → `Id (id)`, `BusinessName (business_name)`, `MainLocationId (main_location_id)`, `Currency (currency): Currency?`; ⚠ `Country` is `required` | B | GET · yes | `map/operations/Merchants.md`; `Requests/Merchants/RetrieveMerchantRequest.cs`; `Models/Merchant.cs` |
| `Locations.ListLocations(…)` — no request record | — | — | `ListLocationsResponse.Locations: IReadOnlyList<Location>?` → `Id`, `Name`, `Status: LocationStatus?`, `Currency: Currency?`, `MerchantId` | B | GET · yes | `map/operations/Locations.md`; `Models/ListLocationsResponse.cs`; `Models/Location.cs` |
| `Catalog.BatchRetrieveCatalogObjects(BatchRetrieveCatalogObjectsOperationRequest, …)` | `Body, required` | `ObjectIds (object_ids): IReadOnlyList<string>, req` · `IncludeRelatedObjects (include_related_objects): bool?` · `IncludeDeletedObjects: bool?` | `Objects: IReadOnlyList<CatalogObject>?`, `RelatedObjects` | B | POST · no | `map/operations/Catalog.md`; `Models/BatchRetrieveCatalogObjectsRequest.cs`; `Models/BatchRetrieveCatalogObjectsResponse.cs` |
| `Catalog.SearchCatalogObjects(SearchCatalogObjectsOperationRequest, …)` | `Body, required` | `ObjectTypes (object_types): IReadOnlyList<CatalogObjectType>?` · `Query (query): CatalogQuery?` → `SetQuery (set_query): CatalogQuerySet?` → `AttributeName (attribute_name): string, req` = `"sku"` (searchable on ItemVariation), `AttributeValues (attribute_values): IReadOnlyList<string>, req` · `IncludeRelatedObjects: bool?` · `Cursor (cursor): string?` · `Limit (limit): int?` (advisory, max 1000) | `Objects`, `RelatedObjects`, `Cursor: string?` | B | POST · no | `Models/SearchCatalogObjectsRequest.cs`; `Models/CatalogQuery.cs`; `Models/CatalogQuerySet.cs`; `Models/SearchCatalogObjectsResponse.cs` |
| `Catalog.BatchUpsertCatalogObjects(BatchUpsertCatalogObjectsOperationRequest, …)` | `Body, required` | `IdempotencyKey (idempotency_key): string, req` (1–128) · `Batches (batches): IReadOnlyList<CatalogObjectBatch>, req` → `Objects (objects): IReadOnlyList<CatalogObject>, req`; each batch atomic, ≤1000 objects, ≤10000 per request; `#`-prefixed ids create | `Objects`, `IdMappings: IReadOnlyList<CatalogIdMapping>?` → `ClientObjectId (client_object_id)`, `ObjectId (object_id)`; `Errors` | B | POST · no | `Models/BatchUpsertCatalogObjectsRequest.cs`; `Models/CatalogObjectBatch.cs`; `Models/BatchUpsertCatalogObjectsResponse.cs`; `Models/CatalogIdMapping.cs` |
| `Catalog.CreateCatalogImage(CreateCatalogImageOperationRequest, …)` | `Request: CreateCatalogImageRequest?` · `ImageFile: Square.Core.Models.BinaryContent?` (multipart parts `request` JSON + `image_file`) | `IdempotencyKey (idempotency_key): string, req` · `ObjectId (object_id): string?` · `Image (image): CatalogObject, req` (type `IMAGE`, `ImageData`) · `IsPrimary (is_primary): bool?` (true replaces primary image). Accepts JPEG/PJPEG/PNG/GIF ≤ 15 MB | `CreateCatalogImageResponse.Image: CatalogObject?` → `Id`, `ImageData.Url (url)` | B | POST multipart w/ binary · never | `Requests/Catalog/CreateCatalogImageOperationRequest.cs`; `Models/CreateCatalogImageRequest.cs`; `Models/CreateCatalogImageResponse.cs`; `Core/Models/BinaryContent.cs` (`Stream`, `FileName`, `ContentType: MediaTypeHeaderValue`, all `required`); remarks `Api/Catalog.cs` |
| `Orders.CreateOrder(CreateOrderOperationRequest, …)` | `Body, required` | `Order (order): Order?` → `LocationId (location_id): string, req` · `ReferenceId (reference_id): string?` (≤40) · `LineItems (line_items): IReadOnlyList<OrderLineItem>?` → `Quantity (quantity): string, req` · `Name (name)` · `CatalogObjectId (catalog_object_id)` (an ITEM_VARIATION id) · `BasePriceMoney (base_price_money): Money?` · `ItemType: OrderLineItemItemType?`; `IdempotencyKey (idempotency_key): string?` (≤192; same key re-sent = no duplicate) | `CreateOrderResponse.Order` → `Id`, `State`, `TotalMoney`, `LineItems` | B | POST · no | `Models/CreateOrderRequest.cs`; `Models/Order.cs`; `Models/OrderLineItem.cs`; `Models/Money.cs` (`Amount: long?`, `Currency: Currency?`) |
| `Orders.RetrieveOrder(RetrieveOrderRequest, …)` | `OrderId: string, required` | — | `RetrieveOrderResponse.Order` → `Id`, `State`, `TotalMoney`, `ReferenceId` | B | GET · yes | `Requests/Orders/RetrieveOrderRequest.cs`; `Models/RetrieveOrderResponse.cs` |
| `OrderCustomAttributes.RetrieveOrderCustomAttributeDefinition(RetrieveOrderCustomAttributeDefinitionRequest, …)` | `Key: string, required` · `Version: int?` | — | `CustomAttributeDefinition: CustomAttributeDefinition?` | B | GET · yes | `map/operations/OrderCustomAttributes.md`; `Requests/OrderCustomAttributes/RetrieveOrderCustomAttributeDefinitionRequest.cs` |
| `OrderCustomAttributes.CreateOrderCustomAttributeDefinition(CreateOrderCustomAttributeDefinitionOperationRequest, …)` | `Body, required` | `CustomAttributeDefinition (custom_attribute_definition): CustomAttributeDefinition, req` → `Key (key)` (≤60 `[A-Za-z0-9._-]`, unique per app+seller+resource), `Schema (schema): object?` (task-given `$ref` String schema), `Name (name)` + `Description (description)` (required for read/write visibility), `Visibility (visibility)`; `IdempotencyKey: string?` (1–45) | `CustomAttributeDefinition` | B | POST · no | `Models/CreateOrderCustomAttributeDefinitionRequest.cs`; `Models/CustomAttributeDefinition.cs` |
| `OrderCustomAttributes.UpsertOrderCustomAttribute(UpsertOrderCustomAttributeOperationRequest, …)` | `OrderId: string, required` · `CustomAttributeKey: string, required` · `Body, required` | `CustomAttribute (custom_attribute): CustomAttribute, req` → `Value (value): object?`; `IdempotencyKey (idempotency_key): string?` (1–45) | `CustomAttribute` | B | POST · no | `Requests/OrderCustomAttributes/UpsertOrderCustomAttributeOperationRequest.cs`; `Models/UpsertOrderCustomAttributeRequest.cs`; `Models/CustomAttribute.cs` |
| `OrderCustomAttributes.RetrieveOrderCustomAttribute(RetrieveOrderCustomAttributeRequest, …)` | `OrderId, required` · `CustomAttributeKey, required` · `Version: int?` · `WithDefinition: bool = false` | — | `CustomAttribute.Value: object?` (a `System.Text.Json.JsonElement` after a read) | B | GET · yes | `Requests/OrderCustomAttributes/RetrieveOrderCustomAttributeRequest.cs`; `Models/RetrieveOrderCustomAttributeResponse.cs` |

`CatalogObject` (`Models/CatalogObject.cs`): `Type (type): CatalogObjectType, req` · `Id (id): string, req` · `Version (version): long?` (supply on update; three-way merge, `VERSION_MISMATCH` on conflicting field) · `IsDeleted` · `PresentAtAllLocations` · `ItemData (item_data): CatalogItem?` · `ItemVariationData (item_variation_data): CatalogItemVariation?` · `ImageData (image_data): CatalogImage?`.
`CatalogItem` (`Models/CatalogItem.cs`): `Name (name)`, `Variations (variations): IReadOnlyList<CatalogObject>?`, `ImageIds (image_ids)`. `CatalogItemVariation` (`Models/CatalogItemVariation.cs`): `ItemId (item_id)`, `Name`, `Sku (sku)`, `PricingType (pricing_type): CatalogPricingType?`, `PriceMoney (price_money): Money?`. `CatalogImage` (`Models/CatalogImage.cs`): `Name`, `Url (url)`, `Caption`.

Enums used (wire values from declaring files):

| Enum (namespace `Square.Models.Enums`) | Members used | Source |
| --- | --- | --- |
| `CatalogObjectType` | `Item`=ITEM, `ItemVariation`=ITEM_VARIATION, `Image`=IMAGE | `Models/Enums/CatalogObjectType.cs` |
| `CatalogPricingType` | `FixedPricing`=FIXED_PRICING | `Models/Enums/CatalogPricingType.cs` |
| `LocationStatus` | `Active`=ACTIVE | `Models/Enums/LocationStatus.cs` |
| `OrderLineItemItemType` | `Item`=ITEM | `Models/Enums/OrderLineItemItemType.cs` |
| `CustomAttributeDefinitionVisibility` | `VisibilityReadWriteValues`=VISIBILITY_READ_WRITE_VALUES | `Models/Enums/CustomAttributeDefinitionVisibility.cs` |
| `Currency` | resolved from location via `TryGetKnownValue`/`.Value` | `Models/Enums/Currency.cs` |
| `Square.Servers.ServerEnvironment` (closed) | `Production`="production", `Sandbox`="sandbox"; parse with `TryGetKnownValue` | `Servers/ServerEnvironment.cs` |

Client / auth facts:
- `new Square.SquareClient(HttpClient, Square.SquareClientOptions)` — only constructor (`SquareClient.cs`). `AddSquareClient` registers a singleton over the default unnamed client (`ServiceCollectionExtensions.cs`) — not used; registering over a named client instead.
- Options: `Environment`, `Retry` (`Square.Core.Configuration.RetryOptions`), `Logging`, `Oauth2` (`Square.Core.Authentication.OAuth2.AuthorizationCode.OAuth2AuthorizationCodeCredentials`: `ClientId` req, `RedirectUri` req, `PromptForAuthorizationCode` req, `ClientSecret`, `Scope`, `State`), `Oauth2TokenStrategy` (`IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>`: `GetToken`, `TryRefreshToken`) — used only when `Oauth2` is also set (`AuthSchemes.cs`).
- Scheme caches the token per client instance until `IsExpired` (`ExpiresIn` seconds minus min(30, ExpiresIn/2)); `ExpiresIn == null` = never expires; when expired with a non-null `RefreshToken` it calls `TryRefreshToken` first (`Core/Authentication/OAuth2/OAuth2RefreshableScheme.cs`). Header sent: `Authorization: Bearer <AccessToken>`.
- Servers: one group `Default`; Production `https://connect.squareup.com`, Sandbox `https://connect.squareupsandbox.com`, Custom `{custom_url}` (sdk-map.md Servers & auth).

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| `state` on the callback must be one this app issued from `connect`, unexpired, unused | `GET /api/square/callback` ← `GET /api/square/connect` (state row) | `SquareOAuthService.CompleteAsync` — consume (delete) state row before `ObtainToken` |
| `Order.LocationId` must be an ACTIVE location of the connected merchant | `CreateOrder` ← `ListLocations` | `SquareMerchantContextProvider.GetAsync` picks from `ListLocations` result (main location if active, else first active) |
| `Money.Currency` on prices/line items must be the location's currency | `BatchUpsertCatalogObjects`, `CreateOrder` ← `ListLocations` | `SquareMerchantContextProvider` → `SquareMoney.From(price, currency)` |
| `OrderLineItem.CatalogObjectId` must be an ITEM_VARIATION this app created for that eShop item (SKU marker), with the same current price | `CreateOrder` ← `BatchUpsertCatalogObjects` / `SearchCatalogObjects` + `BatchRetrieveCatalogObjects` | `SquareCatalogLocator.ResolveAsync` + price check in `SquareOrderPublisher` before `CreateOrder` (else ad-hoc line with name + base price) |
| `CreateCatalogImage.ObjectId` must be the Square ITEM this app created for that eShop item | `CreateCatalogImage` ← `BatchUpsertCatalogObjects` / `SearchCatalogObjects` | `SquarePhotoService.UploadAsync` via `SquareCatalogLocator.ResolveAsync` |
| `Upsert/RetrieveOrderCustomAttribute.OrderId` must be the Square order created for THIS eShop order of THIS buyer | `UpsertOrderCustomAttribute`, `RetrieveOrderCustomAttribute`, `RetrieveOrder` ← `CreateOrder` (stored in `SquareOrderLink`) | `SquareOrderService` loads order by id **and** `BuyerId` before any Square call |
| `CustomAttributeKey` must be the definition this app ensured | `UpsertOrderCustomAttribute` ← `CreateOrderCustomAttributeDefinition` / `RetrieveOrderCustomAttributeDefinition` | `SquareGiftMessageField.EnsureDefinitionAsync` before the upsert |

## 3. Trap notes

- Step 1 — the generator's DI extension registers a singleton over the default unnamed `HttpClient`; lifetime, DNS and token-cache consequences decide the registration shape. **MUST load `square:dotnet-client-initialization`.**
- Step 1/2 — an unset credential sends unauthenticated requests without any exception; the SDK's own auth-code strategy blocks on an interactive prompt and its cache semantics decide when our token source is consulted. **MUST load `square:dotnet-authentication`.**
- Step 2 — a custom token source's returned lifetime decides whether a newly connected merchant's token is ever picked up by a long-lived client. **MUST load `square:dotnet-authentication`.**
- Steps 3–8 — request records vs body models, file parts and `cancellationToken:` position. **MUST load `square:dotnet-calling-endpoints`.**
- Steps 5–8 — enums are not C# enums (string interpolation of an enum is not its wire value); `object?` fields come back as something other than a string; unknown fields ride in an extension bag that `with` preserves. **MUST load `square:dotnet-models`.**
- All steps — which exception types escape a typed catch, and how a non-matching body surfaces. **MUST load `square:dotnet-error-handling`.**
- All steps — what `Timeout` bounds, which verbs/requests the SDK resends, what the logger writes, and how an unknown write outcome must be settled. **MUST load `square:dotnet-configuration-resilience`.**
- Step 9 — which seam to fake, and why request bodies must be captured inside the handler. **MUST load `square:dotnet-testing`.**

## 4. REQUIRED READING (load all before implementation starts; this sheet deliberately does not carry their contents)

- `square:dotnet-client-initialization` — step 1 (client construction, DI, HttpClient lifetime)
- `square:dotnet-authentication` — steps 1–3 (credentials, token strategy, fail-fast)
- `square:dotnet-calling-endpoints` — steps 3–8 (request records, files, cancellation)
- `square:dotnet-models` — steps 5–8 (enums, `object?` values, extension data, `with`)
- `square:dotnet-error-handling` — every call site (Case B ladder, connection failures)
- `square:dotnet-configuration-resilience` — every call site (retries, timeouts, logging, unknown outcomes)
- `square:dotnet-testing` — step 9 (stub handler seam)
- Hazard (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `SquareSettings` bound from `Square:` (`Environment`, `ApplicationId`, `ApplicationSecret`, `RedirectUri`, `AccessToken`) with `ValidateOnStart()`: `Environment` must parse to `Production`/`Sandbox`; `ApplicationId`, `ApplicationSecret`, `RedirectUri` must be non-blank (and `RedirectUri` absolute https/http URI). Error names the key, never the value. `AccessToken` optional (blank = unset). |
| 2 | Secret sourcing & rotation | Values come from configuration (user-secrets in dev, env vars/secret store elsewhere). Options are built once when the client holder builds a client; `ApplicationSecret` rotation needs a restart. OAuth tokens are not captured: the strategy reads them from the DB on every token acquisition (lifetime handed to the SDK capped at 5 min), and the holder rebuilds the client when a merchant connects — new tokens take effect without restart. |
| 3 | Total timeout budget | Named HttpClient `Timeout` 20 s (per attempt, backstop); `Retry.Timeout` 15 s per attempt; every endpoint handler links `RequestAborted` to a deadline (`Square:` code constant 45 s; catalog sync 120 s) and passes it to every call. GET worst case 4×15 s + 7 s = 67 s > 45 s → the deadline wins; caller waits ≤ budget. |
| 4 | Write-retry ownership | Every write in scope is POST (`ObtainToken`, `BatchUpsertCatalogObjects`, `CreateCatalogImage` multipart+binary, `CreateOrder`, `CreateOrderCustomAttributeDefinition`, `UpsertOrderCustomAttribute`) → SDK never resends. `HttpMethodsToRetry` left at default (no PUT in scope). App-level re-send only with the operation's real idempotency key (row 5). |
| 5 | Idempotency & ambiguous writes | `BatchUpsertCatalogObjects.IdempotencyKey` = new GUID per sync run, reused on the settle re-send. `CreateCatalogImage.IdempotencyKey` = new GUID per upload, reused on settle. `CreateOrder.IdempotencyKey` = GUID stored on `SquareOrderLink` before the call (stable across settle attempts, also on later GET). `UpsertOrderCustomAttribute.IdempotencyKey` = GUID per publish attempt, reused on re-send. `CreateOrderCustomAttributeDefinition.IdempotencyKey` = GUID, conflict settled by re-reading the definition. `ObtainToken` has no key: code exchange is not re-sent (codes are single-use); refresh re-reads the persisted connection. Generator `Idempotency-Key` header is not cited as a key. |
| 6 | Observability | SDK `Logging.LoggerFactory` = host `ILoggerFactory` explicitly; request/response lines at Information, retries Warning; `LogRequestBody`/headers off. App logs Square `errors[].code`/`category` (from `RawError.ReadAsString()` parsed as `{errors:[…]}`) at Warning with eShop order id / catalog item id; never tokens, codes, secrets or gift messages. |
| 7 | Sensitive data | `ObtainTokenRequest` carries `client_secret`, `code`, `refresh_token`; responses carry tokens; gift message is personal content → `LogRequestBody = false`, `LoggerFactory` assigned explicitly (disables `SQUARECLIENT_LOG`). Tokens encrypted at rest with ASP.NET Core Data Protection. |
| 8 | Environment selection | One server group `Default`. `Square:Environment` = `sandbox` → `https://connect.squareupsandbox.com` for API and OAuth authorize/token; `production` → `https://connect.squareup.com`. `Custom` rejected at startup. Dev/test machines configure `sandbox`; offline tests replace the primary handler so no traffic leaves the process. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| OAuth code exchange (`ObtainToken`, callback) | `SquareOAuthStates` row (PK `State`) in `CatalogContext` | Deleting the already-deleted row → `DbUpdateConcurrencyException` | `SquareOAuthService.CompleteAsync` catch → callback refused (400) | `SquareOAuthService.ConsumeStateAsync` → `SquareOAuthService.ExchangeCodeAsync` |
| Catalog sync creates/updates (`BatchUpsertCatalogObjects`) | `SquareLeases` row (PK `Name` = `catalog-sync`) with expiry | Insert of the same PK → duplicate-key failure | `SquareLeaseStore.TryAcquireAsync` catch → 409 | `SquareLeaseStore.TryAcquireAsync` → `SquareCatalogSyncService.UpsertAsync` |
| Photo upload (`CreateCatalogImage`) | `SquareLeases` row (PK `photo:{catalogItemId}`) | Insert of the same PK | `SquareLeaseStore.TryAcquireAsync` catch → 409 | `SquareLeaseStore.TryAcquireAsync` → `SquarePhotoService.CreateImageAsync` |
| Gift field definition (`CreateOrderCustomAttributeDefinition`) | `SquareLeases` row (PK `gift-definition:{merchantId}`) | Insert of the same PK | `SquareLeaseStore.TryAcquireAsync` catch → second caller waits then re-reads the definition | `SquareLeaseStore.TryAcquireAsync` → `SquareGiftMessageField.CreateDefinitionAsync` |
| Square order for an eShop order (`CreateOrder` + gift upsert) | `SquareOrderLinks` row (PK `OrderId`) saved with the eShop `Order`, carrying the `CreateOrder` idempotency key | Insert of the same PK | `SquareOrderService.PlaceOrderAsync` (one link per order; settle path re-uses the row, never inserts) | `SquareOrderService.PlaceOrderAsync` (SaveChanges of Order+link) → `SquareOrderPublisher.CreateSquareOrderAsync` |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `SearchCatalogObjects` by SKU set | 20 pages per lookup (cursor loop) + no-progress guard | `SquareCatalogLookup.Unresolved` / `IsComplete = false`; sync refuses to create those items and returns them in `skipped` with a reason (`complete: false`); photo upload answers 503; orders fall back to ad-hoc lines | `SquareCatalogLocator.SearchBySkuAsync` (returns `Complete`) → `SquareCatalogLocator.ResolveAsync` (fills `Unresolved`) |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `BatchUpsertCatalogObjects` | Same call re-sent with the same `IdempotencyKey` (documented safe); if still unknown → re-run of sync reconciles via `SearchCatalogObjects` by SKU `eshop-{id}` | idempotency key; SKU marker | `SquareCatalogSyncService.UpsertAsync` catch | `SquareCatalogSyncEndpointTests.UnknownOutcomeIsSettledByResendWithSameKey`, `…UnsettledOutcomeIsReportedAndTheNextSyncDoesNotDuplicate` |
| `CreateCatalogImage` | Same call re-sent with the same `IdempotencyKey` | idempotency key | `SquarePhotoService.CreateImageAsync` catch | `CatalogItemPhotoEndpointTests.UnknownOutcomeIsSettledByResendWithSameKey` |
| `CreateOrder` | Same call re-sent with the stored `IdempotencyKey`; if still unknown, link stays `PendingSquareOrder` and `GET /api/my-orders/{id}` re-sends with the same key | `SquareOrderLink.IdempotencyKey` (+ `reference_id` = eShop order id) | `SquareOrderPublisher.CreateSquareOrderAsync` catch; unsettled → link stays `PendingSquareOrder`, settled by `SquareOrderService.SettleAsync` (called from `GetMyOrderAsync`) | `OrderEndpointTests.CreateOrderUnknownOutcomeIsSettledWithSameKey`, `OrderEndpointTests.PendingOrderIsSettledOnRead` |
| `UpsertOrderCustomAttribute` | Same call re-sent with the same `IdempotencyKey`; if still unknown, link stays `PendingGiftMessage`, message held in process memory only, settled on read | idempotency key | `SquareOrderPublisher.WriteGiftMessageAsync` catch; unsettled → `PendingGiftMessage`, settled by `SquareOrderService.SettleAsync` → `SquareOrderPublisher.SetGiftMessageAsync(settling: true)` which reads the field first | `OrderEndpointTests.GiftMessageUnknownOutcomeIsSettledWithSameKey`, `…PendingGiftMessageIsSettledOnRead`, `…GiftMessageThatLandedIsNotWrittenAgainWhenSettling` |
| `CreateOrderCustomAttributeDefinition` | `RetrieveOrderCustomAttributeDefinition` by key | definition key `eshop-gift-message` | `SquareGiftMessageField.CreateDefinitionAsync` catch | `OrderEndpointTests.DefinitionCreateUnknownOutcomeIsSettledByRead` |
| `ObtainToken` (code exchange) | none — code is single-use; on unknown outcome the connection is left unchanged and the merchant is told to sign in again | — | `SquareOAuthService.ExchangeCodeAsync` catch | `SquareOAuthEndpointTests.CallbackExchangeConnectionFailureLeavesConnectionUnchanged` |
| `ObtainToken` (refresh) | re-read persisted connection; next token acquisition refreshes again with the same refresh token (code flow returns the same refresh token) | refresh token | `SquareTokenSource.RefreshAsync` catch | `SquareOAuthEndpointTests.RefreshConnectionFailureKeepsStoredToken` |

## 6. Assumptions & Blockers

- Assumption (YOUR CALL — not in the map): eShop-owned Square items are identified by variation SKU `eshop-{catalogItemId}`; items without that marker are never touched.
- Assumption (UNVERIFIED): `BatchRetrieveCatalogObjects` returns an ITEM's variations nested in `item_data.variations`. Defensive: request item **and** variation ids and use the top-level variation objects; when updating, send every variation the item carries (only ours modified) so no variation is dropped.
- Assumption (UNVERIFIED): a line item with `catalog_object_id` takes the catalog price. Defensive: only link a line to the catalog variation when its current Square price equals the eShop unit price; otherwise send an ad-hoc line (name + `base_price_money`); after creation compare `Order.TotalMoney` to the eShop total and log a mismatch.
- Assumption (UNVERIFIED): Square search index may lag behind writes. Defensive: the local `SquareCatalogLinks` table is the primary index; SKU search is only for items without a link.
- Assumption (YOUR CALL): `POST /api/orders` takes an optional `shipToAddress`; when omitted the order is stored with an empty address (in-person/pickup), since `Order.ShipToAddress` is required by the existing model.
- Assumption (YOUR CALL): the gift message is never written to eShop's database; while a Square write is unsettled it lives only in process memory (`IMemoryCache`, 24 h).
- Blockers: none.

## 7. Live sandbox findings (2026-10-07)

| Row | Outcome |
| --- | --- |
| UNVERIFIED: item variations nested on read / update keeps variations | Confirmed: updating name+price kept the item's single variation (read back via `SearchCatalogObjects` with related objects). |
| UNVERIFIED: catalog-linked line item takes the catalog price | Confirmed: order lines with `catalog_object_id` came back with the synced `base_price_money`; Square total equalled the eShop total (4825 minor units). |
| UNVERIFIED: search index lag | After an in-memory DB reset, the SKU search found all 12 items; no duplicates were created. |
| `CustomAttribute.Version` (`Models/CustomAttribute.cs` documents it as optional) | Sandbox rejects changing an existing order attribute without it (`BAD_REQUEST`, field `version`). Consequence: a settle re-write with a new key could be refused → `SetGiftMessageAsync(settling: true)` reads first and keeps any existing value; a `Rejected` write is settled by reading. Never overwrites a staff edit. |
| Sign-in URL | Square sandbox answers the built URL with a 302 to its login page (accepted). The registered redirect URI must point at `GET /api/square/callback` of this API for the callback to arrive. |
