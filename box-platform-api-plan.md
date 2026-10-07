# Box Platform API integration plan — eShopOnWeb digital downloads

SDK source: `sdk/dotnet/` inside the **box** plugin (context-plugins marketplace). All `source` cells are relative to that SDK root.

## 1. Scope & sequence

| # | Step | Operations |
| --- | --- | --- |
| 1 | Env prerequisites: `global.json` rollForward `latestMajor`; PublicApi user-secret `Box:AccessToken` loaded from `BOX_ACCESS_TOKEN` | — |
| 2 | Infrastructure references the SDK project in place (`<ProjectReference>` to the plugin's `BoxPlatformApi.csproj`) | — |
| 3 | Client + auth: named `HttpClient` "Box", singleton `BoxPlatformApiClient`, `OAuth2SecurityTokenStrategy` that hands the SDK the configured developer token (`Box:AccessToken`) | — |
| 4 | Resolve the product folder: `Box:DigitalProductsFolderId` if set, else find `Box:DigitalProductsFolderName` (default `eshop-digital-products`) among root (`"0"`) entries, then confirm it is a folder | `Folders.GetFoldersIdItems`, `Folders.GetFoldersId` |
| 5 | List files in that folder (id, name, size), paged with a cap | `Folders.GetFoldersIdItems` |
| 6 | Link catalog item → Box file id; the id must be a readable file in the step-5 listing; the link row is stored in `CatalogContext` | `Folders.GetFoldersIdItems` |
| 7 | `POST /api/orders` → existing `Order`/`OrderItem` aggregate | — (local) |
| 8 | Download: owner + item-in-order + link checks, then stream Box content through with a 30 s stall guard | `Downloads.GetFilesIdContent` |
| 9 | Tests: stub `HttpMessageHandler` behind a real SDK client (no network), plus PublicApi endpoint tests with a fake provider | — |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Each operation that takes input takes ONE request record as its first parameter. Build it with an object initializer whose property names are the record's own, never flat arguments. `requestOptions` sits before `cancellationToken`, so always pass `cancellationToken:` by name.
> ⚠ Write every SDK type fully qualified with the namespace its source path implies, taken from the path the map gives for THAT type (`Requests/Folders/*` → `BoxPlatformApi.Requests.Folders`, `Models/*` → `BoxPlatformApi.Models`, `Models/AnyOf/*` → `BoxPlatformApi.Models.AnyOf`, `Errors/*` → `BoxPlatformApi.Errors`, `Core/Models/BinaryContent.cs` → `BoxPlatformApi.Core.Models`). Never take it from where a neighbouring type sits.

| Controller · method | Request record (members used) | Body | Response (fields read) | Error case + accessors | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.Folders` · `GetFoldersIdItems(GetFoldersIdItemsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `BoxPlatformApi.Requests.Folders.GetFoldersIdItemsRequest`: `FolderId: string, required` · `Fields: IReadOnlyList<string>?` · `Offset: long` (default 0) · `Limit: long?` (`[Maximum(1000)]`) · left out: `Usemarker`, `Marker`, `Sort`, `Direction`, `Boxapi` | none (GET) | `BoxPlatformApi.Models.Items`: `Entries (entries): IReadOnlyList<Item21>?` · `TotalCount (total_count): long?` · `Offset (offset): long?` · `Limit (limit): long?`. Each `BoxPlatformApi.Models.AnyOf.Item21` is read with `TryGetFileFull(out FileFull?)` / `TryGetFolderMini(out FolderMini)` / `TryGetWebLink(out WebLink)`. `BoxPlatformApi.Models.FileFull`: `Id (id): string, required` · `Name (name): string?` · `Size (size): int?` · `Sha1 (sha1): string?` · `FileVersion (file_version): FileVersion1?` · `AdditionalProperties` (extension data). `BoxPlatformApi.Models.FolderMini`: `Id (id): string, required` · `Name (name)` | **A** `ApiException<BoxPlatformApi.Errors.GetFoldersIdItemsError>`: `TryGetClientError(out BoxPlatformApi.Models.ClientError)` [403, 404, 405] · `TryGetRawError(out RawError)` [fallback] | none at SDK level (map silent). Offset paging is hand-driven via `Offset`/`Limit`/`TotalCount` | `map/operations/Folders.md`; `Requests/Folders/GetFoldersIdItemsRequest.cs`; `Models/Items.cs`; `Models/AnyOf/Item21.cs`; `Models/FileFull.cs`; `Models/FolderMini.cs` |
| `client.Folders` · `GetFoldersId(GetFoldersIdRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `BoxPlatformApi.Requests.Folders.GetFoldersIdRequest`: `FolderId: string, required` · `Fields: IReadOnlyList<string>?` | none (GET) | `BoxPlatformApi.Models.FolderFull`: `Id (id): string, required` · `Name (name): string?` | **A** `ApiException<BoxPlatformApi.Errors.GetFoldersIdError>`: `TryGetClientError(out ClientError)` [403, 404, 405] · `TryGetRawError(out RawError)` | none | `map/operations/Folders.md`; `Requests/Folders/GetFoldersIdRequest.cs`; `Models/FolderFull.cs` |
| `client.Downloads` · `GetFilesIdContent(GetFilesIdContentRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` | `BoxPlatformApi.Requests.Downloads.GetFilesIdContentRequest`: `FileId: string, required` · left out: `Version`, `AccessToken`, `Range`, `Boxapi` | none (GET) | `BoxPlatformApi.Core.Models.BinaryContent` (IDisposable/IAsyncDisposable): `Stream: Stream` (the live response body, sent with `HttpCompletionOption.ResponseHeadersRead`, not buffered) · `FileName: string?` (from Content-Disposition `filename*`/`filename`) · `ContentType: MediaTypeHeaderValue` (falls back to `application/octet-stream`) | **B** `ApiException<RawError>` | none | `map/operations/Downloads.md`; `Api/Downloads.cs`; `Requests/Downloads/GetFilesIdContentRequest.cs`; `Core/Models/BinaryContent.cs`; `Core/Response/BinaryResponse.cs` |

`ClientError` (`Models/ClientError.cs`): `Status (status): int?` · `Code (code): BoxPlatformApi.Models.Enums.Code?` · `Message (message): string?` · `RequestId (request_id): string?`. `Code` values matter only for logging (`.Value`). No enum value table is needed for control flow.

**Client construction / auth / servers** (`sdk-map.md`, `BoxPlatformApiClient.cs`, `AuthSchemes.cs`, `ServiceCollectionExtensions.cs`, `Core/Authentication/OAuth2/*`):
- Constructor: `new BoxPlatformApi.BoxPlatformApiClient(HttpClient httpClient, BoxPlatformApiClientOptions options)`. Options are read at construction.
- Auth surface: `OAuth2Security: OAuth2AuthorizationCodeCredentials?` (required members `ClientId`, `RedirectUri`, `PromptForAuthorizationCode`) plus `OAuth2SecurityTokenStrategy: IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>?` (`GetToken(creds, ct) → Task<OAuthTokenRefreshable>`, `TryRefreshToken(creds, refreshToken, ct) → Task<OAuthTokenRefreshable?>`). `OAuth2RefreshableScheme.Create` returns `NoneAuthScheme` when `OAuth2Security` is null, so the credentials object MUST be non-null for the strategy to run. Its `ClientId`/`RedirectUri` are never read by a custom strategy. Probe-verified: the request goes out as `Authorization: Bearer <token>`.
- `OAuthToken.IsExpired` returns false when `ExpiresIn` is null, so a token with no `ExpiresIn` is cached until a 401 calls `Invalidate()`.
- Environment `ServerEnvironment.Production` (default). All three operations use server group `Default` = `https://api.box.com/2.0`. `AuthServer`/`AccessTokenServer` are never contacted because the custom strategy replaces the token exchange.
- `StreamReadTimeout` is used by SSE responses only. `BinaryResponse.Map` ignores it, so **no SDK timeout bounds the download body**. The stall guard is ours.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| A Box file id accepted by `PUT /api/catalog-items/{id}/digital-file` must be one the product-folder listing returns as a readable file | link ← `Folders.GetFoldersIdItems` (product folder) | `DigitalFileService.LinkAsync` calls `IDigitalFileProvider.ListFilesAsync` (→ `BoxDigitalFileProvider.ListProductFolderAsync`) and matches the id before any save |
| The folder id the listing reads must be one the root listing returned under the configured name (or the configured id), confirmed as a folder | `Folders.GetFoldersIdItems(product)` ← `Folders.GetFoldersIdItems("0")` + `Folders.GetFoldersId` | `BoxDigitalFileProvider.ResolveFolderIdAsync`, before any product-folder listing |
| A file id sent to `Downloads.GetFilesIdContent` must be one stored by a link (and so one the listing returned) | download ← link row (← `Folders.GetFoldersIdItems`) | `DigitalFileService.AuthorizeDownloadAsync` reads `CatalogItemDigitalFile`. `DownloadOrderItemEndpoint` never takes a file id from the caller |
| A `catalogItemId` downloaded must be an item of an order whose `BuyerId` is the caller | download ← `POST /api/orders` result | `DigitalFileService.AuthorizeDownloadAsync` |

### Contract trust notes (evidence: SDK source + offline probe against the compiled SDK)
- `Item21` is an AnyOf that tries `FileFull?` first, and `FileFull` requires only `id`. Its `Type` is a get-only constant `"file"`, so the wire `type` is dropped. **Probe-verified:** folder and web-link entries come back as `TryGetFileFull == true` with `Type == "file"`. Directive: classify an entry as a file only when `Sha1 != null || FileVersion != null`. Treat an entry carrying `url` in `AdditionalProperties` as a web link. Confirm a folder candidate with `GetFoldersId`. Live-verified 2026-10-07: both test files carried `sha1` and `file_version`, and the folder carried neither.
- `FileFull.Size` is `int?`. **Probe-verified:** an entry with `size` > `int.MaxValue` (a file of 2 GiB or more, or a large folder when `size` is requested) fails `FileFull` and lands as `FolderMini`. Directive: never treat a `FolderMini`/`WebLink` variant from the product folder as absent. Report it as an unreadable entry and mark the listing incomplete. A link to such an id is refused with a clear reason.
- Box download redirect: `Api/Downloads.cs` `<remarks>` says only "Returns the contents of a file in binary format". Directive: keep the default `AllowAutoRedirect` on the "Box" primary handler. **Live-verified (2026-10-07):** the call is redirected to `public.boxcloud.com` and followed transparently. The final response carries `Content-Length`, `Content-Type: application/pdf`, and a Content-Disposition file name. The SHA-1 of the bytes equals the listing's `sha1`.
- `Fields` serialization (**live-verified**): a multi-element `Fields` list goes on the wire as repeated `fields=a&fields=b…`, and Box honours only the last one. The request record's own doc says "A comma-separated list of attributes". Directive: pass ONE element holding the comma-joined list (`Fields = ["type,id,name,size,sha1,file_version,extension"]`). It is sent as `fields=type%2Cid%2C…` and Box returns every requested field. Also verified live: files carry `sha1` and `file_version`, and the folder entry carries neither, so the file-classification directive above holds.
- `BinaryContent` in this `netstandard2.0` build implements `IDisposable` only (the `IAsyncDisposable` part is `#if NET6_0_OR_GREATER`). Use `using`/`Dispose()`, not `await using`.

## 3. Trap notes

| Step | Hazard → consequence | Skill |
| --- | --- | --- |
| 3 | HttpClient/handler lifetime and the SDK client's own token cache. The wrong lifetime gives stale DNS or a token fetch per request. | MUST load `box:dotnet-client-initialization` |
| 3 | The credential is a pre-issued developer token, not an auth-code grant. Misusing `OAuth2Security` triggers an interactive prompt or the token exchange in a headless host. A blank token boots and 401s later. | MUST load `box:dotnet-authentication` |
| 3, 5, 8 | What `Retry.Timeout` vs `HttpClient.Timeout` vs a token actually bound, and which verbs retry. Get it wrong and a hung Box call holds a shopper's request for minutes. | MUST load `box:dotnet-configuration-resilience` |
| 5 | Hand-driven offset paging has no SDK stop guarantee. Unbounded, it spins. Truncated silently, it lies to the operator. | MUST load `box:dotnet-configuration-resilience` |
| 5, 6 | AnyOf variant order and union readers. Reading the wrong variant drops or misclassifies entries. | MUST load `box:dotnet-models` |
| 4–8 | Which exception reaches each catch, and status vs typed body. An incomplete ladder lets a 2xx/4xx body mismatch or a connection failure escape as a 500 that leaks SDK text. | MUST load `box:dotnet-error-handling` |
| 8 | A `BinaryContent` body read is not covered by SDK exceptions or timeouts. A drop or stall mid-body surfaces differently, and a half-sent response can look complete. | MUST load `box:dotnet-error-handling`, `box:dotnet-calling-endpoints` |
| 9 | The SDK test seam and request-body disposal timing. Tests that mock our own wrapper prove nothing about the SDK wiring. | MUST load `box:dotnet-testing` |

## 4. REQUIRED READING (load all before implementation starts; this sheet deliberately does not carry their contents)

- `box:dotnet-client-initialization` — step 3 (client/DI/HttpClient)
- `box:dotnet-authentication` — step 3 (token strategy, fail-fast)
- `box:dotnet-calling-endpoints` — steps 4–8 (request records, `BinaryContent`)
- `box:dotnet-models` — steps 5–6 (`Item21` union, `AdditionalProperties`)
- `box:dotnet-error-handling` — steps 4–8 (catch ladder at the provider boundary)
- `box:dotnet-configuration-resilience` — steps 3, 5, 8 (timeouts, retries, paging bounds, logging)
- `box:dotnet-testing` — step 9
- Hazard row (verbatim): a body that does not match its declared type surfaces as `ResponseDeserializationException`. That covers a drifted or malformed **2xx** response (a missing `required` member) and a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape. The exception is an `ApiException` that keeps the HTTP status and names the target type, but it is **not** an `ApiException<TError>`. A catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `BoxOptions` is bound from section `Box`. `AccessToken` is `[Required]` and must be non-whitespace. `ValidateDataAnnotations().ValidateOnStart()` makes PublicApi refuse to start, with a message naming `Box:AccessToken` and never echoing the value. Single-part credential. Folder name has a default; folder id is optional. |
| 2 | Secret sourcing & rotation | Source is `Box:AccessToken` from user-secrets (dev), env `Box__AccessToken`, or `BOX_ACCESS_TOKEN` mapped onto `Box:AccessToken` at startup when the key is otherwise empty. The token strategy reads `IOptionsMonitor<BoxOptions>.CurrentValue` on each `GetToken` and returns `ExpiresIn = Box:TokenCacheSeconds` (default 300), so the SDK re-reads config within that window, and immediately after any 401 (`Invalidate`). A rotated secret takes effect without a restart. The SDK client itself is a singleton built once. |
| 3 | Total timeout budget | Per attempt: `HttpClient.Timeout` = `Retry.Timeout` = `Box:AttemptTimeoutSeconds` (default 15 s). `MaxRetries` = 2 (GET only). Whole call: a linked `CancellationToken` deadline of `Box:CallBudgetSeconds` (default 40 s) around every metadata call and around opening a download (until headers). It is enforced in `BoxDigitalFileProvider.Bounded`. Download body: no total cap (files can be large). Each body read is bounded by `Box:StallTimeoutSeconds` (default 30 s) in `StallGuardStream`, and the shopper's `RequestAborted` cancels it. |
| 4 | Write-retry ownership | No Box writes in scope. All three operations are GET and may be resent by the SDK (default `HttpMethodsToRetry`). The list is not widened. |
| 5 | Idempotency & ambiguous writes | No Box writes, so no provider key is needed. Local writes: `PUT …/digital-file` is an upsert keyed by `CatalogItemId` (the primary key), idempotent by construction. `POST /api/orders` takes no key, matching the existing checkout. A resubmit creates a second order, which is harmless for downloads (YOUR CALL — not in the map). |
| 6 | Observability | `options.Logging.LoggerFactory` is set explicitly from DI. SDK request/response lines at Information, failures at Warning/Error, headers and body off. Our adapter logs Box failures at Warning with status, Box `code` and `request_id` (from `ClientError`). Download stalls, drops and short bodies are logged at Error with order id, catalog item id, Box file id and bytes sent. The token is never logged. |
| 7 | Sensitive data | Requests carry no body (all GET). The only secret is the bearer token, in `Authorization`, which the SDK masks by allow-list. `LogRequestBody` stays false, and `LoggerFactory` is assigned explicitly so `BOXPLATFORMAPICLIENT_LOG` cannot switch body/header logging on from outside. |
| 8 | Environment selection | `ServerEnvironment.Production`, server group `Default` (`https://api.box.com/2.0`) only. The groups `AuthServer`/`AccessTokenServer` exist but are untouched (custom token strategy). Environment2–5 are unused. The SDK declares no sandbox. Test traffic stays off Box because automated tests use a stub `HttpMessageHandler`, and live checks target only the folder named in config, read-only. |
| 9 | Duplicate prevention under concurrency | No Box writes, so DUPLICATE CLAIMS is `none`. The local link row's primary key (`CatalogItemId`) makes a concurrent double-link resolve to one row. |
| 10 | Partial results | The listing returns `DigitalFileListing { Files, IsComplete, UnreadableEntries }`. `IsComplete = false` when the page cap is hit or any entry could not be read as a file. The HTTP response carries `complete` and `unreadableEntries`. |
| 11 | Unknown outcomes | No Box writes, so UNKNOWN OUTCOMES is `none`. Reads are retried or fail cleanly. |

**DUPLICATE CLAIMS** — none (the scope makes no Box writes).

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| Product-folder listing (`GetFoldersIdItems`, offset paging, `Limit` 1000) | `Box:MaxListPages` (default 10) page cap + call budget | `DigitalFileListing.IsComplete == false` → response `complete: false`. Unreadable entries → `UnreadableEntries` | `BoxDigitalFileProvider.ListProductFolderAsync` (sets `IsComplete` from the `null` cap marker yielded by `ListFolderPagesAsync` and from unreadable entries), surfaced as `complete` by `DigitalFileListEndpoint.HandleAsync` |
| Root listing to resolve the folder by name | same page cap | Folder not found within the cap → `DigitalFileProviderException(Kind = FolderNotFound)` → 502 explaining that the folder could not be located (and to set `Box:DigitalProductsFolderId`) | `BoxDigitalFileProvider.ResolveFolderIdAsync` |

**UNKNOWN OUTCOMES** — none (the scope makes no Box writes).

## 6. Assumptions & Blockers

- Assumption: "Box id" in the link/download flows is the Box **file** id. The download always fetches the file's current version.
- Assumption: the folder `eshop-digital-products` is found among the root folder's direct children (resolution by name). `Box:DigitalProductsFolderId` overrides this for accounts where it lives elsewhere.
- Assumption: an order's shipping address is optional for digital purchases. When omitted, a fixed "digital delivery" address value object is stored to satisfy the existing `Order` model (YOUR CALL — not in the map).
- Limitation (SDK model, probe-verified): items whose `size` exceeds `int.MaxValue` cannot be read through `FileFull`. They are surfaced as unreadable entries and cannot be linked. Download streaming itself has no size limit.
- Blockers: none.

## 7. Verification record (2026-10-07)

- Offline: `dotnet test eShopOnWeb.sln` → 120 passed (new: `BoxDigitalFileProviderTests` and `DigitalFileServiceTests` in UnitTests, `DigitalDownloadFlowTest` in PublicApiIntegrationTests).
- Live, through PublicApi on `https://localhost:40063`: the listing returned both files (PDF 301034 B, PNG 183277 B, `complete: true`). Linking item 1 to the PDF returned 200, and linking a bogus id returned 422. The order was created. The PDF download returned 200, `application/pdf`, `Content-Disposition: attachment; filename=eshop-digital-guide.pdf`, 301034 bytes, SHA-1 `ccb84b5b…` = Box `sha1`. The PNG also matched Box's `sha1`. Another user, an item not in the order, and an item with no file each returned 404. No token returned 401.
- Fail-fast: started without `Box:AccessToken`, the host exits with `OptionsValidationException` naming the key.
