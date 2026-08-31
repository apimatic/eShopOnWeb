# Twilio integration plan — eShopOnWeb order notifications (`src/PublicApi`, .NET 8)

**SDK**: NuGet `AsadAli.TwilioSdk` — install **version-less** (`dotnet add package AsadAli.TwilioSdk`), floats to latest. SDK targets `netstandard2.0` → compatible with `net8.0`. Root namespace `Twilio`; client `TwilioClient`; options `TwilioClientOptions`.

> **⚠ MAP DRIFT (verified against SDK source at `main` HEAD, commit `3d2efed`):** the bundled plugin map (stamp `51fdf48`) is **stale on the client layer** — it says namespace `TwilioSdk`, `TwilioSdkClient`, `TwilioSdkClientOptions`, `AddTwilioSdkClient`. The current source (matching this brief and the latest package) says **`Twilio` / `TwilioClient` / `TwilioClientOptions` / `AddTwilioClient`**, and options gained a `Hooks` member. All **operation signatures, model shapes, and enum values in this sheet were re-verified against source and are unchanged**. If any name below fails to compile, trust the compiler and report back — do not patch from memory.

## 1. Scope & sequence

| # | Step | SDK operations used |
|---|---|---|
| 1 | Add package + register client & auth in DI (`Program.cs` / DI extension) | — (client construction) |
| 2 | Phone-number validation gateway method (Lookup v2) | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | Send SMS now / schedule SMS | `Api20100401Message.CreateMessage` |
| 4 | Cancel scheduled message | `Api20100401Message.UpdateMessage` |
| 5 | Fetch delivery outcome by SID | `Api20100401Message.FetchMessage` |
| 6 | Reconciliation list (From + date range, paged) | `Api20100401Message.ListMessage` |
| 7 | Redact message body | `Api20100401Message.UpdateMessage` |
| 8 | Error boundary + Polly/retry config + tests | (cross-cutting) |

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

**Namespaces in scope (verified at HEAD):** `Twilio` (client, options, `ServerOptions`, DI ext.) · `Twilio.Api` (controllers) · `Twilio.Models` (records) · `Twilio.Models.Enums` (enums) · `Twilio.Servers` (`ServerEnvironment`, `DefaultOptions`…`Default14Options`) · `Twilio.Core` (`RequestOptions`) · `Twilio.Core.Authentication.Basic` (`BasicAuthCredentials`) · `Twilio.Core.Configuration` (`RetryOptions`, `LoggingOptions`) · `Twilio.Core.ErrorResponse` (`RawError`, `ApiError`) · `Twilio.Core.Exceptions` (`SdkException<T>`)

### 2.1 Client construction, auth, per-capability base URL (map: sdk-map.md *Servers & auth*; source-verified)

| Fact | Value |
|---|---|
| Constructor | `TwilioClient(HttpClient httpClient, TwilioClientOptions options)` — the only constructor |
| DI helper | `services.AddTwilioClient(o => { … })` (`Twilio.ServiceCollectionExtensions`) |
| Auth | `o.AccountSidAuthToken = new BasicAuthCredentials { Username = accountSid, Password = authToken };` — both `required string`, init-only. (SDK doc: API key + secret preferred; account SID + auth token "limit to local testing".) |
| Environment | `o.Environment = ServerEnvironment.Production` (default; only member) |
| **Messaging API base URL** | server group `Default`, default `https://api.twilio.com` → override: `o.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` |
| **Lookup API base URL** | server group `Default4`, default `https://lookups.twilio.com` → override point `o.Server.Default4.Production.BaseUrl` — **leave untouched**; the app's `Twilio:BaseUrl` override applies ONLY to `Default` |
| Per-call config | every operation takes `RequestOptions? requestOptions = null` — members: `LogLevel? LogLevel`, `IReadOnlyList<SdkHook>? Hooks` **only**. No per-call headers, timeout, or idempotency key |
| Other options | `o.Retry` (`RetryOptions`, all members `required` — start from `RetryOptions.Default()`), `o.Logging`, `o.Hooks` |

Per-capability base URL **is supported**: each of the 15 server groups has its own `…Options.Production.BaseUrl`. Messaging ops run on `Default`, Lookup ops on `Default4` — configuring one does not affect the other.

### 2.2 Operations

**Common:** every row is **Case B** error — throws `SdkException<RawError>` (`Twilio.Core.Exceptions`, `Twilio.Core.ErrorResponse`); accessors: `ex.Error.StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes()`. No no-throw `…Result` variant exists anywhere in this SDK. `accountSid` (first param of every op) is the same account SID used in auth. All nullable params have **no C# default — must be passed explicitly** (pass `null` to skip); use named arguments.

| Capability | Controller · signature (verbatim) | Returns | Notes |
|---|---|---|---|
| **Validate number** (map: operations/LookupsV2PhoneNumber.md) | `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `LookupResponse` | Server group `Default4`. `phoneNumber`: "E.164 or national format. Default country code is +1 (North America)" (param doc). `countryCode`: ISO 3166-1 alpha-2, used when national-format → pass `"CA"`/`"US"` explicitly for the test destinations. Pass `null` for `fields` and all identity-match params (base validation needs none). |
| **Send now** (map: operations/Api20100401Message.md) | `client.Api20100401Message.CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ApiV2010AccountMessage` (direct, **no envelope**) | Immediate send: `scheduleType: null, sendAt: null`, plus `to:`, `body:`, and exactly one of `from:` / `messagingServiceSid:` (see UNVERIFIED row below); everything else `null`. |
| **Schedule** (same op) | same signature | same | `scheduleType: MessageEnumScheduleType.Fixed` + `sendAt: <DateTimeOffset>` (wire `SendAt`). Enum doc: "For Messaging Services only" → scheduling requires `messagingServiceSid:`, not `from:`. Lead-time constraints are API-side (UNVERIFIED below). |
| **Cancel scheduled** (map: operations/Api20100401Message.md) | `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ApiV2010AccountMessage` | Cancel: `body: null, status: MessageEnumUpdateStatus.Canceled`. Remarks: "used to redact Message `body` text and to cancel not-yet-sent messages". Already-sent behaviour is API-side (UNVERIFIED). |
| **Redact body** (same op) | same signature | same | Redact: `body: "", status: null`. Record (sid/status/dates) survives; only `Body` is wiped. Do **not** use `DeleteMessage` — that deletes the whole record (`DeleteMessage(string accountSid, string sid, …)` → `void`). |
| **Fetch by SID** (map: operations/Api20100401Message.md) | `client.Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ApiV2010AccountMessage` | Read `Status`, `ErrorCode`, `ErrorMessage`. |
| **List / reconcile** (map: operations/Api20100401Message.md) | `client.Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` | `ListMessageResponse` (envelope) | Wire names: `To`←`to`, `From`←`from`, `DateSent`←`dateSent` (exact date), `DateSent<`←`dateSentQuery` (on/before), `DateSent>`←`dateSentQueryQuery` (on/after). Param docs: filters accept GMT dates `YYYY-MM-DD` — treat as **date-granular**. `pageSize`: default 50, max 1000. `page`: "simply for client state". `pageToken`: provided by the API. **No SDK auto-paging** — loop: read `Messages`, follow `NextPageUri` (null ⇒ last page); pass `page`/`pageToken` through. |

### 2.3 Response models (records: `Twilio.Models`; `init`-only, all nullable)

`ApiV2010AccountMessage` (map: records-1-Ac-Ca.md; source-verified) — fields the integration reads:

| C# property (wire name) | Type | Note |
|---|---|---|
| `Sid (sid)` | `string?` | message SID |
| `Status (status)` | `MessageEnumStatus?` | delivery state — see enum table |
| `ErrorCode (error_code)` | `int?` | set when `Status` is `failed`/`undelivered`, else null |
| `ErrorMessage (error_message)` | `string?` | SDK doc caveat: values "subject to change… should not use programmatically" — log it, branch on `Status` instead |
| `Body (body)` | `string?` | empty after redaction |
| `From (from)` / `To (to)` | `string?` | |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | |
| `DateCreated/DateUpdated/DateSent (date_created/date_updated/date_sent)` | `string?` | **RFC 2822 strings, not `DateTimeOffset`** — parse if needed |
| `Price (price)`, `PriceUnit (price_unit)`, `NumSegments (num_segments)`, `Direction (direction)`, `Uri (uri)`, `AccountSid (account_sid)`, `NumMedia (num_media)`, `ApiVersion (api_version)`, `SubresourceUris (subresource_uris)` | various | available, not required by scope |

`LookupResponse` (map: records-4-Li-Me.md; source-verified) — validation reads: `Valid (valid): bool?` · `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` · `PhoneNumber (phone_number): string?` (canonical E.164 — **store this, not the raw input**) · `NationalFormat (national_format): string?` · `CountryCode (country_code): string?` · `CallingCountryCode (calling_country_code): string?`. Remaining properties (`CallerName`, `SimSwap`, `LineTypeIntelligence`, …) are paid add-on packages — null when `fields` is null.

`ListMessageResponse` (map: records-4-Li-Me.md; source-verified): `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` · `NextPageUri (next_page_uri): string?` · `PreviousPageUri`, `FirstPageUri`, `Uri`: `string?` · `Page`, `PageSize`, `Start`, `End`: `int?`.

### 2.4 Enums (`Twilio.Models.Enums`; `StringEnum<T>` records — static members or `FromValue("wire")`, **not** C# enums)

| Enum (map: enums.md; source-verified) | Members (wire value) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` — only value |
| `MessageEnumScheduleType` | `Fixed (fixed)` — only value |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |

### 2.5 Error & outcome contract

| Situation | What reaches your code |
|---|---|
| API rejects request (invalid destination at send time, bad schedule window, cancel-too-late, auth failure, …) | `SdkException<RawError>` — read `StatusCode` + `ReadAsString()`/`ReadAsJson<T>()` for the Twilio error payload. All six in-scope ops are Case B; there is no typed `{Operation}Error` for them. |
| Network/transport failure | not an `SdkException` — transport exception type and retry interplay per `dotnet-configuration-resilience` / `dotnet-error-handling` (see trap notes). |
| Carrier-stage delivery failure (the live account's US-destination refusals) | **NOT an exception.** Create succeeds (2xx); the failure is later resource state: `Status` = `MessageEnumStatus.Failed` or `Undelivered` with `ErrorCode`/`ErrorMessage` set, observed via `FetchMessage`/`ListMessage`. (Mechanism is SDK-grounded; which of the two statuses this account produces is `UNVERIFIED` — live behaviour.) |
| Idempotency keys on create | **Not supported.** No idempotency param on `CreateMessage`; `RequestOptions` carries only `LogLevel`/`Hooks` — no header injection point (source-verified absence). The app must dedupe itself (e.g. store `(orderId, kind)` → `messageSid`, check before create). |

### 2.6 UNVERIFIED rows (only live traffic / Twilio docs can confirm — defensive directives are the contract)

| # | Open fact | Defensive directive |
|---|---|---|
| U1 | `from` vs `messagingServiceSid` mutual exclusion / precedence — SDK enforces nothing (all nullable, no client-side validation in `CreateMessage` body) | Pass **exactly one**. Scheduling ⇒ `messagingServiceSid` (per `MessageEnumScheduleType` doc). If both configured, prefer `messagingServiceSid` for scheduled, `from` allowed for immediate. |
| U2 | Scheduling min/max lead time (Twilio docs cite 15 min – 7 days) — not in SDK | Validate app-side from config; on rejection surface `SdkException<RawError>` body to the caller. |
| U3 | Create-with-schedule response status — expected `Scheduled` | Assert `Status == MessageEnumStatus.Scheduled` on first live call before relying on it. |
| U4 | Cancel of an already-sent message: error status vs no-op | Catch `SdkException<RawError>` around cancel; re-`FetchMessage` afterwards and treat `Status == Canceled` as the success criterion. |
| U5 | Post-redact `Body` value (`""` vs `null`) | Don't read `Body` from the update response; re-fetch if proof needed. |
| U6 | Lookup of garbage input: 4xx `SdkException<RawError>` vs 200 with `Valid == false` | Handle **both**: catch the exception AND treat `Valid != true` as invalid; log `ValidationErrors`. |
| U7 | National-format CA number with default country +1 | Always pass explicit `countryCode` (`"CA"`/`"US"`) for national-format input. |

## 3. Trap notes (hazard named, resolution lives in the skill — load before coding that step)

> ⚠ Step 1 (client registration) — the SDK's DI helper registers the client as a **singleton** wrapping an `IHttpClientFactory`-created `HttpClient`; whether that matches the factory's intended handler-lifetime model, and how to register correctly if constructing manually, is not decidable from the signature. **MUST load `dotnet-client-initialization`.**

> ⚠ Step 1 (auth) — credentials must come from configuration, never hardcoded; the SDK doc steers production use to API-key/secret rather than account SID + auth token, which affects what `Username`/`Password` you bind from config. **MUST load `dotnet-authentication`.**

> ⚠ Steps 2–7 (calling) — `CreateMessage` has 24 nullable params with no defaults and several adjacent same-type params (`string?` runs); a positional call mis-binds silently. Named-argument discipline and what "must pass explicitly" costs are governed by **MUST load `dotnet-calling-endpoints`.**

> ⚠ Steps 2–7 (models) — enums are `StringEnum<T>` records, not C# enums: construction, equality, and display semantics differ (no `switch` on raw strings without conversion; `FromValue` vs static members). Records carry `[JsonExtensionData] AdditionalProperties`. **MUST load `dotnet-models`.**

> ⚠ Step 8 (retry/timeout) — the SDK's retry options gate only the **status** trigger; what happens on a **transport failure** for a non-idempotent `POST` (duplicate SMS risk), and what `Timeout` actually bounds, decide whether a failed create can be re-sent. **MUST load `dotnet-configuration-resilience`.**

> ⚠ Step 8 (error boundary) — every in-scope op is Case B (`RawError`, no typed accessors); which exception types escape the SDK on transport failure, and how to build the catch ladder, **MUST load `dotnet-error-handling`.**

> ⚠ **Mandatory hazard rows (verbatim):**
> - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary;
> - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed.
>
> **MUST load `dotnet-error-handling`** before writing that boundary.

> ⚠ Step 8 (tests) — the test seam is the `HttpClient` constructor argument; how to fake it and what to assert, **MUST load `dotnet-testing`.**

## 4. REQUIRED READING (load all **before implementation starts** — this sheet deliberately does not carry their contents)

| Skill | Governs step |
|---|---|
| `dotnet-client-initialization` | 1 — client construction, HttpClient ownership, DI registration |
| `dotnet-authentication` | 1 — `BasicAuthCredentials` wiring, secrets from config |
| `dotnet-calling-endpoints` | 2–7 — named arguments, must-pass-explicitly params, async/ct usage |
| `dotnet-models` | 2–7 — `StringEnum<T>` handling, records, `AdditionalProperties` |
| `dotnet-error-handling` | 8 — Case A/B boundary, `RawError` accessors, the two `JsonException` hazards |
| `dotnet-configuration-resilience` | 8 — retries on POST, timeouts, base-URL/pagination tuning |
| `dotnet-testing` | 8 — faking the `HttpClient` seam |

## 5. Recommended layout (thin gateway, Clean Architecture)

| Project | File | Contents |
|---|---|---|
| `ApplicationCore` (Core) | `Interfaces/ISmsGateway.cs` | `Task<PhoneValidationResult> ValidatePhoneNumberAsync(string raw, string? countryCode, CancellationToken ct)` · `Task<SmsSendResult> SendAsync(string toE164, string body, CancellationToken ct)` · `Task<SmsSendResult> ScheduleAsync(string toE164, string body, DateTimeOffset sendAt, CancellationToken ct)` · `Task CancelScheduledAsync(string messageSid, …)` · `Task<SmsDeliveryStatus> GetStatusAsync(string messageSid, …)` · `Task RedactBodyAsync(string messageSid, …)` — plain DTOs, **no `Twilio.*` type escapes the interface** |
| `Infrastructure` | `Twilio/TwilioSmsGateway.cs` | implements `ISmsGateway`; owns all SDK calls, enum mapping (`MessageEnumStatus` → domain status), the Case-B catch ladder, and the U1–U7 defensive directives |
| `Infrastructure` | `Twilio/TwilioSettings.cs` | `AccountSid`, `AuthToken`, `FromNumber`, `MessagingServiceSid`, `BaseUrl` (messaging-host only), `DefaultCountryCode` |
| `Infrastructure` | `Twilio/TwilioServiceCollectionExtensions.cs` | `AddTwilioGateway(IConfiguration)` — binds settings, calls `AddTwilioClient(o => …)` applying `o.Server.Default.Production.BaseUrl` only when `BaseUrl` set, registers `ISmsGateway` |
| `PublicApi` | endpoints/mediator handlers | depend on `ISmsGateway` only; JWT auth unchanged; idempotency record (orderId+kind → sid) checked before create |

## 6. Assumptions & Blockers

**Assumptions**
1. Lookup **v2** chosen over v1: v1's `LookupsV1PhoneNumber` has **no `Valid` field** (map: records-4-Li-Me.md) — only v2's `LookupResponse` carries `Valid`/`ValidationErrors`. v1 remains available via `client.LookupsV1PhoneNumberApi.FetchPhoneNumber2` (also server group `Default4`).
2. The `Twilio:BaseUrl` override targets the `2010-04-01` messaging host ⇒ mapped to `o.Server.Default.Production.BaseUrl`; Lookup (`Default4`) deliberately untouched.
3. Account SID is passed both as auth `Username` and as the `accountSid` first parameter of every operation.
4. No MMS/content-template needs in scope (`mediaUrl`, `contentSid` stay null).
5. eShopOnWeb project names taken from the brief (`ApplicationCore`/`Infrastructure`/`PublicApi`); adjust to actual csproj names at implementation time.
6. Latest NuGet package matches the source HEAD this sheet was verified against; the bundled plugin map's stale client-layer names (`TwilioSdk*`) were corrected here — compiler is the backstop.

**Blockers** — none.
