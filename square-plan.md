# square-plan.md — SquareCheck (sign-in + account check CLI)

SDK source: `sdk/dotnet/` inside the **square** plugin (plugin-relative; all *source* cells below are relative to that SDK root).

## 1. Scope & sequence

| # | Step | Operations / SDK surface |
| --- | --- | --- |
| 1 | `global.json` → `rollForward: latestMajor`; new console project `src/SquareCheck` (+ `tests/SquareCheck.UnitTests`), added to `eShopOnWeb.sln` and `Everything.sln`; `ProjectReference` to the plugin's `Square.csproj` | — |
| 2 | Configuration: bind `Square:Environment`, `Square:ApplicationId`, `Square:ApplicationSecret`, `Square:RedirectUri` from user-secrets + `SQUARE_*` env vars; validate, refuse to start on any blank value | `ServerEnvironment.TryGetKnownValue` |
| 3 | Loopback redirect listener (Kestrel, bound to the host/port of `Square:RedirectUri`), per-run random `state`, one-shot callback | — (application code) |
| 4 | Build `SquareClient` with `Oauth2` = `OAuth2AuthorizationCodeCredentials` whose `PromptForAuthorizationCode` opens the browser once and awaits the listener (5 min) | SDK built-in authorization-code flow (`/oauth2/authorize`, `/oauth2/token`) |
| 5 | Read merchant (this is the call that triggers sign-in + token exchange) | `Merchants.RetrieveMerchant` (`MerchantId = "me"`) |
| 6 | Read locations | `Locations.ListLocations` |
| 7 | Complete the browser's held callback response with the business name; print merchant + locations; map failures to exit codes 1 / 2 / 130 | — |
| 8 | Tests (offline, fake `HttpMessageHandler` + fake browser driving the real listener on port 0); live sandbox check of steps 5–6 with `SQUARE_ACCESS_TOKEN` via a test-only `Oauth2TokenStrategy` | `IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>` |

## 2. CONTRACT SHEET

> ⚠ Signatures are generated code, verbatim. Each operation that takes input takes ONE request record as its first parameter (an operation with no inputs takes none), built with an object initializer whose property names are the record's own — never flat arguments.
> ⚠ Every SDK type is written fully-qualified with the namespace its source path implies, taken from the path the map gives for THAT type — never from where a neighbouring type sits.

| Controller · signature | Request record + members | Body model | Response envelope → fields read | Error | Pagination | Source |
| --- | --- | --- | --- | --- | --- | --- |
| `client.Merchants` · `RetrieveMerchant(RetrieveMerchantRequest request, RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` · Auth `options.Oauth2` · GET `/v2/merchants/{merchant_id}` | `Square.Requests.Merchants.RetrieveMerchantRequest`: `MerchantId: string, required` — `"me"` = the merchant accessible to this call | none | `Square.Models.RetrieveMerchantResponse` → `Merchant (merchant): Square.Models.Merchant?`, `Errors (errors): IReadOnlyList<Square.Models.Error>?`. `Merchant` → `Id (id): string?`, `BusinessName (business_name): string?`, `Country (country): Country, required` | B — `Square.Core.Exceptions.ApiException<Square.Core.ErrorResponse.RawError>` | none | `map/operations/Merchants.md`; `Requests/Merchants/RetrieveMerchantRequest.cs`; `Models/RetrieveMerchantResponse.cs`; `Models/Merchant.cs`; `Api/Merchants.cs` |
| `client.Locations` · `ListLocations(RequestOptions? requestOptions = null, CancellationToken cancellationToken = default)` · Auth `options.Oauth2` | none | none | `Square.Models.ListLocationsResponse` → `Locations (locations): IReadOnlyList<Square.Models.Location>?`, `Errors (errors)` ("either errors or locations, never both"). `Location` → `Id (id)`, `Name (name): string?`, `Status (status): Square.Models.Enums.LocationStatus?`, `Address (address): Square.Models.Address?`. `Address` → `AddressLine1 (address_line_1)`, `AddressLine2 (address_line_2)`, `AddressLine3 (address_line_3)`, `Locality (locality)`, `AdministrativeDistrictLevel1 (administrative_district_level_1)`, `PostalCode (postal_code)`, `Country (country): Country?` — all optional | B — `ApiException<RawError>` | none (map silent → default) | `map/operations/Locations.md`; `Models/ListLocationsResponse.cs`; `Models/Location.cs`; `Models/Address.cs` |
| SDK OAuth2 authorization-code flow (not a controller op; runs inside `Apply` of the first `options.Oauth2` call) | `Square.Core.Authentication.OAuth2.AuthorizationCode.OAuth2AuthorizationCodeCredentials`: `ClientId: string, required` · `ClientSecret: string?` · `RedirectUri: string, required` · `Scope: string?` · `State: string?` · `Pkce: PkceMethod?` (default `S256`; `null` disables PKCE and then requires `ClientSecret`) · `PromptForAuthorizationCode: AuthorizationCodePrompt, required` = `Task<string>(string authorizationUrl, CancellationToken ct)` returning the `code` | authorize query: `response_type=code`, `client_id`, `redirect_uri`, `scope`, `state`, (+ `code_challenge`/`code_challenge_method` when PKCE). Token POST (form body): `grant_type=authorization_code`, `code`, `redirect_uri`, (`code_verifier`), `client_id`, `client_secret` | token → `Square.Core.Authentication.OAuth2.OAuthTokenRefreshable` (`access_token` required, `token_type` required, `expires_in`, `refresh_token`); cached for the client's lifetime | token failure surfaces as `ApiException<RawError>` (RawError response) thrown out of the first call | — | `Core/Authentication/OAuth2/AuthorizationCode/OAuth2AuthorizationCodeCredentials.cs`; `…/OAuth2AuthorizationCodeStrategy.cs`; `Core/Authentication/OAuth2/OAuth2RefreshableScheme.cs`; `AuthSchemes.cs` |
| Token-strategy seam (tests / live check only) | `options.Oauth2TokenStrategy: IOAuth2RefreshableTokenStrategy<OAuth2AuthorizationCodeCredentials>?` — `GetToken(TCredentials, CancellationToken) → Task<OAuthTokenRefreshable>`, `TryRefreshToken(…) → Task<OAuthTokenRefreshable?>`; `Oauth2` must still be non-null for the scheme to exist | — | — | — | — | `SquareClientOptions.cs`; `Core/Authentication/OAuth2/IOAuth2RefreshableTokenStrategy.cs`; `OAuth2RefreshableScheme.cs` (`Create`: null credentials → `NoneAuthScheme`) |

`ObtainToken` (`client.OAuth`, JSON body, `Models/ObtainTokenRequest.cs`) is **not** used: the SDK's own flow performs the exchange. Its `<remarks>` (in `Api/OAuth.cs`) define the two flows — *code flow*: `code`, `client_id`, `client_secret`; *PKCE flow*: `code`, `client_id`, `code_verifier`.

**Enums needed**

| Type | Members (C# → wire) | Source |
| --- | --- | --- |
| `Square.Models.Enums.LocationStatus` (open) | `Active` → `ACTIVE`, `Inactive` → `INACTIVE`, `otherwise(string)` | `Models/Enums/LocationStatus.cs` |
| `Square.Servers.ServerEnvironment` (closed) | `Production` → `production` (default), `Sandbox` → `sandbox`, `Custom` → `custom`; parse with `TryGetKnownValue` | `Servers/ServerEnvironment.cs`, `Core/Enum/TypedEnum.cs` |
| `Square.Core.Authentication.OAuth2.AuthorizationCode.PkceMethod` | `S256`, `Plain` | `…/AuthorizationCode/PkceMethod.cs` |

**Client / auth / servers**: only ctor `new Square.SquareClient(HttpClient, Square.SquareClientOptions)`. One server group `Default`: Production `https://connect.squareup.com`, Sandbox `https://connect.squareupsandbox.com`; the authorize/token URLs resolve through the same group (`server.Default("/oauth2/authorize")`, `server.Default("/oauth2/token")`). `RetryOptions` lives in `Square.Core.Configuration` (all members `required`; start from `RetryOptions.Default()`). Source: `sdk-map.md` → *Getting a client*, *Servers & auth*; `AuthSchemes.cs`; `Server.cs`.

**Facts that shape the design** (source-read, consequences are application decisions):

| Fact | Consequence | Label |
| --- | --- | --- |
| The prompt + token exchange run inside `AuthPolicies.Apply`, which is **inside** the per-call resilience pipeline (retry outer, timeout inner) | a per-attempt `RetryOptions.Timeout` shorter than 5 min would cut the sign-in wait and a retryable GET would re-prompt — the run's deadlines are owned by caller `CancellationToken`s instead | `Core/RawClient.cs`, `Core/ResiliencePipelineFactory.cs` |
| A 401 on any call calls `InvalidateRevocable()`; the next `Oauth2` call then re-runs `GetToken` → prompt again | the prompt delegate itself must refuse a second invocation (sign-in page opened once per run) | `Core/RawClient.cs`, `OAuth2RefreshableScheme.cs` |
| Built-in strategy posts the token exchange **form-encoded**, while generated `ObtainToken` posts JSON — two generated definitions that disagree | live-probed against sandbox with a bogus code: form body is parsed (same "Authorization code not found" 401 as JSON) | `OAuth2AuthorizationCodeStrategy.cs` vs `Api/OAuth.cs` — verified live |
| `Pkce` defaults to `S256`; with a `ClientSecret` set the exchange would send **both** `client_secret` and `code_verifier`, matching neither flow in `ObtainToken`'s remarks | set `Pkce = null` → pure *code flow* (`code`, `client_id`, `client_secret`) exactly as the remarks describe | `Api/OAuth.cs` remarks; `OAuth2AuthorizationCodeCredentials.cs` |
| The prompt contract: return the `code` from the redirect. What Square puts on the redirect when the seller **declines** is not in the plugin | defensive: a redirect whose `state` matches but carries no `code` is "not approved" → exit 2 (report `error`/`error_description` if present); `state` is required on every accepted redirect | **UNVERIFIED** |
| `Merchant.Country` is `required` | a 2xx missing it surfaces as `ResponseDeserializationException` → handled as a Square failure (exit 1) | `Models/Merchant.cs` |
| Anything thrown while credentials are applied (our prompt's own exceptions, the token exchange's `ApiException<RawError>` / `SdkConnectionException`) is rethrown as `AuthSchemeException` with the cause in `SchemeFailures`/`InnerException`; `OperationCanceledException` passes through unwrapped | the error boundary unwraps `AuthSchemeException` before classifying; Ctrl+C is recognised by the caller's own token | `Core/Authentication/IAuthScheme.cs` (`AuthSchemeExtensions.Apply`), `Core/Exceptions/AuthSchemeException.cs` |
| Live: the sandbox authorize URL built by the SDK (`response_type`, `client_id`, `redirect_uri`, `scope=MERCHANT_PROFILE_READ`, `state`) is answered by Square's sign-in app with "To start the OAuth flow for a sandbox account, first launch the seller test account from the Developer Console" when the browser has no sandbox session | operator guide: launch the sandbox seller test account in the same browser before running the tool | verified live (curl, no session) |

### CROSS-OPERATION INVARIANTS

| Invariant | Operations | Enforced where |
| --- | --- | --- |
| the redirect's `state` must equal the `state` this run put into the authorization URL | SDK authorize (built with `OAuth2AuthorizationCodeCredentials.State`) ← redirect callback | in the callback listener, before the `code` is handed back to the SDK (constant-time compare); non-matching visits are answered and ignored |
| the `code` exchanged must be one the redirect for **this** run's authorize request delivered | authorize redirect → token exchange (SDK) | the prompt returns only the code from the state-matching callback; the prompt runs at most once |

## 3. Trap notes

| Step | Hazard → consequence | Load |
| --- | --- | --- |
| 4 | Which `HttpClient` the client owns and how long it lives → socket/handle leaks or a disposed client mid-run | MUST load `square:dotnet-client-initialization` |
| 4 | How each credential property is applied and what happens when one is unset → a blank secret shows up as a confusing 401 instead of a startup refusal | MUST load `square:dotnet-authentication` |
| 5–6 | Request-record construction and where cancellation goes on each call → wrong overload / un-cancellable call on Ctrl+C | MUST load `square:dotnet-calling-endpoints` |
| 6 | `LocationStatus`/`Country` are open string enums, not C# enums → `switch` on them or `ToString` assumptions misprint unknown values | MUST load `square:dotnet-models` |
| 5–7 | Which exception types actually reach the catch for a Case B op, and how to read the body safely → an escaped exception prints a stack trace | MUST load `square:dotnet-error-handling` |
| 4 | What `Timeout`, retries and the log environment variable actually do → sign-in cut short, re-prompts, or the token exchange (carrying `client_secret` and `code`) printed to the console | MUST load `square:dotnet-configuration-resilience` |
| 8 | Which seam to fake for offline tests → tests that hit the network or couple to SDK internals | MUST load `square:dotnet-testing` |

## 4. REQUIRED READING (load ALL before implementation starts — this sheet deliberately does not carry their contents)

All from the **square** plugin (`square:` prefix — every APIMatic .NET plugin ships the same `dotnet-*` names):

- `square:dotnet-client-initialization` — step 4 (client construction, HttpClient lifetime)
- `square:dotnet-authentication` — step 4 (OAuth2 authorization-code credentials, fail-fast)
- `square:dotnet-calling-endpoints` — steps 5–6
- `square:dotnet-models` — step 6 (open enums, optional fields)
- `square:dotnet-error-handling` — steps 5–7 (error boundary → exit codes)
- `square:dotnet-configuration-resilience` — step 4 (retries, timeouts, logging)
- `square:dotnet-testing` — step 8

Hazard row (verbatim requirement): a body that does not match its declared type — a drifted or malformed **2xx** response (a missing `required` member) or a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape — surfaces as `ResponseDeserializationException`, an `ApiException` that keeps the HTTP status and names the target type but is **not** an `ApiException<TError>`; a catch ladder that handles only `ApiException<TError>` lets it escape, so it must also catch `ResponseDeserializationException` (or `ApiException`).

## 5. PRODUCTION READINESS

| # | Concern | Decision |
| --- | --- | --- |
| 1 | Credential fail-fast | `Square:Environment`, `Square:ApplicationId`, `Square:ApplicationSecret`, `Square:RedirectUri` bound into `SquareCheckSettings`; startup validation rejects any missing/blank/whitespace value, an unknown environment (`sandbox`/`production` only — `Custom` has no URL key here), and a redirect that is not an absolute `http` URL on a loopback host. On failure: one line naming the missing keys (never values), exit 3, before any browser or network activity. |
| 2 | Secret sourcing & rotation | .NET user-secrets (`UserSecretsId` in the csproj, stored outside the repo) overlaid by environment variables (`SQUARE_*` aliases and `Square__*`). Settings are read once per run; the process is a one-shot CLI, so a rotated secret takes effect on the next run — no in-process rotation needed. |
| 3 | Total timeout budget | SDK `RetryOptions.Timeout = null` (the sign-in runs inside the attempt). Budgets owned by `CancellationToken`s: sign-in wait **5 min** from browser launch; then **30 s** shared by token exchange + `RetrieveMerchant` (armed when the code arrives); **30 s** for `ListLocations`; `HttpClient.Timeout` infinite so the CTS deadline is the single bound. Worst case ≈ 5 min + 60 s. |
| 4 | Write-retry ownership | Only write is the SDK's token-exchange `POST` — never resent (POST not in retry methods). The two reads are `GET`s and may be retried by the SDK within their 30 s deadline. |
| 5 | Idempotency & ambiguous writes | Token exchange: no caller-supplied key exists (the injected `Idempotency-Key` is not one). An authorization code is single-use, so an ambiguous exchange cannot be replayed; reconciliation = fail the run (exit 1) and the operator signs in again (nothing is persisted). |
| 6 | Observability | SDK logging: `LogRequestBody = false`, `LoggerFactory` set explicitly to `NullLoggerFactory` (so `SQUARECLIENT_LOG` cannot switch logging on). The tool prints one plain line per failure: HTTP method + URL path (no query), status, and Square's first `errors[].code`/`detail`. No stack traces. |
| 7 | Sensitive data | Token-exchange form body carries `client_secret` and `code`; responses carry the access token → SDK body logging off and logger pinned (row 6); the access token is never printed or stored; the authorization URL printed for manual copy contains only `client_id`, `redirect_uri`, `scope`, `state`. |
| 8 | Environment selection | One server group `Default`. `Square:Environment=sandbox` → `connect.squareupsandbox.com` (development/testing — all of this task); `production` → `connect.squareup.com`. No default: a blank environment is refused, so a misconfigured box never falls through to production. |
| 9 | Duplicate prevention under concurrency | Only write is the token exchange, triggered once per run by the SDK under its own lock, from a one-shot prompt that refuses a second invocation; nothing persists between runs. See DUPLICATE CLAIMS. |
| 10 | Partial results | `ListLocations` is not paged (map default) — the full list is one response. N/A. |
| 11 | Unknown outcomes | Token exchange whose connection fails: no store to re-read (code is single-use, nothing persisted) → exit 1, operator re-runs. See UNKNOWN OUTCOMES. |

**DUPLICATE CLAIMS**

none — the only write (token exchange) is made by the SDK at most once per process: its scheme serialises acquisition under a lock, the prompt is one-shot (a second invocation throws before reaching Square), and the tool keeps no state between runs, so no caller can trigger it twice.

**PAGED READS**

none — `ListLocations` and `RetrieveMerchant` are single-response reads.

**UNKNOWN OUTCOMES**

| Write | The operation you re-read with | The reference you search by | Where in the code | The test that fails the connection |
| --- | --- | --- | --- | --- |
| SDK token exchange (`POST /oauth2/token`) | none exists — the only token-lookup op (`OAuth.RetrieveTokenStatus`) needs the token itself; see §6 | the run's authorization code (single-use) | `FailureClassifier.Classify` (unwraps `AuthSchemeException` → `SdkConnectionException` arm, exit 1, browser page "could not connect"), called from the `catch` in `SquareCheckApp.RunAsync` | `SquareCheckAppTests.SquareUnreachableDuringTheCodeExchange_SaysSo_ExitsOne_WithoutResendingTheCode` (asserts exactly one `POST /oauth2/token`, one browser open, one plain line) |

## 6. Assumptions & Blockers

- Assumption: scope = `MERCHANT_PROFILE_READ` (given by the task) covers both `RetrieveMerchant` and `ListLocations`. If Square refuses `ListLocations` under it, the tool reports it as a Square refusal (exit 1).
- Assumption: the redirect address is plain `http` on a loopback host (the configured one is). `https` redirects are refused at startup with a clear message — a local TLS listener is out of scope.
- Assumption: decline redirect shape (UNVERIFIED, above).
- Assumption: exit code `3` for "the tool cannot start" (bad configuration, redirect port busy) — the task defines 0/1/2/130 only.
- Known limit (not blocking): a token exchange whose connection fails after Square may have issued a token cannot be re-read — Square's only token lookup (`RetrieveTokenStatus`) needs the token itself. Nothing is persisted and the code is single-use, so the orphaned token is never usable by anyone and reaches no state of ours; the run ends with exit 1 and the operator signs in again on the next run.
- Blockers: none.
