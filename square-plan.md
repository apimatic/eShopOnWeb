# Square plan — `src/SquareCheck` (sign-in + account check CLI)

SDK source: `sdk/dotnet/` in the **square** plugin (paths below are relative to that SDK root).
Scope: OAuth authorization-code sign-in for one run → `Merchants.RetrieveMerchant` → `Locations.ListLocations`.

## 1. Scope & sequence

| # | Step | Operations / SDK surface |
| --- | --- | --- |
| 1 | Bind + validate `Square:Environment`, `Square:ApplicationId`, `Square:ApplicationSecret`, `Square:RedirectUri` (user-secrets, then env vars `SQUARE_*`); refuse to start on any blank/invalid one | — |
| 2 | Start loopback listener on the redirect address (before opening the browser); fail with one line if it cannot bind | — |
| 3 | Build the authorize URL: `{environment base URL}/oauth2/authorize` with `client_id`, `response_type=code`, `scope=MERCHANT_PROFILE_READ`, `state` (fresh random per run), `redirect_uri`; open browser **once** | base URL read from `ServerOptions.Default.{Sandbox|Production}.BaseUrl` |
| 4 | Wait ≤ 5 min for a callback whose `state` matches; others answered with a "not this sign-in" page and ignored | — |
| 5 | Exchange the code | `client.OAuth.ObtainToken` (JSON body, no auth) |
| 6 | Build the signed-in client: `Oauth2` credentials + own `Oauth2TokenStrategy` returning the exchanged token | `IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>` |
| 7 | Read merchant, finish the browser page naming the business | `client.Merchants.RetrieveMerchant` (`MerchantId = "me"`) |
| 8 | Read and print locations | `client.Locations.ListLocations` |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Each operation that takes input takes ONE request record as its first parameter, built with an object initializer using the record's own property names — never flat arguments. `ListLocations` takes no input and has no request parameter.
> ⚠ Every SDK type is written fully-qualified with the namespace its own source path implies (`Models/` → `Square.Models`, `Models/Enums/` → `Square.Models.Enums`, `Requests/<Controller>/` → `Square.Requests.<Controller>`, `Core/...` → the `namespace` declared in that file).

| Controller · method | Request record (members) | Body model (fields) | Response + fields read | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.OAuth` · `ObtainToken(ObtainTokenOperationRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` — POST `/oauth2/token`, JSON body, **no Auth bullet** (no credential sent) | `Square.Requests.OAuth.ObtainTokenOperationRequest { Body: ObtainTokenRequest, required }` | `Square.Models.ObtainTokenRequest`: `ClientId (client_id): string, required` · `GrantType (grant_type): string, required` = `"authorization_code"` · `ClientSecret (client_secret): string?` (code flow: required by remarks) · `Code (code): string?` (required for authorization_code) · `RedirectUri (redirect_uri): string?` (required because the authorize URL carries `redirect_uri`). Left out: `RefreshToken`, `MigrationToken`, `Scopes`, `ShortLived`, `CodeVerifier` (PKCE flow only), `UseJwt` | `Square.Models.ObtainTokenResponse`: `AccessToken (access_token): string?` · `TokenType (token_type): string?` · `ExpiresAt (expires_at): string?` · `MerchantId (merchant_id): string?` · `Errors (errors): IReadOnlyList<Error>?` | B — `ApiException<RawError>` | none | `map/operations/OAuth.md`; `Models/ObtainTokenRequest.cs`; `Models/ObtainTokenResponse.cs`; remarks `Api/OAuth.cs` |
| `client.Merchants` · `RetrieveMerchant(RetrieveMerchantRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` — GET, Auth `options.Oauth2` | `Square.Requests.Merchants.RetrieveMerchantRequest { MerchantId: string, required }` — `"me"` = the merchant accessible to this call | — | `Square.Models.RetrieveMerchantResponse`: `Merchant (merchant): Merchant?` → `Square.Models.Merchant`: `Id (id): string?` · `BusinessName (business_name): string?` · `Country (country): Country, **required**`; `Errors (errors)` | B — `ApiException<RawError>` | none | `map/operations/Merchants.md`; `Requests/Merchants/RetrieveMerchantRequest.cs`; `Models/RetrieveMerchantResponse.cs`; `Models/Merchant.cs` |
| `client.Locations` · `ListLocations(RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` — GET, Auth `options.Oauth2`; lists all locations incl. inactive, alphabetical | — (no input) | — | `Square.Models.ListLocationsResponse`: `Locations (locations): IReadOnlyList<Location>?` (either `errors` or `locations`) → `Square.Models.Location`: `Id (id)`, `Name (name)`, `Status (status): LocationStatus?`, `Address (address): Address?` → `Square.Models.Address`: `AddressLine1/2/3 (address_line_1/2/3)`, `Locality (locality)`, `AdministrativeDistrictLevel1 (administrative_district_level_1)`, `PostalCode (postal_code)`, `Country (country): Country?` — all `string?` unless noted | B — `ApiException<RawError>` | none (single response) | `map/operations/Locations.md`; `Models/ListLocationsResponse.cs`; `Models/Location.cs`; `Models/Address.cs` |

Error body model (read defensively from `RawError.ReadAsString()`): `Square.Models.Error` — `Category (category): ErrorCategory, required` · `Code (code): ErrorCode, required` · `Detail (detail): string?` · `Field (field): string?` — `Models/Error.cs`.

**Enums used**

| Enum | Values | Source |
| --- | --- | --- |
| `Square.Models.Enums.LocationStatus` | `Active` = `ACTIVE`, `Inactive` = `INACTIVE` (open enum — print `.Value`) | `Models/Enums/LocationStatus.cs` |
| `Square.Models.Enums.Country` | ISO 3166-1 alpha-2, e.g. `Zz` = `ZZ` (print `.Value`) | `Models/Enums/Country.cs` |
| `Square.Servers.ServerEnvironment` | `Production` = `production` (default), `Sandbox` = `sandbox`, `Custom` = `custom` (closed) | `Servers/ServerEnvironment.cs` |

**Client / auth / server facts**

| Fact | Source |
| --- | --- |
| Only ctor `new Square.SquareClient(HttpClient, Square.SquareClientOptions)` | `sdk-map.md` § Getting a client |
| `Oauth2: Square.Core.Authentication.OAuth2.AuthorizationCode.OAuth2AuthorizationCodeCredentials?` — `ClientId` (required), `ClientSecret?`, `RedirectUri` (required), `Scope?`, `State?`, `Pkce?` (default S256), `PromptForAuthorizationCode` (required delegate `Task<string>(string authorizationUrl, CancellationToken)`) | `Core/Authentication/OAuth2/AuthorizationCode/OAuth2AuthorizationCodeCredentials.cs` |
| `Oauth2TokenStrategy: IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>?` (`Square.Core.Authentication.OAuth2`) — `GetToken(creds, ct)` / `TryRefreshToken(creds, refreshToken, ct)` (null ⇒ full re-auth). Used only when `Oauth2` is set. Token applied as `Authorization: Bearer {AccessToken}` | `Core/Authentication/OAuth2/IOAuth2RefreshableTokenStrategy.cs`; `Core/Authentication/OAuth2/OAuth2RefreshableScheme.cs` |
| `Square.Core.Authentication.OAuth2.OAuthTokenRefreshable` — `AccessToken` (required), `TokenType` (required), `ExpiresIn: int?` (null ⇒ never expires), `Scope?`, `RefreshToken?` | `Core/Authentication/OAuth2/OAuthToken.cs`, `OAuthTokenRefreshable.cs` |
| Built-in authorization-code strategy (used when `Oauth2TokenStrategy` is null) authorizes at `server.Default("/oauth2/authorize")` with `response_type`, `client_id`, `redirect_uri`, `scope`, `state`, PKCE; `State` is sent but **never checked**; token exchange is **form-url-encoded** to `/oauth2/token` | `AuthSchemes.cs`; `Core/Authentication/OAuth2/AuthorizationCode/OAuth2AuthorizationCodeStrategy.cs` |
| One server group `Default`: Production `https://connect.squareup.com`, Sandbox `https://connect.squareupsandbox.com`, Custom `{custom_url}`; defaults readable from `new Square.ServerOptions().Default.{Production|Sandbox}.BaseUrl` | `sdk-map.md` § Servers & auth; `Servers/DefaultOptions.cs` |
| `Square.Core.Configuration.RetryOptions` (all `required`; use `Default() with {…}`); `Square.Core.Configuration.LoggingOptions { LoggerFactory, LogRequestBody, … }`; `Square.Core.RequestOptions { LogLevel?, Hooks? }` — **no per-call retry/timeout** | `Core/Configuration/RetryOptions.cs`; `Core/Configuration/LoggingOptions.cs`; `Core/RequestOptions.cs` |
| Exceptions: `Square.Core.Exceptions.{ApiException<T>, ResponseDeserializationException, SdkConnectionException, SdkTimeoutException, AuthSchemeException, SdkException}`; `Square.Core.ErrorResponse.RawError` | `sdk-map.md` § Error-handling model |

**Trust judgment (source-visible):** the built-in strategy posts the code as a **form** body and deserializes into `OAuthToken` (requires `token_type`, reads `expires_in`), while the generated `ObtainToken` for the same route declares a **JSON** body and a response with `expires_at` and optional fields. The two disagree; this plan uses the generated `ObtainToken` contract. Also, the built-in prompt runs **inside** the first API call's retry attempt, so a 5-minute sign-in would be cut by the per-attempt `Timeout` and a retried attempt **prompts again** — incompatible with "opens the sign-in page only once". → sign-in runs before any authenticated call; `Oauth2TokenStrategy` hands the exchanged token to the client.

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| The `code` passed to `ObtainToken` must come from the redirect that answers **this run's** authorize URL (same `state`) | `ObtainToken` ← authorize redirect built in step 3 | `RedirectListener.WaitForAuthorizationAsync` — fixed-time compare of callback `state` with the run's state before any code is accepted |
| `RedirectUri` sent to `ObtainToken` must equal the one in the authorize URL | `ObtainToken` ← authorize URL | both built from the single validated `SquareSettings.RedirectUri` |
| `MerchantId` for `RetrieveMerchant` is `"me"` (the merchant the token belongs to) — never caller-supplied | `RetrieveMerchant` ← `ObtainToken` | `SquareAccountReader.GetMerchantAsync` (constant) |

## 3. Trap notes

| Step | Hazard → consequence | Load |
| --- | --- | --- |
| 1 | An unset credential yields a request with no credential, not an error — the failure shows up one round-trip later as a 401 far from its cause | MUST load `square:dotnet-authentication` |
| 4–5 | Where the built-in prompt runs relative to the retry pipeline and per-attempt timeout decides whether a slow operator triggers a second prompt | MUST load `square:dotnet-authentication`, `square:dotnet-configuration-resilience` |
| 5–8 | What the per-attempt `Timeout` actually bounds, and which verbs are resent, decides how long an operator can be left waiting | MUST load `square:dotnet-configuration-resilience` |
| 5 | The token-exchange body carries the client secret and the code; what the SDK logger may write, and what can turn it on from outside the code | MUST load `square:dotnet-configuration-resilience` |
| 5–8 | Which exception types reach the catch, and which escape a ladder built only on `ApiException<RawError>` | MUST load `square:dotnet-error-handling` |
| 7–8 | Enum values printed through interpolation do not print the wire value | MUST load `square:dotnet-models` |
| 6 | HttpClient / client lifetime and what the client owns (token cache) | MUST load `square:dotnet-client-initialization` |
| 5–8 | Request record vs flat args; `requestOptions` position before the token | MUST load `square:dotnet-calling-endpoints` |
| tests | Which seam to fake; request bodies disposed before assertions | MUST load `square:dotnet-testing` |

## 4. REQUIRED READING (load before implementation starts — the sheet does not carry their contents)

- `square:dotnet-authentication` — steps 1, 4–6
- `square:dotnet-client-initialization` — step 6 (client + HttpClient)
- `square:dotnet-calling-endpoints` — steps 5, 7, 8
- `square:dotnet-models` — steps 7–8 (enums, nullable models)
- `square:dotnet-error-handling` — steps 5, 7, 8 (error boundary)
- `square:dotnet-configuration-resilience` — steps 5–8 (timeouts, retries, logging)
- `square:dotnet-testing` — tests

Hazard row (verbatim): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`). (Here: `Merchant.Country` is `required`.)

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `SquareSettings` bound from `Square:` section; `SquareSettings.Validate()` runs before the listener/browser/client exist and names every missing/blank/invalid key (`Square:Environment`, `Square:ApplicationId`, `Square:ApplicationSecret`, `Square:RedirectUri`), never the value; tool exits 3 with one line. Environment must be `sandbox` or `production`; redirect must be absolute `http` on a loopback host. |
| 2 | Secret sourcing & rotation | Sources: .NET user-secrets (`UserSecretsId` on the project) overridden by env vars `SQUARE_*` (and `Square__*`). Read once per process; every run is a new process, so rotation takes effect on the next run. N/A beyond that — the tool is short-lived. |
| 3 | Total timeout budget | Sign-in wait: 5 min (own deadline, outside the SDK). After the code arrives: one 60 s deadline (`CancellationToken`) covers ObtainToken + RetrieveMerchant + ListLocations. Per attempt: `Retry.Timeout` 15 s, `HttpClient.Timeout` 20 s backstop. Ctrl+C token linked into all of it. |
| 4 | Write-retry ownership | Only write-shaped call is `ObtainToken` (POST) — default `HttpMethodsToRetry` never resends POST, and the code is single-use, so it must not be resent. GETs (`RetrieveMerchant`, `ListLocations`) keep default retries (≤ 3), bounded by the 60 s deadline. No PUT in scope. |
| 5 | Idempotency & ambiguous writes | `ObtainTokenRequest` has no caller-supplied idempotency key (the injected `Idempotency-Key` header is not one). The exchange creates no merchant-visible state the tool must reconcile: an ambiguous outcome leaves at most an unused token server-side; the run reports failure (exit 1) and the next run signs in fresh. |
| 6 | Observability | SDK logger off: `LoggerFactory = NullLoggerFactory.Instance` assigned explicitly (disables `SQUARECLIENT_LOG`), `LogRequestBody = false`. Operator output: progress on stderr, account on stdout, one failure line on stderr that includes HTTP status and Square `errors[].code/detail` when the body carries them. No stack traces. |
| 7 | Sensitive data | Yes: `ObtainTokenRequest.ClientSecret`, `Code`; the access token. Never printed or logged; settings type redacts in `ToString`; SDK logging forced off as in row 6. Token lives only in memory for the run. |
| 8 | Environment selection | One server group `Default`. `Square:Environment` = `sandbox` → `ServerEnvironment.Sandbox` (`connect.squareupsandbox.com`), `production` → `ServerEnvironment.Production`; anything else (incl. `custom`) refused at startup. Authorize URL uses the same environment's base URL from `ServerOptions`. Development/testing config is sandbox; automated tests never reach the network (stub `HttpMessageHandler`). |
| 9 | Duplicate prevention under concurrency | N/A — no write a caller can trigger twice: the single-use code is exchanged once per run by construction (one callback accepted, listener closes). See DUPLICATE CLAIMS. |
| 10 | Partial results | N/A — `ListLocations` is not paginated (single response with all locations). |
| 11 | Unknown outcomes | `ObtainToken` connection failure: outcome is irrelevant to the merchant (a token we never received cannot be used); reported as Square failure, exit 1, and the operator re-runs (new code). See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

none — the tool performs no merchant-visible write; the only POST (`ObtainToken`) consumes a single-use code once per process.

**PAGED READS**

none — `ListLocations` has no pagination (map: silent ⇒ default).

**UNKNOWN OUTCOMES**

none — `ObtainToken` is the only POST and creates nothing the application stores or the merchant sees; no re-read is meaningful (the SDK offers no lookup for an unreceived token), and the next run starts a fresh sign-in.

## 6. Assumptions & Blockers

- UNVERIFIED: Square's redirect on decline follows RFC 6749 §4.1.2.1 (`error=access_denied` + `state`) — the plugin does not spell out the decline redirect. Defensive directive: matching `state` + `error=access_denied` → "declined" (exit 2); matching `state` + any other `error` → Square refused (exit 1, error shown); missing `code` and `error` → ignored.
- UNVERIFIED: `scope` on the authorize URL is a single value `MERCHANT_PROFILE_READ` (task fact).
- Assumption: exit code `3` for local setup problems (invalid settings, redirect port unusable) — not defined by the task.
- Assumption: code flow (client secret), no PKCE — `ObtainToken` remarks: code flow = `code` + `client_id` + `client_secret`.
- Blockers: none.

## 7. Verification record (after implementation)

- Live sandbox (`SQUARE_ACCESS_TOKEN`, opt-in tests `LiveSandboxTests`): `RetrieveMerchant("me")` and `ListLocations` succeed through `SquareClientFactory.CreateSignedIn` + `SquareAccountReader`; a rejected token surfaces as `ApiException<RawError>` 401 → `SquareRequestException`.
- Live sandbox `ObtainToken` (JSON, as in the contract row) with an unknown code → HTTP 401 `UNAUTHORIZED: Authorization code not found for app …` — the request shape reaches code validation with the configured app credentials. The successful exchange itself needs a real sign-in and stays covered by the offline tests only.
- Still UNVERIFIED live: the decline redirect shape (§6) and the browser leg of the sign-in.
- Build consequence: `src/SquareCheck` references the SDK project inside the plugin install (`SquareSdkProject` MSBuild property), and that project needs the .NET 10 SDK (C# 14). A machine or CI runner without the plugin cannot build the solution until it is installed and the property is pointed at it. The project fails with an explicit error naming the property.
