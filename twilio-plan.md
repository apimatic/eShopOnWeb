# Twilio .NET SDK — SMS order notifications (eShopOnWeb)

Package: `AsadAli.TwilioSdk` (install version-less). Root namespace: `TwilioSdk`. Client: `TwilioSdkClient`. Map stamp: `51fdf48`.

## Scope & sequence

1. **Client + DI + auth + BaseUrl split** — one `TwilioSdkClient`; messaging host override on `Server.Default` only; Lookup keeps `Server.Default4`. Ops: none.
2. **Validate / canonicalize contact number** (`POST /api/contact-numbers`) — `LookupsV2PhoneNumber.FetchPhoneNumber3`. Store `LookupResponse.PhoneNumber` (E.164). Reject when not a usable destination (see op row).
3. **Send SMS immediately** (order placed / dispatched / cancelled / operator resend) — `Api20100401Message.CreateMessage` with `from` = `Twilio:FromNumber`. Persist `Sid` + `Status`. Catch provider/transport failures; do **not** fail the order.
4. **Queue follow-up SMS at Twilio** (after dispatch, a few days later) — same `CreateMessage` with `scheduleType` + `sendAt` + `messagingServiceSid`. Persist scheduled `Sid`.
5. **Cancel scheduled follow-up** (order cancel) — `Api20100401Message.UpdateMessage` with `status: MessageEnumUpdateStatus.Canceled`.
6. **Read delivery outcome** (no webhooks) — `Api20100401Message.FetchMessage` by SID; read `Status`, `ErrorCode`, `ErrorMessage`.
7. **Operator resend + caller idempotency key** — `CreateMessage` again. **GAP:** the SDK does not accept a caller-supplied idempotency key (see CONTRACT SHEET + Assumptions).
8. **Redact message body at the provider** (`DELETE /api/notifications/{id}/content`) — `UpdateMessage` with `body: ""` (empty string, not null). Do **not** call `DeleteMessage` (that removes the resource).
9. **Reconciliation list** (`GET /api/notifications/reconciliation?from=&to=`) — `Api20100401Message.ListMessage` with `from` = `Twilio:FromNumber` and `DateSent>` / `DateSent<` at the API. Page via `pageToken` until `NextPageUri` is null.
10. **Error boundary** around every SDK call — Case B `SdkException<RawError>` plus the two `JsonException` directions. Sending failures are recorded, not thrown through order flows.

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

### Client construction / auth / servers

| Fact | Value | Source |
|---|---|---|
| Client | `TwilioSdk.TwilioSdkClient` | `sdk-map.md`, `TwilioSdkClient.cs` |
| Ctor | `TwilioSdkClient(System.Net.Http.HttpClient httpClient, TwilioSdk.TwilioSdkClientOptions options)` | `sdk-map.md` |
| DI | `TwilioSdk.ServiceCollectionExtensions.AddTwilioSdkClient(Action<TwilioSdkClientOptions>? configure = null)` — registers `IHttpClientFactory` + singleton `TwilioSdkClient` | `ServiceCollectionExtensions.cs` |
| Options | `TwilioSdk.TwilioSdkClientOptions`: `Environment` (`TwilioSdk.Servers.ServerEnvironment`), `Retry` (`TwilioSdk.Core.Configuration.RetryOptions`), `Logging` (`TwilioSdk.Core.Configuration.LoggingOptions`), `Server` (`TwilioSdk.ServerOptions`), `AccountSidAuthToken` (`TwilioSdk.Core.Authentication.Basic.BasicAuthCredentials?`) | `sdk-map.md`, `TwilioSdkClientOptions.cs` |
| Auth credentials | `new BasicAuthCredentials { Username = <Twilio:AccountSid>, Password = <Twilio:AuthToken> }` — both members `required string`. Applied as HTTP Basic. Map XML: account SID + auth token are accepted (docs also mention API key as username / secret as password). | `sdk-map.md` *Servers & auth*, `BasicAuthCredentials.cs` |
| Environment | `TwilioSdk.Servers.ServerEnvironment.Production` (wire `"production"`). `Default()` → `Production`. Only member. | `sdk-map.md`, `Servers/ServerEnvironment.cs` |
| Messaging host (2010 Messages API) | `options.Server.Default` is `TwilioSdk.Servers.DefaultOptions`. `Default.Production.BaseUrl` default `"https://api.twilio.com"`. **When `Twilio:BaseUrl` is set, assign that string VERBATIM to `options.Server.Default.Production.BaseUrl`.** Every in-scope messaging call (`CreateMessage` / `FetchMessage` / `ListMessage` / `UpdateMessage` / `DeleteMessage`) uses `_server.Default(...)`. | `ServerOptions.cs`, `Servers/DefaultOptions.cs`, `Api/Api20100401Message.cs` |
| Lookup host | `options.Server.Default4` is `TwilioSdk.Servers.Default4Options`. `Default4.Production.BaseUrl` default `"https://lookups.twilio.com"`. **Do not assign `Twilio:BaseUrl` here.** `FetchPhoneNumber3` uses `_server.Default4(...)`. One client **can** split hosts: set `Server.Default` only. Two clients are not required. | `Servers/Default4Options.cs`, `Api/LookupsV2PhoneNumber.cs` |
| How BaseUrl is applied | `UrlTemplate(BaseUrl, path)` → `"{BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}`. `HttpRequestMessage` is constructed with that **absolute** URI. `Twilio:BaseUrl` is the SDK server-node override, not `HttpClient.BaseAddress`. | `Server.cs`, `Core/TemplateParamsFactory.cs`, `Core/RawClient.cs` |
| Other hosts on the same options object (do not touch for this integration) | `Default1` default `"https://messaging.twilio.com"` (Messaging Services REST, unused here), plus Default2–Default14. | `ServerOptions.cs`, `Servers/Default1Options.cs` |
| Retry options | `TwilioSdk.Core.Configuration.RetryOptions` — all members `required`; use `RetryOptions.Default()` or `Disabled()`. Members: `StatusCodesToRetry` (`IReadOnlyList<HttpStatusCode>`), `HttpMethodsToRetry` (`IReadOnlyList<HttpMethod>`), `MaxRetries` (`int`), `Delay` (`TimeSpan`), `Timeout` (`TimeSpan?`), `BackOffFactor` (`int`), `UseExponentialBackoff` (`bool`), `MaxJitter` (`TimeSpan`), `OnRetry` (`Action<RetryAttempt>?`). | `sdk-map.md`, `Core/Configuration/RetryOptions.cs` |
| Per-request options | `TwilioSdk.Core.RequestOptions` — **only** `LogLevel? LogLevel { get; init; }`. No header bag, no idempotency slot, no per-call base URL. | `Core/RequestOptions.cs` |
| Success HTTP | Empty allowlist → **any 2xx** is success. Non-2xx → `SdkException<TError>`. 401 detected separately for auth invalidation. | `Core/HttpStatusPolicy.cs` |
| Timeouts that surface as cancellation | `RetryOptions.Timeout` elapsed → `TaskCanceledException` wrapping `TimeoutException` with message `"The request was canceled due to the configured RetryOptions.Timeout elapsing."` | `Core/RawClient.cs` |

### Operation: FetchPhoneNumber3 (validate / canonicalize)

| | |
|---|---|
| Controller | `client.LookupsV2PhoneNumber` (`TwilioSdk.Api.LookupsV2PhoneNumber`) |
| Method | `Task<TwilioSdk.Models.LookupResponse> FetchPhoneNumber3(string phoneNumber, string? fields, string? countryCode, string? firstName, string? lastName, string? addressLine1, string? addressLine2, string? city, string? state, string? postalCode, string? addressCountryCode, string? nationalId, string? dateOfBirth, string? lastVerifiedDate, string? verificationSid, string? partnerSubId, TwilioSdk.Core.RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 15 nullable params `fields` … `partnerSubId` — pass `null` to skip |
| HTTP | `GET /v2/PhoneNumbers/{PhoneNumber}` on **Default4 (lookups)** |
| Query wire ← C# | `Fields` ← `fields`, `CountryCode` ← `countryCode`, plus identity-match / reassigned / prefill fields (unused unless those packages are requested) |
| `fields` | Comma-separated **string** (not `Field` enum). XML values: `validation`, `caller_name`, `sim_swap`, `call_forwarding`, `line_status`, `line_type_intelligence`, `identity_match`, `reassigned_number`, `sms_pumping_risk`, `phone_number_quality_score`, `pre_fill`. Enum `TwilioSdk.Models.Enums.Field` wire values: `caller_name`, `sim_swap`, `call_forwarding`, `line_type_intelligence`, `line_status`, `identity_match`, `reassigned_number`, `sms_pumping_risk` (no `validation` member). |
| Envelope | **None** — return is `LookupResponse` itself (`TwilioSdk.Models`) |
| Canonical number | `PhoneNumber (phone_number): string?` — E.164 (`+` + country code + subscriber). **This is what gets stored.** Also: `NationalFormat (national_format): string?`, `CountryCode (country_code): string?`, `CallingCountryCode (calling_country_code): string?` |
| Usable-destination detection | `Valid (valid): bool?` — XML: true when the number is in a valid range a carrier can freely assign. `ValidationErrors (validation_errors): IReadOnlyList<TwilioSdk.Models.Enums.ValidationError>?`. **Reject registration when `Valid == false`, when `ValidationErrors` is non-empty, or when the call throws `SdkException<RawError>` (e.g. 4xx).** Optional extra packages: `LineTypeIntelligence (line_type_intelligence): LineTypeIntelligenceInfo?` (`Type (type): string?` — **not** the `LineType` enum), `LineStatus (line_status): LineStatusInfo?` (`Status (status): string?`). Which `Type`/`Status` strings mean “cannot receive SMS” is **UNVERIFIED** (live); treat `Valid == false` as the map-grounded reject. |
| `phoneNumber` arg | E.164 or national format; default country +1. Pass `countryCode` (ISO 3166-1 alpha-2) when national. |
| Error | **Case B** `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` |
| Accessors | `StatusCode: HttpStatusCode` · `ReadAsBytes()` · `ReadAsString()` · `ReadAsJson<T>()` |
| Pagination | none |
| Map | `operations/LookupsV2PhoneNumber.md`, `records-4-Li-Me.md` (`LookupResponse`), `records-3-Fl-Li.md` (`LineTypeIntelligenceInfo`, `LineStatusInfo`), `enums.md` (`ValidationError`, `Field`) |

Lookup v1 `FetchPhoneNumber2` (`client.LookupsV1PhoneNumberApi`, returns `LookupsV1PhoneNumber`) also yields E.164 `PhoneNumber` but `Carrier` is untyped `object?` and there is **no** `Valid` flag. Do not use v1 for this capability.

### Operation: CreateMessage (immediate send, scheduled send, resend)

| | |
|---|---|
| Controller | `client.Api20100401Message` (`TwilioSdk.Api.Api20100401Message`) |
| Method | `Task<TwilioSdk.Models.ApiV2010AccountMessage> CreateMessage(string accountSid, string to, string? statusCallback, string? applicationSid, double? maxPrice, bool? provideFeedback, int? attempt, int? validityPeriod, bool? forceDelivery, TwilioSdk.Models.Enums.MessageEnumContentRetention? contentRetention, TwilioSdk.Models.Enums.MessageEnumAddressRetention? addressRetention, bool? smartEncoded, IReadOnlyList<string>? persistentAction, TwilioSdk.Models.Enums.MessageEnumTrafficType? trafficType, bool? shortenUrls, TwilioSdk.Models.Enums.MessageEnumScheduleType? scheduleType, DateTimeOffset? sendAt, bool? sendAsMms, string? contentVariables, TwilioSdk.Models.Enums.MessageEnumRiskCheck? riskCheck, string? from, string? fallbackFrom, string? messagingServiceSid, string? body, IReadOnlyList<string>? mediaUrl, string? contentSid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Required | `accountSid` (`Twilio:AccountSid`), `to` (canonical E.164). **24** params `statusCallback` … `contentSid` are nullable with **no C# default** — pass `null` to skip. |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages.json` on **Default (api)** — uses `Twilio:BaseUrl` when set |
| Body encoding | `application/x-www-form-urlencoded` (not JSON; not query). Null params are **omitted**. Map labels these “query params”; source sends them as form fields. Trust source: `Api/Api20100401Message.cs`. |
| Wire ← C# (form) | `To` ← `to`, `From` ← `from`, `Body` ← `body`, `MessagingServiceSid` ← `messagingServiceSid`, `ScheduleType` ← `scheduleType`, `SendAt` ← `sendAt`, plus the other listed names in the map row |
| Immediate send | `from`: `Twilio:FromNumber`. `body`: SMS text. `to`: stored canonical. `scheduleType`/`sendAt`: `null`. `messagingServiceSid`: pass `Twilio:MessagingServiceSid` or `null` (both config keys exist; neither is required by the signature). |
| Scheduled send | `scheduleType`: `MessageEnumScheduleType.Fixed` (only member; wire `"fixed"`). `sendAt`: `DateTimeOffset` for the follow-up instant. Enum XML: **Messaging Services only** — pass `messagingServiceSid` = `Twilio:MessagingServiceSid`. `from` may still be passed. Persist returned `Sid`. |
| `sendAt` wire format | Form-encoded via `JsonSerializer.Serialize` of `DateTimeOffset` (STJ default round-trip), **not** the ListMessage `ToIso8601()` helper. Timezone: `DateTimeOffset` (offset preserved in that serialization). **UNVERIFIED** vs provider’s documented ISO-8601 expectation — send UTC `DateTimeOffset`. |
| Schedule window | **Not in map or CreateMessage XML.** How far in the future (min/max) is **UNVERIFIED**. If the provider rejects the time, that is Case B `SdkException<RawError>` — read `StatusCode` + body; do not invent a window. |
| Response envelope | **None** — `ApiV2010AccountMessage` (`TwilioSdk.Models`) |
| Persist | `Sid (sid): string?` (pattern `^(SM|MM)[0-9a-fA-F]{32}$`) · `Status (status): MessageEnumStatus?` · also useful: `To`, `From`, `DateCreated (date_created): string?` (RFC 2822 GMT), `DateSent (date_sent): string?`, `ErrorCode (error_code): int?`, `ErrorMessage (error_message): string?` |
| Error | **Case B** `SdkException<RawError>` — 4xx (bad `to`/`from`, schedule rejected, auth) and 5xx. **No typed accessors.** |
| Accepted vs later failure | A **2xx** return means the provider accepted the message (`Status` often `queued` / `accepted` / `scheduled`). Later `failed` / `undelivered` is **not** an exception — discovered via `FetchMessage`. US destinations that are accepted then carrier-refused are expected. Catch `SdkException<RawError>` / transport exceptions on **CreateMessage** so order placement still succeeds; record the failure. |
| Idempotency | See dedicated row below. |
| Pagination | none |
| Map | `operations/Api20100401Message.md`, `records-1-Ac-Ca.md` (`ApiV2010AccountMessage`), `enums.md` (`MessageEnumScheduleType`, `MessageEnumStatus`, …) |

### Idempotency on CreateMessage — GAP

| | |
|---|---|
| Method parameter | **None.** `CreateMessage` has no idempotency / `IdempotencyKey` argument. |
| `RequestOptions` | Cannot carry headers. |
| What the SDK actually sends | `CreateMessage` (and `UpdateMessage` / `DeleteMessage`) always add header **`Idempotency-Key`** with value **`Guid.NewGuid()`** — a fresh GUID per invocation. The caller cannot supply or override it. |
| Consequence | Repeating `POST /api/notifications/{id}/resend` under the same caller key **will send a second message** if it calls `CreateMessage` again. Do **not** invent an SDK header pass-through. Application-level idempotency (persist caller key → existing `Sid`) is the only in-scope way to satisfy the resend contract. |
| Source | `Api/Api20100401Message.cs` (`new HeaderParam("Idempotency-Key", Guid.NewGuid())`), `Core/RequestOptions.cs` |

### Operation: UpdateMessage (cancel scheduled · redact body)

| | |
|---|---|
| Controller | `client.Api20100401Message` |
| Method | `Task<TwilioSdk.Models.ApiV2010AccountMessage> UpdateMessage(string accountSid, string sid, string? body, TwilioSdk.Models.Enums.MessageEnumUpdateStatus? status, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | `body`, `status` — pass `null` to omit that form field |
| HTTP | `POST /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` on **Default (api)** |
| Form wire | `Body` ← `body`, `Status` ← `status` |
| Notes (map + XML) | “used to redact Message `body` text and to cancel not-yet-sent messages” |
| Cancel follow-up | `sid` = persisted scheduled SID, `status: MessageEnumUpdateStatus.Canceled` (only member; wire `"canceled"`), `body: null`. Success: 2xx + `ApiV2010AccountMessage` with `Status` = `MessageEnumStatus.Canceled`. |
| Already sent | **UNVERIFIED** exact HTTP status. Expect Case B `SdkException<RawError>` (non-2xx). Read `StatusCode` + body best-effort; optionally `FetchMessage` first and skip cancel when `Status` is not `Scheduled` / `Queued` / `Accepted`. |
| Redact | `body: ""` (**empty string** — `null` is omitted and will not clear the body), `status: null`. Success: same envelope; `Sid` / `Status` / `DateCreated` / `DateUpdated` / `DateSent` / `ErrorCode` remain on the model. `Body` after redact is **UNVERIFIED** empty vs null — persist locally that content was redacted. |
| Do not use | `DeleteMessage` — `DELETE …/Messages/{Sid}.json`, returns `void`; XML “Deletes a Message resource from your account” (resource gone, not “body cleared”). |
| Error | **Case B** `SdkException<RawError>` |
| Map | `operations/Api20100401Message.md`, `enums.md` (`MessageEnumUpdateStatus`) |

### Operation: FetchMessage (delivery outcome)

| | |
|---|---|
| Controller | `client.Api20100401Message` |
| Method | `Task<TwilioSdk.Models.ApiV2010AccountMessage> FetchMessage(string accountSid, string sid, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages/{Sid}.json` on **Default (api)** |
| Envelope | **None** — `ApiV2010AccountMessage` |
| Read | `Status` · `ErrorCode` · `ErrorMessage` · `Sid` · `DateSent` · `DateUpdated` · `To` · `From`. XML: `error_code` / `error_message` populated when status is `failed` or `undelivered`; otherwise null. XML also says those two fields should not be used programmatically (values can change) — still persist them for the operator UI. |
| Missing SID | Case B (typically 404) via `RawError.StatusCode` |
| Error | **Case B** `SdkException<RawError>` |
| Map | `operations/Api20100401Message.md`, `records-1-Ac-Ca.md` |

### Operation: ListMessage (reconciliation)

| | |
|---|---|
| Controller | `client.Api20100401Message` |
| Method | `Task<TwilioSdk.Models.ListMessageResponse> ListMessage(string accountSid, string? to, string? from, DateTimeOffset? dateSent, DateTimeOffset? dateSentQuery, DateTimeOffset? dateSentQueryQuery, long? pageSize, int? page, string? pageToken, RequestOptions? requestOptions = null, CancellationToken ct = default)` |
| Must-pass-explicitly | 8 params `to` … `pageToken` |
| HTTP | `GET /2010-04-01/Accounts/{AccountSid}/Messages.json` on **Default (api)** — **`Twilio:BaseUrl` applies** |
| Query wire ← C# | `To` ← `to`, **`From` ← `from`**, `DateSent` ← `dateSent`, **`DateSent<` ← `dateSentQuery`**, **`DateSent>` ← `dateSentQueryQuery`**, `PageSize` ← `pageSize`, `Page` ← `page`, `PageToken` ← `pageToken` |
| This-app filter | Pass `from: <Twilio:FromNumber>` (sender filter **at the API**). Pass `to: null`. Do not list unfiltered and filter client-side. |
| Date range | Caller `from` (range start) → **`dateSentQueryQuery`** (`DateSent>`). Caller `to` (range end) → **`dateSentQuery`** (`DateSent<`). Exact-day `dateSent` unused for a range. XML: GMT. SDK serializes with `ToIso8601()` = **`yyyy-MM-ddTHH:mm:ss.fff'Z'`** (UTC). Wire operators are `>` / `<` (not `>=` / `<=`). Inclusive/exclusive edge **UNVERIFIED** — pass the caller’s ISO-8601 instants as `DateTimeOffset` UTC. |
| `pageSize` | XML: default 50, **maximum 1000**. |
| Envelope | `TwilioSdk.Models.ListMessageResponse` — **not** a `Meta` wrapper. Inner list: `Messages (messages): IReadOnlyList<ApiV2010AccountMessage>?`. Paging fields: `NextPageUri (next_page_uri): string?`, `PageToken` is **not** a response property — XML: “The page token. This is provided by the API.” Also `End`, `Start`, `Page`, `PageSize`, `FirstPageUri`, `PreviousPageUri`, `Uri`. |
| Pagination | Map: **none** (no `ExecutePaged` / no `perPage`). Manual: call `ListMessage` with the same `from` + date filters; while `NextPageUri` is non-null, call again with `pageToken` taken from that URI’s `PageToken` query value (and `page` / `pageSize` as needed) until `NextPageUri` is null. |
| Error | **Case B** `SdkException<RawError>` |
| Map | `operations/Api20100401Message.md`, `records-4-Li-Me.md` (`ListMessageResponse`) |

### `ApiV2010AccountMessage` fields (integration subset)

`TwilioSdk.Models.ApiV2010AccountMessage` — `Models/ApiV2010AccountMessage.cs`, `records-1-Ac-Ca.md`:

| C# (wire) | Type |
|---|---|
| `Sid (sid)` | `string?` |
| `Status (status)` | `MessageEnumStatus?` |
| `Body (body)` | `string?` |
| `From (from)` | `string?` |
| `To (to)` | `string?` |
| `MessagingServiceSid (messaging_service_sid)` | `string?` |
| `ErrorCode (error_code)` | `int?` |
| `ErrorMessage (error_message)` | `string?` |
| `DateCreated (date_created)` | `string?` (RFC 2822 GMT) |
| `DateSent (date_sent)` | `string?` (RFC 2822 GMT) |
| `DateUpdated (date_updated)` | `string?` (RFC 2822 GMT) |
| `AccountSid (account_sid)` | `string?` |
| `Direction (direction)` | `MessageEnumDirection?` |
| `NumSegments (num_segments)` | `string?` |
| `Price (price)` / `PriceUnit (price_unit)` | `string?` |
| `Uri (uri)` | `string?` |
| `SubresourceUris (subresource_uris)` | `object?` |

No extra envelope property — the object **is** the payload.

### Enums in scope (`TwilioSdk.Models.Enums` — `StringEnum<T>`, not C# enums)

Build with static members or `Type.FromValue("wire")`. Compare members; read `.Value` for the wire string. **MUST load `dotnet-models`.**

| Type | Members (C# · wire) |
|---|---|
| `MessageEnumStatus` | `Queued (queued)`, `Sending (sending)`, `Sent (sent)`, `Failed (failed)`, `Delivered (delivered)`, `Undelivered (undelivered)`, `Receiving (receiving)`, `Received (received)`, `Accepted (accepted)`, `Scheduled (scheduled)`, `Read (read)`, `PartiallyDelivered (partially_delivered)`, `Canceled (canceled)` |
| `MessageEnumUpdateStatus` | `Canceled (canceled)` |
| `MessageEnumScheduleType` | `Fixed (fixed)` |
| `MessageEnumDirection` | `Inbound (inbound)`, `OutboundApi (outbound-api)`, `OutboundCall (outbound-call)`, `OutboundReply (outbound-reply)` |
| `MessageEnumContentRetention` | `Retain (retain)`, `Discard (discard)` — create-time only; not the redact-after-send API |
| `ValidationError` | `TooShort (TOO_SHORT)`, `TooLong (TOO_LONG)`, `InvalidButPossible (INVALID_BUT_POSSIBLE)`, `InvalidCountryCode (INVALID_COUNTRY_CODE)`, `InvalidLength (INVALID_LENGTH)`, `NotANumber (NOT_A_NUMBER)` |
| `Field` | `CallerName (caller_name)`, `SimSwap (sim_swap)`, `CallForwarding (call_forwarding)`, `LineTypeIntelligence (line_type_intelligence)`, `LineStatus (line_status)`, `IdentityMatch (identity_match)`, `ReassignedNumber (reassigned_number)`, `SmsPumpingRisk (sms_pumping_risk)` |
| `LineType` | `Mobile (mobile)`, `Landline (landline)`, `TollFree (tollFree)`, `FixedVoip (fixedVoip)`, `NonFixedVoip (nonFixedVoip)`, `Personal (personal)`, `Premium (premium)`, `Voicemail (voicemail)`, `SharedCost (sharedCost)`, `Uan (uan)`, `Pager (pager)`, `Unknown (unknown)` — **override package enum**, not the type of `LineTypeIntelligenceInfo.Type` (`string?`) |

Map: `map/models/enums.md`.

### Errors (every in-scope operation is Case B)

| Situation | Type | How to read |
|---|---|---|
| Non-2xx (validation 4xx, not found, cancel-too-late, 5xx, **401/403 auth**) | `TwilioSdk.Core.Exceptions.SdkException<TwilioSdk.Core.ErrorResponse.RawError>` | `ex.Error.StatusCode`; body `ReadAsString()` or `ReadAsJson<T>()`. **No** `TryGet…` typed accessors. There is **no** generated `{Op}Error` for these ops. |
| Case B JSON body shape | **UNVERIFIED** | Extract `code` / `message` / `more_info` / `status` best-effort from JSON if present; **fall back to `ReadAsString()`** (and HTTP status). Do not assume a typed model. |
| Number not a usable destination (registration) | Lookup: `Valid == false` / `ValidationErrors` **or** `SdkException<RawError>` (4xx). CreateMessage 4xx on `to` is a send-time reject — registration should have caught it. | Reject at `POST /api/contact-numbers`. |
| Message accepted, fails later | 2xx `CreateMessage` then `FetchMessage.Status` in `{Failed, Undelivered}` + `ErrorCode`/`ErrorMessage` | Record outcome; not an exception on create. |
| Transport / timeout | `HttpRequestException`, `TaskCanceledException` (± inner `TimeoutException` from `RetryOptions.Timeout`) | Record; do not fail the order on send. |
| 2xx deserialize miss | `System.Text.Json.JsonException` — **not** `SdkException` | See trap notes. |
| Non-2xx body that does not match a typed error | These ops are Case B (`RawError` already wraps raw bytes) so construction of `{Op}Error` does not apply; `JsonException` still possible on **success** deserialize of `ApiV2010AccountMessage` / `LookupResponse` / `ListMessageResponse`. | See trap notes. |

No `…Result` variants exist on this SDK (`sdk-map.md`).

---

## Trap notes

⚠ Step 1 (client / DI) — `HttpClient` ownership and lifetime versus the SDK wrapper are not implied by the constructor. Getting this wrong produces socket exhaustion or disposed-client failures. **MUST load `dotnet-client-initialization`** before registering the client.

⚠ Step 1 (auth) — `AccountSidAuthToken` is a credentials **object** with `required` `Username`/`Password`, not two loose options properties; omitting it before first call yields unauthenticated traffic. **MUST load `dotnet-authentication`** before wiring `Twilio:AccountSid` / `Twilio:AuthToken`.

⚠ Step 1 (BaseUrl / retries / timeout) — `Retry` / `Timeout` on `TwilioSdkClientOptions` are not the timeout on the `HttpClient` you register, and they do not bound “the whole call” the way an app-level cancellation does. A transport failure can still re-execute a `POST`. **MUST load `dotnet-configuration-resilience`** before setting `Retry`, `Timeout`, or `Server.Default.Production.BaseUrl`.

⚠ Steps 2–9 (calls) — list/create signatures have many optional parameters **without C# defaults**; positional calls mis-bind. Named arguments; cancellation is `ct:`. **MUST load `dotnet-calling-endpoints`** before the first `CreateMessage` / `ListMessage` / `FetchPhoneNumber3`.

⚠ Steps 2–9 (models / enums) — statuses and schedule type are `StringEnum<T>`, not C# enums; unmodeled JSON is dropped on deserialize; `LineTypeIntelligenceInfo.Type` is `string?`. **MUST load `dotnet-models`** before mapping `LookupResponse` or `MessageEnumStatus`.

⚠ Step 10 (error boundary) — a drifted or malformed **2xx** body (a missing `required` member) surfaces as a `System.Text.Json.JsonException` from deserialization, **not** as an `SdkException` — so an SDK-exception-only catch ladder lets it escape the integration boundary. **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Step 10 (error boundary) — a **non-2xx** body that does not match its operation's generated `{Operation}Error` shape throws `JsonException` *while the error object is being constructed*, so the `JsonException` **replaces** the `SdkException` and the HTTP status is destroyed with it — a boundary that maps every `JsonException` to a 5xx then reports a deterministic rejection as an outage, and a caller that retries 5xx retries something that can never succeed. (In-scope message/lookup ops are Case B / `RawError`, so this construction path is the success-envelope deserialize plus any Case A call added later.) **MUST load `dotnet-error-handling`** before writing that boundary.

⚠ Tests — the constructor `HttpClient` argument is the test seam; do not stub SDK internals. **MUST load `dotnet-testing`** before writing integration tests.

---

## REQUIRED READING

Load **before implementation starts**. This sheet deliberately does not carry their contents.

| Skill | Governs |
|---|---|
| `dotnet-client-initialization` | Step 1 — constructing / DI-registering `TwilioSdkClient`, `HttpClient` lifetime |
| `dotnet-authentication` | Step 1 — `AccountSidAuthToken` / `BasicAuthCredentials` |
| `dotnet-configuration-resilience` | Step 1 — `RetryOptions`, `Timeout`, `Server.Default` vs `Default4` BaseUrl, ListMessage paging loop |
| `dotnet-calling-endpoints` | Steps 2–9 — named args, `ct:`, must-pass-null optionals |
| `dotnet-models` | Steps 2–9 — `StringEnum<T>`, request/response records, wire names |
| `dotnet-error-handling` | Step 10 — Case B `SdkException<RawError>`, both `JsonException` directions, send-must-not-fail-order boundary |
| `dotnet-testing` | Tests for the integration layer |

---

## Assumptions & Blockers

**Assumptions**

- Lookup **v2** `FetchPhoneNumber3` is the validate/canonicalize operation (v1 has no `Valid` flag).
- Immediate SMS uses `from` = `Twilio:FromNumber`. Scheduled SMS uses `messagingServiceSid` = `Twilio:MessagingServiceSid` because `MessageEnumScheduleType` is documented as Messaging Services only.
- Follow-up delay (“a few days later”) is an application constant converted to `DateTimeOffset` UTC for `sendAt`.
- Operator resend idempotency is implemented **in the app** (store caller key → SID) because of the SDK GAP below.
- Live US carrier refusal after API accept is expected, not a sheet gap.
- Destinations for live checks: only `TWILIO_TEST_TO_NUMBER` and `TWILIO_UNREACHABLE_TO_NUMBER`.

**Blockers / GAPs**

- **GAP — caller-supplied idempotency on Create Message:** no method parameter; `RequestOptions` has only `LogLevel`; SDK always sends `Idempotency-Key: Guid.NewGuid()`. Do not invent a header API. Application-level dedupe is required for `POST /api/notifications/{id}/resend`.
- **UNVERIFIED — schedule min/max lead time** and exact `SendAt` string the provider accepts (SDK form-encodes `DateTimeOffset` via STJ, not `ToIso8601()`).
- **UNVERIFIED — HTTP status when cancelling a message that already sent** (handle via Case B `RawError`).
- **UNVERIFIED — which `LineTypeIntelligenceInfo.Type` strings are SMS-capable** (`Type` is `string?`; `LineType` enum is a different model). Registration reject is grounded on `Valid` / `ValidationErrors` / lookup 4xx.
- **UNVERIFIED — Case B error JSON field names.** Extract `code`/`message` best-effort; fall back to `ReadAsString()`.
