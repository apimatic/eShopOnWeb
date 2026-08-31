# Twilio SMS order notifications — integration plan (`src/PublicApi`, eShopOnWeb)

SDK: `AsadAli.TwilioSdk` (NuGet; install **version-less** — `dotnet add package AsadAli.TwilioSdk`, floats to latest; do not pin from memory). Package targets `netstandard2.0` → compatible with the repo's .NET 8 target and the `global.json` 8.0.x SDK pin. Root namespace `TwilioSdk`; client `TwilioSdkClient`; options `TwilioSdkClientOptions`. (Map: `sdk-map.md`.)

## 1. Scope & sequence

| # | Step | SDK operation(s) | App endpoint / trigger |
|---|---|---|---|
| 1 | Add NuGet package `AsadAli.TwilioSdk` to `src/PublicApi` | — | — |
| 2 | `TwilioOptions` POCO + bind `Twilio` config section; register client via DI | client construction, auth, server override | `Program.cs` / startup |
| 3 | Validate + canonicalize shopper mobile number | `LookupsV2PhoneNumber.FetchPhoneNumber3` | shopper registration endpoint |
| 4 | Send SMS immediately | `Api20100401Message.CreateMessage` (with `from`, no scheduling params) | order confirmation endpoint |
| 5 | Schedule follow-up SMS (provider-side) | `Api20100401Message.CreateMessage` (with `messagingServiceSid`, `scheduleType`, `sendAt`) | order dispatched endpoint |
| 6 | Cancel a scheduled follow-up | `Api20100401Message.UpdateMessage` (`status: Canceled`) | order cancelled endpoint |
| 7 | Poll delivery outcome by Sid | `Api20100401Message.FetchMessage` | status endpoint / background poller (no public callback URL — poll, never webhook) |
| 8 | GDPR redact message body | `Api20100401Message.UpdateMessage` (`body: ""`) | privacy/erasure endpoint |
| 9 | Reconciliation report (date range, filtered by sender) | `Api20100401Message.ListMessage` (paged loop) | admin reconciliation endpoint |
| 10 | Error boundary around all SDK calls | `SdkException<RawError>` (all in-scope ops are Case B) | middleware / facade |

Recommended layout: keep the SDK behind one app-side facade (e.g. `ISmsNotificationService` + `TwilioSmsNotificationService`) living next to `src/PublicApi`'s existing integration/infrastructure code; endpoints (steps 3–9) depend only on the facade, never on `TwilioSdk.*` types directly. `TwilioOptions` carries `AccountSid`, `AuthToken`, `FromNumber`, `MessagingServiceSid`, `BaseUrl` (bind from the `Twilio` config section, which the environment variables `TWILIO_ACCOUNT_SID` etc. feed).

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

### 2a. Client construction, auth, per-capability base URL

| Fact | Contract | Source |
|---|---|---|
| Client | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md` |
| Options members | `TwilioSdkClientOptions`: `Environment: TwilioSdk.Servers.ServerEnvironment` · `Retry: TwilioSdk.Core.Configuration.RetryOptions` · `Logging: TwilioSdk.Core.Configuration.LoggingOptions` · `Server: TwilioSdk.ServerOptions` · `AccountSidAuthToken: TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md`; `TwilioSdkClientOptions.cs` |
| DI registration | `services.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` — extension on `IServiceCollection` in namespace `TwilioSdk`; registers the client as a **singleton** built on an `IHttpClientFactory`-created `HttpClient` | `ServiceCollectionExtensions.cs` |
| Auth | `o.AccountSidAuthToken = new BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> };` — `BasicAuthCredentials` (namespace `TwilioSdk.Core.Authentication.Basic`) has `required string Username` and `required string Password` (init-only). Source doc note: an API key + secret is the recommended username/password; account SID + auth token is flagged "limit … to local testing" — the brief mandates SID+token, so use it and note the caveat | `sdk-map.md` *Servers & auth*; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environment | `o.Environment` — `TwilioSdk.Servers.ServerEnvironment` has one member: `ServerEnvironment.Production` (default) | `sdk-map.md` *Servers & auth* |
| **Messaging base-URL override** (`Twilio:BaseUrl`) | Every `Api20100401Message` operation resolves server **"Default (api)"** → `o.Server.Default.Production.BaseUrl`, default `"https://api.twilio.com"`. Set `o.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` (verbatim string) when the config value is present; it then governs **every** messaging call (create/fetch/update/delete/list). Types: `ServerOptions` is in the **root** namespace `TwilioSdk` (file at repo root); `DefaultOptions` / `Default4Options` are in `TwilioSdk.Servers`; each exposes `Production.BaseUrl` | `ServerOptions.cs`; `Server.cs`; `Servers/DefaultOptions.cs`; `operations/Api20100401Message.md` |
| **Lookup base URL** (NOT governed by `Twilio:BaseUrl`) | `LookupsV2PhoneNumber` / `LookupsV1PhoneNumberApi` resolve server **"Default4 (lookups)"** → `o.Server.Default4.Production.BaseUrl`, default `"https://lookups.twilio.com"`. Overriding `Server.Default` leaves Lookup untouched — matching the requirement that `Twilio:BaseUrl` applies only to the messaging API. (For reference: `Default1` = `https://messaging.twilio.com` serves the `MessagingV1*` service controllers, which this integration does not use.) | `ServerOptions.cs`; `Server.cs`; `Servers/Default4Options.cs`; `operations/LookupsV2PhoneNumber.md` |
| Per-request options | Every operation ends with `RequestOptions? requestOptions = null, CancellationToken ct = default`. `TwilioSdk.Core.RequestOptions` is a sealed record with one member: `LogLevel: Microsoft.Extensions.Logging.LogLevel?` | `Core/RequestOptions.cs` |
| Retry options | `RetryOptions` (namespace `TwilioSdk.Core.Configuration`): all members `required` — build a full instance or start from `RetryOptions.Default()`. Members: `StatusCodesToRetry: IReadOnlyList<HttpStatusCode>` · `HttpMethodsToRetry: IReadOnlyList<HttpMethod>` · `MaxRetries: int` · `Delay: TimeSpan` · `Timeout: TimeSpan?` · `BackOffFactor: int` · `UseExponentialBackoff: bool` · `MaxJitter: TimeSpan` · `OnRetry: Action<RetryAttempt>?` | `sdk-map.md` |

### 2b. Operations

**Op 1 — Validate + canonicalize a phone number (Lookup V2)** · `client.LookupsV2PhoneNumber` · map: `operations/LookupsV2PhoneNumber.md`

- `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<LookupResponse>`
- HTTP `GET /v2/PhoneNumbers/{PhoneNumber}` on server **Default4 (lookups)**. `phoneNumber` is a path template param (E.164; pass `countryCode` as a hint when the input is national-format).
- 15 nullable params (`fields` … `partnerSubId`) have **no C# defaults — pass each explicitly** (`null` to skip). For validation-only: `fields: null`, everything else `null`.
- Response `TwilioSdk.Models.LookupResponse` (map: `records-4-Li-Me.md`) — fields the integration reads:
  - `Valid (valid): bool?` — the provider's usability verdict; reject when not `true`.
  - `ValidationErrors (validation_errors): IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` — rejection reasons.
  - `PhoneNumber (phone_number): string?` — **canonical E.164 form; store this, not the caller-typed string.**
  - `NationalFormat (national_format): string?`, `CountryCode (country_code): string?`, `CallingCountryCode (calling_country_code): string?` — display/metadata.
  - (`CallerName`, `SimSwap`, `LineTypeIntelligence`, etc. are separate paid field groups — not requested with `fields: null`.)
- Error: **Case B** `SdkException<RawError>` (no typed error). No-throw variant: absent. Pagination: none.
- Note: Lookup **V1** (`client.LookupsV1PhoneNumberApi.FetchPhoneNumber2`, map: `operations/LookupsV1PhoneNumberApi.md`) returns `LookupsV1PhoneNumber`, which has **no `valid` field** (map: `records-4-Li-Me.md`) — it cannot answer "is this a usable destination". V2 is the correct capability.

**Op 2 — Send SMS immediately** · `client.Api20100401Message` · map: `operations/Api20100401Message.md`

- `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<ApiV2010AccountMessage>`
- HTTP `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` on server **Default (api)**. Params travel as a **form-url-encoded body** (not query string), and the SDK auto-attaches an `Idempotency-Key: Guid.NewGuid()` header per invocation (source: `Api/Api20100401Message.cs`).
- 24 nullable params (`statusCallback` … `contentSid`) — **no C# defaults, pass each explicitly** (`null` to skip). Use named arguments.
- Immediate send sets: `accountSid` (the auth account SID), `to` (canonical E.164 from Op 1), `from: <Twilio:FromNumber>`, `body: <text>`; everything else `null` (`scheduleType`/`sendAt` stay `null`).
- Returns `TwilioSdk.Models.ApiV2010AccountMessage` directly — **no envelope wrapper** (map: `records-1-Ac-Ca.md`). Read `Sid (sid): string?` (persist it — the handle for fetch/cancel/redact) and `Status (status): MessageEnumStatus?`.
- Error: **Case B** `SdkException<RawError>`. No-throw variant: absent. Pagination: none.

**Op 3 — Schedule a message for later (provider-side)** · same operation, `CreateMessage` · map: `operations/Api20100401Message.md`

- Scheduling = same call with: `messagingServiceSid: <Twilio:MessagingServiceSid>`, `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt: <DateTimeOffset of dispatch + N days>`, `from: null`.
- `MessageEnumScheduleType` (namespace `TwilioSdk.Models.Enums`, a `StringEnum<T>` — use the static member, not a C# enum) has exactly one value: `Fixed (fixed)`. Its map doc states scheduling is **"For Messaging Services only"** — i.e. `messagingServiceSid` is required for scheduling; do not schedule with `from` (map: `enums.md`).
- A scheduled-but-unsent message carries status `MessageEnumStatus.Scheduled (scheduled)` (map: `enums.md`).
- Returns the same `ApiV2010AccountMessage`; persist `Sid` for later cancel/status.

**Op 4 — Cancel a scheduled message** · map: `operations/Api20100401Message.md`

- `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<ApiV2010AccountMessage>`
- HTTP `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` on server **Default (api)**. Wire params: `Body` ← `body`, `Status` ← `status`. `body` and `status` are nullable with **no defaults — pass both explicitly**.
- Cancel call: `UpdateMessage(accountSid, sid, body: null, status: MessageEnumUpdateStatus.Canceled)`. `MessageEnumUpdateStatus` (`TwilioSdk.Models.Enums`) has exactly one value: `Canceled (canceled)` (map: `enums.md`).
- Constraint: the operation's own remarks say it cancels **"not-yet-sent messages"** (source: `Api/Api20100401Message.cs`). The exact set of statuses from which a cancel is accepted is **UNVERIFIED** (only live traffic could confirm) — defensive directive: on `SdkException<RawError>` from the cancel call, read `StatusCode`, then `FetchMessage` and treat `Status` ∈ {`Sent`, `Delivered`, `Undelivered`, `Failed`} as "too late, already sent" rather than retrying the cancel.
- Error: **Case B** `SdkException<RawError>`.

**Op 5 — Fetch message state by Sid** · map: `operations/Api20100401Message.md`

- `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<ApiV2010AccountMessage>`
- HTTP `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` on server **Default (api)**.
- Response `ApiV2010AccountMessage` (map: `records-1-Ac-Ca.md`) — full field list with wire names:
  `Sid (sid): string?` · `Status (status): MessageEnumStatus?` · `ErrorCode (error_code): int?` · `ErrorMessage (error_message): string?` · `Body (body): string?` · `From (from): string?` · `To (to): string?` · `Direction (direction): MessageEnumDirection?` · `MessagingServiceSid (messaging_service_sid): string?` · `NumSegments (num_segments): string?` · `NumMedia (num_media): string?` · `Price (price): string?` · `PriceUnit (price_unit): string?` · `AccountSid (account_sid): string?` · `Uri (uri): string?` · `ApiVersion (api_version): string?` · `DateCreated (date_created): string?` · `DateUpdated (date_updated): string?` · `DateSent (date_sent): string?` · `SubresourceUris (subresource_uris): object?`
  (Dates on the record are `string?`, not `DateTimeOffset` — parse if needed.)
- `MessageEnumStatus` (`TwilioSdk.Models.Enums`) — full value list (map: `enums.md`): `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)`.
- Delivery outcome = `Status` + `ErrorCode`/`ErrorMessage`. Error: **Case B** `SdkException<RawError>`.

**Op 6 — Redact message body (GDPR)** · same operation as Op 4, `UpdateMessage` · map: `operations/Api20100401Message.md`

- Redact call: `UpdateMessage(accountSid, sid, body: "", status: null)` — the operation's remarks state it is "used to redact Message `body` text" (source: `Api/Api20100401Message.cs`). Afterwards the Message **record survives** (Sid, status/outcome, dates, price) with an empty body — matching the requirement.
- Alternative: `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task` (void) — HTTP `DELETE …/Messages/{Sid}.json`; "Deletes a Message resource from your account" — this destroys the record itself, so it does **not** meet "the record and its outcome survive". Use `UpdateMessage` with `body: ""`.
- Error (both): **Case B** `SdkException<RawError>`.

**Op 7 — List/reconcile messages (date range, sender-filtered, server-side)** · map: `operations/Api20100401Message.md`

- `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task<ListMessageResponse>`
- HTTP `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` on server **Default (api)**. 8 nullable params (`to` … `pageToken`) — **pass all explicitly**.
- Server-side filter mapping (wire ← C#), serialized with `ToIso8601()` so ISO-8601 date-times go on the wire (source: `Api/Api20100401Message.cs`):
  - `From` ← `from` — "Filter by sender" → pass `<Twilio:FromNumber>`; only this application's traffic is returned.
  - `DateSent<` ← `dateSentQuery` — sent **before** (range end).
  - `DateSent>` ← `dateSentQueryQuery` — sent **after** (range start). (Yes: the C# name with two `Query`s maps to `DateSent>` — generated verbatim.)
  - `DateSent` ← `dateSent` — exact-day filter; leave `null` for a range.
  - `PageSize` ← `pageSize` (`long?`; doc: default 50, max 1000) · `Page` ← `page` (`int?`; doc: "simply for client state") · `PageToken` ← `pageToken` (`string?`; doc: "provided by the API").
- Response `TwilioSdk.Models.ListMessageResponse` (map: `records-4-Li-Me.md`): `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` · `NextPageUri (next_page_uri): string?` · `PreviousPageUri (previous_page_uri): string?` · `FirstPageUri (first_page_uri): string?` · `Page (page): int?` · `PageSize (page_size): int?` · `Start (start): int?` · `End (end): int?` · `Uri (uri): string?`.
- Pagination: the SDK has **no built-in pager** for this operation (map row: "Pagination: none (only `page`, no `perPage`)") — the reconciliation loop pages manually until the range is exhausted (`NextPageUri` null = done). See trap note ⚠ step 9.
- Error: **Case B** `SdkException<RawError>`.

### 2c. Enums needed (all `StringEnum<T>` in `TwilioSdk.Models.Enums` — use static members / `FromValue("wire")`, never C# enum syntax) · map: `enums.md`

| Enum | Values (Member (wire)) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` (only value) |
| `MessageEnumScheduleType` | `Fixed (fixed)` (only value) |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |

### 2d. Error handling (all in-scope operations)

Every operation above is **Case B**: throws `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`; read `ex.Error.StatusCode: HttpStatusCode`, `ex.Error.ReadAsString(): string`, `ex.Error.ReadAsJson<T>(): T?`, `ex.Error.ReadAsBytes()` (map: `sdk-map.md` error-handling section). There is **no generated typed error model** for these operations — so 400 (invalid number), 401 (bad auth), 404 (unknown Sid) all surface the same way, distinguished by `StatusCode`; if structured access to Twilio's error JSON (`status`/`code`/`message`/`more_info`) is needed, define a small local DTO and use `ReadAsJson<T>()`. No `…Result` no-throw variant exists on any of these operations.

## 3. Trap notes

- ⚠ Step 2 (client registration) — `AddTwilioSdkClient` registers the SDK client as a singleton wrapping an `IHttpClientFactory`-created `HttpClient`; which component owns the `HttpClient`/handler lifetime, and whether that registration shape fits a long-lived ASP.NET Core app, is not visible from the signature. **MUST load `dotnet-client-initialization`** before wiring DI.
- ⚠ Step 2 (auth) — credentials must be set before the client is constructed (or inside the DI callback), and secrets must come from configuration, not code; the credentials-property shape has its own pitfalls. **MUST load `dotnet-authentication`**.
- ⚠ Steps 3–9 (every call) — these operations take 8–24 nullable parameters with **no C# defaults**; a positional call mis-binds silently. How to call list/search/create ops safely (named arguments, explicit nulls) is a skill topic. **MUST load `dotnet-calling-endpoints`** before the first call.
- ⚠ Steps 3–9 (models) — enums are `StringEnum<T>` (not C# enums), records are immutable with `init`/`required` members, and unmodeled JSON fields are dropped on deserialize; constructing or reading these naïvely fails in non-obvious ways. **MUST load `dotnet-models`**.
- ⚠ Steps 4–5 (writes) — `CreateMessage` auto-attaches a fresh `Idempotency-Key` per invocation, and a transport failure on a `POST` interacts with the retry policy in a way the signature does not show: whether a failed send can execute more than once, and what that means for duplicate SMS, must be settled before sending. **MUST load `dotnet-configuration-resilience`**.
- ⚠ Step 2 / ops tuning (retry & timeout) — the SDK's retry/timeout options do **not** bound a whole call and are **not** the timeout on the `HttpClient` you register; what `Timeout` actually bounds and which triggers gate on `HttpMethodsToRetry` must come from the skill, not from the member names. **MUST load `dotnet-configuration-resilience`**.
- ⚠ Step 9 (reconciliation paging) — `ListMessage` has no SDK pager; `page` is "simply for client state" and `pageToken` is "provided by the API", so the loop-termination and page-token mechanics for walking a whole date range are a skill topic. **MUST load `dotnet-configuration-resilience`**.
- ⚠ Step 10 (error boundary) — all in-scope ops are Case B (`SdkException<RawError>`), but the boundary has two `JsonException` failure directions (see REQUIRED READING) plus the Case A/B mechanics; writing the catch ladder from the signature alone gets it wrong. **MUST load `dotnet-error-handling`**.
- ⚠ Tests — the test seam for stubbing this SDK is specific (the `HttpClient` constructor argument), and matching the repo's existing test framework/assertion style matters. **MUST load `dotnet-testing`** before writing integration tests.

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry their contents:

- `dotnet-client-initialization` — governs step 2 (client construction & DI registration).
- `dotnet-authentication` — governs step 2 (credentials wiring).
- `dotnet-calling-endpoints` — governs steps 3–9 (every operation call).
- `dotnet-models` — governs steps 3–9 (records, `StringEnum<T>` enums, wire names).
- `dotnet-error-handling` — governs step 10 (the exception boundary). Mandatory even though all in-scope ops are Case B:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
- `dotnet-configuration-resilience` — governs step 2 (retry/timeout/base-URL tuning) and step 9 (pagination).
- `dotnet-testing` — governs tests for the integration layer.

## 5. Assumptions & Blockers

**Assumptions**

1. Lookup **V2** (`FetchPhoneNumber3`) is the validation capability — chosen because V1's response model carries no `valid` field (visible in its map row). Validation-only usage assumed (`fields: null`); paid field groups (caller name, line type, etc.) are out of scope.
2. Scheduling uses `MessagingServiceSid` (the schedule enum's doc says scheduling is "For Messaging Services only"); `TWILIO_MESSAGING_SERVICE_SID` is therefore required for the dispatch-follow-up feature, while immediate sends use `FromNumber`. If the account has no Messaging Service, step 5 is blocked on creating one (outside this SDK plan).
3. `accountSid` passed as the first parameter of every messaging operation is the same Account SID used as the auth username.
4. The exact set of message statuses from which a cancel (`UpdateMessage` → `Canceled`) is accepted is **UNVERIFIED** — the SDK source says only "not-yet-sent messages"; only live traffic can confirm the provider's rejection behavior. Defensive directive is inlined at Op 4.
5. Whether the provider's live wire payloads exactly match the generated models (e.g. `date_sent` always present as a string) is **UNVERIFIED** — the `JsonException`-on-2xx hazard row in REQUIRED READING exists for exactly this; all response fields the integration reads are nullable in the models, so read them defensively.
6. No public callback URL exists, so `statusCallback` stays `null` and all delivery state is obtained by polling `FetchMessage` / `ListMessage` (per the brief).
7. The map was generated from source commit `51fdf48` ("Publish v2.0.0 SDK"); the NuGet install floats to the latest release. If any name here fails to compile, trust the compiler and re-check the named source file — do not patch from memory.

**Blockers** — none.
