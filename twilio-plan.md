# Twilio .NET SDK integration — plan & contract sheet (eShopOnWeb, src/PublicApi)

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 1 | Add package, register client + auth + optional messaging-only base-URL override | — (client setup) |
| 2 | Validate + canonicalize a contact number at registration | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | Send SMS immediately (Messaging Service SID, `From` as alternative) | `Api20100401Message.CreateMessage` |
| 4 | Schedule follow-up survey SMS (SendAt + ScheduleType) | `Api20100401Message.CreateMessage` |
| 5 | Cancel a scheduled message | `Api20100401Message.UpdateMessage` |
| 6 | Fetch current delivery outcome by SID | `Api20100401Message.FetchMessage` |
| 7 | Redact body (record survives) / full delete | `Api20100401Message.UpdateMessage` / `Api20100401Message.DeleteMessage` |
| 8 | Reconciliation list by sending number + date-sent range, paged | `Api20100401Message.ListMessage` |
| 9 | Error boundary around all of the above | `SdkException<RawError>` (all in-scope ops are Case B) |

**Package / identity** (map: `sdk-map.md`):
- NuGet: `AsadAli.TwilioSdk` — install **version-less** (`dotnet add package AsadAli.TwilioSdk`), floats to latest.
- Repo uses central package management (`Directory.Packages.props`, `ManagePackageVersionsCentrally=true`) and has **no existing Twilio reference** — implementation will need a `PackageVersion` entry there plus a `PackageReference` in `src/PublicApi` (main agent's edit; not done here).
- Root namespace `TwilioSdk`; client `TwilioSdkClient`; options `TwilioSdkClientOptions`.
- Async: every operation returns `Task<T>`/`Task`, is **throw-only** (no `…Result` no-throw variants exist anywhere in this SDK), and takes a trailing `CancellationToken ct = default`.

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

Namespaces in play: `TwilioSdk` (client, options, `ServerOptions`) · `TwilioSdk.Servers` (`ServerEnvironment`, `DefaultOptions`…`Default14Options`) · `TwilioSdk.Core.Authentication.Basic` (`BasicAuthCredentials`) · `TwilioSdk.Core.Configuration` (`RetryOptions`, `LoggingOptions`) · `TwilioSdk.Core` (`RequestOptions`) · `TwilioSdk.Core.Exceptions` (`SdkException<TError>`) · `TwilioSdk.Core.ErrorResponse` (`RawError`) · `TwilioSdk.Models` (records) · `TwilioSdk.Models.Enums` (enums).

### Client construction, auth, base-URL override (map: `sdk-map.md` *Getting a client* / *Servers & auth*; source: `TwilioSdkClientOptions.cs`, `ServerOptions.cs`, `Server.cs`, `Servers/DefaultOptions.cs`, `Servers/Default4Options.cs`)

| Fact | Value |
|---|---|
| Constructor | `TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)` |
| DI registration | `services.AddTwilioSdkClient(o => { … })` — registers the client as a **singleton** built on `IHttpClientFactory` |
| Auth property | `o.AccountSidAuthToken = new BasicAuthCredentials { Username = <AccountSid>, Password = <AuthToken> };` — `BasicAuthCredentials` (`TwilioSdk.Core.Authentication.Basic`) has `required string Username` / `required string Password` (init-only) |
| Environment | `o.Environment` — `ServerEnvironment.Production` is the only member (`TwilioSdk.Servers`) |
| **Messaging-only base-URL override** | `o.Server.Default.Production.BaseUrl = "<Twilio:BaseUrl>";` — `ServerOptions` (namespace `TwilioSdk`) carries one property per server node (`Default`…`Default14`), each with a `Production.BaseUrl` string. Node **`Default`** = `https://api.twilio.com` and serves **all** `Api20100401Message` operations; node **`Default4`** = `https://lookups.twilio.com` serves Lookup. Setting `Server.Default.Production.BaseUrl` applies the override verbatim to every messaging-API call and leaves Lookup untouched. There is **no edge/region mechanism** in this SDK — the per-node `BaseUrl` is the only override point. |
| Other options | `o.Retry` (`RetryOptions`, all members `required` — use `RetryOptions.Default()`), `o.Logging` (`LoggingOptions`) — both `TwilioSdk.Core.Configuration` |

### Op 1 — Validate + canonicalize number (map: `operations/LookupsV2PhoneNumber.md`, `records-4-Li-Me.md`, `enums.md`)

| | |
|---|---|
| Call | `client.LookupsV2PhoneNumber.FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | the 15 nullable params `fields`…`partnerSubId` have **no C# default** — pass each explicitly (`null` to skip). Use named arguments. |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` on server node `Default4` (`https://lookups.twilio.com`) — **not** affected by the messaging base-URL override |
| Key params | `phoneNumber` (path): E.164 or national format; `countryCode`: ISO 3166-1 alpha-2, used when `phoneNumber` is national format; `fields`: comma-separated list — pass `fields: "validation"` to get the validity signals (source doc lists `validation` among the field values) |
| Returns | `LookupResponse` (`TwilioSdk.Models`): `CallingCountryCode (calling_country_code): string?`, `CountryCode (country_code): string?`, **`PhoneNumber (phone_number): string?` ← canonical E.164 form to store**, `NationalFormat (national_format): string?`, **`Valid (valid): bool?`**, **`ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?`**, `Url (url): string?`, plus nullable add-on blocks (`CallerName`, `SimSwap`, `CallForwarding`, `LineTypeIntelligence`, `LineStatus`, `IdentityMatch`, `ReassignedNumber`, `SmsPumpingRisk`, `PhoneNumberQualityScore`, `PreFill`) |
| Invalid-number signals | (a) 2xx with `Valid == false` and `ValidationErrors` populated; (b) provider error status → `SdkException<RawError>` (Case B). Handle **both**. |
| Error case | **B** — `SdkException<RawError>`; accessors `StatusCode`, `ReadAsString()`, `ReadAsJson<T>()`, `ReadAsBytes()` |

`ValidationError` enum (`TwilioSdk.Models.Enums`, StringEnum): `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)`.

### Op 2 — CreateMessage: immediate send AND scheduled send (map: `operations/Api20100401Message.md`, `records-1-Ac-Ca.md`, `enums.md`)

| | |
|---|---|
| Call | `client.Api20100401Message.CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass | the 24 nullable params `statusCallback`…`contentSid` have **no C# default** — pass each explicitly (`null` to skip). Named arguments are effectively mandatory here. |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` on server node `Default` (`https://api.twilio.com` — the node `Twilio:BaseUrl` overrides). Parameters go as a **form-url-encoded body** (map's "query params" label is generic; source builds `FormUrlEncodedRequest`), and the SDK auto-adds an `Idempotency-Key: Guid.NewGuid()` header per invocation. |
| Immediate send (Messaging Service) | `to: <E.164>`, `messagingServiceSid: <Twilio:MessagingServiceSid>`, `body: <text>`, `from: null`, everything else `null` |
| Immediate send (direct From) | same but `from: <Twilio:FromNumber>`, `messagingServiceSid: null` |
| Scheduled send | Messaging-Service form above **plus** `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt: <DateTimeOffset>` (wire `ScheduleType`, `SendAt`). Scheduling requires a Messaging Service SID. |
| Returns | `ApiV2010AccountMessage` — see response model below. A scheduled message comes back with `Status == MessageEnumStatus.Scheduled`. |
| Error case | **B** — `SdkException<RawError>` |

`ApiV2010AccountMessage` (`TwilioSdk.Models`; returned by Create/Fetch/Update, and the element type of List): `Body (body): string?`, `NumSegments (num_segments): string?`, `Direction (direction): MessageEnumDirection?`, `From (from): string?`, `To (to): string?`, `DateUpdated (date_updated): string?`, `Price (price): string?`, `ErrorMessage (error_message): string?`, `Uri (uri): string?`, `AccountSid (account_sid): string?`, `NumMedia (num_media): string?`, **`Status (status): MessageEnumStatus?`**, `MessagingServiceSid (messaging_service_sid): string?`, **`Sid (sid): string?`**, `DateSent (date_sent): string?` (string, not `DateTimeOffset`), `DateCreated (date_created): string?`, **`ErrorCode (error_code): int?`**, `PriceUnit (price_unit): string?`, `ApiVersion (api_version): string?`, `SubresourceUris (subresource_uris): object?`. No envelope — the payload **is** the return type.

### Op 3 — UpdateMessage: cancel scheduled + redact body (map: `operations/Api20100401Message.md`)

| | |
|---|---|
| Call | `client.Api20100401Message.UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` are nullable with **no default: pass both explicitly** (`null` to leave unchanged) |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (node `Default`) |
| Cancel scheduled | `status: MessageEnumUpdateStatus.Canceled` (wire `Status=canceled`), `body: null`. Only valid while the message is still `scheduled`. |
| Redact body | `body: ""` (empty string, wire `Body`), `status: null`. The Message record (Sid, status, dates) survives; the body text is disposed of. |
| Already-sent cancel | provider rejects the update → `SdkException<RawError>` (Case B). The SDK models no typed status/code for this — read `StatusCode` + parse the body best-effort (see Errors below). |
| Returns | `ApiV2010AccountMessage` |
| Error case | **B** — `SdkException<RawError>` |

### Op 4 — FetchMessage: current delivery outcome (map: `operations/Api20100401Message.md`)

| | |
|---|---|
| Call | `client.Api20100401Message.FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (node `Default`) |
| Returns | `ApiV2010AccountMessage` — read `Status` (`MessageEnumStatus?`), `ErrorCode` (`int?`), `ErrorMessage` (`string?`), `DateSent` |
| Error case | **B** — `SdkException<RawError>` (unknown SID → error status, e.g. 404, surfaced only through `RawError`) |

### Op 5 — DeleteMessage: full delete (map: `operations/Api20100401Message.md`)

| | |
|---|---|
| Call | `client.Api20100401Message.DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| HTTP | `DELETE /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` (node `Default`) |
| Returns | `void` (`Task`) |
| Note | Prefer **UpdateMessage with `body: ""`** for redaction — it disposes of the content while the record survives, which the reconciliation step (Op 6) depends on. DeleteMessage removes the record entirely. |
| Error case | **B** — `SdkException<RawError>` |

### Op 6 — ListMessage: reconciliation by sender + date range (map: `operations/Api20100401Message.md`, `records-4-Li-Me.md`)

| | |
|---|---|
| Call | `client.Api20100401Message.ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — the 8 nullable params `to`…`pageToken` have **no default: pass all explicitly** |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` (node `Default`) |
| Filters (wire ← C#) | `To` ← `to` · `From` ← `from` (set to `Twilio:FromNumber`) · `DateSent` ← `dateSent` (exact day) · **`DateSent<` ← `dateSentQuery` (sent BEFORE — use for range end)** · **`DateSent>` ← `dateSentQueryQuery` (sent AFTER — use for range start)** · `PageSize` ← `pageSize` (`long?`, default 50, max 1000) · `Page` ← `page` · `PageToken` ← `pageToken` |
| ⚠ generated-name trap | `dateSentQuery` vs `dateSentQueryQuery` is mechanical codegen, not a typo: the single-`Query` one is the **`<`** (before) filter, the double-`QueryQuery` one is the **`>`** (after) filter. A range [from, to] maps to `dateSentQueryQuery: from`, `dateSentQuery: to`. |
| Returns | `ListMessageResponse` (`TwilioSdk.Models`): **`Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`** plus paging fields `End (end): int?`, `FirstPageUri (first_page_uri): string?`, **`NextPageUri (next_page_uri): string?`**, `Page (page): int?`, `PageSize (page_size): int?`, `PreviousPageUri (previous_page_uri): string?`, `Start (start): int?`, `Uri (uri): string?` |
| Pagination | **No built-in pager on this operation** (map: "none — only `page`, no `perPage`"). Page manually: loop with `pageSize` + incrementing `page` (or follow `NextPageUri`/`pageToken`) until a page returns null/empty `Messages` or no `NextPageUri`. |
| Error case | **B** — `SdkException<RawError>` |

### Enums actually needed (`TwilioSdk.Models.Enums`; all are `StringEnum<T>` — use the static members, e.g. `MessageEnumStatus.Scheduled`, or `…FromValue("wire")`; map: `enums.md`)

| Enum | Values (Member `wire`) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, **`Scheduled (scheduled)`**, `Read (read)`, `PartiallyDelivered (partially_delivered)`, **`Canceled (canceled)`** |
| `MessageEnumScheduleType` | **`Fixed (fixed)`** (only value) |
| `MessageEnumUpdateStatus` | **`Canceled (canceled)`** (only value) |
| `MessageEnumContentRetention` | `Retain (retain)`, `Discard (discard)` |
| `MessageEnumAddressRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` |
| `MessageEnumTrafficType` | `Free (free)` |
| `MessageEnumRiskCheck` | `Enable (enable)`, `Disable (disable)` |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |

### Errors (map: `sdk-map.md` *Error-handling model*; every in-scope operation row)

- **All six in-scope operations are Case B**: they throw `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`. There are no typed `{Operation}Error` classes and no `TryGet…` status accessors for them.
- Read a failure via: `ex.Error.StatusCode` (`HttpStatusCode`) · `ex.Error.ReadAsString()` · `ex.Error.ReadAsJson<T>()` · `ex.Error.ReadAsBytes()`.
- **Provider error codes (21211 invalid `To`, 21610 unsubscribed/blocked, 20404 not found, etc.) are NOT SDK-modeled types** — they exist only as fields in the raw error body JSON. Defensive directive: deserialize the body best-effort with `ReadAsJson<T>()` into a small local DTO (fields such as `code`, `message`, `status`, `more_info`), fall back to `ReadAsString()`, and never let body parsing throw inside the catch. `UNVERIFIED`: the exact error-payload wire shape — only live traffic confirms it; extract best-effort, fall back to the generic message.
- Invalid-number (Op 1) is usually **not** an exception at all — it is a 2xx `LookupResponse` with `Valid == false`. Treat exception and `Valid == false` as the two distinct rejection paths.

## 3. Trap notes (hazards — load the named skill BEFORE writing that step's code)

> ⚠ Step 1 (client registration) — the `HttpClient`/handler pipeline behind the SDK client has lifetime rules the constructor signature does not show; getting them wrong exhausts sockets. **MUST load `dotnet-client-initialization`** before writing `new TwilioSdkClient(...)` or `AddTwilioSdkClient`.

> ⚠ Step 1 (auth) — where credentials may be set, and when, is not visible from the options type; secrets must come from configuration (`Twilio:AccountSid`/`Twilio:AuthToken`), never code. **MUST load `dotnet-authentication`**.

> ⚠ Steps 2–8 (every call) — most optional parameters are nullable **without C# defaults** and mis-bind in positional calls; list/search calls are the worst offenders. **MUST load `dotnet-calling-endpoints`** before the first call.

> ⚠ Steps 2–8 (models) — SDK enums are `StringEnum<T>`, not C# enums; records are immutable with `init`-only/required members; unmodeled JSON fields are silently dropped on deserialize. **MUST load `dotnet-models`** before constructing or reading payloads.

> ⚠ Step 9 (error boundary) — which exception types actually reach a catch block, and what `TryGetRawError` does and does not cover, is not derivable from the signatures; a boundary written from the signatures alone mis-classifies failures. **MUST load `dotnet-error-handling`** before writing any `try/catch`.

> ⚠ Step 1 + Step 8 (resilience & paging) — what the SDK's retry/timeout options actually bound, whether a failed write can be re-sent (CreateMessage is a non-idempotent POST; the SDK adds an `Idempotency-Key` header per invocation), and how to drive a list endpoint that has no built-in pager — none of this is visible from the signatures. **MUST load `dotnet-configuration-resilience`** before tuning the client and before writing the reconciliation paging loop.

> ⚠ Tests — the SDK's test seam is specific (the `HttpClient` constructor argument), and faking at the wrong seam produces tests that assert nothing. **MUST load `dotnet-testing`** before stubbing the SDK.

## 4. REQUIRED READING (load ALL before implementation starts — this sheet deliberately does not carry their contents)

- `dotnet-client-initialization` — Step 1 (client construction & DI lifetime)
- `dotnet-authentication` — Step 1 (credentials wiring)
- `dotnet-calling-endpoints` — Steps 2–8 (every operation call)
- `dotnet-models` — Steps 2–8 (request/response models, StringEnum enums)
- `dotnet-error-handling` — Step 9 (the error boundary)
- `dotnet-configuration-resilience` — Steps 1 & 8 (retries/timeouts, base-URL, manual pagination)
- `dotnet-testing` — integration tests (fake seam)

Two hazards belong in the FIRST boundary design, verbatim:

- a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
  `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an
  SDK-exception-only catch ladder lets it escape the integration boundary;
- a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
  throws `JsonException` *while the error object is being constructed*, so the `JsonException`
  **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
  maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
  and a caller that retries 5xx retries something that can never succeed.

**MUST load `dotnet-error-handling`** before writing that boundary.

## 5. Assumptions & Blockers

- **Assumed** Lookup v2 is the validation capability (the map exposes `LookupsV2PhoneNumber.FetchPhoneNumber3` and a v1 variant; v2 carries `Valid`/`ValidationErrors`/canonical `PhoneNumber`). Pass `fields: "validation"` so the validity signals come back.
- **Assumed** Messaging Service SID is the primary send path (needed for scheduling); `from` is covered as the direct alternative. Exactly one of `from`/`messagingServiceSid` should be non-null per send.
- **Assumed** redaction = `UpdateMessage(body: "")` (record survives for reconciliation); `DeleteMessage` documented as the stronger alternative.
- **Drift flag (potential blocker)**: this sheet is grounded in the SDK map pinned at source commit `51fdf48` ("Publish v2.0.0"). The SDK repo's `main` has since renamed the client surface (`TwilioSdkClient`→`TwilioClient`, `TwilioSdk`→`Twilio` namespaces), and `dotnet add package` installs the **latest** release. If any name in this sheet fails to compile, trust the compiler — that is the drift surfacing — and re-ground the affected rows from the installed package's source before coding further. Do not patch around it from memory.
- **UNVERIFIED**: the exact JSON shape of provider error bodies (where codes like 21211/21610/20404 live) — only live traffic confirms it; the contract is the defensive `ReadAsJson<T>`-with-fallback directive above.
- No blockers to starting implementation beyond the drift flag.
