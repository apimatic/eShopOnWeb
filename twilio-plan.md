# Twilio SMS notifications — contract sheet (eShopOnWeb)

NuGet: `AsadAli.TwilioSdk` (version-less). Root namespace: `TwilioSdk` (not `Twilio`). Client: `TwilioSdk.TwilioSdkClient`. Provenance: `sdk-map.md` stamp `51fdf48`.

---

## Scope & sequence

| Step | What | Operations |
|---|---|---|
| 1 | Construct `TwilioSdkClient` from AccountSid + AuthToken; apply optional `Twilio:BaseUrl` **only** to the messaging host | client options / `Server.Default` (not Lookup) |
| 2 | Register shopper contact number: validate + persist provider canonical E.164 | `LookupsV2PhoneNumber.FetchPhoneNumber3` |
| 3 | Immediate SMS on place-order, dispatch, cancel, resend | `Api20100401Message.CreateMessage` (`from` + `body` + `to`) |
| 4 | Provider-queued follow-up SMS on dispatch (few days later) | `Api20100401Message.CreateMessage` (`scheduleType` + `sendAt` + `messagingServiceSid`) |
| 5 | Cancel a not-yet-sent follow-up on order cancel | `Api20100401Message.UpdateMessage` (`status: Canceled`) |
| 6 | Read delivery outcome by provider SID (no webhooks) | `Api20100401Message.FetchMessage` |
| 7 | Dispose message body at the provider; keep SID + outcome | `Api20100401Message.UpdateMessage` (`body`) — **not** `DeleteMessage` |
| 8 | Reconciliation list scoped to this app’s sending number | `Api20100401Message.ListMessage` (`from` + `DateSent>` + `DateSent<`) |
| 9 | Resend idempotency (application-level; see CONTRACT) | same `CreateMessage` as step 3 |
| 10 | Error boundary + PII-safe logging | all of the above |

`DeleteMessage` is **out of scope** for content disposal: it deletes the Message resource (`Api/Api20100401Message.cs`). Use `UpdateMessage` so SID/status/error fields survive.

`LookupsV1PhoneNumberApi.FetchPhoneNumber2` is **not** the registration operation: `LookupsV1PhoneNumber.Carrier` is `object?` and the model has no `Valid` / `ValidationErrors` (`records-4-Li-Me.md`).

---

## CONTRACT SHEET

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

No-throw `…Result` variants: **absent** on every operation below (`sdk-map.md`).

---

### Client construction, auth, servers

| Fact | Value | Source |
|---|---|---|
| Constructor | `TwilioSdk.TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md`, `TwilioSdkClient.cs` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` — registers `TwilioSdkClient` as singleton via `IHttpClientFactory.CreateClient()` | `ServiceCollectionExtensions.cs` |
| Auth property | `TwilioSdkClientOptions.AccountSidAuthToken` : `TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?` | `sdk-map.md` Servers & auth, `TwilioSdkClientOptions.cs` |
| Credentials members | `Username` (required `string`), `Password` (required `string`) — AccountSid → Username, AuthToken → Password. Applied as HTTP Basic (`Authorization: Basic …`) | `BasicAuthCredentials.cs`, `BasicAuthScheme.cs` |
| Environment | `TwilioSdkClientOptions.Environment` : `TwilioSdk.Servers.ServerEnvironment` — only member `Production` (wire `"production"`). `Default()` → `Production` | `Servers/ServerEnvironment.cs` |
| Other options | `Retry` : `TwilioSdk.Core.Configuration.RetryOptions` · `Logging` : `TwilioSdk.Core.Configuration.LoggingOptions` · `Server` : `TwilioSdk.ServerOptions` | `sdk-map.md`, `TwilioSdkClientOptions.cs` |

**Messaging BaseUrl override (`Twilio:BaseUrl` used verbatim, messaging host only):**

| | |
|---|---|
| Messaging operations (`CreateMessage` / `FetchMessage` / `ListMessage` / `UpdateMessage` / `DeleteMessage`) resolve through `_server.Default(...)` | `Api/Api20100401Message.cs` |
| Lookup resolves through `_server.Default4(...)` | `Api/LookupsV2PhoneNumber.cs` |
| Override | `options.Server.Default.Production.BaseUrl = <Twilio:BaseUrl verbatim>` · type `TwilioSdk.Servers.DefaultOptions.ProductionOptions.BaseUrl` : `string` | `ServerOptions.cs`, `Servers/DefaultOptions.cs` |
| Messaging default if unset | `"https://api.twilio.com"` | `Servers/DefaultOptions.cs` |
| Lookup default (do **not** overwrite when applying `Twilio:BaseUrl`) | `options.Server.Default4.Production.BaseUrl` default `"https://lookups.twilio.com"` | `Servers/Default4Options.cs` |
| How it is applied | `new UrlTemplate(Production.BaseUrl, path, [])` — BaseUrl is the verbatim origin | `Servers/DefaultOptions.cs` |

Do not set `Server.Default1`…`Default3` / `Default5`…`Default14` for this integration.

`TwilioSdk.Core.RequestOptions` (per-call) has **only** `LogLevel` : `Microsoft.Extensions.Logging.LogLevel?` — no custom headers, no BaseUrl, no idempotency key (`Core/RequestOptions.cs`).

Path `accountSid` on every Message operation is the Account SID string (same value as Basic Username when using AccountSid + AuthToken).

---

### 1. `LookupsV2PhoneNumber.FetchPhoneNumber3` — contact-number validation

| | |
|---|---|
| Controller | `client.LookupsV1PhoneNumberApi` is **not** used. Use `client.LookupsV2PhoneNumber` |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` (Default4 / lookups) |
| Signature | `FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 15 params `fields` … `partnerSubId` — nullable, no default → pass `null` to skip |
| Path | `phoneNumber` → `{PhoneNumber}` — E.164 or national; default country +1 if national (`Api/LookupsV2PhoneNumber.cs`) |
| Query (wire ← C#) | `Fields` ← `fields`, `CountryCode` ← `countryCode`, plus identity-match/reassigned/pre_fill params (all `null` here) |
| `fields` | comma-separated **string** (not `Field` enum). XML possible values: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill` |
| Registration call | `fields: "line_type_intelligence,line_status"` · `countryCode: <ISO alpha-2 or null>` · remaining optionals `null` |
| Returns | `TwilioSdk.Models.LookupResponse` (**no wrapper field**) |
| Error | Case B `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Accessors | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Pagination | none |
| Map | `operations/LookupsV2PhoneNumber.md`, `records-4-Li-Me.md` |

**`LookupResponse` fields this step reads** (`Models/LookupResponse.cs`, `records-4-Li-Me.md`):

| C# (wire) | Type | Use |
|---|---|---|
| `PhoneNumber (phone_number)` | `string?` | **Canonical E.164** (`+` + country code + subscriber). **This is what gets stored.** |
| `Valid (valid)` | `bool?` | Documented as: number is in a valid range a carrier can assign. Reject registration when not `true`. |
| `ValidationErrors (validation_errors)` | `IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?` | Reject when non-empty |
| `NationalFormat (national_format)` | `string?` | Do not store as canonical |
| `CallingCountryCode (calling_country_code)` | `string?` | E.164 prefix |
| `CountryCode (country_code)` | `string?` | ISO 3166-1 alpha-2 |
| `LineTypeIntelligence (line_type_intelligence)` | `TwilioSdk.Models.LineTypeIntelligenceInfo?` | Populated when requested via `fields` |
| `LineStatus (line_status)` | `TwilioSdk.Models.LineStatusInfo?` | Populated when requested via `fields` |

**`LineTypeIntelligenceInfo`** (`records-3-Fl-Li.md`): `MobileCountryCode (mobile_country_code): string?`, `MobileNetworkCode (mobile_network_code): string?`, `CarrierName (carrier_name): string?`, `Type (type): string?`, `ErrorCode (error_code): int?`. **`Type` has no SDK-documented value list** (plain `string?`, no XML). **UNVERIFIED** which `Type` strings are SMS-capable. Defensive: reject when `Valid != true` or `ValidationErrors` is non-empty or `PhoneNumber` is null; persist `Type` / `LineStatus.Status`; if `LineTypeIntelligence.ErrorCode` is non-null, treat as lookup-package failure and reject rather than inventing Type literals.

**`LineStatusInfo`**: `Status (status): string?`, `ErrorCode (error_code): int?` — `Status` likewise undocumented as an enum.

**`ValidationError`** (`map/models/enums.md`, `Models/Enums/ValidationError.cs`) — `TwilioSdk.Models.Enums.ValidationError` (`StringEnum`):

| Member | Wire |
|---|---|
| `TooShort` | `TOO_SHORT` |
| `TooLong` | `TOO_LONG` |
| `InvalidButPossible` | `INVALID_BUT_POSSIBLE` |
| `InvalidCountryCode` | `INVALID_COUNTRY_CODE` |
| `InvalidLength` | `INVALID_LENGTH` |
| `NotANumber` | `NOT_A_NUMBER` |

Invalid numbers that never produce a 2xx body throw Case B (`RawError`), not a typed lookup error.

`TwilioSdk.Models.Enums.Field` exists (`LineTypeIntelligence` wire `line_type_intelligence`, `LineStatus` wire `line_status`) but **`FetchPhoneNumber3` does not take `Field`** — it takes `string? fields`.

---

### 2–4. `Api20100401Message.CreateMessage` — send now + schedule later

| | |
|---|---|
| Controller | `client.Api20100401Message` |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` (Default / api) |
| Encoding | **form-urlencoded body** (not query), despite the map’s “Query params” label — `FormUrlEncodedRequest` in `Api/Api20100401Message.cs` |
| Signature | `CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention, TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType, bool? shortenUrls, TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Required | `accountSid`, `to` (non-nullable `string`) |
| Must-pass-explicitly | 24 params `statusCallback` … `contentSid` — pass `null` to skip |
| Wire ← C# | `To` ← `to`, `StatusCallback` ← `statusCallback`, `ApplicationSid` ← `applicationSid`, `MaxPrice` ← `maxPrice`, `ProvideFeedback` ← `provideFeedback`, `Attempt` ← `attempt`, `ValidityPeriod` ← `validityPeriod`, `ForceDelivery` ← `forceDelivery`, `ContentRetention` ← `contentRetention`, `AddressRetention` ← `addressRetention`, `SmartEncoded` ← `smartEncoded`, `PersistentAction` ← `persistentAction`, `TrafficType` ← `trafficType`, `ShortenUrls` ← `shortenUrls`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, `SendAsMms` ← `sendAsMms`, `ContentVariables` ← `contentVariables`, `RiskCheck` ← `riskCheck`, `From` ← `from`, `FallbackFrom` ← `fallbackFrom`, `MessagingServiceSid` ← `messagingServiceSid`, `Body` ← `body`, `MediaUrl` ← `mediaUrl`, `ContentSid` ← `contentSid` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` (**no wrapper**) |
| Error | Case B `SdkException<RawError>` |
| Accessors | `StatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Pagination | none |
| Map | `operations/Api20100401Message.md` |

**Immediate SMS** (place-order, dispatch instant notice, cancel notice, resend):

| Param | Value |
|---|---|
| `accountSid` | configured Account SID |
| `to` | shopper canonical E.164 from step 2 |
| `from` | `Twilio:FromNumber` |
| `body` | application-composed text |
| `messagingServiceSid` | `null` |
| `scheduleType` / `sendAt` | `null` |
| `statusCallback` | `null` (no public webhook) |
| all other optionals | `null` |

**Scheduled follow-up** (dispatch only — provider queue, not an app timer):

| Param | Value |
|---|---|
| `to` / `body` / `accountSid` | same pattern as immediate |
| `from` | `null` (Messaging Service selects the sender) **or** `Twilio:FromNumber` if that number is in the service — both are optional on the signature |
| `messagingServiceSid` | `Twilio:MessagingServiceSid` (**required for scheduling** per `MessageEnumScheduleType` XML: “For Messaging Services only”) |
| `scheduleType` | `TwilioSdk.Models.Enums.MessageEnumScheduleType.Fixed` (wire `fixed`) |
| `sendAt` | `DateTimeOffset` a few days ahead |
| all other optionals | `null` |

`MessageEnumScheduleType` has **only** `Fixed (fixed)`. XML refers to `send_time`; the generated wire name is `SendAt` / C# `sendAt` (`Models/Enums/MessageEnumScheduleType.cs`, `operations/Api20100401Message.md`).

**Minimum/maximum schedule offset is not in the map or in `CreateMessage` XML. UNVERIFIED.** Defensive: do not invent an offset in code as an SDK contract; if the provider rejects `sendAt`, that is a **create-time** Case B error — read `ex.Error.StatusCode` + `ReadAsString()`.

**Create-time vs later delivery (undeliverable US destinations):** a successful `CreateMessage` returns `ApiV2010AccountMessage` (typically `Status` `queued` / `accepted` / `scheduled`). Carrier refusal later is **not** a throw on create. Persist `Sid` + `Status`. Later `FetchMessage` yields `failed` / `undelivered` plus `ErrorCode` / `ErrorMessage`. Treat those as delivery outcome.

---

### 5 + 7. `Api20100401Message.UpdateMessage` — cancel scheduled / redact body

| | |
|---|---|
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` |
| Notes | “Update a Message resource (used to redact Message `body` text and to cancel not-yet-sent messages)” |
| Signature | `UpdateMessage(string accountSid, string sid, string? body, TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `body`, `status` — pass `null` to skip that field |
| Wire ← C# | `Body` ← `body`, `Status` ← `status` (form-urlencoded) |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` |
| Error | Case B `SdkException<RawError>` |
| Map | `operations/Api20100401Message.md` |

**Cancel not-yet-sent follow-up:** `sid` = persisted scheduled SID, `status: MessageEnumUpdateStatus.Canceled` (wire `canceled`), `body: null`. Success: returned `Status` is `MessageEnumStatus.Canceled`. Already sent / already canceled: **no typed error shape** — Case B only. **UNVERIFIED** which HTTP statuses the provider uses. Defensive: catch `SdkException<RawError>`, read `StatusCode` + `ReadAsString()`; then `FetchMessage` and persist whatever `Status` / `ErrorCode` the resource currently has. If `FetchMessage` shows `sent`/`delivered`/`undelivered`/`failed`, the follow-up already left the provider and cannot be recalled.

**Redact body at provider:** `status: null`, `body: ""` (empty string — `null` skips the field per must-pass-explicitly). Operation remarks document redaction of `body`; param XML does not spell out `""`. **UNVERIFIED** live treatment of empty `Body`. Defensive: persist returned `Sid`/`Status`/`ErrorCode`/`ErrorMessage`; confirm `Body` is empty/null on the response (or a subsequent `FetchMessage`). Do **not** call `DeleteMessage`.

`MessageEnumUpdateStatus`: only `Canceled (canceled)`.

---

### 6. `Api20100401Message.FetchMessage` — GET by SID

| | |
|---|---|
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` |
| Signature | `FetchMessage(string accountSid, string sid, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Returns | `TwilioSdk.Models.ApiV2010AccountMessage` |
| Error | Case B `SdkException<RawError>` |
| Map | `operations/Api20100401Message.md` |

Unknown SID → Case B (read `StatusCode`; typically a 4xx body via `ReadAsString()`).

---

### 8. `Api20100401Message.ListMessage` — reconciliation

| | |
|---|---|
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` |
| Signature | `ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `to` … `pageToken` (8 params) |
| Query wire ← C# | `To` ← `to`, `From` ← `from`, `DateSent` ← `dateSent`, `DateSent<` ← `dateSentQuery`, `DateSent>` ← `dateSentQueryQuery`, `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken` |
| Date encoding | each `DateTimeOffset?` is written with `ToIso8601()` → `yyyy-MM-ddTHH:mm:ss.fffZ` (UTC) (`Core/Extensions/DateTimeOffsetExtensions.cs`) — **not** the `YYYY-MM-DD` / `<=` prefix described in XML |
| Returns | `TwilioSdk.Models.ListMessageResponse` |
| Error | Case B `SdkException<RawError>` |
| Pagination (map) | none (only `page`, no `perPage`) — **no auto-pager** |
| XML `pageSize` | default 50, maximum 1000 |
| Map | `operations/Api20100401Message.md`, `records-4-Li-Me.md` |

**Reconciliation call** (`GET /api/notifications/reconciliation?from={from}&to={to}` — those are **app** ISO-8601 range bounds, not SMS To):

| Param | Value |
|---|---|
| `from` | `Twilio:FromNumber` — provider-side **From** filter (this app’s sending number; do not list the whole account then filter) |
| `to` | `null` (do not filter by recipient) |
| `dateSent` | `null` (not an exact-day match) |
| `dateSentQueryQuery` | range start → wire `DateSent>` |
| `dateSentQuery` | range end → wire `DateSent<` |
| `pageSize` | e.g. `1000L` (max) |
| `page` / `pageToken` | first page `null`; subsequent pages from the previous response |

**`ListMessageResponse`** (`Models/ListMessageResponse.cs`): `End (end): int?`, `FirstPageUri (first_page_uri): string?`, `NextPageUri (next_page_uri): string?`, `Page (page): int?`, `PageSize (page_size): int?`, `PreviousPageUri (previous_page_uri): string?`, `Start (start): int?`, `Uri (uri): string?`, `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`. Walk until `NextPageUri` is null / `Messages` empty. The SDK does not follow `NextPageUri` for you.

**UNVERIFIED:** whether a **not-yet-sent** scheduled message (From assigned only after send; see `NumSegments` XML: initially `0` for Messaging Service) appears under `From=Twilio:FromNumber`. Defensive: reconciliation is a DateSent-bounded From list of **provider resources**; count items in `Messages` as returned.

---

### `ApiV2010AccountMessage` — persist / report fields

Direct return of create/fetch/update; list items are the same record (`records-1-Ac-Ca.md`, `Models/ApiV2010AccountMessage.cs`). **No envelope wrapper.**

| C# (wire) | Type | Persist? |
|---|---|---|
| `Sid (sid)` | `string?` (pattern `^(SM\|MM)[0-9a-fA-F]{32}$`) | **provider message id** |
| `Status (status)` | `MessageEnumStatus?` | **delivery/schedule outcome** |
| `ErrorCode (error_code)` | `int?` | when `failed` / `undelivered`; XML: do not treat as a stable programmatic enum |
| `ErrorMessage (error_message)` | `string?` | same |
| `Body (body)` | `string?` | text; empty after redact |
| `From (from)` | `string?` | E.164 sender |
| `To (to)` | `string?` | E.164 recipient |
| `DateSent (date_sent)` | `string?` | RFC 2822 GMT |
| `DateCreated (date_created)` | `string?` | RFC 2822 GMT |
| `DateUpdated (date_updated)` | `string?` | RFC 2822 GMT |
| `Direction (direction)` | `MessageEnumDirection?` | outbound API sends → `outbound-api` |
| `MessagingServiceSid (messaging_service_sid)` | `string?` | scheduled sends |
| `AccountSid (account_sid)` | `string?` | |
| `NumSegments (num_segments)` | `string?` | Messaging Service: initially `"0"` until sender assigned |
| `NumMedia (num_media)` | `string?` | |
| `Price (price)` | `string?` | |
| `PriceUnit (price_unit)` | `string?` | |
| `ApiVersion (api_version)` | `string?` | |
| `Uri (uri)` | `string?` | |
| `SubresourceUris (subresource_uris)` | `object?` | |

---

### Enums in scope (`TwilioSdk.Models.Enums`, `map/models/enums.md`)

All are `StringEnum<T>` (not C# enums). Use static members or `FromValue("wire")`.

**`MessageEnumStatus`**

| Member | Wire |
|---|---|
| `Queued` | `queued` |
| `Sending` | `sending` |
| `Sent` | `sent` |
| `Failed` | `failed` |
| `Delivered` | `delivered` |
| `Undelivered` | `undelivered` |
| `Receiving` | `receiving` |
| `Received` | `received` |
| `Accepted` | `accepted` |
| `Scheduled` | `scheduled` |
| `Read` | `read` |
| `PartiallyDelivered` | `partially_delivered` |
| `Canceled` | `canceled` |

Create-time success statuses commonly `queued` / `accepted` / `scheduled`. Later carrier refusal → `failed` / `undelivered` on fetch. Cancelled schedule → `canceled`.

**`MessageEnumDirection`:** `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)`.

**`MessageEnumScheduleType`:** `Fixed (fixed)` only.

**`MessageEnumUpdateStatus`:** `Canceled (canceled)` only.

**Optional CreateMessage enums (pass `null` unless needed):**

| Type | Members (wire) |
|---|---|
| `MessageEnumContentRetention` | `Retain (retain)`, `Discard (discard)` |
| `MessageEnumAddressRetention` | `Retain (retain)`, `Obfuscate (obfuscate)` |
| `MessageEnumTrafficType` | `Free (free)` |
| `MessageEnumRiskCheck` | `Enable (enable)`, `Disable (disable)` |

---

### 9. Idempotent resend

`CreateMessage` / `UpdateMessage` / `DeleteMessage` **always** attach header `Idempotency-Key` with `Guid.NewGuid()` inside the generated method (`Api/Api20100401Message.cs`). That value is **not** a method parameter. `RequestOptions` cannot set headers.

There is **no** `uniqueName` (or any other caller-supplied idempotency) on `CreateMessage`.

**The app cannot pass a stable Twilio idempotency key.** Resend idempotency is **application-level** (store the first `Sid`; do not call `CreateMessage` again for the same app key). A second `CreateMessage` always sends a new `Idempotency-Key` and creates a new Message.

HTTP-layer retries of the **same** `Execute` reuse the Guid already built for that invocation; that is not app-level resend protection.

---

### 10. Errors (every in-scope operation is Case B)

Catch `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>`.

| Read | How |
|---|---|
| HTTP status | `ex.Error.StatusCode` (`HttpStatusCode`) |
| Body string | `ex.Error.ReadAsString()` |
| Body bytes | `ex.Error.ReadAsBytes()` |
| JSON | `ex.Error.ReadAsJson<T>()` — `T` is **not** a generated `{Op}Error`; there is none |

There are **no** `TryGet…` accessors (Case B, not Case A). `RawError` is not an `ApiError`.

`SdkException<TError>` has only `Error` (`Core/Exceptions/SdkException.cs`).

**Create-time failures** (auth, invalid To, missing From/MessagingServiceSid, bad `sendAt`, unknown SID on update/fetch): thrown `SdkException<RawError>`.

**Later delivery failures:** not thrown on create; `FetchMessage` / list item `Status` + `ErrorCode` / `ErrorMessage`.

Also reaches the boundary (not `SdkException`): `HttpRequestException` (transport), `TaskCanceledException` / `OperationCanceledException` (`ct` or timeout), `System.Text.Json.JsonException` (see trap notes).

---

### 11. Logging / PII (`TwilioSdk.Core.Configuration.LoggingOptions`)

| Member | Role |
|---|---|
| `LoggerFactory` | `ILoggerFactory?` — if set (including via `AddTwilioSdkClient` + DI `ILoggerFactory`), Information HTTP lines emit |
| `LogRequestHeaders` / `LogResponseHeaders` | `bool` |
| `LogRequestBody` | `bool` — form body includes `To`, `From`, `Body` |
| `RedactedKeys` | default `sig`, `signature`, `access_token`, `apikey`, `api_key`, `client_secret`, `password`, `refresh_token`, `code`, `assertion`, `client_assertion` — **does not include** `To`/`From`/`Body`/`AuthToken` |
| `RedactedHeaders` / `UnmaskHeaders` | default empty |
| `LoggableContentTypes` | default `application/json`, `application/x-www-form-urlencoded` |
| Env hook | `TWILIOSDKCLIENT_LOG` = `info` \| `debug` \| `trace` (`LoggingEnvironment.cs`) — `debug`/`trace` turn on header logging; `trace` also body |

Source behavior (`Core/Logging/HttpLogger.cs`): Information logs `HTTP {Method} {Url}` with **unredacted `AbsolutePath`**. Lookup path is `/v2/PhoneNumbers/{PhoneNumber}` → **destination number is in the URL**. Query string keys not in a small allow-list are masked. `Authorization` is not in the known-safe header list, so header logging redacts it unless `UnmaskHeaders` includes it. Form-body logging redacts only `RedactedKeys`.

`RequestOptions.LogLevel` can raise per-call verbosity into header/body logs.

Avoid: `LogRequestBody = true`; `UnmaskHeaders` containing `Authorization`; `TWILIOSDKCLIENT_LOG=debug|trace`; per-call `LogLevel` that enables body/headers. Information-level URL logging still contains Lookup path PII when `LoggerFactory` is set.

---

## Trap notes

⚠ Step 1 (client / DI) — `HttpClient` is a constructor argument and `AddTwilioSdkClient` owns factory vs wrapper lifetime; constructing a new `HttpClient` per request vs reusing the pipeline is not visible from the signature. **MUST load `dotnet-client-initialization`** before writing `new TwilioSdkClient` or `AddTwilioSdkClient`.

⚠ Step 1 (auth) — `AccountSidAuthToken` is nullable and silently becomes no-auth if left null (`BasicAuthScheme.Create`); credentials belong on options before first call, from configuration not literals. **MUST load `dotnet-authentication`**.

⚠ Step 1 (BaseUrl / retry / timeout) — `RetryOptions` / `Timeout` on the SDK options object are **not** the timeout of the `HttpClient` you register and do **not** bound a whole logical operation (send + fetch + list). Transport-failure retry can still apply to verbs that create messages. **MUST load `dotnet-configuration-resilience`** before wiring `Retry`, `Timeout`, or `Server.Default.Production.BaseUrl`.

⚠ Steps 2–8 (calls) — `CreateMessage` has 24 nullable no-default parameters; `ListMessage` has 8; `FetchPhoneNumber3` has 15. Positional calls mis-bind; skipped fields must still be passed as `null`; the token is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first `client.Api20100401Message` / `client.LookupsV2PhoneNumber` call.

⚠ Steps 2–8 (models) — statuses/direction/schedule/validation errors are `StringEnum<T>`, not C# enums; `LookupResponse` extra packages are null unless `fields` requested; unmodeled JSON is dropped. **MUST load `dotnet-models`** before constructing/reading any of these records.

⚠ Step 8 (list walk) — map pagination is “none”; `NextPageUri` / `pageToken` / `pageSize` still have to cover the whole DateSent range without dropping pages or double-counting. **MUST load `dotnet-configuration-resilience`** before writing reconciliation.

⚠ Step 10 (error boundary) — every in-scope op is Case B (`SdkException<RawError>`); a catch of a typed `{Op}Error` will not compile and an `SdkException`-only ladder is incomplete. **MUST load `dotnet-error-handling`** before any `try/catch`.

⚠ Step 10 — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 — a **non-2xx** body that does not match its operation’s generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 11 — `LoggingOptions`, `TWILIOSDKCLIENT_LOG`, and `RequestOptions.LogLevel` are the SDK logging hooks; enabling the wrong combination emits destination numbers (Lookup path, form `To`/`From`/`Body`) or auth material. **MUST load `dotnet-configuration-resilience`** before attaching a logger to the client.

⚠ Tests — the `HttpClient` constructor argument is the test seam; do not fake generated controllers. **MUST load `dotnet-testing`** before stubbing.

---

## REQUIRED READING

Load **before implementation starts**. This sheet does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — `TwilioSdkClient` / `AddTwilioSdkClient` / `HttpClient` lifetime |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-calling-endpoints` | Steps 2–8 — named arguments, must-pass `null`s, `ct:` |
| `dotnet-models` | Steps 2–8 — `StringEnum<T>`, records, nullability |
| `dotnet-error-handling` | Step 10 — Case B `RawError`, catch types, **both** `JsonException` directions |
| `dotnet-configuration-resilience` | Step 1 BaseUrl/retry/timeout; step 8 pagination; step 11 logging |
| `dotnet-testing` | Test doubles against `HttpClient` |

---

## Assumptions & Blockers

**Assumptions**

- Path `accountSid` equals the configured Account SID used as `BasicAuthCredentials.Username`.
- Immediate sends use `from: Twilio:FromNumber` and `messagingServiceSid: null`. Scheduled sends set `messagingServiceSid: Twilio:MessagingServiceSid` and `scheduleType: Fixed` as required by `MessageEnumScheduleType`.
- Registration uses **only** `FetchPhoneNumber3` (Lookup v2). Lookup v1 is skipped because `Carrier` is untyped and there is no `Valid` flag.
- `DeleteMessage` is not used for `/api/notifications/{id}/content`.
- App reconciliation `from`/`to` query params map to `dateSentQueryQuery` / `dateSentQuery` (`DateSent>` / `DateSent<`), and `ListMessage.from` is `Twilio:FromNumber`.

**Blockers**

- None of the required capabilities are missing as operations: Lookup v2, create (immediate + scheduled), update (cancel + redact), fetch-by-SID, and list-with-From + DateSent bounds all exist on the map.
- Schedule min/max offset: **not documented** in map/XML — **UNVERIFIED** (handled above as create-time Case B).
- `LineTypeIntelligenceInfo.Type` allowed values: **not documented** — **UNVERIFIED** (reject on `Valid` / `ValidationErrors` / missing canonical `PhoneNumber`).
- Empty-string `Body` as the redaction payload: operation remarks name redaction; param XML does not — **UNVERIFIED** (pass `""`, confirm on response).
- Already-sent / already-canceled HTTP codes on `UpdateMessage`: Case B only — **UNVERIFIED** specific statuses.
- Caller-controlled idempotency: **not exposed**; not a missing send operation — app must implement resend keys itself.
