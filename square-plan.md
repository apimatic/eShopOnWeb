# Square integration plan — eShopOnWeb PublicApi

SDK: Square .NET SDK shipped in the `square` plugin at `sdk/dotnet/` (plugin-relative). Map: `sdk/dotnet/sdk-map.md` + `sdk/dotnet/map/operations/`.
Host: `src/PublicApi` (JWT, MinimalApi.Endpoint `IEndpoint<…>` classes; exemplar `src/PublicApi/CatalogItemEndpoints/CreateCatalogItemEndpoint.cs`; admin role `BlazorShared.Authorization.Constants.Roles.ADMINISTRATORS`).

> **STATUS: BLOCKED. Implementation has not started.** §6 lists two capabilities this integration needs that the plugin does not document (B1, B2). Per the task's tooling mandate, no project code was written to work around them.

---

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 1 | SDK project reference, `Square:` options binding + fail-fast validation, `IHttpClientFactory`-owned `HttpClient`, per-request `SquareClient` with a custom `Oauth2TokenStrategy` (stored OAuth tokens → refresh → fallback to `Square:AccessToken`) | — |
| 2 | Persistence (CatalogContext): `SquareConnection` (single row, tokens encrypted with Data Protection), `SquareOAuthState` (PK = SHA-256 of state), `SquareCatalogItemLink` (PK = CatalogItemId), `SquareOrderLink` (PK = OrderId) | — |
| 3 | Flow 1: `GET /api/square/connect` (state claim + sign-in URL), `GET /api/square/callback` (consume state → code exchange → merchant lookup → store), `GET /api/square/connection` | `OAuth.ObtainToken`, `Merchants.RetrieveMerchant` |
| 4 | Flow 2a: `POST /api/square/catalog/sync` (walk Square items, match eShop-owned ones by variation SKU marker, create/update via read-modify-write) | `Catalog.ListCatalog`, `Catalog.BatchUpsertCatalogObjects` |
| 5 | Flow 2b: `PUT /api/catalog-items/{id}/photo` (magic-byte JPEG/PNG check, ≤ 5 MB, before any Square call) | `Catalog.CreateCatalogImage` |
| 6 | Flow 3: gift-message definition ensure, `POST /api/orders`, `GET /api/my-orders/{orderId}` | `Merchants.RetrieveMerchant`, `Locations.ListLocations`, `Orders.CreateOrder`, `OrderCustomAttributes.RetrieveOrderCustomAttributeDefinition` / `CreateOrderCustomAttributeDefinition` / `UpsertOrderCustomAttribute` / `RetrieveOrderCustomAttribute` — **blocked by B1** |
| 7 | Offline tests (fake `HttpMessageHandler` seam), sandbox self-verification | — |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Every operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's own property names — never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its own source path implies (`Models/` → `Square.Models`, `Models/Enums/` → `Square.Models.Enums`, `Requests/<C>/` → `Square.Requests.<C>`, `Core/Models/` → `Square.Core.Models`), taken from THAT type's path.

All rows: **Case B** — `Square.Core.Exceptions.ApiException<Square.Core.ErrorResponse.RawError>` (`StatusCode`, `ReadAsBytes()`, `ReadAsString()`, `ReadAsJson<T>()`); throw-only; server group `Default`; no SDK pagination object (cursor is a plain field).

| Controller · method | Request record (members) | Body model (C# (wire): type, req?) | Response → fields read | Source |
| --- | --- | --- | --- | --- |
| `OAuth.ObtainToken` (no Auth bullet → no credential sent) | `Square.Requests.OAuth.ObtainTokenOperationRequest { Body: ObtainTokenRequest, required }` | `Square.Models.ObtainTokenRequest`: `ClientId (client_id): string, req` · `ClientSecret (client_secret): string?` · `Code (code): string?` · `RedirectUri (redirect_uri): string?` · `GrantType (grant_type): string, req` (`authorization_code` / `refresh_token`) · `RefreshToken (refresh_token): string?`. Code flow: code + client_id + client_secret. Left out: `Scopes`, `ShortLived`, `CodeVerifier`, `UseJwt`, `MigrationToken` | `ObtainTokenResponse` → `AccessToken (access_token)`, `TokenType (token_type)`, `ExpiresAt (expires_at)` ISO-8601 string, `MerchantId (merchant_id)`, `RefreshToken (refresh_token)` (code flow: multi-use, never expires; refresh returns same token) | `map/operations/OAuth.md`; `Models/ObtainTokenRequest.cs`; `Models/ObtainTokenResponse.cs`; remarks `Api/OAuth.cs` |
| `Merchants.RetrieveMerchant` (Auth `options.Oauth2`) | `Square.Requests.Merchants.RetrieveMerchantRequest { MerchantId: string, required }` — `"me"` = merchant of the current token | — | `RetrieveMerchantResponse.Merchant` → `Id (id)`, `BusinessName (business_name)`, `MainLocationId (main_location_id)`; ⚠ `Country (country)` is `required` on `Merchant` | `map/operations/Merchants.md`; `Requests/Merchants/RetrieveMerchantRequest.cs`; `Models/Merchant.cs` |
| `Locations.ListLocations` (Auth `options.Oauth2`) | none | — | `ListLocationsResponse.Locations` → `Id (id)`, `Status (status): Square.Models.Enums.LocationStatus` (`Active`=`ACTIVE`, `Inactive`=`INACTIVE`) | `map/operations/Locations.md`; `Models/ListLocationsResponse.cs`; `Models/Location.cs`; `Models/Enums/LocationStatus.cs` |
| `Catalog.ListCatalog` (Auth `options.Oauth2`) | `Square.Requests.Catalog.ListCatalogRequest { Cursor: string?, Types: string?, CatalogVersion: long? }` | — | `ListCatalogResponse` → `Cursor (cursor)`, `Objects (objects): IReadOnlyList<CatalogObject>?` | `map/operations/Catalog.md`; `Requests/Catalog/ListCatalogRequest.cs`; `Models/ListCatalogResponse.cs` |
| `Catalog.BatchUpsertCatalogObjects` (Auth `options.Oauth2`) | `Square.Requests.Catalog.BatchUpsertCatalogObjectsOperationRequest { Body, required }` | `BatchUpsertCatalogObjectsRequest`: `IdempotencyKey (idempotency_key): string, req` · `Batches (batches): IReadOnlyList<CatalogObjectBatch>, req`; `CatalogObjectBatch.Objects (objects): IReadOnlyList<CatalogObject>, req`. `CatalogObject`: `Type (type): CatalogObjectType, req` · `Id (id): string, req` (`#`-prefixed temp id on create) · `Version (version): long?` · `ItemData (item_data): CatalogItem?` · `ItemVariationData (item_variation_data): CatalogItemVariation?`. `CatalogItem`: `Name (name)`, `Variations (variations): IReadOnlyList<CatalogObject>?`, `ImageIds (image_ids)`. `CatalogItemVariation`: `ItemId (item_id)`, `Name (name)`, `Sku (sku)`, `PricingType (pricing_type): CatalogPricingType` (`FixedPricing`), `PriceMoney (price_money): Money?`. `Money`: `Amount (amount): long?`, `Currency (currency): Currency?` | `BatchUpsertCatalogObjectsResponse` → `Objects`, `IdMappings (id_mappings)` → `ClientObjectId (client_object_id)`, `ObjectId (object_id)` | `map/operations/Catalog.md`; `Models/BatchUpsertCatalogObjectsRequest.cs`; `Models/CatalogObjectBatch.cs`; `Models/CatalogObject.cs`; `Models/CatalogItem.cs`; `Models/CatalogItemVariation.cs`; `Models/Money.cs`; `Models/CatalogIdMapping.cs`; `Models/Enums/CatalogObjectType.cs`; `Models/Enums/CatalogPricingType.cs` |
| `Catalog.CreateCatalogImage` (Auth `options.Oauth2`, multipart) | `Square.Requests.Catalog.CreateCatalogImageOperationRequest { Request: CreateCatalogImageRequest?, ImageFile: Square.Core.Models.BinaryContent? }` (nothing required — both are needed for an accepted call) | `CreateCatalogImageRequest`: `IdempotencyKey (idempotency_key): string, req` (1–128) · `ObjectId (object_id): string?` · `Image (image): CatalogObject, req` (`Type = Image`, `ImageData: CatalogImage`) · `IsPrimary (is_primary): bool?`. `BinaryContent { Stream, FileName, ContentType: MediaTypeHeaderValue }` all required | `CreateCatalogImageResponse.Image` → `Id`, `ImageData.Url (url)` | `map/operations/Catalog.md`; `Requests/Catalog/CreateCatalogImageOperationRequest.cs`; `Models/CreateCatalogImageRequest.cs`; `Models/CreateCatalogImageResponse.cs`; `Models/CatalogImage.cs`; `Core/Models/BinaryContent.cs` |
| `Orders.CreateOrder` (Auth `options.Oauth2`) | `Square.Requests.Orders.CreateOrderOperationRequest { Body, required }` | `CreateOrderRequest`: `Order (order): Order?` · `IdempotencyKey (idempotency_key): string?` (real key: same key ⇒ no duplicate order). `Order`: `LocationId (location_id): string, req` · `ReferenceId (reference_id)` · `Source (source): OrderSource { Name }` · `LineItems (line_items)` · `TicketName (ticket_name)`. `OrderLineItem`: `Quantity (quantity): string, req` · `CatalogObjectId (catalog_object_id)` (variation id) · `Name (name)` · `BasePriceMoney (base_price_money)` | `CreateOrderResponse.Order` → `Id`, `LineItems[].BasePriceMoney`, `TotalMoney` | `map/operations/Orders.md`; `Models/CreateOrderRequest.cs`; `Models/Order.cs`; `Models/OrderLineItem.cs`; `Models/OrderSource.cs` |
| `Orders.RetrieveOrder` (Auth `options.Oauth2`) | `Square.Requests.Orders.RetrieveOrderRequest { OrderId: string, required }` | — | `RetrieveOrderResponse.Order` | `map/operations/Orders.md` |
| `OrderCustomAttributes.RetrieveOrderCustomAttributeDefinition` | `{ Key: string, required; Version: int? }` | — | `.CustomAttributeDefinition` | `map/operations/OrderCustomAttributes.md` |
| `OrderCustomAttributes.CreateOrderCustomAttributeDefinition` | `{ Body: CreateOrderCustomAttributeDefinitionRequest, required }` | `CustomAttributeDefinition (custom_attribute_definition), req`: `Key (key)` (≤ 60, `[A-Za-z0-9._-]`) · **`Schema (schema): object?` — required on create; value NOT documented in the plugin (B1)** · `Name (name)` ("Gift message") · `Description (description)` (both required for the visibilities below) · `Visibility (visibility)`: `CustomAttributeDefinitionVisibility.VisibilityReadWriteValues`; `IdempotencyKey (idempotency_key): string?` | `.CustomAttributeDefinition` → `Key` | `Models/CustomAttributeDefinition.cs`; `Models/Enums/CustomAttributeDefinitionVisibility.cs` |
| `OrderCustomAttributes.UpsertOrderCustomAttribute` | `{ OrderId, CustomAttributeKey, Body: UpsertOrderCustomAttributeRequest — all required }` | `CustomAttribute (custom_attribute), req`: `Value (value): object?` · `Version (version): int?`; `IdempotencyKey (idempotency_key): string?` | `.CustomAttribute.Value` | `Models/UpsertOrderCustomAttributeRequest.cs`; `Models/CustomAttribute.cs` |
| `OrderCustomAttributes.RetrieveOrderCustomAttribute` | `{ OrderId, CustomAttributeKey — required; Version: int?; WithDefinition: bool = false }` | — | `.CustomAttribute.Value` | `Requests/OrderCustomAttributes/RetrieveOrderCustomAttributeRequest.cs` |

**Client / auth / server facts** (`sdk-map.md` → Getting a client, Servers & auth; `AuthSchemes.cs`; `Core/Authentication/OAuth2/…`):
- `new Square.SquareClient(HttpClient, Square.SquareClientOptions)` is the only constructor.
- Every operation above except `ObtainToken` authenticates with `options.Oauth2` only (`OAuth2AuthorizationCodeCredentials`: `ClientId` req, `ClientSecret`, `RedirectUri` req, `Scope`, `State`, `Pkce` (default `S256`), `PromptForAuthorizationCode` req). `Oauth2ClientSecret` (API-key header) is not used by any operation in scope.
- `options.Oauth2TokenStrategy`: `IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>` with `GetToken(creds, ct) → OAuthTokenRefreshable` and `TryRefreshToken(creds, refreshToken, ct) → OAuthTokenRefreshable?`. `OAuthTokenRefreshable`: `AccessToken` req, `TokenType` req, `ExpiresIn: int?` (null ⇒ never expires), `RefreshToken`.
- The authorization URL is `server.Default("/oauth2/authorize")` for the selected environment, with query `response_type=code`, `client_id`, `redirect_uri`, `scope` (if any), `state` (if any), PKCE challenge (if any) — `OAuth2AuthorizationCodeStrategy.cs`.
- `Square.Servers.ServerEnvironment.Sandbox` → `https://connect.squareupsandbox.com`; `Production` (default!) → `https://connect.squareup.com`. One server group (`Default`).

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| `Order.LocationId` must be an ACTIVE location the merchant returned (prefer `Merchant.MainLocationId`) | `CreateOrder` ← `ListLocations` (+ `RetrieveMerchant("me")`) | location resolver, before `CreateOrder` |
| `OrderLineItem.CatalogObjectId` must be a variation id our sync created/matched for that eShop item | `CreateOrder` ← `BatchUpsertCatalogObjects` / `ListCatalog` (stored in `SquareCatalogItemLink`) | order service, before `CreateOrder` (item re-synced if price/name drifted) |
| `CreateCatalogImageRequest.ObjectId` must be the Square item id linked to that eShop item | `CreateCatalogImage` ← `BatchUpsertCatalogObjects` / `ListCatalog` | photo service, before `CreateCatalogImage` |
| `CustomAttributeKey` must be the key of the definition this app created/retrieved | `Upsert/RetrieveOrderCustomAttribute` ← `Create/RetrieveOrderCustomAttributeDefinition` | gift-message service, before upsert/read |
| `ObtainToken.Code` is accepted only for a `state` this app issued and has not consumed | `ObtainToken` ← `GET /api/square/connect` (state claim) | callback, before `ObtainToken` |
| Only Square items carrying the eShop SKU marker are updated | `BatchUpsertCatalogObjects` ← `ListCatalog` | sync planner, before `BatchUpsertCatalogObjects` |

## 3. Trap notes

- Step 1 — the generated client's built-in authorization-code strategy blocks on an interactive prompt, and a 401 drops the cached refresh token, which matters in a web host nobody is sitting at. **MUST load `square:dotnet-authentication`**.
- Step 1 — `HttpClient` ownership and lifetime of the client versus the handler pipeline. **MUST load `square:dotnet-client-initialization`**.
- Step 1 — which methods the SDK resends, what `Timeout` bounds, what the default environment is, and what the log env var can switch on. **MUST load `square:dotnet-configuration-resilience`**.
- Steps 3–6 — request records versus bodies, required members, and what the injected header is (and is not). **MUST load `square:dotnet-calling-endpoints`**.
- Steps 4–6 — open enums with no factory, `object?` values (`Schema`, `CustomAttribute.Value`), full-replacement upserts and extension-data round-trips. **MUST load `square:dotnet-models`**.
- Every step — the catch ladder for Case B and the non-typed failures that escape it. **MUST load `square:dotnet-error-handling`**.
- Step 7 — which seam to fake and how to assert the request the SDK actually built. **MUST load `square:dotnet-testing`**.

## 4. REQUIRED READING (load all before implementation starts; this sheet deliberately does not carry their contents)

| Skill (plugin `square`) | Governs |
| --- | --- |
| `square:dotnet-authentication` | step 1, token strategy, fail-fast credentials |
| `square:dotnet-client-initialization` | step 1, DI and HttpClient |
| `square:dotnet-configuration-resilience` | step 1, retries, timeouts, logging, environment |
| `square:dotnet-calling-endpoints` | steps 3–6 |
| `square:dotnet-models` | steps 4–6 |
| `square:dotnet-error-handling` | all steps |
| `square:dotnet-testing` | step 7 |

Hazard (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`). (In scope: `Merchant.Country` and `CatalogObject.Type/Id` are `required`.)

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `SquareOptions` bound from `Square:`; `Environment`, `ApplicationId`, `ApplicationSecret`, `RedirectUri` are `[Required]`, and blank values are rejected with `ValidateOnStart()`. `Environment` must parse to `sandbox`/`production`, with no silent default to Production. `AccessToken` is optional. |
| 2 | Secret sourcing & rotation | User-secrets in dev, env vars or a secret store in deployment. App id and secret are read once at startup, so rotating them needs a restart. The OAuth tokens live in the DB, encrypted with Data Protection, and are read on every request (the client is per-request), so a refresh or reconnect takes effect without a restart. |
| 3 | Total timeout budget | 30 s per inbound request, set by a linked `CancellationTokenSource` passed to every SDK call. Per-attempt `Timeout` is 10 s. |
| 4 | Write-retry ownership | SDK retries stay at the default methods. None of the writes in scope are `PUT`; they are all POST, so the SDK never resends them. The app resends them only with the same real idempotency key. |
| 5 | Idempotency & ambiguous writes | `BatchUpsertCatalogObjects.idempotency_key`: hash of the planned batch content. `CreateCatalogImage.idempotency_key`: SHA-256 of item id + image bytes. `CreateOrder.idempotency_key`: GUID stored on `SquareOrderLink` before the call. `CreateOrderCustomAttributeDefinition.idempotency_key` and `UpsertOrderCustomAttribute.idempotency_key`: derived from key / order id. `ObtainToken`: none (a code is single-use, and the state claim gates it). |
| 6 | Observability | `ILogger` logs Square status, operation name and the error `code` at Warning. SDK `Logging.LoggerFactory` is set explicitly and `LogRequestBody` is off. |
| 7 | Sensitive data | Yes: `client_secret`, `code`, `refresh_token`, access tokens and the gift message. `LogRequestBody` stays off, `LoggerFactory` is assigned explicitly, and tokens and messages are never logged. |
| 8 | Environment selection | One group, `Default`. `Square:Environment` is mapped explicitly to `ServerEnvironment.Sandbox`/`Production`, and an unknown value fails at startup. |
| 9 | Duplicate prevention | See DUPLICATE CLAIMS. |
| 10 | Partial results | See PAGED READS. |
| 11 | Unknown outcomes | See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

| Write | Where the claim is stored | What rejects the second one | Where that rejection is caught | Where in the code |
| --- | --- | --- | --- | --- |
| OAuth code exchange (callback replay) | `SquareOAuthState` row (PK = SHA-256(state)) | delete of an already-deleted row → `DbUpdateConcurrencyException` | callback handler → 400 | TBD |
| Catalog sync (concurrent runs → 429 / duplicates) | `SquareSyncLock` row (PK = "catalog") | duplicate PK insert → `DbUpdateException`/`InvalidOperationException` | sync endpoint → 409 | TBD |
| Order placement (client retry with same `idempotencyKey`) | `SquareOrderLink` row (PK = OrderId) + unique claim row (PK = buyerId + key) | duplicate PK insert | order endpoint → returns 409 with the existing orderId | TBD |
| Photo upload (double submit) | `SquareImageClaim` row (PK = catalogItemId + content hash) | duplicate PK insert | photo endpoint → 409 | TBD |

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| `ListCatalog` (cursor) | max page count (safety bound) | sync aborts before writing and returns `complete: false` + 503. It never creates items from a partial view. | TBD |

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| `CreateOrder` | `CreateOrder` re-sent with the stored idempotency key (returns the original order). If that is still unknown, `SquareOrderLink.State = Unknown` and the next `GET /api/my-orders/{id}` settles it the same way. | stored idempotency key, `reference_id` = eShop order id | TBD | TBD |
| `BatchUpsertCatalogObjects` | `ListCatalog` | variation SKU marker `eshop-<catalogItemId>` | TBD | TBD |
| `CreateCatalogImage` | re-send with the same content-derived idempotency key | idempotency key | TBD | TBD |
| `UpsertOrderCustomAttribute` | re-send with the same key, then `RetrieveOrderCustomAttribute` | order id + definition key | TBD | TBD |

## 6. Assumptions & Blockers

### Blockers (plugin gaps; implementation stopped here)

- **B1 — Gift-message field: the custom-attribute `schema` value is not in the plugin.** `CreateOrderCustomAttributeDefinition` needs `CustomAttributeDefinition.Schema` ("This field is required when creating a definition", `Models/CustomAttributeDefinition.cs`). It is typed `object?`, and its only documentation is an external link ("see Custom Attributes Overview" on developer.squareup.com). No map page, model, enum, remark, README or skill in the plugin gives the schema value that makes a definition a **string** (text) attribute. The "Gift message" field, its staff-editable visibility and the read-back in `GET /api/my-orders/{id}` all depend on creating this definition. Supplying the value from general knowledge, or probing the live API with guessed payloads, is the invention the task forbids.
- **B2 — OAuth permission names (scopes) for the sign-in URL are not in the plugin.** The sign-in URL must request the permissions the shop needs: read the merchant profile and locations, read/write catalog items and images, read/write orders, and read/write order custom attributes. The plugin documents the `scope` query parameter as an optional free string (`OAuth2AuthorizationCodeCredentials.Scope`), with no list of permission names, no mapping from operations to required permissions, and no encoding for several scopes in the authorize URL. The only names it mentions are the three examples in `ObtainTokenRequest.Scopes` docs (`MERCHANT_PROFILE_READ`, `PAYMENTS_READ`, `BANK_ACCOUNTS_READ`); none covers catalog or orders. A merchant connecting through a URL built without the right scopes would get a token that cannot sync or create orders. Nobody signs in during this session, so this would never surface.

### Assumptions (non-blocking; decided)

- A1 — Orders created via the API, with no fulfillment or payment, are what "Square staff see". The plugin does not say how such orders appear in Square's seller UI (`UNVERIFIED`).
- A2 — The Square order references catalog variations (`catalog_object_id`) without overriding `base_price_money`. The plugin does not say how the two interact, so price parity comes from syncing the ordered items before `CreateOrder` and comparing the returned `base_price_money` (`UNVERIFIED`).
- A3 — Square catalog items created by eShop are identified by the variation SKU `eshop-<catalogItemId>` (`YOUR CALL — not in the map`). With the in-memory DB, links are rebuilt from Square on every sync, so a restart creates no duplicates.
- A4 — `Square:Environment` accepts `sandbox` / `production` (`YOUR CALL — not in the map`).
