# Twilio .NET SDK integration plan — eShopOnWeb SMS order notifications

SDK: `AsadAli.TwilioSdk` (APIMatic-generated) · root namespace `TwilioSdk` · targets `netstandard2.0` (consumable from the repo's `net8.0`) · map stamp: source commit `51fdf48`.

## 1. Scope & sequence

Implementation steps in order, with the SDK operations each uses:

1. **Package reference** — add `AsadAli.TwilioSdk` to the web project; central version in `Directory.Packages.props` (see §5).
2. **Options & DI registration** — build `TwilioSdkClientOptions` from `Twilio:*` config (credentials, messaging base-URL override), register via `AddTwilioSdkClient`.
3. **Phone-number validation** (shopper contact registration) — `client.LookupsV2PhoneNumber.FetchPhoneNumber3`.
4. **Send SMS immediately** — `client.Api20100401Message.CreateMessage` (with `from`, no scheduling params).
5. **Schedule a message** — `CreateMessage` with `messagingServiceSid` + `scheduleType` + `sendAt`.
6. **Cancel a scheduled message** — `client.Api20100401Message.UpdateMessage` with `status: Canceled`.
7. **Fetch message state by SID** — `client.Api20100401Message.FetchMessage`.
8. **Reconciliation list** — `client.Api20100401Message.ListMessage` with `from` + date filters, manual paging.
9. **Redact message body** — `UpdateMessage` with `body: ""`.
10. **Error boundary** — wrap all of the above; every in-scope operation is throw-only, Case B (`SdkException<RawError>`).
11. **Tests** — fake the `HttpClient` seam.

## 2. CONTRACT SHEET

> **Signatures are generated code, verbatim — every parameter name is the literal
> C# identifier. The cancellation-token parameter really is named `ct`: in named
> arguments write `ct:`, never `cancellationToken:`.**
>
> **Every SDK type is written fully-qualified with the namespace the map gives it** — take
> each one from that type's own map row, never from where a neighbouring type sits. A members
> table names the namespace outright; otherwise the row's source path implies it
> (`Core/Configuration/…` ⇒ `…Core.Configuration`; a file at the repo root ⇒ the root
> namespace). Enums, unions, auth, server and client-config types are spread across different
> child namespaces, and two types configured side by side in the same options object routinely
> live in different ones. Dropping a type to the root or to `.Models` makes the implementer
> guess the wrong `using`, and the build breaks.

### 2.1 Operations (all on `client.Api20100401Message` unless noted; every one is error Case B, throw-only, no `…Result` variant)

| Step | Operation (map page) | Signature (verbatim) | Returns | Error |
|---|---|---|---|---|
| 4, 5 | `CreateMessage` (`operations/Api20100401Message.md`) — `POST /2010-04-01/Accounts/{AccountSid}/Messages.json`, server **Default (api)** | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 24 params `statusCallback`…`contentSid` are nullable with **no C# default: pass each explicitly** (`null` to skip); use named arguments | `TwilioSdk.Models.ApiV2010AccountMessage` | `SdkException<RawError>` |
| 6, 9 | `UpdateMessage` (`operations/Api20100401Message.md`) — `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json`, server **Default (api)**. Map note: *"used to redact Message `body` text and to cancel not-yet-sent messages"* | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` must be passed explicitly | `ApiV2010AccountMessage` | `SdkException<RawError>` |
| 7 | `FetchMessage` (`operations/Api20100401Message.md`) — `GET …/Messages/{Sid}.json`, server **Default (api)** | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ApiV2010AccountMessage` | `SdkException<RawError>` |
| 8 | `ListMessage` (`operations/Api20100401Message.md`) — `GET …/Messages.json`, server **Default (api)** | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 params `to`…`pageToken` must be passed explicitly. Wire names: `To`←`to`, `From`←`from`, `DateSent`←`dateSent`, **`DateSent<`←`dateSentQuery`**, **`DateSent>`←`dateSentQueryQuery`**, `PageSize`←`pageSize`, `Page`←`page`, `PageToken`←`pageToken`. **Pagination: none in the SDK** (no pager; `page` only, no `perPage` cursor helper) — iterate manually via `page`/`pageToken` and the response's `NextPageUri` | `TwilioSdk.Models.ListMessageResponse` | `SdkException<RawError>` |
| 3 | `client.LookupsV2PhoneNumber.FetchPhoneNumber3` (`operations/LookupsV2PhoneNumber.md`) — `GET /v2/PhoneNumbers/{PhoneNumber}`, server **Default4 (lookups)** | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 15 params `fields`…`partnerSubId` must be passed explicitly (all `null` for plain validation) | `TwilioSdk.Models.LookupResponse` | `SdkException<RawError>` |
| (alt) | `client.LookupsV1PhoneNumberApi.FetchPhoneNumber2` (`operations/LookupsV1PhoneNumberApi.md`), server **Default4 (lookups)** — **not recommended**: its response `LookupsV1PhoneNumber` has **no validity flag** (`records-4-Li-Me.md`) | `FetchPhoneNumber2(string phoneNumber, string? countryCode, IReadOnlyList<string>? type, IReadOnlyList<string>? addOns, object? addOnsData, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `TwilioSdk.Models.LookupsV1PhoneNumber` | `SdkException<RawError>` |
| (not used) | `DeleteMessage` (`operations/Api20100401Message.md`) — `DELETE …/Messages/{Sid}.json`. **Not the redaction op**: it deletes the whole Message resource, destroying the record the reconciliation needs. Redaction is `UpdateMessage` with `body: ""` | `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `void` | `SdkException<RawError>` |

### 2.2 Response/request models (records pages: `records-1-Ac-Ca.md`, `records-4-Li-Me.md`; namespace `TwilioSdk.Models`)

`ApiV2010AccountMessage` (returned by Create/Update/Fetch; element type of List) — all fields nullable, `init`-only:

| Field (wire) | Type | Used for |
|---|---|---|
| `Sid (sid)` | `string?` | message SID |
| `Status (status)` | `MessageEnumStatus?` | delivery state (§2.3) |
| `ErrorCode (error_code)` | `int?` | provider error code |
| `ErrorMessage (error_message)` | `string?` | provider error message |
| `DateCreated (date_created)` | `string?` | **string, not DateTime** — parse if needed |
| `DateSent (date_sent)` | `string?` | same |
| `DateUpdated (date_updated)` | `string?` | same |
| `From (from)` / `To (to)` | `string?` | endpoints |
| `Body (body)` | `string?` | text (empty after redaction) |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | service used |
| `Direction (direction)` | `MessageEnumDirection?` | inbound/outbound |
| `NumSegments (num_segments)`, `NumMedia (num_media)`, `Price (price)`, `PriceUnit (price_unit)`, `AccountSid (account_sid)`, `Uri (uri)`, `ApiVersion (api_version)`, `SubresourceUris (subresource_uris): object?` | — | informational |

`ListMessageResponse`: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` plus paging fields `End (end): int?`, `FirstPageUri (first_page_uri): string?`, `NextPageUri (next_page_uri): string?`, `Page (page): int?`, `PageSize (page_size): int?`, `PreviousPageUri (previous_page_uri): string?`, `Start (start): int?`, `Uri (uri): string?`. **No envelope unwrapping needed — `Messages` is a direct field.**

`LookupResponse` (Lookups V2): `Valid (valid): bool?` (validity flag), `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` (reasons when invalid), `PhoneNumber (phone_number): string?` (**canonical E.164 form**), `NationalFormat (national_format): string?`, `CountryCode (country_code): string?`, `CallingCountryCode (calling_country_code): string?`, plus optional intelligence fields (`CallerName`, `SimSwap`, `CallForwarding`, `LineTypeIntelligence`, `LineStatus`, `IdentityMatch`, `ReassignedNumber`, `SmsPumpingRisk` — all nullable records, only populated when requested via `fields`).

### 2.3 Enums (map page `models/enums.md`; namespace `TwilioSdk.Models.Enums`; all are `StringEnum<T>` — **not C# enums**: use the static members, e.g. `MessageEnumStatus.Scheduled`, or `MessageEnumStatus.FromValue("scheduled")`)

`MessageEnumStatus` (full list — C# member (wire value)):
`Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.

`MessageEnumScheduleType`: `Fixed (fixed)` — map doc: *"For Messaging Services only: Include this parameter with a value of `fixed` in conjuction with the `send_time` parameter in order to schedule a Message."* ⇒ **scheduling requires `messagingServiceSid`; `from` cannot be used to schedule.**

`MessageEnumUpdateStatus`: `Canceled (canceled)` — the only status `UpdateMessage` accepts; this is the cancel path.

`MessageEnumDirection`: `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

`ValidationError` (Lookups): `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.

Also on `CreateMessage` (not needed for this scope, pass `null`): `MessageEnumContentRetention` (`Retain`/`Obfuscate`), `MessageEnumAddressRetention` (`Retain`/`Obfuscate`), `MessageEnumTrafficType` (`Free`), `MessageEnumRiskCheck` (`Enable`/`Disable`).

### 2.4 Client construction, auth, servers (map `sdk-map.md` *Getting a client* / *Servers & auth*; source files named there)

- Package: `AsadAli.TwilioSdk` — install **version-less** (`dotnet add package AsadAli.TwilioSdk`), floats to latest; the map deliberately pins no version.
- `TwilioSdk.TwilioSdkClientOptions` (root namespace `TwilioSdk`): `Environment: ServerEnvironment` · `Retry: RetryOptions` · `Logging: LoggingOptions` · `Server: ServerOptions` · `AccountSidAuthToken: BasicAuthCredentials?`.
- Constructor: `new TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)` — both `TwilioSdk`.
- DI: `services.AddTwilioSdkClient(o => { … })` — extension in root namespace `TwilioSdk` (`ServiceCollectionExtensions.cs`); registers `TwilioSdkClient` as a **singleton** over an `IHttpClientFactory`-created `HttpClient`.
- Auth: `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials` — `required string Username { get; init; }`, `required string Password { get; init; }`. Set `o.AccountSidAuthToken = new BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> }`. One scheme covers the whole client — **Lookups uses the same basic auth, no separate client/credentials**.
- Environment: `TwilioSdk.Servers.ServerEnvironment` — only member `ServerEnvironment.Production` (the default).
- **Base-URL override (per server template)** — `TwilioSdk.ServerOptions` (root namespace; file at repo root) has one property per server template, `Default`…`Default14`, each with `Production.BaseUrl: string` (settable):
  - Messaging API (`Api20100401Message.*`) → server **Default (api)** → `options.Server.Default.Production.BaseUrl` (default `"https://api.twilio.com"`). **Set this from `Twilio:BaseUrl` when present; used verbatim as the base address for every messaging call.**
  - Lookups (`LookupsV2PhoneNumber.*`) → server **Default4 (lookups)** → `options.Server.Default4.Production.BaseUrl` (default `"https://lookups.twilio.com"`). **Leave untouched** — the messaging override cannot leak into Lookups because they are different `ServerOptions` properties.
  - Types: `TwilioSdk.ServerOptions`, `TwilioSdk.Servers.DefaultOptions` / `TwilioSdk.Servers.Default4Options` (nested `ProductionOptions` classes).
- `TwilioSdk.Core.RequestOptions` — `record` with `LogLevel: LogLevel?`; per-request, optional everywhere.
- Resilience knobs: `TwilioSdk.Core.Configuration.RetryOptions` (all members `required` — start from `RetryOptions.Default()`): `StatusCodesToRetry`, `HttpMethodsToRetry`, `MaxRetries`, `Delay`, `Timeout: TimeSpan?`, `BackOffFactor`, `UseExponentialBackoff`, `MaxJitter`, `OnRetry`. `LoggingOptions` also lives in `TwilioSdk.Core.Configuration`.

### 2.5 Error model (map `sdk-map.md` *Error-handling model*)

- Every in-scope operation is **Case B**: throws `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`; `ex.Error` is the `RawError`.
- `RawError` members: `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>`. HTTP status ← `StatusCode`; Twilio error code/message ← parse the body via `ReadAsString()`/`ReadAsJson<T>()` (no typed accessors exist on Case B).
- Auth failure (bad SID/token) surfaces the same way — a Case B `SdkException<RawError>` whose `StatusCode` is 401; there is no distinct auth exception type.
- No-throw `…Result` variants: **absent across this SDK** — every call can throw.

### 2.6 Using directives needed

```csharp
using TwilioSdk;                              // TwilioSdkClient, TwilioSdkClientOptions, ServerOptions, AddTwilioSdkClient
using TwilioSdk.Servers;                      // ServerEnvironment, DefaultOptions/Default4Options (only if named)
using TwilioSdk.Models;                       // ApiV2010AccountMessage, ListMessageResponse, LookupResponse
using TwilioSdk.Models.Enums;                 // MessageEnumStatus, MessageEnumScheduleType, MessageEnumUpdateStatus, ValidationError
using TwilioSdk.Core;                         // RequestOptions (only if used)
using TwilioSdk.Core.Authentication.Basic;    // BasicAuthCredentials
using TwilioSdk.Core.Configuration;           // RetryOptions, LoggingOptions (only if tuned)
using TwilioSdk.Core.Exceptions;              // SdkException<>
using TwilioSdk.Core.ErrorResponse;           // RawError
```

## 3. Trap notes

> ⚠ Step 2 (client registration) — the `HttpClient`/handler pipeline behind the SDK has lifetime rules the constructor signature hides; getting ownership wrong (new client per request vs. captive dependencies in the singleton registration) is a socket-exhaustion / stale-DNS hazard. **MUST load `dotnet-client-initialization`** before wiring DI.

> ⚠ Step 2 (auth) — the credentials doc note distinguishes API-key auth from account-SID/auth-token auth and restricts one of them to local testing; which value goes in `Username` vs `Password`, and how secrets reach the options callback from configuration without being hardcoded, is not visible from the property type. **MUST load `dotnet-authentication`** before setting credentials.

> ⚠ Steps 4–9 (calling endpoints) — `CreateMessage` has 24 nullable parameters with **no C# defaults**; a positional call mis-binds silently (e.g. `body` landing in `statusCallback`). All calls must use named arguments with the literal parameter names from §2.1. **MUST load `dotnet-calling-endpoints`** before the first call.

> ⚠ Steps 3–9 (models) — enums are `StringEnum<T>` records, not C# enums (no `switch` exhaustiveness, no `Enum.Parse`); date fields on `ApiV2010AccountMessage` are **strings**; and unmodeled JSON fields are silently dropped on deserialize, so a field read as "missing" may mean "unmodeled". **MUST load `dotnet-models`** before mapping SDK models onto domain types.

> ⚠ Step 10 (error boundary) — every operation here is Case B, so there are no typed `TryGet…` accessors to lean on; and `System.Text.Json.JsonException` reaches the boundary from two directions needing opposite handling (see REQUIRED READING). **MUST load `dotnet-error-handling`** before writing any `try/catch`.

> ⚠ Step 2/10 (resilience) — the SDK's retry/timeout options do **not** bound a whole call, are **not** the timeout on the registered `HttpClient`, and whether a failed `CreateMessage` (a non-idempotent POST) can be re-executed by the retry layer — i.e. whether a transient transport failure can send the SMS twice — is a hazard the option names conceal; the reconciliation flow (step 8) is the dedup backstop. **MUST load `dotnet-configuration-resilience`** before wiring the client or tuning retries/timeouts. The same skill governs the list-pagination iteration pattern for step 8 (the SDK exposes raw `page`/`pageToken` only).

> ⚠ Step 11 (testing) — the test seam is the `HttpClient` constructor argument; faking `TwilioSdkClient` itself or mocking SDK internals couples tests to generated code. Match the repo's xunit + NSubstitute style. **MUST load `dotnet-testing`** before writing integration-layer tests.

## 4. REQUIRED READING

Load **before implementation starts** (this sheet deliberately does not carry their contents):

- `dotnet-client-initialization` — governs step 2 (client construction, HttpClient ownership, DI lifetime).
- `dotnet-authentication` — governs step 2 (credential shape and secret sourcing).
- `dotnet-calling-endpoints` — governs steps 3–9 (named-argument calling convention, explicit-null params).
- `dotnet-models` — governs steps 3–9 (StringEnum, string dates, init-only records, dropped fields).
- `dotnet-error-handling` — governs step 10 (Case A/B mechanics, the JsonException traps below).
- `dotnet-configuration-resilience` — governs steps 2, 8, 10 (retry/timeout semantics, base-URL/server selection, pagination).
- `dotnet-testing` — governs step 11 (the HttpClient seam, error-path coverage).

Mandatory hazard rows (verbatim):

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

**Assumptions**

- **Package version**: the map mandates version-less install (floats to latest release) and deliberately names no version. The repo uses central package management (`Directory.Packages.props`, `ManagePackageVersionsCentrally=true`, `net8.0`): add one `<PackageVersion Include="AsadAli.TwilioSdk" Version="…" />` entry there carrying the latest version resolved at install time, and a version-less `<PackageReference>` in the web project. (Not edited — planning only.)
- **Lookups V2 chosen over V1**: only V2's `LookupResponse` carries `Valid` + `ValidationErrors` (map rows `records-4-Li-Me.md`); V1's response has no validity flag. Both live on server `Default4 (lookups)`, so the `Twilio:BaseUrl` messaging override (server `Default`) cannot affect either.
- **Invalid-number behaviour (Lookups V2)**: the map-visible model shape (`Valid: bool?` + `ValidationErrors` whose enum doc reads "reasons why a phone number is invalid") indicates invalid numbers are reported **in-band** (`Valid == false`), not via exception. Whether the provider additionally returns non-2xx for some malformed numbers is only live-traffic-confirmable → `UNVERIFIED`. Defensive directive: treat `Valid != true` as invalid, read `ValidationErrors` best-effort, and still route `SdkException<RawError>` through the standard boundary.
- **Scheduling constraints**: the map/source contract carries **no minimum lead time or maximum window** for `sendAt` (the SDK's XML docs on `sendAt`/`scheduleType` are empty) → `UNVERIFIED`. Defensive directive: validate `sendAt` locally as comfortably future-dated before calling, and surface any provider rejection through the Case-B `RawError` body rather than pre-emptively hardcoding a window. What IS contract-grounded (enum doc, `enums.md`): scheduling is **Messaging-Services-only** — `scheduleType: MessageEnumScheduleType.Fixed` + `sendAt` must be combined with `messagingServiceSid` (from `Twilio:MessagingServiceSid`), and `from` must be `null` on scheduled sends.
- **Cancellation semantics**: the only status `UpdateMessage` accepts is `Canceled` (`MessageEnumUpdateStatus`, `enums.md`), and the map notes the op cancels "not-yet-sent messages". Which pre-send statuses the provider accepts cancellation from, and the exact error when the message already went out, are not in the map/source → `UNVERIFIED`. Defensive directive: `FetchMessage` first and attempt cancel only when `Status == MessageEnumStatus.Scheduled`; on `SdkException<RawError>` during cancel, read `StatusCode`/body and treat it as "already sent, cannot cancel" rather than retrying.
- **Redaction semantics**: redact via `UpdateMessage(accountSid, sid, body: "", status: null)` (map note: the op is used "to redact Message `body` text"); the record (SID, status, dates, error fields) survives. Whether a subsequent fetch returns `Body` as `""` or `null` → `UNVERIFIED`; read it null-tolerantly. `DeleteMessage` is documented here only to warn it off — it deletes the whole resource.
- **`Twilio:FromNumber`** is assumed to be a Twilio-owned E.164 number passed as the plain `from` string; immediate sends use `from`, scheduled sends use `messagingServiceSid` instead.
- **Reconciliation filter**: `ListMessage`'s `from` parameter is a server-side filter (wire `From`), satisfying the "filter in the request, not client-side" requirement; date range uses `dateSentQuery` (wire `DateSent<`) and `dateSentQueryQuery` (wire `DateSent>`) — note the awkward generated names.
- net8.0 target consuming a `netstandard2.0` package under the .NET 10 SDK with roll-forward: compatible; no framework change needed.

**Blockers** — none.
