# Twilio .NET SDK integration plan — eShopOnWeb `src/PublicApi` (SMS order notifications)

SDK: NuGet `AsadAli.TwilioSdk` **2.0.0** (the only published version; matches the SDK-map pinned
commit `51fdf48` "Publish v2.0.0 SDK"). Root namespace `TwilioSdk`, client `TwilioSdkClient`,
options `TwilioSdkClientOptions`. SDK targets `netstandard2.0` → runs on .NET 8.
(sdk-map.md; `TwilioSdkClient.cs`)

## 1. Scope & sequence

| # | Step | Operations used |
|---|---|---|
| 0 | Add `PackageVersion Include="AsadAli.TwilioSdk" Version="2.0.0"` to `Directory.Packages.props`; `PackageReference` in `src/PublicApi` | — |
| 1 | Register client in DI; bind `Twilio:AccountSid` / `Twilio:AuthToken` / optional `Twilio:BaseUrl` (messaging host only) | client construction |
| 2 | Validate + canonicalize contact number before storing | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | Send SMS immediately (order confirmation) | `Api20100401Message.CreateMessage` |
| 4 | Schedule delivery follow-up days later (provider-queued) | `Api20100401Message.CreateMessage` (schedule params) |
| 5 | Cancel a not-yet-sent scheduled message | `Api20100401Message.UpdateMessage` |
| 6 | Poll delivery outcome by SID (no webhooks) | `Api20100401Message.FetchMessage` |
| 7 | Reconcile: list messages by our From number + date-sent range, all pages | `Api20100401Message.ListMessage` |
| 8 | Redact message content on shopper disposal request (record must survive) | `Api20100401Message.UpdateMessage` |
| 9 | Integration boundary: no messaging failure may fail the business operation | error model, §2.5 |

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

### 2.1 Client construction, auth, servers, base-URL override

| Fact | Value | Source |
|---|---|---|
| Package | `AsadAli.TwilioSdk` `2.0.0` (add to `Directory.Packages.props` under CPM) | sdk-map.md + NuGet |
| Client | `TwilioSdk.TwilioSdkClient(HttpClient httpClient, TwilioSdkClientOptions options)` (sealed; every API group is a get-only property) | sdk-map.md; `TwilioSdkClient.cs` |
| Options | `TwilioSdk.TwilioSdkClientOptions`: `Environment: ServerEnvironment` (default `ServerEnvironment.Default()`), `Retry: RetryOptions`, `Logging: LoggingOptions`, `Server: ServerOptions`, `AccountSidAuthToken: BasicAuthCredentials?` | sdk-map.md; `TwilioSdkClientOptions.cs` |
| DI | `services.AddTwilioSdkClient(o => { … })` — extension in `TwilioSdk.ServiceCollectionExtensions`; internally calls `services.AddHttpClient()`, builds the `HttpClient` from `IHttpClientFactory`, registers the client as **singleton** | `ServiceCollectionExtensions.cs` |
| Auth | `o.AccountSidAuthToken = new TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> };` — both members `required string` (init). Basic auth on every operation (`[_auth.AccountSidAuthToken]` in each call) | sdk-map.md *Servers & auth*; `Core/Authentication/Basic/BasicAuthCredentials.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment` — only member `Production`; leave at default. Environment is **not** the base-URL override point | `Servers/ServerEnvironment.cs` |
| **Messaging base-URL override** | `o.Server.Default.Production.BaseUrl = <Twilio:BaseUrl>` when non-empty. `TwilioSdk.ServerOptions.Default` is a `TwilioSdk.Servers.DefaultOptions` whose nested `ProductionOptions.BaseUrl` defaults to `https://api.twilio.com`. Request URL = `BaseUrl.TrimEnd('/') + "/" + path` — the override is used **verbatim** as the base address | `ServerOptions.cs`; `Servers/DefaultOptions.cs`; `Core/TemplateParamsFactory.cs` |
| Lookup host isolation | Lookup operations resolve server **Default4** (`o.Server.Default4.Production.BaseUrl`, default `https://lookups.twilio.com`). Overriding `Server.Default` touches only the messaging API; **`Twilio:BaseUrl` cannot affect Lookup** | `Servers/Default4Options.cs`; `Api/LookupsV2PhoneNumber.cs` (`_server.Default4(...)`) |
| `accountSid` param | First parameter of every messaging operation; path template `{AccountSid}`. Pass `Twilio:AccountSid` (same value as the auth username, but an independent parameter) | operations/Api20100401Message.md |
| `RequestOptions` | `TwilioSdk.Core.RequestOptions` (sealed record, only member `LogLevel? LogLevel`) — last-but-one param of every op, default `null`; omit it | `Core/RequestOptions.cs` |

Config mapping: `Twilio:AccountSid` → auth `Username` + `accountSid` arg · `Twilio:AuthToken` → auth `Password` · `Twilio:BaseUrl` → `o.Server.Default.Production.BaseUrl` (only when set) · `Twilio:FromNumber` → `from` (send) / `from` (list filter) · `Twilio:MessagingServiceSid` → `messagingServiceSid` (required for scheduled send).

### 2.2 Operations

**① Validate phone number — `client.LookupsV2PhoneNumber.FetchPhoneNumber3`** (operations/LookupsV2PhoneNumber.md)
`GET {lookups-host}/v2/PhoneNumbers/{PhoneNumber}` · server Default4 (NOT governed by `Twilio:BaseUrl`)
```csharp
Task<TwilioSdk.Models.LookupResponse> FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, RequestOptions? requestOptions = null, CancellationToken ct = default)
```
- Call: `FetchPhoneNumber3(number, fields: null, countryCode: null-or-ISO3166, …all remaining nullable params…: null)` — 15 nullable params have **no C# default → must be passed explicitly** (pass `null`); use named arguments. `fields: null` returns the base validation payload (doc: comma-separated list, `validation` is the base set).
- `phoneNumber`: E.164 or national format; default country +1 (North America); `countryCode` (ISO 3166-1 alpha-2) is used when national format is supplied (`Api/LookupsV2PhoneNumber.cs` XML docs).
- Response `TwilioSdk.Models.LookupResponse` (records-4-Li-Me.md) — read: `Valid (valid): bool?` (treat `null` as not-validated), `PhoneNumber (phone_number): string?` (**canonical E.164 — store this**), `NationalFormat (national_format): string?`, `CountryCode (country_code): string?`, `CallingCountryCode (calling_country_code): string?`, `ValidationErrors (validation_errors): IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?`, `Url (url): string?`. Other fields (`CallerName`, `SimSwap`, …) are paid add-on payloads — ignore.
- Reject when `Valid != true`; `ValidationErrors` explains why (enum §2.4).
- Error: **Case B** `SdkException<RawError>`.

**② Send SMS now / ④ schedule — `client.Api20100401Message.CreateMessage`** (operations/Api20100401Message.md)
`POST {api-host}/2010-04-01/Accounts/{AccountSid}/Messages.json` · server Default · **form-url-encoded body** (not JSON; `FormUrlEncodedRequest`, `Api/Api20100401Message.cs`) · SDK auto-adds `Idempotency-Key: <new Guid>` per call
```csharp
Task<TwilioSdk.Models.ApiV2010AccountMessage> CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, MessageEnumContentRetention? contentRetention, MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, MessageEnumTrafficType? trafficType, bool? shortenUrls, MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)
```
- 24 nullable params (`statusCallback`…`contentSid`) have **no C# default → must be passed explicitly**; call with named arguments only.
- Immediate send: `to: <E.164 recipient>`, `body: <text>`, `from: <Twilio:FromNumber>` (or `messagingServiceSid:`), everything else `null`.
- Scheduled send: `messagingServiceSid: <Twilio:MessagingServiceSid>` (**required** — enum doc: "For Messaging Services only", enums.md), `scheduleType: MessageEnumScheduleType.Fixed`, `sendAt: <future instant>`, plus `to`/`body`. Do **not** pass `from` when scheduling via a messaging service (provider-side exclusivity UNVERIFIED — pass exactly one sender identity).
- `sendAt` wire format: form field `SendAt`; the value goes through `JsonSerializer.Serialize` → System.Text.Json default `DateTimeOffset` rendering (ISO-8601 with offset, e.g. `2026-09-04T14:00:00+00:00`) — pass a UTC-based `DateTimeOffset` (`Core/ParameterFlattener.cs`, `Core/Extensions/ObjectExtensions.cs`).
- Scheduling window (min lead time / max horizon): **not stated in map or source — UNVERIFIED.** Defensive directive: validate `sendAt` is comfortably in the future before calling, and surface the provider's rejection body via the §2.5 boundary rather than pre-encoding limits.
- Persist from response `ApiV2010AccountMessage`: `Sid (sid)`, `Status (status)` — `MessageEnumStatus.Queued` for immediate accept, `MessageEnumStatus.Scheduled` (`scheduled`) for a scheduled message (enums.md).
- Error: **Case B** `SdkException<RawError>` (invalid `To` etc. arrive here — §2.5).

**⑤ Cancel scheduled — `client.Api20100401Message.UpdateMessage`** (operations/Api20100401Message.md)
`POST {api-host}/2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` · form body · doc: "used to redact Message `body` text and to cancel not-yet-sent messages"
```csharp
Task<TwilioSdk.Models.ApiV2010AccountMessage> UpdateMessage(string accountSid, string sid, string? body, MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)
```
- Cancel: `UpdateMessage(accountSid, sid, body: null, status: MessageEnumUpdateStatus.Canceled)` — the enum's only value is `Canceled (canceled)` (enums.md). `body`/`status` must be passed explicitly (`null` omits the form field — `Core/ParameterFlattener.cs` skips nulls).
- Cancelable statuses: doc says "not-yet-sent" — the precise provider-side set is **UNVERIFIED**. Defensive directive: only attempt when our last known `Status` is `Scheduled`; on failure read the provider code/message from the error body (§2.5) and treat as "no longer cancelable".
- Error if already sent: provider error status → **Case B** `SdkException<RawError>`; the specific provider error code is **UNVERIFIED** — extract from body, never hardcode.

**⑥ Poll one message — `client.Api20100401Message.FetchMessage`** (operations/Api20100401Message.md)
`GET {api-host}/2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json`
```csharp
Task<TwilioSdk.Models.ApiV2010AccountMessage> FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)
```
- Read: `Status (status): MessageEnumStatus?`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?`, `DateSent (date_sent): string?`, `Price (price)`, `PriceUnit (price_unit)`.
- Error: **Case B** `SdkException<RawError>` (unknown SID → error status; read body).

**⑦ Reconcile — `client.Api20100401Message.ListMessage`** (operations/Api20100401Message.md)
`GET {api-host}/2010-04-01/Accounts/{AccountSid}/Messages.json` · filters are **server-side query params**
```csharp
Task<TwilioSdk.Models.ListMessageResponse> ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)
```
- Wire mapping (operations page + `Api/Api20100401Message.cs`): `From` ← `from` (pass `Twilio:FromNumber`) · `To` ← `to` · `DateSent` ← `dateSent` (exact date) · **`DateSent<` ← `dateSentQuery` (before)** · **`DateSent>` ← `dateSentQueryQuery` (after)** — the generated names are misleading; named arguments mandatory · `PageSize` ← `pageSize` (doc: default 50, max 1000) · `Page` ← `page` ("simply for client state") · `PageToken` ← `pageToken` ("provided by the API").
- Date serialization: SDK sends `dateSent?.ToIso8601()` = UTC `yyyy-MM-ddTHH:mm:ss.fffZ` (`Core/Extensions/DateTimeOffsetExtensions.cs`). The XML doc describes date-only formats (`YYYY-MM-DD`, `<=…`, `>=…`); whether the provider honors the time component is **UNVERIFIED** → defensive directive: use whole-day UTC boundaries (after = day 00:00:00Z, before = next-day 00:00:00Z) so the granularity cannot change the result.
- Pagination: **no SDK pager** (operations page: "Pagination: none"). Manual loop over the envelope `TwilioSdk.Models.ListMessageResponse` (records-4-Li-Me.md): `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`, `NextPageUri (next_page_uri): string?`, plus `FirstPageUri`, `PreviousPageUri`, `Page (page): int?`, `PageSize (page_size): int?`, `Start (start)`, `End (end)`, `Uri (uri)`. Loop: while `NextPageUri` is non-null, take the `PageToken` query value from it and pass as `pageToken`. Whether `next_page_uri` is absolute or relative is **UNVERIFIED** → parse defensively (tolerate both).
- Per-item fields for reconciliation: `Sid`, `To`, `From`, `Status`, `DateSent`, `ErrorCode` — all on `ApiV2010AccountMessage` (§2.3). **Date fields are `string?`, not `DateTimeOffset`** — parse app-side.
- Error: **Case B** `SdkException<RawError>`.

**⑧ Redact content — `client.Api20100401Message.UpdateMessage`** (same row as ⑤)
- Redact: `UpdateMessage(accountSid, sid, body: "", status: null)` — empty string **is** transmitted (`Body=` form field; only `null` is skipped — `Core/ParameterFlattener.cs`). The record (Sid, status, outcome) survives; doc names this the redaction path.
- `DeleteMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` → `Task` (void), doc: "Deletes a Message resource from your account" — removes the record; **do not use** for this requirement (the record must survive).

### 2.3 Models referenced (all `TwilioSdk.Models`, records-1-Ac-Ca.md / records-4-Li-Me.md)

`ApiV2010AccountMessage` (returned by Create/Fetch/Update; item of List): `Body (body): string?`, `NumSegments (num_segments): string?`, `Direction (direction): MessageEnumDirection?`, `From (from): string?`, `To (to): string?`, `DateUpdated (date_updated): string?`, `Price (price): string?`, `ErrorMessage (error_message): string?`, `Uri (uri): string?`, `AccountSid (account_sid): string?`, `NumMedia (num_media): string?`, `Status (status): MessageEnumStatus?`, `MessagingServiceSid (messaging_service_sid): string?`, `Sid (sid): string?`, `DateSent (date_sent): string?`, `DateCreated (date_created): string?`, `ErrorCode (error_code): int?`, `PriceUnit (price_unit): string?`, `ApiVersion (api_version): string?`, `SubresourceUris (subresource_uris): object?` — no envelope; the payload **is** the record.

### 2.4 Enums (all `TwilioSdk.Models.Enums`, enums.md) — `StringEnum<T>`, not C# enums

| Enum | Members (wire) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumScheduleType` | `Fixed (fixed)` — "For Messaging Services only … in conjuction with the send_time parameter" |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |
| (optional create params, pass `null` in scope) | `MessageEnumContentRetention` `Retain/Discard`; `MessageEnumAddressRetention` `Retain/Obfuscate`; `MessageEnumTrafficType` `Free`; `MessageEnumRiskCheck` `Enable/Disable` |

### 2.5 Error model (sdk-map.md *Error-handling model*)

- Every in-scope operation is **Case B**: throws `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`; no `…Result` no-throw variants exist in this SDK.
- `RawError` members: `StatusCode: HttpStatusCode` · `ReadAsString(): string` · `ReadAsJson<T>(): T?` · `ReadAsBytes(): ReadOnlyMemory<byte>`.
- Twilio's error JSON carries `code` / `message` / `more_info` / `status` (shape evidenced by the SDK's own error records, e.g. `AccountsCallsRecordingsSidJson201041408Error`, records-1-Ac-Ca.md). There is **no** message-specific typed error model — deserialize via `ex.Error.ReadAsJson<AppTwilioErrorDto>()` into an app-owned DTO with those four `[JsonPropertyName]`s, falling back to `ReadAsString()`.
- Catch ladder (per call site): `catch (SdkException<RawError> ex)` → branch on `ex.Error.StatusCode`, extract provider code/message via the DTO → then a general `Exception` backstop. Specific provider codes for invalid-`To` on create and cancel-after-send are **UNVERIFIED** from map/source — branch on HTTP status + log the provider code, never hardcode numeric Twilio codes.
- Requirement "messaging must never fail the business operation": the boundary catches, logs, and returns a result object; it never rethrows into the order pipeline. Shape per `dotnet-error-handling` (REQUIRED READING).

## 3. Trap notes

> ⚠ Step 1 (client registration) — the SDK's DI helper builds its `HttpClient` from `IHttpClientFactory` and registers the client singleton; hand-rolling construction (or "fixing" lifetime without the factory pattern) is how socket exhaustion and stale-DNS bugs enter. What the factory/handler pipeline must look like, and what may be transient vs singleton, is not visible from the constructor signature. **MUST load `dotnet-client-initialization`** before wiring DI.

> ⚠ Step 1 (auth) — credentials must be set on the options before the client is constructed (the DI callback is the seam), and secrets come from configuration, never code; the SDK doc also prefers an API key over account SID + auth token outside local testing. **MUST load `dotnet-authentication`** before wiring credentials.

> ⚠ Steps 2–8 (every call) — 24 of `CreateMessage`'s params (15 of `FetchPhoneNumber3`'s, 8 of `ListMessage`'s) are nullable with **no C# default**: positional calls mis-bind silently, and `dateSentQuery` vs `dateSentQueryQuery` are actively misleading names. **MUST load `dotnet-calling-endpoints`** before the first call.

> ⚠ Steps 2–8 (models) — enums are `StringEnum<T>` (construct via static members like `MessageEnumScheduleType.Fixed` or `FromValue("fixed")`, never C# enum syntax); records are immutable with init-only setters; unmodeled JSON fields are silently dropped on deserialize (matters for the error DTO and any drifted response). **MUST load `dotnet-models`**.

> ⚠ Step 9 (error boundary) — Case A vs Case B differs per operation (all seven here are Case B), `TryGetRawError` is not a catch-all, and `System.Text.Json.JsonException` reaches the boundary from two directions needing opposite handling (see REQUIRED READING). **MUST load `dotnet-error-handling`** before writing any `try/catch`.

> ⚠ Steps 3–4, 7 (resilience & pagination) — the SDK's retry options do not bound a whole call, are not the `HttpClient` timeout, and whether a failed `CreateMessage` POST can be re-executed by the retry pipeline (the SDK stamps one `Idempotency-Key` per call) decides whether a duplicate SMS can be sent; `ListMessage` has no SDK pager, so enumeration mechanics are yours. **MUST load `dotnet-configuration-resilience`** before tuning retries/timeouts and before writing the reconciliation loop.

> ⚠ Tests — the `HttpClient` constructor argument is the test seam; stub there, not by wrapping generated controllers. **MUST load `dotnet-testing`** before writing integration tests.

## 4. REQUIRED READING

Load **before implementation starts**; this sheet deliberately does not carry their contents.

- `dotnet-client-initialization` — Step 1 (client construction & DI lifetime).
- `dotnet-authentication` — Step 1 (credentials wiring).
- `dotnet-calling-endpoints` — Steps 2–8 (explicit-null params, named arguments).
- `dotnet-models` — Steps 2–8 (StringEnum construction, record immutability, dropped fields).
- `dotnet-error-handling` — Step 9 (the integration boundary). Two hazards, verbatim:
  - a drifted or malformed **2xx** body (a missing `required` member) surfaces as a
    `JsonException` from deserialization, **not** as an `SdkException` — so an
    SDK-exception-only catch ladder lets it escape the integration boundary;
  - a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape
    throws `JsonException` *while the error object is being constructed*, so the `JsonException`
    **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that
    maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage,
    and a caller that retries 5xx retries something that can never succeed.

  **MUST load `dotnet-error-handling`** before writing that boundary.
- `dotnet-configuration-resilience` — Steps 3, 4, 7 (retry/timeout semantics, pagination).
- `dotnet-testing` — test seam for the integration layer.

## 5. Assumptions & Blockers

- **Lookup v2 chosen over v1** for validation: v1's response model (`LookupsV1PhoneNumber`, records-4-Li-Me.md) has no `Valid`/`ValidationErrors` fields; v2's `LookupResponse` does. Assumed the brief's "valid flag + canonical form" requirement settles on v2.
- **Redact, not delete**, for content disposal — matches the brief's lean and is grounded: `UpdateMessage` doc names body redaction; `DeleteMessage` removes the record (returns void).
- **UNVERIFIED (live-traffic only), each with a defensive directive in §2:** the provider's scheduling window (min lead / max horizon); the exact provider-side set of cancelable statuses and the error code when canceling an already-sent message; the provider error code for an invalid `To` on create; whether `From` and `MessagingServiceSid` are mutually exclusive on create; whether `DateSent` comparisons honor time-of-day; whether `next_page_uri` is absolute or relative. None of these are in the map or the SDK source; the sheet's directives make each one safe without knowing.
- **Drift report:** the SDK repo's `main` has moved past the map's pinned commit to a "v4 beta codegen" regen whose root namespace is `Twilio` (not `TwilioSdk`). NuGet publishes only `2.0.0`, which corresponds to the pinned commit this sheet is grounded against — so the sheet is correct for the package you will install. If a newer package ever publishes, re-ground before upgrading.
- No blockers.
