# Box Platform API integration plan — eShopOnWeb digital downloads

SDK root (plugin-relative): `sdk/dotnet/` of the **box** plugin. All `source` cells are relative to that root.

## 1. Scope & sequence

| # | Step | Box operations |
| --- | --- | --- |
| 1 | `global.json` roll-forward; reference `BoxPlatformApi.csproj` from `src/PublicApi` only (keeps Web untouched) | — |
| 2 | Bind `Box` config section (`Box:AccessToken` ← user-secrets / `Box__AccessToken` env), fail-fast validation, register singleton `BoxPlatformApiClient` with a static-token strategy | — |
| 3 | ApplicationCore: `CatalogItemDigitalFile` aggregate (PK = catalog item id), `IDigitalFileSource` port, direct `IOrderService.CreateOrderAsync(buyerId, lines, address)` reusing `Order`/`OrderItem` | — |
| 4 | Infrastructure: EF configuration for the link entity | — |
| 5 | PublicApi Box adapter: resolve folder `eshop-digital-products` under root `0`, list its files | `Folders.GetFoldersIdItems`, `Folders.GetFoldersId` |
| 6 | `GET /api/digital-files`, `PUT /api/catalog-items/{id}/digital-file` (admin) | via step 5 |
| 7 | `POST /api/orders` (shopper) | — |
| 8 | `GET /api/orders/{orderId}/downloads/{catalogItemId}` — streamed pass-through with 30 s stall watchdog | `Downloads.GetFilesIdContent` |
| 9 | Tests (no network: fake `HttpMessageHandler` behind the real SDK client + fake port for endpoint tests); live verification | — |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Each operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's own property names — never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type (e.g. `Requests/Folders/…` → `BoxPlatformApi.Requests.Folders`, `Models/AnyOf/…` → `BoxPlatformApi.Models.AnyOf`).

| Controller | Signature | Request record (members used) | Body | Response + fields read | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `client.Folders` | `GetFoldersIdItems(GetFoldersIdItemsRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `Items` | `BoxPlatformApi.Requests.Folders.GetFoldersIdItemsRequest`: `FolderId: string, required` · `Fields: IReadOnlyList<string>?` · `Usemarker: bool?` · `Marker: string?` · `Limit: long?` ([Maximum(1000)]) · (left out: `Offset`, `Sort` — unsupported with marker on root, `Direction`, `Boxapi`) | — | `BoxPlatformApi.Models.Items`: `Entries (entries): IReadOnlyList<Item21>?` · `NextMarker (next_marker): string?`. `BoxPlatformApi.Models.AnyOf.Item21`: `TryGetFileFull(out FileFull?)`, `TryGetFolderMini(out FolderMini)`, `TryGetWebLink(out WebLink)`. `BoxPlatformApi.Models.FileFull`: `Id (id): string, required` · `Name (name): string?` · `Size (size): int?` · `Sha1 (sha1): string?` · `FileVersion (file_version): FileVersion1?` | Case A `ApiException<BoxPlatformApi.Errors.GetFoldersIdItemsError>` · `TryGetClientError(out ClientError)` [403,404,405] · `TryGetRawError` | none generated (map silent) — app walks `Usemarker=true` + `NextMarker` itself | `map/operations/Folders.md`; `Requests/Folders/GetFoldersIdItemsRequest.cs`; `Models/Items.cs`; `Models/AnyOf/Item21.cs`; `Models/FileFull.cs` |
| `client.Folders` | `GetFoldersId(GetFoldersIdRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `FolderFull` | `BoxPlatformApi.Requests.Folders.GetFoldersIdRequest`: `FolderId: string, required` · `Fields: IReadOnlyList<string>?` (set to `["name"]`; nothing else set) | — | `BoxPlatformApi.Models.FolderFull`: `Id (id): string, required` · `Name (name)` | Case A `ApiException<BoxPlatformApi.Errors.GetFoldersIdError>` · `TryGetClientError(out ClientError)` [403,404,405] · `TryGetRawError` | none | `map/operations/Folders.md`; `Models/FolderFull.cs` |
| `client.Downloads` | `GetFilesIdContent(GetFilesIdContentRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` → `BinaryContent` | `BoxPlatformApi.Requests.Downloads.GetFilesIdContentRequest`: `FileId: string, required` · (left out: `Version`, `AccessToken` — would put a token in a URL, `Range`, `Boxapi`) | — | `BoxPlatformApi.Core.Models.BinaryContent` (IDisposable): `Stream: Stream` · `FileName: string?` (Content-Disposition `filename*`/`filename`) · `ContentType: MediaTypeHeaderValue` (falls back to `application/octet-stream`). Length is NOT on the return value → captured from `Content-Length` with a per-call `SdkHook.OnResponse` (`BoxPlatformApi.Core.Hooks`, via `RequestOptions.Hooks`) | Case B `ApiException<RawError>` (`StatusCode`, `ReadAsString()` …) | none | `map/operations/Downloads.md`; `Api/Downloads.cs`; `Requests/Downloads/GetFilesIdContentRequest.cs`; `Core/Models/BinaryContent.cs`; `Core/Response/BinaryResponse.cs`; `Core/RawClient.cs` (sends with `HttpCompletionOption.ResponseHeadersRead` → body is not buffered); `Core/RequestOptions.cs`; `Core/Hooks/SdkHook.cs` |

`ClientError` (`Models/ClientError.cs`, `BoxPlatformApi.Models`): `Status (status): int?` · `Code (code): Code?` · `Message (message): string?` · `RequestId (request_id): string?` — `RequestId` is the correlation id carried into our logs.

**Shape hazards found in source (decided here, not later):**
- `FileFull.Type`, `FolderMini.Type`, `FolderFull.Type` are get-only constants (`"file"`/`"folder"`), and `Item21`'s converter tries `FileFull` first with only `Id` required → a folder entry also matches `TryGetFileFull`. **Directive:** discriminate files by file-only fields: request fields `name,size,sha1,file_version` (sent as ONE comma-separated element — see §7); an entry is a file iff `Sha1 != null || FileVersion != null`. The root-level folder candidate is then confirmed with `GetFoldersId` (only resolves folders). `UNVERIFIED` that Box omits `sha1`/`file_version` on folders — the folder confirmation and the link-time membership check bound the damage.
- `FileFull.Size` is `int?` → a file ≥ 2 GiB fails `FileFull` deserialization and falls through the union. Recorded as a limitation (§6); download path does not depend on it (length comes from `Content-Length`).

**Enums:** none needed.

**Client construction / auth / server:** only ctor `BoxPlatformApiClient(HttpClient, BoxPlatformApiClientOptions)` (`BoxPlatformApiClient.cs`). Auth = `OAuth2Security` (`OAuth2AuthorizationCodeCredentials`: `ClientId` required, `RedirectUri` required, `PromptForAuthorizationCode` required) + `OAuth2SecurityTokenStrategy` (`IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>`: `GetToken(creds, ct)` → `OAuthTokenRefreshable { AccessToken, TokenType, ExpiresIn?, RefreshToken? }`, `TryRefreshToken(...)` → `null` = refresh failed). `AuthSchemes.cs`: credentials `null` ⇒ `NoneAuthScheme` (request sent unauthenticated) — so credentials MUST be non-null even though our strategy ignores them. Environment `ServerEnvironment.Production` → group `Default` `https://api.box.com/2.0`; all three operations are on `Default`. Sources: `BoxPlatformApiClientOptions.cs`, `AuthSchemes.cs`, `Core/Authentication/OAuth2/*.cs`, `sdk-map.md` Servers & auth.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| A Box file id accepted by `PUT /api/catalog-items/{id}/digital-file` must be a **file** entry returned by listing the `eshop-digital-products` folder | `PUT …/digital-file` ← `Folders.GetFoldersIdItems(folderId)` | `DigitalFileService.LinkAsync` (ApplicationCore) calls `IDigitalFileStorage.ListFilesAsync` and refuses (422) an id not among the listed files before `SaveLinkAsync` |
| The folder id passed to `GetFoldersIdItems` must be the one folder named `eshop-digital-products` returned by listing the root | `GetFoldersIdItems(folderId)` ← `GetFoldersIdItems("0")` + `GetFoldersId` | `BoxDigitalFileSource.ResolveFolderIdAsync` before any folder listing |
| A file id passed to `GetFilesIdContent` must be one stored by a successful link (i.e. previously in the listing) | `GetFilesIdContent` ← stored `CatalogItemDigitalFile.BoxFileId` | download endpoint reads the link row; never accepts a file id from the shopper |

## 3. Trap notes

| Step | Hazard | Consequence | Pointer |
| --- | --- | --- | --- |
| 2 | Lifetime of the `HttpClient` handed to a singleton SDK client | stale DNS / socket exhaustion | MUST load `box:dotnet-client-initialization` |
| 2 | Credential that is unset yields an unauthenticated request, not an exception | 401 at first call instead of failed boot | MUST load `box:dotnet-authentication` (loaded) |
| 2 | What `RetryOptions.Timeout` bounds vs. the whole call; which methods retry | a hung GET costs a multiple of the knob; download header phase can be resent | MUST load `box:dotnet-configuration-resilience` |
| 2 | Built-in SDK logger and `BOXPLATFORMAPICLIENT_LOG` env switch | headers/bodies switched on from outside the code | MUST load `box:dotnet-configuration-resilience` |
| 5,8 | Building request records / per-call `RequestOptions` | wrong member names, missing required members | MUST load `box:dotnet-calling-endpoints` |
| 5 | Union `Item21` reading and unknown-field bags | mis-classified entries | MUST load `box:dotnet-models` |
| 5,6,8 | Which exception types actually reach the catch ladder | Box failures escaping as 500s | MUST load `box:dotnet-error-handling` |
| 9 | Which seam to fake for SDK tests | tests coupled to SDK internals | MUST load `box:dotnet-testing` |

## 4. REQUIRED READING (load before implementation starts — this sheet deliberately does not carry their contents)

- `box:dotnet-client-initialization` — step 2 (client + DI)
- `box:dotnet-authentication` — step 2 (credentials, fail-fast)
- `box:dotnet-calling-endpoints` — steps 5, 8
- `box:dotnet-models` — step 5 (union, models)
- `box:dotnet-error-handling` — steps 5, 6, 8
- `box:dotnet-configuration-resilience` — steps 2, 8 (retries, timeouts, logging)
- `box:dotnet-testing` — step 9

Hazard row (verbatim requirement): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `Box:AccessToken` bound to `BoxOptions` with `[Required]` + `ValidateDataAnnotations().ValidateOnStart()` — PublicApi refuses to boot when blank; message names the key, never the value. The OAuth client id/secret/redirect are not used (pre-issued token), so only the token is validated. |
| 2 | Secret sourcing & rotation | Source: .NET user-secrets in Development (`Box:AccessToken`), env `Box__AccessToken` elsewhere. Token strategy reads `IOptionsMonitor<BoxOptions>.CurrentValue` on every `GetToken`, and returns `ExpiresIn = 60` so the SDK re-asks every ~30 s → a rotated token is picked up without restart (config providers with reload). The SDK client itself is built once. |
| 3 | Total timeout budget | Metadata calls (list/resolve): a `CancellationToken` deadline of `Box:RequestTimeout` (default 30 s) per Box call. Download: header phase bounded by the same deadline; body bounded by an idle watchdog of `Box:StallTimeout` (default 30 s) per read — no total cap (large files). |
| 4 | Write-retry ownership | No Box writes in scope: all three operations are GET → SDK may resend any of them (before response headers only). Local DB writes are never retried. |
| 5 | Idempotency & ambiguous writes | No Box writes → no key needed; none of the request records carries a key member. `PUT …/digital-file` is an idempotent upsert keyed by catalog item id; `POST /api/orders` intentionally creates a new order per call (no provider side effect). |
| 6 | Observability | App logs: Information on link/download start+completion (order id, catalog item id, Box file id, bytes), Warning on refused access, Error on Box failure/stall (with `ClientError.RequestId` when present, HTTP status). SDK logger: `LoggerFactory` set explicitly from DI; `LogRequestBody` off (no bodies anyway). Never log the token. |
| 7 | Sensitive data | Request records carry no personal data; the only secret is the bearer token (header). `LoggerFactory` assigned explicitly so `BOXPLATFORMAPICLIENT_LOG` cannot switch on tracing from outside; body logging stays off. `AccessToken` query member of `GetFilesIdContentRequest` is never set. |
| 8 | Environment selection | One server group used: `Default`, `ServerEnvironment.Production` (`https://api.box.com/2.0`); the SDK declares no sandbox. Test traffic: automated tests replace the `HttpClient` handler (no network); live checks are read-only GETs against the merchant folder only. |
| 9 | Duplicate prevention under concurrency | See DUPLICATE CLAIMS — none (no Box writes). |
| 10 | Partial results | Folder listing capped at `Box:MaxListingPages` (default 10) × `Box:PageSize` (default 1000) → `DigitalFileListing.IsTruncated` → `isTruncated` in the API response; root walk capped likewise → `FolderNotFound` error says the scan was truncated. |
| 11 | Unknown outcomes | None — no Box writes. |

**DUPLICATE CLAIMS**: none — the scope makes no Box write; every Box call is a GET. Local link writes are upserts on the catalog-item primary key.

**PAGED READS**

| Read | What caps it | How the caller learns the answer was cut short | Where in the code |
| --- | --- | --- | --- |
| Files in `eshop-digital-products` (`GetFoldersIdItems`, marker) | `Box:MaxListingPages` × `Box:PageSize`, plus a no-progress guard on the marker | `DigitalFileListing.IsTruncated` → `ListDigitalFilesResponse.IsTruncated`; a link refused against a truncated listing says so in its 422 detail | `BoxDigitalFileStorage.ReadFolderAsync` (returns `FolderPage.IsTruncated`) → `BoxDigitalFileStorage.ListFilesAsync` → `ListDigitalFilesEndpoint.HandleAsync` |
| Root walk to find the folder (`GetFoldersIdItems("0")`, marker) | `Box:MaxListingPages` × `Box:PageSize` | `DigitalFileStorageException` with `Failure = FolderNotFound` whose message states the scan was truncated (502 problem detail) | `BoxDigitalFileStorage.ResolveFolderIdAsync` → `BoxDigitalFileStorage.FolderNotFound(options, root.IsTruncated, …)` |

**UNKNOWN OUTCOMES**: none — no Box writes.

## 6. Assumptions & Blockers

- Assumption: `BOX_ACCESS_TOKEN` is a pre-issued developer token; the SDK's OAuth code flow is bypassed through its public `OAuth2SecurityTokenStrategy` extension point (placeholder non-secret `ClientId`/`RedirectUri` so the scheme is not `NoneAuthScheme`; prompt throws).
- Assumption: the folder is a direct child of the root (`0`) of the token's user. `YOUR CALL — not in the map`.
- Limitation (SDK shape): `FileFull.Size` is `int?` → files ≥ 2 GiB cannot appear in the listing. Not a blocker for the test files.
- `UNVERIFIED`: Box returns the content with a `Content-Length`; when absent the response is sent chunked and aborted on failure.
- Blockers: none.

## 7. Implementation notes (verified while coding)

| Fact | Evidence | Consequence in code |
| --- | --- | --- |
| `GetFoldersIdItemsRequest.Fields` (`IReadOnlyList<string>`) is sent as repeated `fields=` keys; the member doc says Box takes one comma-separated list. With repeated keys the live root listing returned no names. | live run (log: `fields=***&fields=***&fields=***`); `Requests/Folders/GetFoldersIdItemsRequest.cs` member docs | `BoxDigitalFileStorage` passes a single element `"name,size,sha1,file_version"` |
| `GetFoldersIdItemsRequest.Offset` defaults to `0L` and is always sent beside `usemarker=true`; Box accepted it. | live run | none |
| `GetFilesIdContent` followed Box's redirect and returned `Content-Length` (captured via per-call `SdkHook.OnResponse`). | live run: 301034 bytes, SHA-1 equal to Box's `sha1` | download announces the length; a short body is aborted |
| A 401 from Box carries no body → may surface as `ResponseDeserializationException` with status 401. | `dotnet-error-handling`; test `ReportsRejectedCredentialsWithoutABody` | non-2xx `ResponseDeserializationException` is mapped by status like any rejection |
