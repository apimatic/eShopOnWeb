# Twilio .NET SDK integration plan — eShopOnWeb (ASP.NET Core)

## 1. Scope & sequence

| # | Step | Operations used |
|---|------|-----------------|
| 1 | Install package `AsadAli.TwilioSdk` (version-less: `dotnet add package AsadAli.TwilioSdk`); add `using`s per namespace table below | — |
| 2 | Register client + auth + messaging-only base-URL override in DI | client construction (see §3) |
| 3 | Phone-number validation at registration (Lookup V2) | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 4 | Send SMS immediately | `Api20100401Message.CreateMessage` |
| 5 | Send SMS scheduled (provider-queued) | `Api20100401Message.CreateMessage` (`scheduleType`/`sendAt`) |
| 6 | Cancel a scheduled message | `Api20100401Message.UpdateMessage` (`status`) |
| 7 | Read delivery status by SID | `Api20100401Message.FetchMessage` |
| 8 | Redact message body at provider | `Api20100401Message.UpdateMessage` (`body`) |
| 9 | Reconciliation listing (From + date range, all pages) | `Api20100401Message.ListMessage` |
| 10 | Error boundary around all of the above | `SdkException<RawError>` (all in-scope ops are Case B) |

**SDK identity** (map: `sdk-map.md`): NuGet `AsadAli.TwilioSdk` · root namespace `TwilioSdk` · client `TwilioSdkClient` · options `TwilioSdkClientOptions` · targets `netstandard2.0`. All operations are `async Task<…>` and take a trailing `CancellationToken ct = default`.

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

### 2a. Client construction, auth, base-URL override (map: `sdk-map.md` *Getting a client* / *Servers & auth*)

| Fact | Value | Namespace |
|------|-------|-----------|
| Constructor | `TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)` | `TwilioSdk` |
| DI extension | `services.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` (extension on `IServiceCollection`; registers via `IHttpClientFactory`) | `TwilioSdk` |
| Options members | `Environment: ServerEnvironment` (default `ServerEnvironment.Production`) · `Retry: RetryOptions` · `Logging: LoggingOptions` · `Server: ServerOptions` · `AccountSidAuthToken: BasicAuthCredentials?` | `TwilioSdk` |
| Credentials | `AccountSidAuthToken = new BasicAuthCredentials { Username = "<AccountSid>", Password = "<AuthToken>" }` — both members `required string`, init-only. (Source XML doc: an API key + secret is the preferred username/password; account SID + auth token is accepted but flagged for local testing.) | `TwilioSdk.Core.Authentication.Basic` |
| Environment | `ServerEnvironment.Production` is the only member; default already Production | `TwilioSdk.Servers` |
| Per-request options | `RequestOptions` is `sealed record` with one member `LogLevel? LogLevel` — pass `null` on every call here | `TwilioSdk.Core` |

**Base-URL override mechanism — per-server-node, NOT global.** `ServerOptions` (namespace `TwilioSdk`) has one property per Twilio host node, each with `Production.BaseUrl` (settable `string`):

| Node property on `options.Server` | Default `Production.BaseUrl` | Governs |
|-----------------------------------|------------------------------|---------|
| `Default` | `https://api.twilio.com` | the whole 2010 API host — **all `Api20100401*` controllers, including every Message operation (steps 4–9)** |
| `Default4` | `https://lookups.twilio.com` | Lookup V1/V2 (`LookupsV2PhoneNumber`, `LookupsV1PhoneNumberApi`) |
| `Default1` | `https://messaging.twilio.com` | `MessagingV1*` controllers (Messaging Service config — not in scope) |
| `Default2,3,5…14` | (other Twilio hosts) | not in scope |

So the `Twilio:BaseUrl` requirement maps to exactly one assignment in the options callback:
`o.Server.Default.Production.BaseUrl = twilioBaseUrl;` — applied **only when the config value is non-empty**, used verbatim as the base address for every 2010-API (message) call. Lookup calls keep `Default4` untouched; no other host is affected. There is no single global base-URL knob.

### 2b. Operations

**`client.LookupsV2PhoneNumber`** — source `Api/LookupsV2PhoneNumber.cs`, server node `Default4` (lookups) — map: `operations/LookupsV2PhoneNumber.md`

| Operation | `FetchPhoneNumber3` — `GET /v2/PhoneNumbers/{PhoneNumber}` |
|---|---|
| Signature | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Params | `phoneNumber` required (path; the number to validate, ideally E.164-ish user input). The 15 params `fields…partnerSubId` are **nullable with no C# default — must be passed explicitly (pass `null` to skip); use named arguments**. For validation-only: `fields: null` (base response already carries `Valid`/`ValidationErrors`), `countryCode: <ISO hint, e.g. "US">` or `null` when input is full international. Wire names: `Fields`, `CountryCode`, … |
| Returns | `LookupResponse` (`TwilioSdk.Models`) — fields read: `Valid (valid): bool?` · `ValidationErrors (validation_errors): IReadOnlyList<ValidationError>?` · `PhoneNumber (phone_number): string?` ← **canonical E.164 form to store** · `NationalFormat (national_format): string?` · `CountryCode (country_code): string?` · `CallingCountryCode (calling_country_code): string?` (map: `records-4-Li-Me.md`) |
| Error | **Case B** — `SdkException<RawError>`; accessors `StatusCode` / `ReadAsString()` / `ReadAsJson<T>()` / `ReadAsBytes()` |
| Notes | Lookup **V1** (`client.LookupsV1PhoneNumberApi.FetchPhoneNumber2` → `LookupsV1PhoneNumber`) has **no `Valid` field** in its response model (map: `records-4-Li-Me.md`) — do not use V1 for validation. Whether a given bad number comes back as 2xx with `Valid == false` or as a non-2xx `SdkException<RawError>` is provider behaviour — **UNVERIFIED**; treat BOTH as rejection: reject when `Valid != true`, and catch the Case-B exception as rejection too (inspect `StatusCode`). |

**`client.Api20100401Message`** — source `Api/Api20100401Message.cs`, server node `Default` (api) — map: `operations/Api20100401Message.md`. Every operation's first param is `string accountSid` (path `{AccountSid}`) — use the same Account SID as the auth username.

| Operation | `CreateMessage` — `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` |
|---|---|
| Signature | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Params | Required: `accountSid`, `to`. The 24 params `statusCallback…contentSid` are **nullable, no default — pass explicitly (`null` to skip); call with named arguments**. Immediate SMS: `from: <Twilio:FromNumber>` **xor** `messagingServiceSid: <Twilio:MessagingServiceSid>`, plus `body:`. Scheduled SMS: `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt: <DateTimeOffset>`, and **`messagingServiceSid:` — the `ScheduleType` enum's own doc says "For Messaging Services only"** (map: `enums.md`), so a scheduled send cannot use `Twilio:FromNumber`. Setting both `from` and `messagingServiceSid`: provider behaviour **UNVERIFIED** — set exactly one. Scheduling window (min lead time / max horizon): not expressed anywhere in the SDK contract — **UNVERIFIED**; an out-of-window `sendAt` is rejected by the provider with a 4xx Case-B error, so surface the `RawError` body to the caller rather than pre-validating from memory. |
| Returns | `ApiV2010AccountMessage` (`TwilioSdk.Models`) — `Sid (sid): string?` · `Status (status): MessageEnumStatus?` · `From (from)` / `To (to)` / `Body (body): string?` · `DateCreated (date_created)` / `DateSent (date_sent)` / `DateUpdated (date_updated): string?` (**strings on the wire, not `DateTimeOffset`**) · `ErrorCode (error_code): int?` · `ErrorMessage (error_message): string?` · `MessagingServiceSid (messaging_service_sid): string?` · `NumSegments (num_segments)` / `NumMedia (num_media)` / `Price (price)` / `PriceUnit (price_unit)` / `Direction (direction): MessageEnumDirection?` / `Uri (uri)` / `AccountSid (account_sid)` / `ApiVersion (api_version): string?` · `SubresourceUris (subresource_uris): object?` (map: `records-1-Ac-Ca.md`) |
| Error | **Case B** — `SdkException<RawError>` (e.g. invalid destination number ⇒ 4xx; read `StatusCode` + parse body per §2d) |
| Scheduled status | a scheduled-but-unsent message carries `Status == MessageEnumStatus.Scheduled` (wire `scheduled`) |

| Operation | `FetchMessage` — `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` |
|---|---|
| Signature | `FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `ApiV2010AccountMessage` (fields as above) — read `Status`, `ErrorCode`, `ErrorMessage`, `DateSent` |
| Error | **Case B** — `SdkException<RawError>` (unknown SID ⇒ 404) |

| Operation | `UpdateMessage` — `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` — map note: *"used to redact Message `body` text and to cancel not-yet-sent messages"* |
|---|---|
| Signature | `UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` — `body` and `status` **nullable, no default — pass explicitly**. Wire: `Body` ← `body`, `Status` ← `status` |
| Cancel (step 6) | `UpdateMessage(accountSid, sid, body: null, status: MessageEnumUpdateStatus.Canceled)` — `Canceled` is the **only** value of `MessageEnumUpdateStatus`. Cancel of an already-sent message ⇒ provider rejection as **Case B** `SdkException<RawError>`; the exact HTTP status is **UNVERIFIED** — branch on `ex.Error.StatusCode` and the parsed `code` field, do not assume one. |
| Redact (step 8) | `UpdateMessage(accountSid, sid, body: "", status: null)` — empty string sends `Body=` and erases the stored text; `null` would skip the parameter. The message **record** (SID, status, dates) survives; only the body is redacted. (`DeleteMessage` exists — `DeleteMessage(string accountSid, string sid, …)` returns `Task`, Case B — but it deletes the whole resource; it is **not** the redact path.) |
| Returns | `ApiV2010AccountMessage` (updated resource) |
| Error | **Case B** — `SdkException<RawError>` |

| Operation | `ListMessage` — `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` |
|---|---|
| Signature | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` — 8 params `to…pageToken` **nullable, no default — pass explicitly; named arguments** |
| Filters (wire ← C#) | `To` ← `to` · `From` ← `from` (the SENDING number, E.164) · `DateSent` ← `dateSent` (exact day) · **`DateSent<` ← `dateSentQuery` (before)** · **`DateSent>` ← `dateSentQueryQuery` (after)** — a from/to range = both inequality params · `PageSize` ← `pageSize` (`long?`) · `Page` ← `page` (`int?`) · `PageToken` ← `pageToken` (`string?`) |
| Returns | `ListMessageResponse` (`TwilioSdk.Models`) — `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?` · `NextPageUri (next_page_uri): string?` · `FirstPageUri (first_page_uri)` / `PreviousPageUri (previous_page_uri)` / `Uri (uri): string?` · `Page (page)` / `PageSize (page_size)` / `Start (start)` / `End (end): int?` (map: `records-4-Li-Me.md`). Item fields per `ApiV2010AccountMessage` above (sid, from, to, status, date_sent, date_created, error_code all present). |
| Pagination | **No SDK pager** (map row: "Pagination: none (only `page`, no `perPage`)") — enumerating the whole range is a hand-written loop over `page`/`pageToken`/`pageSize` driven by `NextPageUri`. The mechanics are a trap — see Trap notes. |
| Error | **Case B** — `SdkException<RawError>` |

### 2c. Enum values (map: `models/enums.md`; all are `StringEnum<T>` in `TwilioSdk.Models.Enums` — static members, not C# enum members)

| Enum | Values (`CSharpMember (wire)`) |
|------|-------------------------------|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` — the only settable status |
| `MessageEnumScheduleType` | `Fixed (fixed)` — the only value; Messaging-Services-only per its doc |
| `ValidationError` (Lookup V2) | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |

### 2d. Errors (map: `sdk-map.md` *Error-handling model*; `Core/Exceptions/SdkException.cs`, `Core/ErrorResponse/RawError.cs`)

- **Every in-scope operation is Case B, throw-only** (no `…Result` no-throw variants exist anywhere in this SDK): on an error **status** the SDK throws `SdkException<RawError>` (`TwilioSdk.Core.Exceptions`), `sealed`, one member `required TError Error { get; init; }`.
- `RawError` (`TwilioSdk.Core`, per `Core/ErrorResponse/`): `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>`. The exception itself has **no** StatusCode member — status lives at `ex.Error.StatusCode`.
- Twilio error bodies carry `code` / `message` / `more_info` / `status` (evidence: the SDK's generated error models, e.g. `AccountsCallsRecordingsSidJson201041408Error` in `records-1-Ac-Ca.md`, all declare exactly `Code (code): int?`, `Message (message): string?`, `MoreInfo (more_info): string?`, `Status (status): int?`). For Case-B ops define one small local DTO with those four wire names and read it via `ex.Error.ReadAsJson<TwilioErrorDto>()` — that is how the Twilio error code (e.g. invalid-number, cancel-too-late) is extracted.
- **Transport vs API errors:** `SdkException<T>` is thrown only for an error HTTP **status**. A transport failure never becomes an `SdkException` — it surfaces as `HttpRequestException` out of the HTTP layer. The boundary must catch both kinds (plus the two `JsonException` directions in REQUIRED READING).

## 3. Trap notes

> ⚠ Step 2 (client registration) — the `HttpClient`/handler pipeline and the SDK client wrapper have different lifetime rules, and the DI helper's registration makes its own choice about both; getting this wrong sockets-exhausts or needlessly rebuilds the pipeline. **MUST load `dotnet-client-initialization`** before wiring DI.
>
> ⚠ Step 2 (auth) — which credential pair to feed `AccountSidAuthToken` (API key + secret vs account SID + auth token) and where secrets may live (configuration, not code) is a decision the signature does not make for you. **MUST load `dotnet-authentication`**.
>
> ⚠ Steps 3–9 (every call) — the in-scope signatures carry 8–24 nullable parameters with **no C# defaults**; a positional call mis-binds silently. The calling convention (named arguments, explicit `null`s) is mandatory, not stylistic. **MUST load `dotnet-calling-endpoints`** before the first call.
>
> ⚠ Steps 3–9 (models) — SDK enums are `StringEnum<T>`, not C# enums: construction, equality, and display all behave differently, and message date fields arrive as `string?`, not `DateTimeOffset`. Mapping these onto domain types naïvely corrupts data. **MUST load `dotnet-models`**.
>
> ⚠ Step 10 (error boundary) — all in-scope ops are Case B, but `TryGetRawError` is not a catch-all and a status-only catch ladder misses two `JsonException` paths (see REQUIRED READING). Writing the boundary from this note alone will misclassify errors. **MUST load `dotnet-error-handling`**.
>
> ⚠ Steps 4–5 (retries on writes) — whether a failed `CreateMessage` POST can be re-sent by the retry layer, what `RetryOptions.Timeout` actually bounds, and what you must still wire yourself are not visible from the options' member names. A wrong assumption here means duplicate SMS sends. **MUST load `dotnet-configuration-resilience`** before tuning or accepting retry defaults.
>
> ⚠ Step 9 (reconciliation loop) — `ListMessage` has no SDK-level pager; turning `NextPageUri`/`page`/`pageToken` into a correct enumerate-every-page loop (and not silently dropping the last page) is hand-rolled. **MUST load `dotnet-configuration-resilience`** before writing the loop.
>
> ⚠ Tests — the test seam for this SDK is a specific constructor argument, not an interface over the client; stubbing the wrong seam couples tests to SDK internals. **MUST load `dotnet-testing`** before writing integration-layer tests.

## 4. REQUIRED READING

Load **before implementation starts** — this sheet deliberately does not carry their contents:

- `dotnet-client-initialization` — governs step 2: client construction, `HttpClient` ownership/lifetime, DI registration.
- `dotnet-authentication` — governs step 2: credential shape, secret sourcing, API-key-vs-auth-token choice.
- `dotnet-calling-endpoints` — governs steps 3–9: required-vs-optional params, named-argument calling, response envelopes, cancellation.
- `dotnet-models` — governs steps 3–9: `StringEnum<T>` semantics, wire names vs C# names, nullable/init-only record rules.
- `dotnet-error-handling` — governs step 10: Case A/B mechanics, `RawError` accessors, the exception boundary.
- `dotnet-configuration-resilience` — governs steps 2, 4–5, 9: retry/timeout semantics, base-URL/server selection, pagination, logging.
- `dotnet-testing` — governs tests: the fake seam, error-path coverage, SDK-independence of tests.

Two `System.Text.Json.JsonException` hazards reach the boundary from opposite directions and need opposite handling:

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

- **Scheduled messages require `Twilio:MessagingServiceSid`** — the `MessageEnumScheduleType` doc restricts scheduling to Messaging Services, so step 5 cannot use `Twilio:FromNumber`. If the app only has a raw from-number, scheduled send is blocked until a Messaging Service SID is provisioned.
- **UNVERIFIED (provider behaviour, not in the SDK contract):** the min/max scheduling window for `sendAt`; the HTTP status returned when canceling an already-sent message; whether Lookup V2 answers a given invalid number with 2xx+`valid:false` or a 4xx; provider behaviour when both `from` and `messagingServiceSid` are set. Defensive directives for each are inline in §2b — none of them block implementation.
- Assumed the eShopOnWeb "messaging API" in `Twilio:BaseUrl` means the 2010 Messages API host (`api.twilio.com`, node `Default`) — that is where all of steps 4–9 run. Lookup validation (step 3) deliberately stays on the default lookups host.
- Assumed `accountSid` for the path parameter equals the Account SID used as the basic-auth username.
- No blockers.
